using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;

namespace WarpforgeRevival
{
    /// <summary>
    /// Some installed prebuilt decks use cards the revival card bundle does not have yet
    /// (e.g. the Space Wolves decks). The game assumes every deck resolves completely and
    /// throws while building its deck lists, which leaves screens such as "Select a deck to
    /// play against" empty. Keep those decks out of the game's deck collection.
    /// (Patched method verified to have unique native code.)
    /// </summary>
    internal static class PrebuiltDecks
    {
        private static HashSet<string> known;
        private static int knownCount = -1;
        private static int skipped;

        private static HashSet<string> KnownCards()
        {
            var all = PlayerDataManager.singletonManager?.allCardCollection;
            if (all == null) return null;
            if (known == null || all.Count != knownCount)
            {
                known = new HashSet<string>();
                for (int i = 0; i < all.Count; i++)
                {
                    var c = all[i];
                    if ((object)c != null && !string.IsNullOrEmpty(c.uniqueId)) known.Add(c.uniqueId);
                }
                knownCount = all.Count;
            }
            return known;
        }

        [HarmonyPatch(typeof(PrebuiltDeckCollection), nameof(PrebuiltDeckCollection.AddDeck))]
        private static class AddDeck
        {
            private static bool Prefix(PrebuiltDeck deck)
            {
                try
                {
                    if ((object)deck == null) return true;
                    var cards = KnownCards();
                    if (cards == null || cards.Count == 0) return true;
                    string missing = null;
                    string hero = deck.deckHero?.targetId;
                    if (!string.IsNullOrEmpty(hero) && !cards.Contains(hero)) missing = hero;
                    var ids = deck.cardLibraryIds;
                    for (int i = 0; missing == null && ids != null && i < ids.Count; i++)
                        if (!cards.Contains(ids[i])) missing = ids[i];
                    if (missing == null) return true;
                    if (skipped++ < 3) RevivalMod.Log.Msg($"[decks] leaving out prebuilt deck '{deck.deckId}' (card {missing} is not rebuilt yet)");
                    else if (skipped == 4) RevivalMod.Log.Msg("[decks] ... more prebuilt decks left out for the same reason");
                    return false;
                }
                catch (Exception e)
                {
                    RevivalMod.Log.Warning("[decks] could not check prebuilt deck: " + e.Message);
                    return true;
                }
            }
        }
    }
}
