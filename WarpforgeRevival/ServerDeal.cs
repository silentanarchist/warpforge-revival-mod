using System;
using System.Text;
using HarmonyLib;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace WarpforgeRevival
{
    /// <summary>
    /// Server deal (test), step 1 (0.14.34). A friend challenge whose rules carry "serverDeal" (a tester-only rule on
    /// Social > Challenge, or the Server deal tile in the Testers window) plays with:
    ///  - the match's random table from the server instead of the room creator's game: when the two players
    ///    have such a challenge open and both run a mod with this, the server writes the room properties
    ///    "RevivalDeal" = 1 and "RevivalRng" = its own 100 numbers. The creator's game sends those instead of its
    ///    own (the sending is inside PUNReceiveAckStartGame, so that is what is patched); the joiner's game checks
    ///    what it received against the room property.
    ///  - nothing sent when a hand card is hovered or dragged (CardSeen / CardReorder said which card by its id).
    /// Everything here starts with Active, which is false in every other match, so the game's own code runs
    /// unchanged there. Later steps (claude/server-deal-plan.md): placeholder decks, server-dealt draws, reveals.
    /// </summary>
    internal static class ServerDeal
    {
        private const string DealKey = "RevivalDeal", RngKey = "RevivalRng";
        private static string lastRoom;
        private static bool lastActive;

        /// <summary>True in a match whose room the server marked as a server-deal test (a tester's challenge with the
        /// rule, or the Server Deal game mode); the server decides, the mod only reads the mark. Never in a replay.</summary>
        internal static bool Active
        {
            get
            {
                try
                {
                    if (ChallengeRules.ActiveIsReplay) return false;
                    if (!PhotonNetwork.inRoom) return false;
                    var room = PhotonNetwork.room;
                    var props = (object)room == null ? null : room.CustomProperties;
                    if ((object)props == null) return false;
                    bool on = props.ContainsKey((Il2CppSystem.Object)(Il2CppSystem.String)DealKey);
                    string name = room.Name;
                    if (name != lastRoom || on != lastActive)
                    {
                        lastRoom = name; lastActive = on;
                        if (on) RevivalMod.Log.Msg("[deal] server deal is on for this match");
                        else if (ChallengeRules.ServerDealRule) RevivalMod.Log.Msg("[deal] the challenge asked for the server deal but the server did not mark the room (opponent's mod too old?)");
                    }
                    return on;
                }
                catch { return false; }
            }
        }

        /// <summary>The server's random table for this room, or null.</summary>
        private static int[] ServerTable()
        {
            try
            {
                var props = PhotonNetwork.room?.CustomProperties;
                if ((object)props == null) return null;
                var key = (Il2CppSystem.Object)(Il2CppSystem.String)RngKey;
                if (!props.ContainsKey(key)) return null;
                string text = props[key]?.ToString();
                if (string.IsNullOrEmpty(text)) return null;
                var parts = text.Split(' ');
                var table = new int[parts.Length];
                for (int i = 0; i < parts.Length; i++)
                    if (!int.TryParse(parts[i], out table[i])) return null;
                return table.Length >= 10 ? table : null;
            }
            catch (Exception e) { RevivalMod.Log.Warning("[deal] server table: " + e.Message); return null; }
        }

        private static string Hash(Il2CppStructArray<int> t)
        {
            if (t == null) return "none";
            var sb = new StringBuilder();
            for (int i = 0; i < t.Length; i++) { if (i > 0) sb.Append(' '); sb.Append(t[i]); }
            using (var sha = System.Security.Cryptography.SHA1.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.ASCII.GetBytes(sb.ToString()))).Replace("-", "").Substring(0, 10).ToLowerInvariant() + $" ({t.Length} numbers)";
        }

        private static bool Same(Il2CppStructArray<int> a, int[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < b.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        /// <summary>The creator's game, about to send its table (SendRandomNumbers is compiled into this method): the server's instead.</summary>
        [HarmonyPatch(typeof(SearchOpponentManager), nameof(SearchOpponentManager.PUNReceiveAckStartGame))]
        private static class CreatorSendsTable
        {
            private static void Prefix(SearchOpponentManager __instance)
            {
                try
                {
                    if (!Active) return;
                    var md = __instance.matchData;
                    if ((object)md == null) { RevivalMod.Log.Warning("[deal] no match data when the table is sent"); return; }
                    var table = ServerTable();
                    if (table == null) { RevivalMod.Log.Warning("[deal] the room has no server table; the game's own is used"); return; }
                    var arr = new Il2CppStructArray<int>(table.Length);
                    for (int i = 0; i < table.Length; i++) arr[i] = table[i];
                    md.randomNumbers = arr;
                    md.randomNumbersIndex = 0;
                    RevivalMod.Log.Msg("[deal] using the server's table " + Hash(arr) + " (sent to the opponent)");
                }
                catch (Exception e) { RevivalMod.Log.Warning("[deal] creator's table: " + e.Message); }
            }
        }

        /// <summary>The joiner's game, having stored the table it received: it must be the server's.</summary>
        [HarmonyPatch(typeof(SearchOpponentManager), nameof(SearchOpponentManager.PUNReceiveRandomNumbers))]
        private static class JoinerChecksTable
        {
            private static void Postfix(SearchOpponentManager __instance)
            {
                try
                {
                    if (!Active) return;
                    var md = __instance.matchData;
                    if ((object)md == null) return;
                    var table = ServerTable();
                    if (table == null) { RevivalMod.Log.Warning("[deal] the room has no server table to check against"); return; }
                    if (Same(md.randomNumbers, table))
                        RevivalMod.Log.Msg("[deal] using the server's table " + Hash(md.randomNumbers) + " (received, matches the room)");
                    else
                        // the opponent's game sent its own numbers (its rules were not active in time, or an older mod):
                        // kept, so both games still agree; the match is simply not a server-deal one for the table
                        RevivalMod.Log.Warning("[deal] the table received " + Hash(md.randomNumbers) + " is NOT the server's; kept so both games agree");
                }
                catch (Exception e) { RevivalMod.Log.Warning("[deal] joiner's table: " + e.Message); }
            }
        }

        /// <summary>Hovering a hand card: nothing sent (the message names the card by its id).</summary>
        [HarmonyPatch(typeof(BattleCommsManager), nameof(BattleCommsManager.SendCardSeen))]
        private static class NoCardSeen
        {
            private static bool Prefix() => !Active;
        }

        /// <summary>Dragging a hand card to a new place: nothing sent.</summary>
        [HarmonyPatch(typeof(BattleCommsManager), nameof(BattleCommsManager.SendCardReorder))]
        private static class NoCardReorder
        {
            private static bool Prefix() => !Active;
        }
    }
}
