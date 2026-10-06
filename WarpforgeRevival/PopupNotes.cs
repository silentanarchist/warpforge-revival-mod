using System;
using HarmonyLib;
using Il2Cpp;

namespace WarpforgeRevival
{
    /// <summary>
    /// Notes every popup the game shows in the log, with the text (or text key) it was given.
    /// A popup that comes up blank is one whose wording is missing; the key in the log says which,
    /// so the wording can be supplied from the server's text-terms.json.
    /// </summary>
    internal static class PopupNotes
    {
        [HarmonyPatch(typeof(WindowsManager), nameof(WindowsManager.LoadPopUpAndShow))]
        private static class Shown
        {
            private static void Prefix(string text, bool localizeTexts, Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<GameWindowButton> gameWindowButton)
            {
                try
                {
                    string shown = text;
                    if (localizeTexts && !string.IsNullOrEmpty(text))
                    {
                        try { shown = Il2CppI2.Loc.LocalizationManager.GetTranslation(text, true, 0, true, false, null, null, true); } catch { }
                    }
                    int buttons = (object)gameWindowButton != null ? gameWindowButton.Length : 0;
                    RevivalMod.Log.Msg($"[popup] text {(text == null ? "null" : "'" + text + "'")}, {(localizeTexts ? "a text key" : "literal text")}, {buttons} button(s)" +
                                       (localizeTexts ? $"; wording found: {(string.IsNullOrEmpty(shown) ? "NONE (popup will be blank)" : "'" + shown + "'")}" : ""));
                }
                catch (Exception e) { RevivalMod.Log.Warning("[popup] " + e.Message); }
            }
        }
    }
}
