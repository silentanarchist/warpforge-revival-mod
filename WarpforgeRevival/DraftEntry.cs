using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;

namespace WarpforgeRevival
{
    /// <summary>
    /// Draft entry screen: the revival has no currency, so the two paid entries are removed and the
    /// free entry - normally available once per time window - is always open. The free button moves
    /// under the middle of the screen's texts, so the single button is centred with them.
    /// (The screen's title, texts and picture come from the Draft mode's settings on the server.)
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
                    Centre(free, screen.premiumButton, screen.titleText, screen.subtitleText);
                }
            }
            catch (Exception e) { RevivalMod.Log.Warning("[draft] entry screen: " + e.Message); }
        }

        private static readonly Dictionary<IntPtr, float> freeHome = new Dictionary<IntPtr, float>();
        private static bool placedNoted;

        /// <summary>
        /// Puts the free button under the middle of the screen's texts (title and description). If
        /// those cannot be found, half way between its own place and the (hidden) paid button's.
        /// </summary>
        private static void Centre(EverguildButton free, EverguildButton paid, params Il2CppTMPro.TextMeshProUGUI[] texts)
        {
            var ft = free.transform;
            var parent = ft.parent;
            if ((object)parent == null) return;
            if (!freeHome.TryGetValue(free.Pointer, out float home))
            {
                home = ft.localPosition.x;
                freeHome[free.Pointer] = home;
                // a layout group would put the button back; this one button is placed by hand
                if ((object)parent.GetComponent<UnityEngine.UI.LayoutGroup>() != null)
                {
                    var le = free.GetComponent<UnityEngine.UI.LayoutElement>() ?? free.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
                    le.ignoreLayout = true;
                }
            }
            // middle of the texts' boxes (they are centred text), in the button's parent's space
            float sum = 0f; int n = 0;
            foreach (var t in texts)
            {
                if ((object)t == null || t.Pointer == IntPtr.Zero || !t.gameObject.activeInHierarchy) continue;
                var rt = t.rectTransform;
                sum += parent.InverseTransformPoint(rt.TransformPoint((UnityEngine.Vector3)rt.rect.center)).x;
                n++;
            }
            string how;
            var p = ft.localPosition;
            if (n > 0) { p.x = sum / n; how = $"under the middle of the texts ({n})"; }
            else if ((object)paid != null && paid.Pointer != IntPtr.Zero)
            {
                p.x = (home + parent.InverseTransformPoint(paid.transform.position).x) / 2f;
                how = "half way to the paid button";
            }
            else return;
            ft.localPosition = p;
            if (!placedNoted) { placedNoted = true; RevivalMod.Log.Msg($"[draft] free button moved from x {home:0} to {p.x:0}, {how}"); }
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
