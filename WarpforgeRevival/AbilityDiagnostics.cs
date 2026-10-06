using System;
using HarmonyLib;
using Il2Cpp;

namespace WarpforgeRevival
{
    /// <summary>
    /// Logs what the game decides when a unit with an action keyword (Ferocity, Duty ...) is selected on
    /// the board, so a card whose action cannot be used can be diagnosed from the log.
    /// </summary>
    internal static class AbilityDiagnostics
    {
        [HarmonyPatch(typeof(UnitOnBoardAttackTypeSelector), nameof(UnitOnBoardAttackTypeSelector.Toggle))]
        private static class Selected
        {
            private static void Postfix(bool __0, CardScript __1)
            {
                try
                {
                    if (!__0 || (object)__1 == null || __1.Pointer == IntPtr.Zero) return;
                    IntPtr rawPtr = System.Runtime.InteropServices.Marshal.ReadIntPtr(__1.Pointer + 0x28);
                    if (rawPtr == IntPtr.Zero) return;
                    var raw = new RawCardScript(rawPtr);
                    if (!raw.hasActiveAbility) return;
                    string s = $"[action] selected {__1.cardName}: active={(bool)__1.hasActiveAbility}";
                    try { s += $" ferocity={__1.HasCurrentTrait(DefinedTrait.ferocity)}"; } catch (Exception e) { s += " ferocity?" + e.Message; }
                    try { s += $" canUseNow={__1.CanUseAbilityNow()}"; } catch (Exception e) { s += " canUseNow?" + e.Message; }
                    try { s += $" summonSick={__1.displaySummonSickness}"; } catch (Exception e) { s += " sick?" + e.Message; }
                    try { s += $" state={__1.cardState} usedAbility={(bool)__1.usedActiveAbility}"; } catch (Exception e) { s += " state?" + e.Message; }
                    try { s += $" allowed={new BattleManager(System.Runtime.InteropServices.Marshal.ReadIntPtr(__1.Pointer + 0x258)).CanUseActiveAbility(__1, true, false)}"; } catch (Exception e) { s += " allowed?" + e.Message; }
                    try { s += $" logic={raw.activeAbility.activeAbilityLogic.Count} targeted={raw.activeAbility.targeted}"; } catch (Exception e) { s += " logic?" + e.Message; }
                    RevivalMod.Log.Msg(s);
                }
                catch (Exception e) { RevivalMod.Log.Warning("[action] " + e.Message); }
            }
        }

        [HarmonyPatch(typeof(BattleManager), nameof(BattleManager.TryUsingActiveAbilityOfCard))]
        private static class Tried
        {
            private static void Postfix(CardScript __0, bool __result)
            {
                try { RevivalMod.Log.Msg($"[action] tried the action of {((object)__0 == null ? "?" : __0.cardName)}: {(__result ? "started" : "refused")}"); }
                catch { }
            }
        }
    }
}
