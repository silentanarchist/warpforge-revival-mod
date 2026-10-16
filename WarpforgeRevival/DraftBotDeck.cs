using System;
using HarmonyLib;
using Il2Cpp;

namespace WarpforgeRevival
{
    /// <summary>
    /// The AI's deck fits the mode being played (MatchData.GetBotDeck).
    ///  - Draft against the AI (ClosedDeckBot): the game always gives that bot a deck from the Skirmish bot list
    ///    (AIBotsConfig "BotListFastMode"), because Everguild's own Draft was a Skirmish mode. The revival's Draft
    ///    modes set their own deck size, so the bot's deck is the one, from either list, nearest that size.
    ///  - Skirmish and Classic against the AI (FastModeBot, UnrankedBot; owner, 2026-10-16, replay GYJJ-9K55): in a
    ///    player's first games of a mode the game is in its "first games" state and takes the bot's deck from the
    ///    normal (Classic) list whatever the mode, so a 12-card Skirmish player met a 30-card Classic AI. When the
    ///    game's pick does not fit the mode's deck size, a deck of the nearest size is drawn from the bot lists.
    /// Decks are drawn straight from the server's two bot lists (AIBotsConfig BotList and BotListFastMode), so the
    /// "first games" state cannot send it to the wrong list. Everguild's timed alliance drafts keep the game's own pick.
    /// </summary>
    [HarmonyPatch(typeof(MatchData), nameof(MatchData.GetBotDeck))]
    internal static class DraftBotDeck
    {
        private const int TriesPerList = 6;     // picks are random; a few from each list to find the best fit
        private static bool inside;

        private static bool Prefix(MatchData __instance, MatchType matchType, ref PrebuiltDeck __result)
        {
            if (inside) return true;
            bool draft = matchType == MatchType.ClosedDeckBot;
            if (!draft && matchType != MatchType.FastModeBot && matchType != MatchType.UnrankedBot) return true;
            string tag = draft ? "[draft]" : "[bot deck]";
            try
            {
                var ev = __instance.CurrentEvent;
                if ((object)ev != null && (object)ev.TryCast<TimedDraftEvent>() != null) return true;
                var rules = __instance.GameplayData;
                int want = (object)rules == null ? 0 : rules.deckSize;
                if (want <= 0)
                {
                    RevivalMod.Log.Msg($"{tag} this mode's deck size is not known; the AI keeps the game's own deck");
                    return true;
                }
                inside = true;
                if (!draft)
                {
                    // the game's own pick first; only replaced when it does not fit
                    var own = __instance.GetBotDeck(matchType);
                    int ownSize = Size(own);
                    if (ownSize == want) { __result = own; return false; }
                    RevivalMod.Log.Msg($"{tag} the game picked '{(own == null ? "none" : own.deckName)}' ({ownSize} cards) for a {want}-card {matchType} match; picking one that fits");
                    var fit = Nearest(__instance, want, out string fitList);
                    if ((object)fit == null || Math.Abs(Size(fit) - want) >= Math.Abs(ownSize - want))
                    {
                        if ((object)own == null) return true;
                        __result = own;
                        return false;
                    }
                    RevivalMod.Log.Msg($"{tag} {want}-card {matchType}: the AI plays the {fitList} bot deck '{fit.deckName}' ({Size(fit)} cards)");
                    __result = fit;
                    return false;
                }
                var best = Nearest(__instance, want, out string bestList);
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
                RevivalMod.Log.Warning($"{tag} AI deck: " + e.Message);
                return true;
            }
            finally { inside = false; }
        }

        /// <summary>The bot deck, from either of the server's bot lists, whose size is nearest want (null if none).</summary>
        private static PrebuiltDeck Nearest(MatchData match, int want, out string listName)
        {
            listName = null;
            PrebuiltDeck best = null;
            int bestGap = int.MaxValue;
            var collection = PlayerDataManager.singletonManager?.PrebuiltDeckCollection;
            if ((object)collection == null) return null;
            foreach (var (name, pick) in new (string, Func<AIBotsConfig.BotConfig>)[] { ("Classic", match.GetNormalBotConfig), ("Skirmish", match.GetFastModeBotConfig) })
            {
                Il2CppSystem.Collections.Generic.List<LoadableReference<PrebuiltDeck>> decks = null;
                try { var config = pick(); decks = (object)config == null ? null : config.GetBotDecks(); } catch { }
                if (decks == null || decks.Count == 0) continue;
                for (int i = 0; i < TriesPerList && bestGap > 0; i++)
                {
                    PrebuiltDeck deck = null;
                    try { deck = collection.GetRandomDeck(decks, null, null); } catch { }
                    int size = Size(deck);
                    if (size <= 0) continue;
                    int gap = Math.Abs(size - want);
                    if (gap < bestGap) { best = deck; bestGap = gap; listName = name; }
                }
                if (bestGap == 0) break;
            }
            return best;
        }
        private static int Size(PrebuiltDeck deck)
        {
            if ((object)deck == null || deck.m_CachedPtr == IntPtr.Zero) return 0;
            var ids = deck.cardLibraryIds;
            return ids == null ? 0 : ids.Count;
        }
    }
}
