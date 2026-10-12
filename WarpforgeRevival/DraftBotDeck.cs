using System;
using HarmonyLib;
using Il2Cpp;

namespace WarpforgeRevival
{
    /// <summary>
    /// Draft against the AI. When a Draft search ends against a bot, the match is a ClosedDeckBot match,
    /// and the game (MatchData.GetBotDeck) always gives that bot a deck from the Skirmish bot list
    /// (AIBotsConfig "BotListFastMode"), because Everguild's own Draft was a Skirmish mode.
    ///
    /// The revival's Draft modes set their own rules on the server (the event's gameplayVariables), so
    /// the bot's deck follows the deck size of the Draft being played: decks are drawn from both
    /// bot lists (Classic, the one Classic searches use, and Skirmish) and the one whose size is
    /// nearest the mode's deck size is used. A Classic draft (30 cards) gets a Classic bot deck, a
    /// Skirmish draft (12 cards) a Skirmish one, and a Draft with other deck rules the nearest fit,
    /// with nothing to change in the mod when such a mode is added.
    /// Everguild's timed alliance drafts (TimedDraftEvent) keep the game's own army-matched pick.
    /// </summary>
    [HarmonyPatch(typeof(MatchData), nameof(MatchData.GetBotDeck))]
    internal static class DraftBotDeck
    {
        private const int TriesPerList = 6;     // picks are random; a few from each list to find the best fit
        private static bool inside;

        private static bool Prefix(MatchData __instance, MatchType matchType, ref PrebuiltDeck __result)
        {
            if (inside || matchType != MatchType.ClosedDeckBot) return true;
            try
            {
                var ev = __instance.CurrentEvent;
                if ((object)ev != null && (object)ev.TryCast<TimedDraftEvent>() != null) return true;
                var rules = __instance.GameplayData;
                int want = (object)rules == null ? 0 : rules.deckSize;
                if (want <= 0)
                {
                    RevivalMod.Log.Msg("[draft] this Draft's deck size is not known; the AI keeps the game's own deck");
                    return true;
                }
                inside = true;
                PrebuiltDeck best = null;
                int bestGap = int.MaxValue;
                string bestList = null;
                foreach (var (type, list) in new[] { (MatchType.UnrankedBot, "Classic"), (MatchType.ClosedDeckBot, "Skirmish") })
                {
                    for (int i = 0; i < TriesPerList && bestGap > 0; i++)
                    {
                        var deck = __instance.GetBotDeck(type);
                        int size = Size(deck);
                        if (size <= 0) continue;
                        int gap = Math.Abs(size - want);
                        if (gap < bestGap) { best = deck; bestGap = gap; bestList = list; }
                    }
                    if (bestGap == 0) break;
                }
                if ((object)best == null)
                {
                    RevivalMod.Log.Msg("[draft] no bot deck found; the AI keeps the game's own deck");
                    return true;
                }
                RevivalMod.Log.Msg($"[draft] {want}-card Draft: the AI plays the {bestList} bot deck '{best.deckName}' ({Size(best)} cards)");
                __result = best;
                return false;
            }
            catch (Exception e)
            {
                RevivalMod.Log.Warning("[draft] AI deck: " + e.Message);
                return true;
            }
            finally { inside = false; }
        }

        private static int Size(PrebuiltDeck deck)
        {
            if ((object)deck == null || deck.m_CachedPtr == IntPtr.Zero) return 0;
            var ids = deck.cardLibraryIds;
            return ids == null ? 0 : ids.Count;
        }
    }
}
