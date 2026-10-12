using System;
using HarmonyLib;
using Il2Cpp;

namespace WarpforgeRevival
{
    /// <summary>
    /// Draft against the AI. When a Draft search ends against a bot, the match is a ClosedDeckBot match,
    /// and the game (MatchData.GetBotDeck) always gives that bot a deck from the Skirmish bot list
    /// (AIBotsConfig "BotListFastMode"; Everguild's own Draft was a Skirmish mode). The revival's Draft
    /// plays Classic, so the bot is given a deck from the Classic bot list instead, the same list
    /// Classic (Unranked/Ranked) searches use. Everguild's timed alliance drafts (TimedDraftEvent) keep
    /// the game's own pick, which matches the bot to the draft's armies.
    /// </summary>
    [HarmonyPatch(typeof(MatchData), nameof(MatchData.GetBotDeck))]
    internal static class DraftBotDeck
    {
        private static bool inside;

        private static bool Prefix(MatchData __instance, MatchType matchType, ref PrebuiltDeck __result)
        {
            if (inside || matchType != MatchType.ClosedDeckBot) return true;
            try
            {
                var ev = __instance.CurrentEvent;
                if ((object)ev != null && (object)ev.TryCast<TimedDraftEvent>() != null) return true;
                inside = true;
                var deck = __instance.GetBotDeck(MatchType.UnrankedBot);
                if ((object)deck == null)
                {
                    RevivalMod.Log.Msg("[draft] no Classic bot deck found; keeping the game's own pick");
                    return true;
                }
                RevivalMod.Log.Msg($"[draft] AI opponent gets the Classic bot deck '{deck.deckName}'");
                __result = deck;
                return false;
            }
            catch (Exception e)
            {
                RevivalMod.Log.Warning("[draft] AI deck: " + e.Message);
                return true;
            }
            finally { inside = false; }
        }
    }
}
