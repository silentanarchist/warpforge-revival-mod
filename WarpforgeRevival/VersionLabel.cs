using System;
using HarmonyLib;
using Il2Cpp;

namespace WarpforgeRevival
{
    /// <summary>Settings &gt; General shows the game version; the mod's version goes next to it.</summary>
    internal static class VersionLabel
    {
        private const string Mark = "Revival mod ";

        [HarmonyPatch(typeof(GeneralTab), nameof(GeneralTab.OnSetup))]
        private static class OnSetup
        {
            private static void Postfix(GeneralTab __instance)
            {
                try
                {
                    var label = __instance.versionText;
                    if ((object)label == null) return;
                    string text = label.text ?? "";
                    if (text.Contains(Mark)) return;
                    label.text = text + "  -  " + Mark + RevivalMod.Version;
                    RevivalMod.Log.Msg("[settings] version line: " + label.text);
                }
                catch (Exception e) { RevivalMod.Log.Warning("[settings] version line: " + e.Message); }
            }
        }
    }
}
