using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using Il2CppEverguild.LiveOps;

namespace WarpforgeRevival
{
    /// <summary>
    /// The AI's deck (owner, 2026-10-16: "a true fix"; replaces DraftBotDeck.cs from 0.12.x-0.13.43).
    ///
    /// How the game picks it (decompiled, Windows build; see the project notes "game-ai-notes"):
    ///  - MatchData.GetBotDeck(matchType) chooses one of the server's AIBotsConfig bot lists: BotList (the Classic
    ///    list, 30-card decks) for Ranked / RankedBot / Unranked / UnrankedBot, BotListFastMode (the Skirmish list,
    ///    12-card decks) for FastMode / FastModeBot / FastModeRanked / ClosedDeck / ClosedDeckBot. Tutorial, campaign
    ///    and practice decks come from elsewhere (practice: PracticeMenu.AiDeck).
    ///  - It then calls PrebuiltDeckCollection.GetRandomDeck(list, possibleArmies, currentEvent), which goes through
    ///    every prebuilt deck in the game and is meant to keep those on the list (and of the given armies, without
    ///    hidden or banned cards). But the list check only runs while FeatureConfig "disableNewMatchMaking" is off,
    ///    and the revival's title data (like Everguild's) has it on, so the AI played any prebuilt deck in the game.
    ///    The flag also steers matchmaking and the AI's play, so it stays on.
    ///
    /// The fix, in two parts:
    ///  1. GetRandomDeck always keeps the list check (the root cause), for every caller.
    ///  2. Against the AI (UnrankedBot, RankedBot, FastModeBot, ClosedDeckBot), the list follows the mode's deck size (GameplayData.deckSize from the server's game modes):
    ///     a deck of exactly that size, from the mode's own list first, then the other list. With no deck of that
    ///     size (today: 60-card Custom Test / Long Game), the Classic list is used. Classic Draft (30) therefore gets
    ///     Classic bot decks and a Skirmish Draft (12) would get Skirmish ones; a 60-card bot deck added to a bot list
    ///     on the server is used for 60-card modes without a mod change.
    ///  Everguild's timed alliance drafts (TimedDraftEvent, armies given) keep the game's own army-matched pick.
    /// </summary>
    internal static class BotDecks
    {
        private static readonly System.Random Dice = new System.Random();

        // ---------------------------------------------------------------- 1. the list check, always

        [HarmonyPatch(typeof(PrebuiltDeckCollection), nameof(PrebuiltDeckCollection.GetRandomDeck))]
        private static class RandomDeck
        {
            private static bool Prefix(PrebuiltDeckCollection __instance, Il2CppSystem.Collections.Generic.List<LoadableReference<PrebuiltDeck>> selectableBotDecks,
                                       Il2CppSystem.Collections.Generic.IReadOnlyCollection<CardArmy> possibleArmies, IPlayEvent currentEvent, ref PrebuiltDeck __result)
            {
                try
                {
                    // an army-limited pick (timed alliance drafts) is left to the game
                    if ((object)possibleArmies != null && possibleArmies.Count > 0) return true;
                    var pool = Pool(__instance, selectableBotDecks, currentEvent);
                    if (pool.Count == 0) return true;                 // nothing on the list: the game's own pick
                    __result = pool[Dice.Next(pool.Count)];
                    return false;
                }
                catch (Exception e)
                {
                    RevivalMod.Log.Warning("[bot deck] list check: " + e.Message);
                    return true;
                }
            }
        }

        /// <summary>The prebuilt decks on a bot list, without hidden or banned cards (the game's own GetRandomDeck rules).</summary>
        private static List<PrebuiltDeck> Pool(PrebuiltDeckCollection collection, Il2CppSystem.Collections.Generic.List<LoadableReference<PrebuiltDeck>> refs, IPlayEvent ev)
        {
            var pool = new List<PrebuiltDeck>();
            var decks = (object)collection == null ? null : collection.decks;
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
                    if (Size(d) <= 0 || d.HasHiddenCards() || d.DeckHasBannedCards(ev)) continue;
                    pool.Add(d);
                }
                catch { }
            }
            return pool;
        }

        // ---------------------------------------------------------------- 2. the list follows the mode's deck size

        [HarmonyPatch(typeof(MatchData), nameof(MatchData.GetBotDeck))]
        private static class BotDeck
        {
            // practice matches: the AI's deck follows the Classic/Skirmish switch (PracticeMenu)
            private static void Postfix(MatchType matchType, ref PrebuiltDeck __result) => PracticeMenu.AiDeck.Postfix(matchType, ref __result);

            private static bool Prefix(MatchData __instance, MatchType matchType, ref PrebuiltDeck __result)
            {
                bool skirmishList = matchType == MatchType.FastModeBot || matchType == MatchType.ClosedDeckBot;
                if (!skirmishList && matchType != MatchType.UnrankedBot && matchType != MatchType.RankedBot) return true;
                string tag = matchType == MatchType.ClosedDeckBot ? "[draft]" : "[bot deck]";
                try
                {
                    var ev = __instance.CurrentEvent;
                    if ((object)ev != null && (object)ev.TryCast<TimedDraftEvent>() != null) return true;
                    var rules = __instance.GameplayData;
                    int want = (object)rules == null ? 0 : rules.deckSize;
                    if (want <= 0)
                    {
                        RevivalMod.Log.Msg($"{tag} the mode's deck size is not known; the game picks the AI's deck from its own list");
                        return true;
                    }
                    var collection = PlayerDataManager.singletonManager?.PrebuiltDeckCollection;
                    if ((object)collection == null) return true;
                    var classic = Pool(collection, Decks(__instance.GetNormalBotConfig), ev);
                    var skirmish = Pool(collection, Decks(__instance.GetFastModeBotConfig), ev);
                    var own = skirmishList ? skirmish : classic;
                    var other = skirmishList ? classic : skirmish;
                    string ownName = skirmishList ? "Skirmish" : "Classic", otherName = skirmishList ? "Classic" : "Skirmish";

                    var pick = own.FindAll(d => Size(d) == want);
                    string from = ownName;
                    if (pick.Count == 0) { pick = other.FindAll(d => Size(d) == want); from = otherName; }
                    if (pick.Count == 0) { pick = classic; from = "Classic (no " + want + "-card bot deck on either list)"; }
                    if (pick.Count == 0)
                    {
                        RevivalMod.Log.Msg($"{tag} no bot decks on the lists; the game picks the AI's deck");
                        return true;
                    }
                    var deck = pick[Dice.Next(pick.Count)];
                    RevivalMod.Log.Msg($"{tag} {want}-card {matchType}: the AI plays '{deck.deckId}' ({Size(deck)} cards) from the {from} list ({pick.Count} to choose from)");
                    __result = deck;
                    return false;
                }
                catch (Exception e)
                {
                    RevivalMod.Log.Warning($"{tag} AI deck: " + e.Message);
                    return true;
                }
            }
        }

        private static Il2CppSystem.Collections.Generic.List<LoadableReference<PrebuiltDeck>> Decks(Func<AIBotsConfig.BotConfig> get)
        {
            try { var c = get(); return (object)c == null ? null : c.GetBotDecks(); } catch { return null; }
        }

        private static int Size(PrebuiltDeck deck)
        {
            if ((object)deck == null || deck.m_CachedPtr == IntPtr.Zero) return 0;
            var ids = deck.cardLibraryIds;
            return ids == null ? 0 : ids.Count;
        }
    }
}
