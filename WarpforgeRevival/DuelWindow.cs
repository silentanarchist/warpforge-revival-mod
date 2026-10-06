using System;
using HarmonyLib;
using Il2Cpp;
using UnityEngine;

namespace WarpforgeRevival
{
    /// <summary>
    /// The "challenge a friend" window has one button per game mode. Their captions come from
    /// translations that are no longer available, so the buttons show up blank: name them here.
    /// </summary>
    internal static class DuelWindow
    {
        [HarmonyPatch(typeof(DuelPopupWindow), nameof(DuelPopupWindow.Open))]
        private static class Opened
        {
            private static void Postfix(DuelPopupWindow __instance)
            {
                try
                {
                    Name(__instance.classicButton, "Classic");
                    Name(__instance.skirmishButton, "Skirmish");
                }
                catch (Exception e) { RevivalMod.Log.Warning("[duel] " + e.Message); }
            }
        }

        private static void Name(EverguildButton button, string caption)
        {
            if ((object)button == null || button.Pointer == IntPtr.Zero) return;
            var labels = button.GetComponentsInChildren<Il2CppTMPro.TMP_Text>(true);
            if (labels == null || labels.Length == 0)
            {
                RevivalMod.Log.Msg($"[duel] the {caption} button has no caption to fill in");
                return;
            }
            foreach (var label in labels)
            {
                if ((object)label == null) continue;
                var loc = label.GetComponent<Il2CppI2.Loc.Localize>();
                if ((object)loc != null) loc.enabled = false;    // otherwise it blanks the text again
                label.text = caption;
            }
        }
    }
}
