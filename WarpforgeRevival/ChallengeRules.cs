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
            // in tenths: 10 = x1.0 .. 30 = x3.0, in steps of 0.2 (every warlord's health is a multiple of
            // 5, so the result is always a whole number)
            new Field("warlordHealthMultiplier", "Warlord health multiplier", 10, 30, 2),
            new Field("warlordLifeChange", "Warlord health change (added after the multiplier)", -20, 40),
            new Field("startingHand", "Starting hand (at most the hand limit)", 1, 10),
            new Field("secondPlayerExtraCards", "Extra cards for the second player", 0, 3),
            new Field("drawCardsPerTurn", "Cards drawn per turn", 0, 5),
            new Field("handLimit", "Hand limit", 3, 20),
            new Field("startingMana", "Starting mana (first player)", 0, 10),
            new Field("startingManaSecond", "Starting mana (second player)", 0, 10),
            new Field("manaPerTurn", "Mana gained per turn", 0, 5),
            new Field("overtimeTurn", "Overtime starts on turn", 5, 60),
            new Field("turnSeconds", "Turn timer (seconds)", 30, 300, 5),
        };

        /// <summary>A rule's value as the menu shows it ("x1.4", "+5", "4").</summary>
        internal static string Show(string key, int v) =>
            key == "warlordHealthMultiplier" ? "x" + (v / 10.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
            : key == "warlordLifeChange" && v > 0 ? "+" + v : v.ToString();

        /// <summary>
        /// Keeps the starting hand within the hand limit: the starting hand at most the limit, and the
        /// second player's starting hand (starting hand + extra cards) too.
        /// </summary>
        internal static void CapHand(Dictionary<string, int> v)
        {
            if (!v.TryGetValue("handLimit", out int limit)) return;
            if (v.TryGetValue("startingHand", out int hand) && hand > limit) v["startingHand"] = hand = Math.Max(1, limit);
            if (v.TryGetValue("secondPlayerExtraCards", out int extra) && v.TryGetValue("startingHand", out hand) && hand + extra > limit)
                v["secondPlayerExtraCards"] = Math.Max(0, limit - hand);
        }

        internal static bool HandFits(Dictionary<string, int> v) =>
            v["startingHand"] <= v["handLimit"] && v["startingHand"] + v["secondPlayerExtraCards"] <= v["handLimit"];

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
                if (r.Values.Count != Fields.Length) return null;                              // every rule present
                return HandFits(r.Values) ? r : null;                                          // starting hands within the hand limit
            }

            /// <summary>The rules in a few short lines, for the challenge popup.</summary>
            public string Describe(string modeName)
            {
                int G(string k) => Get(k) ?? 0;
                int change = G("warlordLifeChange");
                return $"Mode: {modeName}\n" +
                       $"Warlord health: {Show("warlordHealthMultiplier", G("warlordHealthMultiplier"))}, then {(change >= 0 ? "+" : "")}{change}\n" +
                       $"Starting hand: {G("startingHand")} (+{G("secondPlayerExtraCards")} for the second player) · hand limit {G("handLimit")}\n" +
                       $"Mana: starts {G("startingMana")} / {G("startingManaSecond")} (first / second player), +{G("manaPerTurn")} per turn\n" +
                       $"Cards drawn per turn: {G("drawCardsPerTurn")} · overtime from turn {G("overtimeTurn")} · turn timer {G("turnSeconds")} s";
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
            double factor = eventId == ServerSettings.LongGameEvent && ServerSettings.LongGameHealth > 0 ? ServerSettings.LongGameHealth : 1.0;
            r.Values["warlordHealthMultiplier"] = (int)Math.Round(factor * 10);
            int secs = ServerSettings.TurnSeconds ?? RevivalMod.Config.TurnSeconds;
            r.Values["turnSeconds"] = secs >= 30 && secs <= 300 ? secs : 60;
            foreach (var f in Fields)
            {
                int v = r.Values.TryGetValue(f.Key, out int got) ? got : f.Min;
                v = Math.Clamp(v, f.Min, f.Max);
                v = f.Min + (int)Math.Round((v - f.Min) / (double)f.Step) * f.Step;   // on the field's step, as the friend's game checks
                r.Values[f.Key] = Math.Min(v, f.Max);
            }
            CapHand(r.Values);
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

        /// <summary>Deck rules of a mode, every rarity spelled out ("60 cards · 4 copies of a Common, 3 of a Rare, ...").</summary>
        internal static string DeckRules(IPlayEvent ev, string eventId)
        {
            var gv = Variables(ev);
            if ((object)gv == null) return "";
            bool isLong = eventId == ServerSettings.LongGameEvent;
            string[] names = { "", "Common", "Rare", "Epic", "Legendary" };
            var parts = new List<string>();
            for (int rarity = 1; rarity <= 4; rarity++)
            {
                int n = isLong && ServerSettings.LongGameCopies(rarity) > 0 ? ServerSettings.LongGameCopies(rarity)
                      : rarity == 4 ? gv.numberOfCopiesLegendary : gv.numberOfCopiesOtherRarities;
                parts.Add(rarity == 1 ? $"{n} {(n == 1 ? "copy" : "copies")} of a {names[rarity]}" : $"{n} of {(rarity == 3 ? "an" : "a")} {names[rarity]}");
            }
            return $"Decks: {gv.deckSize} cards · " + string.Join(", ", parts);
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
            Apply(rules, null);           // the challenger's own match uses them too
        }

        /// <summary>
        /// Called for every private message the game sends: the challenge message ("Practice") to the
        /// friend the menu challenged gets the rules added. Others are passed on unchanged.
        /// </summary>
        internal static string AddTo(string target, string json, string kind)
        {
            try
            {
                if (kind == "RemovePractice") incoming.Remove(target);    // this player turned that friend's challenge down
                if (kind == "RemovePractice" && outgoing != null && string.Equals(target, outgoingTo, StringComparison.OrdinalIgnoreCase))
                {
                    outgoing = null;                                   // the challenger called it off
                    if (!battleSeen) Restore();
                    RevivalMod.Log.Msg($"[challenge] challenge to {target} called off");
                    return json;
                }
                if (kind != "Practice" || outgoing == null) return json;
                // Only the challenge the menu just sent carries the rules. Any other challenge (another friend,
                // or the same friend later from the friend list) is an ordinary one: the noted rules are dropped,
                // so this game never plays custom rules its opponent was not sent.
                if (!string.Equals(target, outgoingTo, StringComparison.OrdinalIgnoreCase) || Time.realtimeSinceStartup - outgoingAt > 15f)
                {
                    outgoing = null;
                    if (!battleSeen) Restore();
                    RevivalMod.Log.Msg($"[challenge] ordinary challenge to {target}; custom rules dropped");
                    return json;
                }
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
                int type = t.ValueKind == JsonValueKind.Number && t.TryGetInt32(out int n) ? n
                         : t.ValueKind == JsonValueKind.String ? (t.GetString() == "Practice" ? 1 : t.GetString() == "RemovePractice" ? 2 : -1) : -1;
                if (type == 2) { incoming.Remove(from); return; }       // the challenger called it off
                if (type != 1) return;
                incoming.Clear();      // only the latest challenge counts: an earlier one's rules never reach another friend's match
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
                // shown inside the game's own "... has challenged you" popup, which comes right after (see Decorate);
                // a popup of our own here would take that one's place and its Accept button with it
                lastRules = rules;
                lastRulesMode = mode;
                lastRulesAt = Time.realtimeSinceStartup;
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

        private static float biggerPopup = -1f;
        private static RuleSet lastRules;
        private static string lastRulesMode;
        private static float lastRulesAt = -100f;

        /// <summary>
        /// Called for every popup the game is about to show: its "... has challenged you in Classic mode!
        /// Play friendly match?" popup gets the rules of the challenge that just arrived, between the two
        /// lines, so the player sees them on the popup they accept with.
        /// </summary>
        internal static string Decorate(string text)
        {
            try
            {
                if (lastRules == null || string.IsNullOrEmpty(text) || Time.realtimeSinceStartup - lastRulesAt > 20f) return text;
                if (text.IndexOf("challenged you", StringComparison.OrdinalIgnoreCase) < 0) return text;
                string head = text, tail = "";
                int q = text.LastIndexOf('\n');
                if (q > 0) { head = text.Substring(0, q).TrimEnd(); tail = text.Substring(q + 1).Trim(); }
                string rules = string.Join("\n", lastRules.Describe(lastRulesMode).Split('\n').Skip(1));    // the mode is already in the game's line
                lastRules = null;
                biggerPopup = Time.realtimeSinceStartup;
                RevivalMod.Log.Msg("[challenge] rules added to the game's challenge popup");
                return $"{head}\nCustom rules:\n{rules}" + (tail.Length > 0 ? "\n\n" + tail : "");
            }
            catch (Exception e) { RevivalMod.Log.Warning("[challenge] popup: " + e.Message); return text; }
        }

        private static string FriendName(string id, string fallback)
        {
            try
            {
                var fl = PlayerDataManager.singletonManager?.friendsData?.friendList;
                if (fl != null)
                    for (int i = 0; i < fl.Count; i++)
                        if ((object)fl[i] != null && fl[i].id == id && !string.IsNullOrEmpty(fl[i].name)) return fl[i].name;
            }
            catch { }
            return fallback;
        }

        /// <summary>
        /// The game's popup sizes its text to fit, so the rules came out small: once it is on screen, its
        /// text is allowed to be larger (and the box taller where the popup lets it grow).
        /// </summary>
        private static void EnlargePopup()
        {
            if (biggerPopup < 0) return;
            if (Time.realtimeSinceStartup - biggerPopup > 3f) { biggerPopup = -1f; return; }
            try
            {
                var popup = WindowsManager.Instance?.PopupWindow;
                if ((object)popup == null || !popup.isActiveAndEnabled) return;
                foreach (var t in popup.GetComponentsInChildren<Il2CppTMPro.TMP_Text>(true))
                {
                    if (t.text == null || t.text.IndexOf("custom rules", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    t.enableAutoSizing = true;
                    t.fontSizeMax = Math.Max(t.fontSizeMax, 38f);
                    t.fontSizeMin = Math.Max(t.fontSizeMin, 22f);
                    t.fontSize = 34f;
                    biggerPopup = -1f;
                    RevivalMod.Log.Msg("[challenge] rules popup text enlarged");
                }
            }
            catch (Exception e) { biggerPopup = -1f; RevivalMod.Log.Warning("[challenge] popup: " + e.Message); }
        }

        // ---------------------------------------------------------------- the match

        private static RuleSet active;
        private static GameplayVariablesData activeVars;
        private static readonly Dictionary<string, int> saved = new Dictionary<string, int>();
        private static float activeAt;
        private static bool battleSeen;
        private static float nextCheck;

        internal static double? HealthMultiplier => active?.Get("warlordHealthMultiplier") is int m ? m / 10.0 : (double?)null;
        internal static int? HealthChange => active?.Get("warlordLifeChange");
        internal static int? StartingHand => active?.Get("startingHand");
        internal static int? SecondExtra => active?.Get("secondPlayerExtraCards");
        internal static int? TurnSeconds => active?.Get("turnSeconds");

        // Both sides start a challenge match through here: the challenger once the friend accepts, the
        // friend when accepting. With custom rules for this opponent, the match is played in the rules'
        // mode and by its values.
        [HarmonyPatch(typeof(ChallengeManager), nameof(ChallengeManager.TryChallengeMatchStart))]
        private static class MatchStart
        {
            // The game's own challenge is left exactly as it is (0.12.35-0.12.37 switched the challenge to the
            // chosen mode here, which stopped the friend's accept from starting the match). Only the rules
            // are noted for the coming match; they go onto the match itself when the battle starts.
            private static bool Prefix(ChallengeManager __instance, ref bool __result)
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
                    // the challenger's game calls this as it sends, before it knows the opponent's id
                    RuleSet rules = null;
                    if (outgoing != null && (opponent.Length == 0 || string.Equals(opponent, outgoingTo, StringComparison.OrdinalIgnoreCase))) rules = outgoing;
                    else if (incoming.TryGetValue(opponent, out var got)) rules = got.rules;
                    else if (incoming.Count == 1 && opponent.Length == 0) rules = incoming.Values.First().rules;
                    RevivalMod.Log.Msg($"[challenge] challenge match starting with '{opponent}': {(rules == null ? "ordinary rules" : "custom rules")}");
                    // nothing is cleared here for an ordinary one: rules already noted stay until the battle
                    // (which only uses them in a friend match), a call-off, or 5 minutes
                    if (rules != null && !ReferenceEquals(rules, active)) Apply(rules, null);
                }
                catch (Exception e) { RevivalMod.Log.Warning("[challenge] match start: " + e.Message); }
                return true;
            }
        }

        private static void Apply(RuleSet rules, IPlayEvent ev)
        {
            Restore();
            active = rules;
            activeAt = Time.realtimeSinceStartup;
            battleSeen = false;
            RevivalMod.Log.Msg($"[challenge] next match with custom rules in {rules.EventId}: {rules.ToJson()["v"]?.ToJsonString()}");
        }

        // The match's own copy of the rules (MatchData.GameplayData: a friend match runs under the
        // practice mode's rules, not the chosen mode's) gets the challenge's values as the battle starts,
        // before the warlords and hands are set up.
        [HarmonyPatch(typeof(BattleManager), nameof(BattleManager.StartBattleManager))]
        private static class BattleStart
        {
            private static void Prefix(BattleManager __instance)
            {
                try { if (active == null) FromIncoming(__instance); } catch (Exception e) { RevivalMod.Log.Warning("[challenge] " + e.Message); }
                if (active == null) return;
                try
                {
                    var md = __instance.matchData;
                    if ((object)md == null || md.playMode != PlayModes.Duel)
                    {
                        RevivalMod.Log.Msg($"[challenge] this match is not a friend match ({((object)md == null ? "no match data" : md.playMode.ToString())}); custom rules not used");
                        Restore();
                        return;
                    }
                    var gv = md.GameplayData;
                    if ((object)gv == null) { RevivalMod.Log.Warning("[challenge] the match has no rules object; custom rules not applied"); return; }
                    if ((object)activeVars != null) foreach (var kv in saved) Write(activeVars, kv.Key, kv.Value);
                    activeVars = gv;
                    saved.Clear();
                    Set(gv, "warlordLifeChange", active); Set(gv, "startingMana", active); Set(gv, "startingManaSecond", active);
                    Set(gv, "manaPerTurn", active); Set(gv, "drawCardsPerTurn", active); Set(gv, "handLimit", active); Set(gv, "overtimeTurn", active);
                    battleSeen = true;
                    outgoing = null;
                    incoming.Clear();
                    RevivalMod.Log.Msg($"[challenge] custom rules applied to this match: mana {gv.startingMana}/{gv.startingManaSecond} +{gv.manaPerTurn}, " +
                                       $"draw {gv.drawCardsPerTurn}, hand limit {gv.handLimit}, overtime {gv.overtimeTurn}, health change {gv.warlordLifeChange}");
                }
                catch (Exception e) { RevivalMod.Log.Warning("[challenge] applying rules: " + e.Message); }
            }
        }

        /// <summary>
        /// The friend's side: accepting goes straight to the match without the challenge-start call the
        /// challenger's game makes, so the rules of the accepted challenge are looked up as the battle starts:
        /// a friend match against the player whose challenge with rules arrived in the last 5 minutes.
        /// </summary>
        private static void FromIncoming(BattleManager battle)
        {
            if (incoming.Count == 0) return;
            var md = battle.matchData;
            if ((object)md == null || md.playMode != PlayModes.Duel) return;
            string opponent = "";
            try { opponent = UnityEngine.Object.FindObjectOfType<ChallengeManager>()?.currentOpponentPlayfabId ?? ""; } catch { }
            float now = Time.realtimeSinceStartup;
            (RuleSet rules, float at) got = default;
            bool found = opponent.Length > 0 && incoming.TryGetValue(opponent, out got);
            if (!found && opponent.Length == 0 && incoming.Count == 1) { got = incoming.Values.First(); found = true; opponent = incoming.Keys.First(); }
            if (!found || now - got.at > 300f) { RevivalMod.Log.Msg($"[challenge] friend match: no challenge rules for '{opponent}'"); return; }
            RevivalMod.Log.Msg($"[challenge] friend match with {opponent}: the accepted challenge's rules");
            Apply(got.rules, null);
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
            EnlargePopup();
            if (active == null) return;
            float now = Time.realtimeSinceStartup;
            if (now < nextCheck) return;
            nextCheck = now + 1f;
            try
            {
                bool inBattle = (object)UnityEngine.Object.FindObjectOfType<BattleManager>() != null;
                if (inBattle) battleSeen = true;
                else if (battleSeen || now - activeAt > 300f) Restore();
            }
            catch { }
        }
    }
}
