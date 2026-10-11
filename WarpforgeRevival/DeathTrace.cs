using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Il2Cpp;

namespace WarpforgeRevival
{
    /// <summary>
    /// TEMPORARY (test builds, 2026-10-11): traces what the game does between a unit dying and the
    /// next card being drawn, to find where a tester's game freezes when Point-Blank Shot kills its
    /// target ("if target dies, draw a card": an OtherUnitDeath trigger on the stratagem while it is
    /// in the discard pile). Two freezes so far, both right after "set as waitingToDie".
    ///
    /// Every hooked step writes "[deathtrace] -> Step (details)" on the way in and "&lt;- Step" on the
    /// way out, so after a freeze the last line names the step that never returned. A step called
    /// more than 50 times within a second is reported once as a likely loop. The busy hooks
    /// (card triggers and abilities) only log the death-related triggers or Point-Blank Shot.
    /// Remove once the freeze is understood.
    ///
    /// Update (owner, 2026-10-11): his game freezes every time he plays the card, kill or not. So
    /// playing Point-Blank Shot (touching it in the hand, or any of its abilities) opens a 20-second
    /// "watch window" in which everything is traced: every ability, damage, death and draw step,
    /// each step of the battle's coroutines (BattleManager / CardScript), and a frame marker every
    /// 0.2 s. If the game logic finishes and only the frame markers stop, the freeze is in drawing
    /// (the card's effect on his graphics card), not in the rules.
    /// </summary>
    internal static class DeathTrace
    {
        private const string Card = "Point-Blank Shot";
        // AbilityTrigger numbers around a unit dying: OtherUnitDeath (90), ThisUnitDeath and the
        // slay/backlash pair OnMinionDeath fires instead (80, 81), and the per-type ones after it (170, 175, 180).
        private static readonly HashSet<int> DeathTriggers = new HashSet<int> { 80, 81, 90, 170, 175, 180 };

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            int n = 0;
            void Hook(Type type, string name, string pre)
            {
                try
                {
                    var m = AccessTools.FirstMethod(type, x => x.Name == name);
                    if (m == null) { RevivalMod.Log.Warning($"[deathtrace] {type.Name}.{name} not found"); return; }
                    harmony.Patch(m, prefix: new HarmonyMethod(typeof(DeathTrace), pre), postfix: new HarmonyMethod(typeof(DeathTrace), pre + "Out"));
                    n++;
                }
                catch (Exception e) { RevivalMod.Log.Warning($"[deathtrace] could not hook {type.Name}.{name}: {e.Message}"); }
            }
            Hook(typeof(CardScript), "ResolveDeadCard", nameof(ResolveDeadCard));
            Hook(typeof(CardScript), "TriggerOnMinionDeath", nameof(TriggerOnMinionDeath));
            Hook(typeof(CardScript), "UnitDeath", nameof(UnitDeath));
            Hook(typeof(RawCardScript), "OnMinionDeath", nameof(OnMinionDeath));
            Hook(typeof(RawCardScript), "TriggerAbilities", nameof(TriggerAbilities));
            Hook(typeof(AbilityLogic), "PlayAbility", nameof(PlayAbility));
            Hook(typeof(AbilityLogic), "AddDrawCard", nameof(AbilityAddDraw));
            Hook(typeof(BattleManager), "AddDrawCard", nameof(AddDrawCard));
            Hook(typeof(BattleManager), "DrawCardFromDeck", nameof(DrawCardFromDeck));
            Hook(typeof(BattleManager), "ResolveDrawCard", nameof(ResolveDrawCard));
            Hook(typeof(BattleManager), "ResolveMinionDeath", nameof(ResolveMinionDeath));
            Hook(typeof(BattleManagerSupport), "BroadcastDeadUnit", nameof(BroadcastDeadUnit));
            Hook(typeof(CardScript), "OnTouchDown", nameof(OnTouchDown));
            Hook(typeof(CardScript), "ReceiveDamage", nameof(ReceiveDamage));
            Hook(typeof(CardScript), "CheckIfDead", nameof(CheckIfDead));
            Hook(typeof(BattleManager), "DealMultiDamage", nameof(DealMultiDamage));
            Hook(typeof(BattleManager), "StartTargetedSpellAnim", nameof(StartTargetedSpellAnim));
            Hook(typeof(BattleManager), "StopTargetedSpellAnim", nameof(StopTargetedSpellAnim));
            Hook(typeof(BattleManager), "ResolvePlayCardFromHand", nameof(ResolvePlayCardFromHand));
            // every step of the battle's coroutines (logged only inside a watch window)
            int steps = 0;
            foreach (var owner in new[] { typeof(BattleManager), typeof(CardScript), typeof(PlayerHand) })
                foreach (var nested in owner.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (!nested.Name.Contains("_d__")) continue;
                    var move = AccessTools.FirstMethod(nested, x => x.Name == "MoveNext");
                    if (move == null) continue;
                    try
                    {
                        harmony.Patch(move, prefix: new HarmonyMethod(typeof(DeathTrace), nameof(StepIn)), postfix: new HarmonyMethod(typeof(DeathTrace), nameof(StepOut)));
                        steps++;
                    }
                    catch { }
                }
            RevivalMod.Log.Msg($"[deathtrace] {n} steps and {steps} coroutines traced (temporary, for the Point-Blank Shot freeze)");
        }

