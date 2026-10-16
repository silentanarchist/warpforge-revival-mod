using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using Il2CppEverguild.LiveOps;

namespace WarpforgeRevival
{
    /// <summary>
    /// The AI's deck comes from the right bot list and fits the mode (MatchData.GetBotDeck).
    ///
    /// How the game picks (decompiled, 2026-10-16): GetBotDeck takes a bot list from the server's AIBotsConfig
    /// (BotList for Classic, BotListFastMode for Skirmish and Draft) and calls PrebuiltDeckCollection.GetRandomDeck,
    /// which goes through every prebuilt deck in the game and keeps those on that list, without hidden or banned
    /// cards, then picks one at random. But the list check is skipped when the title data's
    /// FeatureConfig "disableNewMatchMaking" is on, as it is on the revival server (and was on Everguild's): the AI
    /// then plays any of the game's prebuilt decks, Classic or Skirmish, starter and tutorial decks included. So a
    /// 12-card Skirmish player met a 30-card Classic AI (replay GYJJ-9K55). The flag also steers matchmaking and
    /// the AI's play, so it is left on and the deck is picked here instead.
    ///
    /// Here: Skirmish (FastModeBot), Classic (UnrankedBot) and Draft (ClosedDeckBot) against the AI get a deck
    /// from the bot lists themselves, of the mode's deck size (GameplayData.deckSize): the mode's own list first,
    /// then the other list, then the nearest size (Long Game, 60 cards, gets a Classic deck). Hidden and banned
    /// decks are left out as the game does. Everguild's timed alliance drafts keep the game's own pick.
    /// </summary>
    [HarmonyPatch(typeof(MatchData), nameof(MatchData.GetBotDeck))]
    internal static class DraftBotDeck
    {
        private static readonly System.Random Rng = new System.Random();

        private static bool Prefix(MatchData __instance, MatchType matchType, ref PrebuiltDeck __result)
        {
            bool fast = matchType == MatchType.FastModeBot || matchType == MatchType.ClosedDeckBot;
            if (!fast && matchType != MatchType.UnrankedBot) return true;
            string tag = matchType == MatchType.ClosedDeckBot ? "[draft]" : "[bot deck]";
            try
            {
                var ev = __instance.CurrentEvent;
                if ((object)ev != null && (object)ev.TryCast<TimedDraftEvent>() != null) return true;
                var rules = __instance.GameplayData;
                int want = (object)rules == null ? 0 : rules.deckSize;
                if (want <= 0)
                {
                    RevivalMod.Log.Msg($"{tag} this mode's deck size is not known; the AI keeps the game's own pick");
                    return true;
                }
                var collection = PlayerDataManager.singletonManager?.PrebuiltDeckCollection;
                if ((object)collection == null) return true;
                var classic = Pool(collection, Config(__instance.GetNormalBotConfig), ev);
                var skirmish = Pool(collection, Config(__instance.GetFastModeBotConfig), ev);
                var own = fast ? skirmish : classic;
                var other = fast ? classic : skirmish;
                string ownName = fast ? "Skirmish" : "Classic", otherName = fast ? "Classic" : "Skirmish";
                List<PrebuiltDeck> pick = own.FindAll(d => Size(d) == want);
                string from = ownName;
                if (pick.Count == 0) { pick = other.FindAll(d => Size(d) == want); from = otherName; }
                if (pick.Count == 0)
                {
                    var all = new List<PrebuiltDeck>(own); all.AddRange(other);
                    if (all.Count == 0)
                    {
                        RevivalMod.Log.Msg($"{tag} no bot decks found in the bot lists; the AI keeps the game's own pick");
                        return true;
                    }
                    int gap = int.MaxValue;
                    foreach (var d in all) gap = Math.Min(gap, Math.Abs(Size(d) - want));
                    pick = all.FindAll(d => Math.Abs(Size(d) - want) == gap);
                    from = "nearest-size";
                }
                var deck = pick[Rng.Next(pick.Count)];
                RevivalMod.Log.Msg($"{tag} {want}-card {matchType}: the AI plays the {from} bot deck '{deck.deckId}' ({Size(deck)} cards; {pick.Count} to choose from)");
                __result = deck;
                return false;
            }
            catch (Exception e)
            {
                RevivalMod.Log.Warning($"{tag} AI deck: " + e.Message);
                return true;
            }
        }

        private static AIBotsConfig.BotConfig Config(Func<AIBotsConfig.BotConfig> get)
        {
            try { return get(); } catch { return null; }
        }

        /// <summary>The game's prebuilt decks named on a bot list, without hidden or banned cards (as GetRandomDeck keeps them).</summary>
        private static List<PrebuiltDeck> Pool(PrebuiltDeckCollection collection, AIBotsConfig.BotConfig config, IPlayEvent ev)
        {
            var pool = new List<PrebuiltDeck>();
            var refs = (object)config == null ? null : config.GetBotDecks();
            var decks = collection.decks;
            if (refs == null || decks == null) return pool;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < refs.Count; i++)
            {
                try { var id = refs[i]?.TargetId; if (!string.IsNullOrEmpty(id)) ids.Add(id); } catch { }
            }
            for (int i = 0; i < decks.Count; i++)
            {
                var d = decks[i];
                try
                {
                    if ((object)d == null || d.m_CachedPtr == IntPtr.Zero || !ids.Contains(d.deckId ?? "")) continue;
                    if (d.HasHiddenCards() || d.DeckHasBannedCards(ev)) continue;
                    if (Size(d) > 0) pool.Add(d);
                }
                catch { }
            }
            return pool;
        }

        private static int Size(PrebuiltDeck deck)
        {
            if ((object)deck == null || deck.m_CachedPtr == IntPtr.Zero) return 0;
            var ids = deck.cardLibraryIds;
            return ids == null ? 0 : ids.Count;
        }
    }
}
