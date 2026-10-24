using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using Il2Cpp;

namespace WarpforgeRevival
{
    /// <summary>
    /// Board fingerprints for finding desyncs (0.15.0, owner 2026-10-23). Each game works the match out by itself and
    /// only moves travel between them; when the two come out differently (a match on 2026-10-23: a dying Apothecary
    /// still targetable on one side, gone on the other) the game only notices when a move points at nothing, and the
    /// match ends with a "desync". So as each game resolves an end of turn (BattleManager.ResolveEndTurn, which both
    /// games run for every turn in the same order) it writes its view of the board as a short list - every card in the
    /// match by its in-match number: card id, where it is (deck, hand, play, dying, cemetery, removed) and, for cards in
    /// play, health, max health, attacks and armour - and sends it with its SHA-256 to the server
    /// (Revival/MatchState). The server keeps both players' lists next to the match record and logs the first turn
    /// whose fingerprints differ; comparing the two lists for that turn shows which card the games disagree on.
    /// Only online matches (a match room), never replays. Nothing hidden from a player is sent: both games already
    /// know every card's number and place (the deck order is not part of the list). Logged as [state].
    /// </summary>
    internal static class MatchState
    {
        private static string room;
        private static int turn;
        private static int sentLogged;

        private static string Zone(CardStateOptions s)
        {
            switch (s)
            {
                case CardStateOptions.inDeck:
                case CardStateOptions.inDeckDrawing: return "D";
                case CardStateOptions.inPlay:
                case CardStateOptions.inPlayAttacking:
                case CardStateOptions.inPlayTransforming:
                case CardStateOptions.inPlayAminingAttack: return "P";
                case CardStateOptions.waitingToBePlayed: return "W";
                case CardStateOptions.waitingToDie: return "X";
                case CardStateOptions.inCemetery: return "C";
                case CardStateOptions.removedFromGame: return "R";
                default: return "H";            // in hand, drawing, showing, moving, being played, mulligan
            }
        }

        /// <summary>A card id in the characters the server takes (letters, digits, - _ .); anything else becomes _.</summary>
        private static string Clean(string id)
        {
            var sb = new StringBuilder(Math.Min(id.Length, 64));
            foreach (char ch in id)
            {
                if (sb.Length >= 64) break;
                sb.Append(((ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9')) || ch == '-' || ch == '_' || ch == '.' ? ch : '_');
            }
            return sb.ToString();
        }

        /// <summary>The board as one line of text, the same on both games when they agree.</summary>
        private static string Board(BattleCardManager manager)
        {
            var all = manager.GetAllCardsInGame();
            var rows = new SortedDictionary<int, string>();
            for (int i = 0; all != null && i < all.Count; i++)
            {
                var c = all[i];
                if ((object)c == null) continue;
                string id = Clean(c.rawCardData?.uniqueId ?? "?");
                string zone = Zone(c.cardState);
                string row = c.uniqueInGameID + ":" + id + ":" + zone;
                if (zone == "P" || zone == "X")
                {
                    int hp = c.currentHealth, max = c.currentMaxHealth, armour = c.currentArmourValue;
                    row += ":" + hp + "/" + max + ":" + c.CurrentMeleeAttack + "/" + c.CurrentRangeAttack + ":" + armour;
                }
                rows[c.uniqueInGameID] = row;
            }
            return string.Join(",", rows.Values);
        }

        [HarmonyPatch(typeof(BattleManager), nameof(BattleManager.ResolveEndTurn))]
        private static class EndOfTurn
        {
            private static void Prefix(BattleManager __instance)
            {
                try
                {
                    if ((object)__instance == null || Replays.IsReplay(__instance) || !PhotonNetwork.inRoom) return;
                    var r = PhotonNetwork.room;
                    string name = (object)r == null ? null : r.Name;
                    if (string.IsNullOrEmpty(name)) return;
                    if (name != room) { room = name; turn = 0; }
                    turn++;
                    var manager = UnityEngine.Object.FindObjectOfType<BattleCardManager>();
                    if ((object)manager == null) return;
                    string board = Board(manager);
                    if (board.Length > 12000) board = board.Substring(0, 12000);
                    string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(board))).ToLowerInvariant().Substring(0, 16);
                    int t = turn;
                    string json = Net.Json(w =>
                    {
                        w.WriteString("room", name);
                        w.WriteNumber("turn", t);
                        w.WriteString("hash", hash);
                        w.WriteString("board", board);
                    });
                    if (sentLogged < 3) { sentLogged++; RevivalMod.Log.Msg($"[state] end of turn {t}: board fingerprint {hash} ({board.Length} characters) sent to the server"); }
                    System.Threading.Tasks.Task.Run(async () =>
                    {
                        var reply = await Net.PostJson("/playfab/Revival/MatchState", json, TimeSpan.FromSeconds(15));
                        if (!reply.Ok && sentLogged < 6) { sentLogged++; RevivalMod.Log.Warning("[state] the server did not take the board fingerprint: " + reply.Error); }
                    });
                }
                catch (Exception e) { RevivalMod.Log.Warning("[state] " + e.GetType().Name + ": " + e.Message); }
            }
        }
    }
}
