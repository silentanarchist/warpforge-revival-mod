using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using HarmonyLib;
using Il2Cpp;
using Il2CppEverguild.LiveOps;
using UnityEngine;

namespace WarpforgeRevival
{
    /// <summary>
    /// Rules a friend challenge is played with (Social > Challenge). The challenger picks a game mode
    /// as the preset (its deck rules, and the starting point for the match rules) and may change the
    /// match rules. They travel with the game's own challenge message ("revivalRules" added to its
    /// JSON) and, once the match starts, both games play by them:
    ///  - the mode's gameplay variables (warlord health, mana, cards drawn, hand limit, overtime) are
    ///    set on the mode for the length of the match and put back afterwards;
    ///  - the starting hand, the second player's extra cards and the turn timer are read from here
    ///    by LongGame (starting hand) and TurnClock.
    /// A challenge without "revivalRules" is an ordinary Classic/Skirmish challenge, as before.
    /// </summary>
    internal static class ChallengeRules
    {
        internal sealed class Field
        {
            public string Key, Label;
            public int Min, Max, Step;
            public Field(string key, string label, int min, int max, int step = 1) { Key = key; Label = label; Min = min; Max = max; Step = step; }
        }

        /// <summary>The match rules the menu offers, in its order.</summary>
        internal static readonly Field[] Fields =
        {
            new Field("warlordLifeChange", "Warlord health change", -20, 40),
            new Field("startingHand", "Starting hand", 1, 10),
            new Field("secondPlayerExtraCards", "Extra cards for the second player", 0, 3),
            new Field("startingMana", "Starting mana (first player)", 0, 10),
            new Field("startingManaSecond", "Starting mana (second player)", 0, 10),
            new Field("manaPerTurn", "Mana gained per turn", 0, 5),
            new Field("drawCardsPerTurn", "Cards drawn per turn", 0, 5),
            new Field("handLimit", "Hand limit", 3, 20),
            new Field("overtimeTurn", "Overtime starts on turn", 5, 60),
            new Field("turnSeconds", "Turn timer (seconds)", 30, 300, 15),
        };

        internal sealed class RuleSet
        {
            public string EventId = "";
            public Dictionary<string, int> Values = new Dictionary<string, int>();

            public int? Get(string key) => Values.TryGetValue(key, out int v) ? v : (int?)null;

            public JsonObject ToJson()
            {
                var v = new JsonObject();
                foreach (var kv in Values) v[kv.Key] = kv.Value;
                return new JsonObject { ["event"] = EventId, ["v"] = v };
            }

            /// <summary>
            /// Read from a challenge message. Only the exact shape the menu sends is accepted:
            /// {"event": one of the offered modes, "v": {every rule of Fields, nothing else, each a whole
            /// number within its range and on its step}}. Anything else is refused as a whole (null),
            /// never repaired, so a hand-made message cannot slip other values into a match.
            /// </summary>
            public static RuleSet From(JsonElement e)
            {
                if (e.ValueKind != JsonValueKind.Object) return null;
                int props = 0;
                foreach (var _ in e.EnumerateObject()) props++;
                if (props != 2 || !e.TryGetProperty("event", out var ev) || ev.ValueKind != JsonValueKind.String) return null;
                if (!e.TryGetProperty("v", out var vals) || vals.ValueKind != JsonValueKind.Object) return null;
                var r = new RuleSet { EventId = ev.GetString() ?? "" };
                if (!AllowedModes().Contains(r.EventId)) return null;
                foreach (var p in vals.EnumerateObject())
                {
                    var f = Array.Find(Fields, x => x.Key == p.Name);
                    if (f == null || r.Values.ContainsKey(f.Key)) return null;                 // unknown or repeated rule
                    if (p.Value.ValueKind != JsonValueKind.Number || !p.Value.TryGetInt32(out int n)) return null;
                    if (p.Value.GetRawText().IndexOfAny(new[] { '.', 'e', 'E' }) >= 0) return null; // whole numbers only
                    if (n < f.Min || n > f.Max || (n - f.Min) % f.Step != 0) return null;
                    r.Values[f.Key] = n;
                }
                return r.Values.Count == Fields.Length ? r : null;                             // every rule present
            }

