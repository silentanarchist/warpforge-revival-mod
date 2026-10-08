using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using HarmonyLib;
using Il2Cpp;
using CardList = Il2CppSystem.Collections.Generic.List<Il2Cpp.RawCardScript>;

namespace WarpforgeRevival
{
    /// <summary>
    /// The Offence / Defence card step.
    ///
    /// The game still contains the step that runs after the mulligan: the player going first picks
    /// an Offence card, the player going second a Defence card, the picks travel to the opponent
    /// together with the mulligan, and both are applied when the battle starts. The final build
    /// switches it off in three places (all asking one "is this feature on" function that always
    /// answers no) and, with nothing unlocked on a revival server, would offer no cards anyway.
    ///
    /// When the server's "offenseCards" setting is on, those three places are switched back on and
    /// the cards on offer are:
    ///  - going first: every Offence card of the battlefield (the defender's faction) - a real choice;
    ///  - going second: the Defence card saved in your deck, taken automatically as before; only a
    ///    deck without one gets to choose from the faction's Defence cards.
    /// With the setting off the game behaves as shipped (Defence card from the deck, no Offence card).
    /// Both players get the setting from the same server, so they always agree.
    /// </summary>
    internal static class BattlefieldCards
    {
        [DllImport("kernel32.dll")] private static extern bool VirtualProtect(IntPtr address, UIntPtr size, uint newProtect, out uint oldProtect);
        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")] private static extern bool FlushInstructionCache(IntPtr process, IntPtr address, UIntPtr size);

        private const long AlwaysFalse = 0x6fb110;          // the shared "return false"
        private static readonly (long site, string what)[] Sites =
        {
            (0x9c2d90, "pick step after the mulligan"),
            (0x9a8d23, "applying the chosen cards"),
            (0x9c3f89, "go-second card handling"),
        };
        private static readonly byte[] On = { 0xB0, 0x01, 0x0F, 0x1F, 0x00 };   // mov al,1 ; nop

        private static readonly object Gate = new object();
        private static bool applied;
        private static bool broken;

        private static byte[] Original(long site)
        {
            int rel = (int)(AlwaysFalse - (site + 5));
            var b = new byte[5];
            b[0] = 0xE8;
            BitConverter.GetBytes(rel).CopyTo(b, 1);
            return b;
        }

        /// <summary>Switches the pick step on or off. Safe to call repeatedly and from any thread.</summary>
        public static void Set(bool on)
        {
#if ANDROID_PORT
            // In the phone's code the "is this feature on" question is a function of its own, asked in
            // exactly the same three places, so it is simply answered (see FeatureSwitch below) instead
            // of changing machine code as on Windows.
            lock (Gate)
            {
                if (broken || on == applied) return;
                if (!Answer(on)) { broken = true; return; }
                applied = on;
                RevivalMod.Log.Msg(on
                    ? "[cards] Offence card step on: going first you pick an Offence card; going second you get your deck's Defence card"
                    : "[cards] Offence card step off: standard rules (going second you get your deck's Defence card)");
            }
            return;
#else
            lock (Gate)
            {
                if (broken || on == applied) return;
                try
                {
                    IntPtr gameBase = IntPtr.Zero;
                    foreach (ProcessModule m in Process.GetCurrentProcess().Modules)
                        if (string.Equals(m.ModuleName, "GameAssembly.dll", StringComparison.OrdinalIgnoreCase)) { gameBase = m.BaseAddress; break; }
                    if (gameBase == IntPtr.Zero) { broken = true; RevivalMod.Log.Warning("[cards] game code not found; the Offence card step stays off"); return; }

                    // Check every place holds exactly what we expect before changing any of them.
                    foreach (var (site, what) in Sites)
                    {
                        IntPtr p = new IntPtr(gameBase.ToInt64() + site);
                        byte[] expect = on ? Original(site) : On;
                        for (int i = 0; i < 5; i++)
                            if (Marshal.ReadByte(p, i) != expect[i])
                            {
                                broken = true;
                                RevivalMod.Log.Warning($"[cards] unexpected game code at the {what}; the Offence card step is left as it is");
                                return;
                            }
                    }
                    foreach (var (site, what) in Sites)
                    {
                        IntPtr p = new IntPtr(gameBase.ToInt64() + site);
                        if (!VirtualProtect(p, (UIntPtr)5, 0x40, out uint old)) { broken = true; RevivalMod.Log.Warning($"[cards] could not change the {what}"); return; }
                        Marshal.Copy(on ? On : Original(site), 0, p, 5);
                        VirtualProtect(p, (UIntPtr)5, old, out _);
                        FlushInstructionCache(GetCurrentProcess(), p, (UIntPtr)5);
                    }
                    applied = on;
                    RevivalMod.Log.Msg(on
                        ? "[cards] Offence card step on: going first you pick an Offence card; going second you get your deck's Defence card"
                        : "[cards] Offence card step off: standard rules (going second you get your deck's Defence card)");
                }
                catch (Exception e) { broken = true; RevivalMod.Log.Warning("[cards] " + e); }
            }
#endif
        }

#if ANDROID_PORT
        // The phone's "is this feature on" function is two instructions: "answer no; return". That
        // is too short to hook safely (see NoShortHooks), so its first instruction is rewritten to
        // "answer yes" - after checking that it is exactly what is expected - with the loader's own
        // routine for changing code.
        [DllImport("libdobby.so")] private static extern int DobbyCodePatch(IntPtr address, byte[] buffer, uint size);

