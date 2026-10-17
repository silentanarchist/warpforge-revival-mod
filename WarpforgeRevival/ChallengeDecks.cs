using System;
using HarmonyLib;
using Il2Cpp;
using Il2CppEverguild.LiveOps;

namespace WarpforgeRevival
{
    /// <summary>
    /// Friend challenges use the deck the game remembers for the mode (Classic or Skirmish). Picking a
    /// Custom Test (Long Game) deck on its page sets the deck remembered for Classic too, because
    /// Custom Test decks pose as Classic decks there (LongGame), and the game's own check then accepts
    /// it by its own Custom Test rules: a 60-card deck turned up in a Classic challenge (2026-10-16).
    /// Both ways into a challenge match are checked here: the challenger's (TryChallengeMatchStart,
    /// called from ChallengeRules' hook) and the friend's accept. A Custom Test deck stops the match
    /// with a popup saying what to do; the Long Game page state is cleared first so the game looks up
    /// the real Classic mode, not Custom Test.
    /// </summary>
    internal static class ChallengeDecks
    {
        private static string Message => Loc.T("challenge.customTestDeck", "Your deck for this challenge is a Custom Test deck, which cannot be used in a Classic match. Open Classic, pick one of your Classic decks, then try the challenge again.");

        /// <summary>False (and a popup) when the deck the game would use for this challenge is a Custom Test deck.</summary>
        internal static bool DeckOk(IPlayEvent ev, string where)
        {
            LongGame.LeavePage();
            try
            {
                if ((object)ev == null || LongGame.IsLongEvent(ev)) return true;
                var deck = ev.GetEventDeck();
                if ((object)deck == null || !LongGame.IsStoredLong(deck)) return true;
                RevivalMod.Log.Warning($"[challenge] {where}: the deck for {ev.GetBaseData()?.eventId} is a Custom Test deck (\"{deck.deckName}\"); match not started");
                try { WindowsManager.Instance?.ShowPopUp(Message, false, true, Loc.Game("MainMenu/General/OK", "OK"), (Il2CppSystem.Action)null); } catch { }
                return false;
            }
            catch (Exception e) { RevivalMod.Log.Warning("[challenge] deck check: " + e.Message); return true; }
        }

        [HarmonyPatch(typeof(ChallengeManager), nameof(ChallengeManager.AcceptPracticeChallenge))]
        private static class Accept
        {
            private static bool Prefix(ChallengeManager __instance)
            {
                try
                {
                    LongGame.LeavePage();
                    var gm = LiveOpsManager.GetHandler<GameModes>();
                    var ev = gm?.GetActiveEvent(__instance.isClassic ? PlayModes.Classic : PlayModes.Skirmish);
                    return DeckOk(ev, "accepting");
                }
                catch (Exception e) { RevivalMod.Log.Warning("[challenge] accept deck check: " + e.Message); return true; }
            }
        }
    }
}
