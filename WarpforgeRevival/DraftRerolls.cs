using System;
using HarmonyLib;
using Il2Cpp;

namespace WarpforgeRevival
{
    /// <summary>
    /// Draft: the game counts rerolls across the whole run, so a "first reroll is free" price list
    /// gives one free reroll per run. The revival rule is one per package choice, so the count is
    /// put back to zero each time a package pick is confirmed (the server does the same on its side).
    /// </summary>
    internal static class DraftRerolls
    {
        [HarmonyPatch(typeof(DraftModeSelectPacksStateDemo), nameof(DraftModeSelectPacksStateDemo.OnPackConfirmed))]
        private static class NewChoice
        {
            private static void Postfix(DraftModeSelectPacksStateDemo __instance)
            {
                try
                {
                    var window = __instance.Window;
                    var draft = (object)window == null ? null : window.LiveOp;
                    var save = (object)draft == null ? null : draft.GetBaseSave();
                    if ((object)save == null) return;
                    if (save.currentReRolls == 0) return;
                    save.currentReRolls = 0;
                    __instance.RefreshReRollPrice();
                }
                catch (Exception e) { RevivalMod.Log.Warning("[draft] " + e.Message); }
            }
        }
    }
}