        private static readonly byte[] AnswerNo = { 0xE0, 0x03, 0x1F, 0x2A };     // mov w0, wzr
        private static readonly byte[] AnswerYes = { 0x20, 0x00, 0x80, 0x52 };    // mov w0, #1
        private static readonly byte[] Return = { 0xC0, 0x03, 0x5F, 0xD6 };       // ret

        private static bool Answer(bool yes)
        {
            try
            {
                var method = typeof(BattleManager).GetMethod(nameof(BattleManager.ShouldUseEnviromentalEffects));
                var field = method == null ? null : Il2CppInterop.Common.Il2CppInteropUtils.GetIl2CppMethodInfoPointerFieldForGeneratedMethod(method);
                IntPtr info = field == null ? IntPtr.Zero : (IntPtr)field.GetValue(null);
                IntPtr code = info == IntPtr.Zero ? IntPtr.Zero : Marshal.ReadIntPtr(info);
                if (code == IntPtr.Zero) { RevivalMod.Log.Warning("[cards] the feature check was not found; the Offence card step stays off on this device"); return false; }
                byte[] expect = yes ? AnswerNo : AnswerYes, write = yes ? AnswerYes : AnswerNo;
                for (int i = 0; i < 4; i++)
                    if (Marshal.ReadByte(code, i) != expect[i] || Marshal.ReadByte(code, 4 + i) != Return[i])
                    {
                        RevivalMod.Log.Warning("[cards] unexpected game code at the feature check; the Offence card step is left as it is on this device");
                        return false;
                    }
                int result = DobbyCodePatch(code, write, 4);
                if (result != 0 || Marshal.ReadByte(code, 0) != write[0])
                {
                    RevivalMod.Log.Warning($"[cards] the feature check could not be changed (result {result}); the Offence card step is left as it is on this device");
                    return false;
                }
                return true;
            }
            catch (Exception e) { RevivalMod.Log.Warning("[cards] " + e); return false; }
        }
#endif

        /// <summary>The cards offered in the pick step (the game itself would offer none here).</summary>
        [HarmonyPatch(typeof(BattleManager), nameof(BattleManager.GetEnvEffectCards))]
        private static class Offer
        {
            private static void Postfix(BattleManager __instance, bool shouldUseOffensive, ref CardList __result)
            {
                try
                {
                    if (!applied) return;
                    var battle = __instance;
                    // The battlefield belongs to the defender: the player going second.
                    var defender = battle.playerGoesFirst ? battle.enemyRawBattleData : battle.playerRawBattleData;
                    var warlord = (object)defender == null ? null : defender.deckWarlord;
                    var source = battle.enviromentalEffectCards;
                    if ((object)warlord == null || (object)source == null) return;

                    if (!shouldUseOffensive)
                    {
                        var own = defender.defensiveCard;
                        if ((object)own != null)
                        {
                            var only = new CardList();
                            only.Add(own);
                            __result = only;
                            RevivalMod.Log.Msg($"[cards] Defence card from the deck: '{own.cardName}'");
                            return;
                        }
                    }

                    // The game's own list can point at cards that no longer exist (it does for Space Wolves),
                    // which leaves the picker with nothing to choose and the match stuck. Keep the real ones,
                    // and if none are left, take the army's Offence/Defence cards from the card collection.
                    var listed = source.GetEnvCardList(warlord.cardArmy, shouldUseOffensive, false, 100000);
                    var all = new CardList();
                    if (listed != null)
                        for (int i = 0; i < listed.Count; i++) { var c = listed[i]; if ((object)c != null && c.Pointer != IntPtr.Zero) all.Add(c); }
                    if (all.Count == 0)
                    {
                        var wanted = shouldUseOffensive ? SpellType.OffensiveCard : SpellType.DefensiveCard;
                        var every = PlayerDataManager.singletonManager?.allCardCollection;
                        if (every != null)
                            for (int i = 0; i < every.Count; i++)
                            {
                                var c = every[i];
                                if ((object)c == null || c.cardArmy != warlord.cardArmy || c.spellType != wanted) continue;
                                string id = c.uniqueId;
                                if (string.IsNullOrEmpty(id) || id.Length > 8) continue;      // numbered cards only, not helper copies
                                all.Add(c);
                            }
                    }
                    int count = all.Count;
                    if (count == 0)
                    {
                        RevivalMod.Log.Msg($"[cards] no {(shouldUseOffensive ? "Offence" : "Defence")} cards exist for {warlord.cardArmy}; leaving the game's own list ({(__result == null ? 0 : __result.Count)})");
                        return;
                    }
                    __result = all;
                    var names = new System.Text.StringBuilder();
                    for (int i = 0; i < count; i++) { var c = all[i]; if ((object)c != null) names.Append(i == 0 ? "" : ", ").Append(c.cardName); }
                    RevivalMod.Log.Msg($"[cards] {(shouldUseOffensive ? "Offence" : "Defence")} cards on offer ({warlord.cardArmy} battlefield): {names}");
                }
                catch (Exception e) { RevivalMod.Log.Warning("[cards] " + e); }
            }
        }
    }
}
