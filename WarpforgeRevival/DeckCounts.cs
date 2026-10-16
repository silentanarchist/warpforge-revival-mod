using System;
using HarmonyLib;
using Il2Cpp;

namespace WarpforgeRevival
{
    /// <summary>
    /// Deck editor > Deck info: the Troops and Stratagems lines show their counts again ("Troops: 7").
    /// The game still fills them in (DeckTypeCounterDrawer.Initialize, with each language's own word),
    /// but both labels also carry the translation component, which rewrites them to the bare word
    /// whenever the Deck info panel is shown - after the counts were written while it was hidden.
    /// Switching that component off on those two labels keeps the game's own text.
    /// </summary>
    internal static class DeckCounts
    {
        private static bool logged;

        [HarmonyPatch(typeof(DeckTypeCounterDrawer), nameof(DeckTypeCounterDrawer.Initialize))]
        private static class Counts
        {
            private static void Postfix(DeckTypeCounterDrawer __instance)
            {
                try
                {
                    foreach (var label in new[] { __instance.troopCount, __instance.stratagemsCount })
                    {
                        if ((object)label == null) continue;
                        var loc = label.GetComponent<Il2CppI2.Loc.Localize>();
                        if ((object)loc != null && loc.enabled) loc.enabled = false;
                    }
                    if (!logged)
                    {
                        logged = true;
                        RevivalMod.Log.Msg($"[deck] counts shown: \"{__instance.troopCount?.text}\", \"{__instance.stratagemsCount?.text}\"");
                    }
                }
                catch (Exception e) { RevivalMod.Log.Warning("[deck] counts: " + e.Message); }
            }
        }
    }
}