        // ---- the watch window
        private static float watchUntil = -1f, nextFrameMark;
        private static int frameNo;
        private static bool Watching => UnityEngine.Time.realtimeSinceStartup < watchUntil;

        private static void Watch(string why)
        {
            if (!Watching) RevivalMod.Log.Msg("[deathtrace] watch window opened: " + why);
            watchUntil = UnityEngine.Time.realtimeSinceStartup + 20f;
        }

        /// <summary>Every frame (from OnUpdate): a frame marker every 0.2 s while watching.</summary>
        internal static void Tick()
        {
            try
            {
                frameNo++;
                float now = UnityEngine.Time.realtimeSinceStartup;
                if (watchUntil > 0 && now >= watchUntil) { watchUntil = -1f; RevivalMod.Log.Msg("[deathtrace] watch window closed"); }
                if (Watching && now >= nextFrameMark) { nextFrameMark = now + 0.2f; RevivalMod.Log.Msg($"[deathtrace] frame {frameNo}"); }
            }
            catch { }
        }

        // ---- logging with a loop check
        private static readonly Dictionary<string, (float start, int count, bool told)> calls = new Dictionary<string, (float, int, bool)>();
        private static readonly Dictionary<string, int> open = new Dictionary<string, int>();

        private static void In(string step, string details, int limit = 50)
        {
            try
            {
                float now = UnityEngine.Time.realtimeSinceStartup;
                calls.TryGetValue(step, out var c);
                if (now - c.start > 1f) c = (now, 0, false);
                c.count++;
                if (c.count > limit && !c.told)
                {
                    c.told = true;
                    RevivalMod.Log.Warning($"[deathtrace] {step} called {c.count} times within a second: likely a loop");
                }
                calls[step] = c;
                if (c.count <= Math.Min(limit, 50)) RevivalMod.Log.Msg($"[deathtrace] -> {step}" + (details.Length > 0 ? " (" + details + ")" : ""));
                open[step] = open.TryGetValue(step, out int k) ? k + 1 : 1;
            }
            catch { }
        }

        private static void Out(string step)
        {
            try
            {
                if (!open.TryGetValue(step, out int k) || k <= 0) return;
                open[step] = k - 1;
                calls.TryGetValue(step, out var c);
                if (c.count <= 50) RevivalMod.Log.Msg($"[deathtrace] <- {step}");
            }
            catch { }
        }

        private static string Name(CardScript c)
        {
            try { return (object)c == null ? "none" : c.cardName; } catch { return "?"; }
        }

        private static bool Watched(CardScript a, CardScript b = null, CardScript c = null) => Name(a) == Card || Name(b) == Card || Name(c) == Card;

        // ---- the play of the card itself and what follows (inside a watch window)
        private static void OnTouchDown(CardScript __instance)
        {
            if (Name(__instance) == Card) { Watch("Point-Blank Shot touched"); In("CardScript.OnTouchDown", "Point-Blank Shot"); }
        }
        private static void OnTouchDownOut() => Out("CardScript.OnTouchDown");

        private static void ReceiveDamage(CardScript __instance) { if (Watching) In("CardScript.ReceiveDamage", "card " + Name(__instance)); }
        private static void ReceiveDamageOut() => Out("CardScript.ReceiveDamage");
        private static void CheckIfDead(CardScript __instance) { if (Watching) In("CardScript.CheckIfDead", "card " + Name(__instance)); }
        private static void CheckIfDeadOut() => Out("CardScript.CheckIfDead");
        private static void DealMultiDamage() { if (Watching) In("BattleManager.DealMultiDamage", ""); }
        private static void DealMultiDamageOut() => Out("BattleManager.DealMultiDamage");
        private static void StartTargetedSpellAnim() { if (Watching) In("BattleManager.StartTargetedSpellAnim", ""); }
        private static void StartTargetedSpellAnimOut() => Out("BattleManager.StartTargetedSpellAnim");
        private static void StopTargetedSpellAnim() { if (Watching) In("BattleManager.StopTargetedSpellAnim", ""); }
        private static void StopTargetedSpellAnimOut() => Out("BattleManager.StopTargetedSpellAnim");
        private static void ResolvePlayCardFromHand() { if (Watching) In("BattleManager.ResolvePlayCardFromHand", ""); }
        private static void ResolvePlayCardFromHandOut() => Out("BattleManager.ResolvePlayCardFromHand");