            public string Describe(string modeName)
            {
                var lines = new List<string> { "Mode: " + modeName };
                foreach (var f in Fields)
                    if (Values.TryGetValue(f.Key, out int v))
                        lines.Add($"{f.Label}: {(f.Key == "warlordLifeChange" && v > 0 ? "+" : "")}{v}");
                return string.Join("\n", lines);
            }
        }

        // ---------------------------------------------------------------- the mode's own values

        /// <summary>The preset values of a mode: its gameplay variables, its starting hand and the turn timer.</summary>
        internal static RuleSet Preset(IPlayEvent ev, string eventId)
        {
            var r = new RuleSet { EventId = eventId };
            var gv = Variables(ev);
            if ((object)gv != null)
            {
                r.Values["warlordLifeChange"] = gv.warlordLifeChange;
                r.Values["startingMana"] = gv.startingMana;
                r.Values["startingManaSecond"] = gv.startingManaSecond;
                r.Values["manaPerTurn"] = gv.manaPerTurn;
                r.Values["drawCardsPerTurn"] = gv.drawCardsPerTurn;
                r.Values["handLimit"] = gv.handLimit;
                r.Values["overtimeTurn"] = gv.overtimeTurn;
            }
            var (hand, extra) = ServerSettings.ModeHand(eventId);
            r.Values["startingHand"] = hand > 0 ? hand : 4;
            r.Values["secondPlayerExtraCards"] = extra >= 0 ? extra : 1;
            int secs = ServerSettings.TurnSeconds ?? RevivalMod.Config.TurnSeconds;
            r.Values["turnSeconds"] = secs >= 30 && secs <= 300 ? secs : 60;
            foreach (var f in Fields)
            {
                int v = r.Values.TryGetValue(f.Key, out int got) ? got : f.Min;
                v = Math.Clamp(v, f.Min, f.Max);
                v = f.Min + (int)Math.Round((v - f.Min) / (double)f.Step) * f.Step;   // on the field's step, as the friend's game checks
                r.Values[f.Key] = Math.Min(v, f.Max);
            }
            return r;
        }

        internal static GameplayVariablesData Variables(IPlayEvent ev)
        {
            try
            {
                var data = (object)ev == null ? null : ev.GetBaseData();
                var play = (object)data == null ? null : data.TryCast<PlayEventData>();
                return (object)play == null ? null : play.gameplayVariables;
            }
            catch { return null; }
        }

        /// <summary>Deck rules of a mode, for the menu ("40 cards, 3 copies, 1 of each legendary").</summary>
        internal static string DeckRules(IPlayEvent ev)
        {
            var gv = Variables(ev);
            if ((object)gv == null) return "";
            return $"Decks: {gv.deckSize} cards, up to {gv.numberOfCopiesOtherRarities} copies of a card, {gv.numberOfCopiesLegendary} of a legendary";
        }

        /// <summary>The modes a challenge may use (the menu's presets): Classic, Skirmish, and the long-game mode.</summary>
        internal static HashSet<string> AllowedModes()
        {
            var ids = new HashSet<string> { "RevivalClassic", "RevivalSkirmish" };
            if (!string.IsNullOrEmpty(ServerSettings.LongGameEvent)) ids.Add(ServerSettings.LongGameEvent);
            return ids;
        }

        internal static IPlayEvent FindEvent(string id)
        {
            try
            {
                var found = LiveOpsManager.GetEvent(id);
                return (object)found == null ? null : found.TryCast<IPlayEvent>();
            }
            catch { return null; }
        }

        // ---------------------------------------------------------------- sending and receiving

        private static RuleSet outgoing;
        private static string outgoingTo;
        private static float outgoingAt;
        private static readonly Dictionary<string, (RuleSet rules, float at)> incoming = new Dictionary<string, (RuleSet, float)>();

        /// <summary>The menu's challenge: remembered so it goes with the game's challenge message to this friend.</summary>
        internal static void Outgoing(string to, RuleSet rules)
        {
            outgoing = rules;
            outgoingTo = to;
            outgoingAt = Time.realtimeSinceStartup;
        }

