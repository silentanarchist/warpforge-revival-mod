using System;
using HarmonyLib;
using Il2Cpp;

namespace WarpforgeRevival
{
    /// <summary>
    /// Per-match options (0.13.24).
    ///
    /// Offence cards: each game mode has its own switch on the server ("offenseCards" in its
    /// gameplayVariables, sent to the mod as "offenseCardModes"). As a battle starts the pick step
    /// (BattlefieldCards) is switched on or off for it: a friend challenge with custom rules uses its
    /// rules' choice, a replay the choice saved with it (older replays: their mode's current switch),
    /// anything else its game mode's switch (a mode the server does not list has none).
    ///
    /// Who goes first: a friend challenge's rules may say the challenger or the friend challenged goes
    /// first ("firstPlayer" 1 or 2; 0 leaves it to the game: the warlord with the higher initiative,
    /// then a coin toss). Both games work it out the same way - the challenger's game from its side,
    /// the friend's from theirs - after the game's own decision
    /// (SupportMethods.GetPlayerGoesFirstWithInitiative), and the forced choice is also written on the
    /// match (forceFirstPlayer), which the game saves with the recording, so the replay starts the same.
    /// </summary>
    internal static class MatchOptions
    {
        private static bool forcedFirst;
        /// <summary>The match being played had who goes first set by a challenge's rules (saved with its replay).</summary>
        internal static bool ForcedFirst => forcedFirst;

        internal static void BattleStarting(BattleManager battle)
        {
            forcedFirst = false;
            try
            {
                bool replay = Replays.IsReplay(battle);
                var md = battle.matchData;
                bool on;
                string why;
                int? rule = ChallengeRules.Offence;
                if (rule != null)
                {
                    on = rule == 1;
                    why = ChallengeRules.ActiveIsReplay ? "saved with the replay" : "the challenge's rules";
                }
                else
                {
                    string ev = ChallengeRules.RulesEvent ?? ((object)md == null ? null : Replays.EventOf(md)) ?? LongGame.PlayingEventId();
                    on = ServerSettings.OffenceFor(ev);
                    why = "game mode " + (ev ?? "unknown") + (replay ? " (replay without a saved choice)" : "");
                }
                BattlefieldCards.Set(on);
                RevivalMod.Log.Msg($"[match] Offence cards {(on ? "on" : "off")} for this {(replay ? "replay" : "match")} ({why})" +
                                   (BattlefieldCards.PickOn != on ? " - could not be switched on this device" : ""));
            }
            catch (Exception e) { RevivalMod.Log.Warning("[match] Offence cards for this match: " + e.Message); }
        }

        [HarmonyPatch(typeof(SupportMethods), nameof(SupportMethods.GetPlayerGoesFirstWithInitiative))]
        private static class WhoGoesFirst
        {
            private static void Postfix(MatchData matchData, ref bool __result)
            {
                try
                {
                    if ((object)matchData == null || !ChallengeRules.Active) return;
                    if (ChallengeRules.ActiveIsReplay)
                    {
                        // the recording carries the match's forceFirstPlayer (1 this player, 2 the opponent)
                        int saved = matchData.forceFirstPlayer;
                        if (!ChallengeRules.ReplayForcedFirst || (saved != 1 && saved != 2)) return;
                        bool was = saved == 1;
                        if (__result != was) RevivalMod.Log.Msg($"[match] replay: {(was ? "you" : "the opponent")} went first in the recorded match (challenge rules)");
                        __result = was;
                        return;
                    }
                    int fp = ChallengeRules.FirstPlayer;
                    if ((fp != 1 && fp != 2) || matchData.playMode != PlayModes.Duel) return;
                    bool me = ChallengeRules.ActiveIsChallenger ? fp == 1 : fp == 2;
                    matchData.forceFirstPlayer = me ? 1 : 2;
                    forcedFirst = true;
                    RevivalMod.Log.Msg($"[match] challenge rules: {(fp == 1 ? "the challenger" : "the friend challenged")} goes first - " +
                                       $"{(me ? "you" : "your opponent")} (the game had picked {(__result ? "you" : "your opponent")})");
                    __result = me;
                }
                catch (Exception e) { RevivalMod.Log.Warning("[match] who goes first: " + e.Message); }
            }
        }
    }
}