        // coroutine steps: one MoveNext call = one step of that coroutine
        private static readonly Stack<string> stepNames = new Stack<string>();
        private static void StepIn(MethodBase __originalMethod)
        {
            if (!Watching) { stepNames.Push(null); return; }
            string name = "step " + (__originalMethod?.DeclaringType?.Name ?? "?");
            stepNames.Push(name);
            In(name, "", 1000);      // a waiting coroutine is stepped every frame: only far more than that is a loop
        }
        private static void StepOut()
        {
            string name = stepNames.Count > 0 ? stepNames.Pop() : null;
            if (name != null) Out(name);
        }

        // ---- the hooked steps
        private static void ResolveDeadCard(CardScript __instance) => In("CardScript.ResolveDeadCard", "card " + Name(__instance));
        private static void ResolveDeadCardOut() => Out("CardScript.ResolveDeadCard");

        private static void TriggerOnMinionDeath(CardScript __instance, CardScript __0, CardScript __1) =>
            In("CardScript.TriggerOnMinionDeath", $"on {Name(__instance)}, acting {Name(__0)}, dead {Name(__1)}");
        private static void TriggerOnMinionDeathOut() => Out("CardScript.TriggerOnMinionDeath");

        private static void UnitDeath(CardScript __instance) => In("CardScript.UnitDeath", "card " + Name(__instance));
        private static void UnitDeathOut() => Out("CardScript.UnitDeath");

        private static void OnMinionDeath() => In("RawCardScript.OnMinionDeath", "");
        private static void OnMinionDeathOut() => Out("RawCardScript.OnMinionDeath");

        private static bool tracingTrigger;
        private static void TriggerAbilities(AbilityTrigger __0, CardScript __2, CardScript __3, CardScript __4)
        {
            if (Watched(__2, __3, __4)) Watch("Point-Blank Shot ability (" + __0 + ")");
            tracingTrigger = Watching || DeathTriggers.Contains((int)__0);
            if (tracingTrigger) In("RawCardScript.TriggerAbilities", $"trigger {__0}, card {Name(__2)}, played {Name(__3)}, target {Name(__4)}");
        }
        private static void TriggerAbilitiesOut() { if (tracingTrigger) Out("RawCardScript.TriggerAbilities"); }

        private static bool tracingAbility;
        private static void PlayAbility(AbilityTrigger __2)
        {
            tracingAbility = Watching || DeathTriggers.Contains((int)__2);
            if (tracingAbility) In("AbilityLogic.PlayAbility", "trigger " + __2);
        }
        private static void PlayAbilityOut() { if (tracingAbility) Out("AbilityLogic.PlayAbility"); }

        private static void AbilityAddDraw() => In("AbilityLogic.AddDrawCard", "");
        private static void AbilityAddDrawOut() => Out("AbilityLogic.AddDrawCard");

        private static void AddDrawCard(bool __0, CardScript __3) => In("BattleManager.AddDrawCard", $"{(__0 ? "player" : "enemy")}, caster {Name(__3)}");
        private static void AddDrawCardOut() => Out("BattleManager.AddDrawCard");

        private static void DrawCardFromDeck(bool __0) => In("BattleManager.DrawCardFromDeck", __0 ? "player's deck" : "enemy's deck");
        private static void DrawCardFromDeckOut() => Out("BattleManager.DrawCardFromDeck");

        private static void ResolveDrawCard() => In("BattleManager.ResolveDrawCard", "");
        private static void ResolveDrawCardOut() => Out("BattleManager.ResolveDrawCard");

        private static void ResolveMinionDeath() => In("BattleManager.ResolveMinionDeath", "");
        private static void ResolveMinionDeathOut() => Out("BattleManager.ResolveMinionDeath");

        private static void BroadcastDeadUnit() => In("BattleManagerSupport.BroadcastDeadUnit", "");
        private static void BroadcastDeadUnitOut() => Out("BattleManagerSupport.BroadcastDeadUnit");
    }
}
