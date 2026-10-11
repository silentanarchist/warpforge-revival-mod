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
            RevivalMod.Log.Msg($"[deathtrace] {n} steps traced (temporary, for the Point-Blank Shot freeze)");
        }

        // ---- logging with a loop check
        private static readonly Dictionary<string, (float start, int count, bool told)> calls = new Dictionary<string, (float, int, bool)>();
        private static readonly HashSet<string> open = new HashSet<string>();

        private static void In(string step, string details)
        {
            try
            {
                float now = UnityEngine.Time.realtimeSinceStartup;
                calls.TryGetValue(step, out var c);
                if (now - c.start > 1f) c = (now, 0, false);
                c.count++;
                if (c.count > 50 && !c.told)
                {
                    c.told = true;
                    RevivalMod.Log.Warning($"[deathtrace] {step} called {c.count} times within a second: likely a loop");
                }
                calls[step] = c;
                if (c.count <= 50) RevivalMod.Log.Msg($"[deathtrace] -> {step}" + (details.Length > 0 ? " (" + details + ")" : ""));
                open.Add(step);
            }
            catch { }
        }

        private static void Out(string step)
        {
            try
            {
                if (!open.Remove(step)) return;
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
            tracingTrigger = DeathTriggers.Contains((int)__0) || Watched(__2, __3, __4);
            if (tracingTrigger) In("RawCardScript.TriggerAbilities", $"trigger {__0}, card {Name(__2)}, played {Name(__3)}, target {Name(__4)}");
        }
        private static void TriggerAbilitiesOut() { if (tracingTrigger) Out("RawCardScript.TriggerAbilities"); }

        private static bool tracingAbility;
        private static void PlayAbility(AbilityTrigger __2)
        {
            tracingAbility = DeathTriggers.Contains((int)__2);
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