        /// <summary>
        /// Called for every private message the game sends: the challenge message ("Practice") to the
        /// friend the menu challenged gets the rules added. Others are passed on unchanged.
        /// </summary>
        internal static string AddTo(string target, string json, string kind)
        {
            try
            {
                if (kind != "Practice" || outgoing == null || !string.Equals(target, outgoingTo, StringComparison.OrdinalIgnoreCase)) return json;
                if (Time.realtimeSinceStartup - outgoingAt > 120f) { outgoing = null; return json; }
                var root = JsonNode.Parse(json) as JsonObject;
                if (root == null) return json;
                root["revivalRules"] = outgoing.ToJson();
                RevivalMod.Log.Msg($"[challenge] custom rules sent to {target}: {outgoing.ToJson().ToJsonString()}");
                return root.ToJsonString();
            }
            catch (Exception e) { RevivalMod.Log.Warning("[challenge] " + e.Message); return json; }
        }

        /// <summary>Called for every private message received: a challenge with rules is remembered and shown.</summary>
        internal static void Read(string from, string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("messageType", out var t)) return;
                bool practice = (t.ValueKind == JsonValueKind.Number && t.TryGetInt32(out int n) && n == 1) ||
                                (t.ValueKind == JsonValueKind.String && t.GetString() == "Practice");
                if (!practice) return;
                refused.Remove(from);
                if (!root.TryGetProperty("revivalRules", out var rr)) { incoming.Remove(from); return; }
                if (json.Length > 4096) { Refuse(from, "too large"); return; }
                var rules = RuleSet.From(rr);
                if (rules == null) { Refuse(from, "not in the expected form"); return; }
                incoming[from] = (rules, Time.realtimeSinceStartup);
                string who = from;
                try { if (root.TryGetProperty("parameter1", out var p1) && p1.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(p1.GetString())) who = p1.GetString(); } catch { }
                var ev = FindEvent(rules.EventId);
                string mode = (object)ev == null ? rules.EventId : TestersPage.Label(ev.TryCast<LiveOpsEvent>(), EventLabelReferenceType.Title, rules.EventId);
                RevivalMod.Log.Msg($"[challenge] {from} challenges with custom rules: {rr.GetRawText()}");
                var wm = WindowsManager.Instance;
                if ((object)wm != null)
                    wm.ShowPopUp($"{who} challenges you with custom rules:\n\n{rules.Describe(mode)}\n\nAccept the challenge in your friend list to play by these rules.",
                                 false, true, "OK", (Il2CppSystem.Action)null);
            }
            catch (Exception e) { RevivalMod.Log.Warning("[challenge] " + e.Message); }
        }

        // A challenge whose rules were not in the exact expected form: accepting it is blocked, so the two
        // games can never play by different rules.
        private static readonly HashSet<string> refused = new HashSet<string>();

        private static void Refuse(string from, string why)
        {
            incoming.Remove(from);
            refused.Add(from);
            RevivalMod.Log.Warning($"[challenge] challenge from {from} refused: its rules are {why}");
            try
            {
                WindowsManager.Instance?.ShowPopUp("A challenge arrived with rules this game cannot read, so it cannot be accepted. " +
                                                   "Both players should update the mod and try again.", false, true, "OK", (Il2CppSystem.Action)null);
            }
            catch { }
        }

        // ---------------------------------------------------------------- the match

        private static RuleSet active;
        private static GameplayVariablesData activeVars;
        private static readonly Dictionary<string, int> saved = new Dictionary<string, int>();
        private static float activeAt;
        private static bool battleSeen;
        private static float nextCheck;

        internal static int? StartingHand => active?.Get("startingHand");
        internal static int? SecondExtra => active?.Get("secondPlayerExtraCards");
        internal static int? TurnSeconds => active?.Get("turnSeconds");

        // Both sides start a challenge match through here: the challenger once the friend accepts, the
        // friend when accepting. With custom rules for this opponent, the match is played in the rules'
        // mode and by its values.
        [HarmonyPatch(typeof(ChallengeManager), nameof(ChallengeManager.TryChallengeMatchStart))]
        private static class MatchStart
        {
            private static bool Prefix(ChallengeManager __instance, ref IPlayEvent playEvent, ref bool __result)
            {
                try
                {
                    string opponent = __instance.currentOpponentPlayfabId ?? "";
                    if (refused.Contains(opponent))
                    {
                        RevivalMod.Log.Warning($"[challenge] not starting the match with {opponent}: its rules were refused");
                        __result = false;
                        return false;
                    }
                    RuleSet rules = null;
                    if (outgoing != null && string.Equals(opponent, outgoingTo, StringComparison.OrdinalIgnoreCase)) rules = outgoing;
                    else if (incoming.TryGetValue(opponent, out var got)) rules = got.rules;
                    else if (incoming.Count == 1 && string.IsNullOrEmpty(opponent)) rules = incoming.Values.First().rules;
                    if (rules == null) { Restore(); return true; }
                    var ev = FindEvent(rules.EventId);
                    if ((object)ev == null)
                    {
                        RevivalMod.Log.Warning($"[challenge] mode {rules.EventId} is not on this server; the match is not started");
                        __result = false;
                        return false;
                    }
                    playEvent = ev;
                    Apply(rules, ev);
                    outgoing = null;
                    incoming.Remove(opponent);
                }
                catch (Exception e) { RevivalMod.Log.Warning("[challenge] match start: " + e.Message); }
                return true;
            }
        }

        private static void Apply(RuleSet rules, IPlayEvent ev)
        {
            Restore();
            var gv = Variables(ev);
            active = rules;
            activeAt = Time.realtimeSinceStartup;
            battleSeen = false;
            if ((object)gv != null)
            {
                activeVars = gv;
                saved.Clear();
                Set(gv, "warlordLifeChange", rules); Set(gv, "startingMana", rules); Set(gv, "startingManaSecond", rules);
                Set(gv, "manaPerTurn", rules); Set(gv, "drawCardsPerTurn", rules); Set(gv, "handLimit", rules); Set(gv, "overtimeTurn", rules);
            }
            RevivalMod.Log.Msg($"[challenge] match with custom rules in {rules.EventId}: {rules.ToJson()["v"]?.ToJsonString()}");
        }

        private static void Set(GameplayVariablesData gv, string key, RuleSet rules)
        {
            int? want = rules.Get(key);
            if (want == null) return;
            int now = Read(gv, key);
            if (!saved.ContainsKey(key)) saved[key] = now;
            Write(gv, key, want.Value);
        }

        private static int Read(GameplayVariablesData gv, string key) => key switch
        {
            "warlordLifeChange" => gv.warlordLifeChange,
            "startingMana" => gv.startingMana,
            "startingManaSecond" => gv.startingManaSecond,
            "manaPerTurn" => gv.manaPerTurn,
            "drawCardsPerTurn" => gv.drawCardsPerTurn,
            "handLimit" => gv.handLimit,
            "overtimeTurn" => gv.overtimeTurn,
            _ => 0,
        };

        private static void Write(GameplayVariablesData gv, string key, int v)
        {
            switch (key)
            {
                case "warlordLifeChange": gv.warlordLifeChange = v; break;
                case "startingMana": gv.startingMana = v; break;
                case "startingManaSecond": gv.startingManaSecond = v; break;
                case "manaPerTurn": gv.manaPerTurn = v; break;
                case "drawCardsPerTurn": gv.drawCardsPerTurn = v; break;
                case "handLimit": gv.handLimit = v; break;
                case "overtimeTurn": gv.overtimeTurn = v; break;
            }
        }

        /// <summary>The mode's own values back, once the custom match is over (or never started).</summary>
        internal static void Restore()
        {
            try
            {
                if ((object)activeVars != null && activeVars.Pointer != IntPtr.Zero)
                    foreach (var kv in saved) Write(activeVars, kv.Key, kv.Value);
                if (active != null) RevivalMod.Log.Msg("[challenge] mode rules put back");
            }
            catch (Exception e) { RevivalMod.Log.Warning("[challenge] restore: " + e.Message); }
            active = null;
            activeVars = null;
            saved.Clear();
        }

        /// <summary>Called every frame: puts the mode's rules back when the custom match has ended.</summary>
        internal static void Tick()
        {
            if (active == null) return;
            float now = Time.realtimeSinceStartup;
            if (now < nextCheck) return;
            nextCheck = now + 1f;
            try
            {
                bool inBattle = (object)UnityEngine.Object.FindObjectOfType<BattleManager>() != null;
                if (inBattle) battleSeen = true;
                else if (battleSeen || now - activeAt > 180f) Restore();
            }
            catch { }
        }
    }
}
