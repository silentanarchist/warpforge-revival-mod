using System;
using HarmonyLib;
using Il2Cpp;

namespace WarpforgeRevival
{
    /// <summary>
    /// Draft entry screen: the revival has no currency, so the two paid entries are removed and the
    /// free entry - normally available once per time window - is always open.
    /// </summary>
    internal static class DraftEntry
    {
        private static void Dress(DraftModeMenuPayStateDemo screen)
        {
            try
            {
                if ((object)screen == null || screen.Pointer == IntPtr.Zero) return;
                screen.enableFreeEntranceTimeChecks = false;
                Hide(screen.premiumButton);
                Hide(screen.premiumTenButton);
                var wait = screen.freeTimeText;
                if ((object)wait != null) wait.gameObject.SetActive(false);     // "free again in ..." countdown
                var free = screen.freeButton;
                if ((object)free != null)
                {
                    free.gameObject.SetActive(true);
                    free.interactable = true;
                }
            }
            catch (Exception e) { RevivalMod.Log.Warning("[draft] entry screen: " + e.Message); }
        }

        private static void Hide(EverguildButton button)
        {
            if ((object)button != null && button.Pointer != IntPtr.Zero) button.gameObject.SetActive(false);
        }

        [HarmonyPatch(typeof(DraftModeMenuPayStateDemo), nameof(DraftModeMenuPayStateDemo.GetFreeButtonInteractable))]
        private static class AlwaysFree
        {
            private static void Postfix(ref bool __result) => __result = true;
        }

        [HarmonyPatch(typeof(DraftModeMenuPayStateDemo), nameof(DraftModeMenuPayStateDemo.OnOpen))]
        private static class Opened
        {
            private static void Postfix(DraftModeMenuPayStateDemo __instance) => Dress(__instance);
        }

        // The screen re-evaluates its buttons after a request and on its timer; keep it dressed.
        [HarmonyPatch(typeof(DraftModeMenuPayStateDemo), nameof(DraftModeMenuPayStateDemo.SetAllButtonsInteractable))]
        private static class Rechecked
        {
            private static void Postfix(DraftModeMenuPayStateDemo __instance, bool interactable)
            {
                if (interactable) Dress(__instance);
                else { Hide(__instance.premiumButton); Hide(__instance.premiumTenButton); }
            }
        }

        [HarmonyPatch(typeof(DraftModeMenuPayStateDemo), nameof(DraftModeMenuPayStateDemo.ChangeState))]
        private static class StateChanged
        {
            private static void Postfix(DraftModeMenuPayStateDemo __instance) => Dress(__instance);
        }
    }
}
