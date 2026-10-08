using System;
using HarmonyLib;
using Il2Cpp;
using UnityEngine;

namespace WarpforgeRevival
{
    /// <summary>
    /// Offence cards (the ones the player going first picks from at the start of a battle) are
    /// marked "hide" for Collection > Cards, so nobody can read them outside a battle. Defence cards
    /// are shown there; Offence cards now are too. They still cannot go into a deck, so the deck
    /// editor's card list leaves them out.
    /// </summary>
    internal static class OffenceInCollection
    {
        private static bool noted;

        private static bool IsOffence(RawCardScript c) =>
            (object)c != null && c.spellType == SpellType.OffensiveCard && c.inventoryOptions == CardInventoryOptions.CantAddToDeck;

        /// <summary>Marks the real Offence cards "show" (their internal helper cards stay hidden).</summary>
        private static void Reveal()
        {
            int changed = 0;
            var all = Resources.FindObjectsOfTypeAll<RawCardScript>();
            if (all == null) return;
            for (int i = 0; i < all.Length; i++)
            {
                var c = all[i];
                if (!IsOffence(c) || c.showInCollectionOptions != ShowInCollectionOptions.Hide) continue;
                c.showInCollectionOptions = ShowInCollectionOptions.Show;
                changed++;
            }
            if (changed > 0 || !noted)
            {
                noted = true;
                RevivalMod.Log.Msg($"[collection] Offence cards shown in the collection: {changed} marked ({all.Length} cards looked at)");
            }
        }

        [HarmonyPatch(typeof(CardCollectionTab), nameof(CardCollectionTab.GetCollection))]
        private static class Listing
        {
            private static void Prefix()
            {
                try { Reveal(); }
                catch (Exception e) { RevivalMod.Log.Warning("[collection] " + e.Message); }
            }
        }

        [HarmonyPatch(typeof(DeckEditingWindow), nameof(DeckEditingWindow.GetCardCollection))]
        private static class Editor
        {
            private static void Postfix(Il2CppSystem.Collections.Generic.List<RawCardScript> __result)
            {
                try
                {
                    if (__result == null) return;
                    for (int i = __result.Count - 1; i >= 0; i--)
                        if (IsOffence(__result[i])) __result.RemoveAt(i);
                }
                catch (Exception e) { RevivalMod.Log.Warning("[collection] " + e.Message); }
            }
        }
    }
}
