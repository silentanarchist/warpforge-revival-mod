using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using Il2CppEverguild.LiveOps;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WarpforgeRevival
{
    /// <summary>
    /// Social > Challenge: challenge a friend with rules of your choosing. Social's inner ribbon has an
    /// Alliances tab the revival does not use (there are no alliances); it becomes the Challenge tab,
    /// with its content swapped for this menu: a friend to challenge, a game mode as the preset (its
    /// deck rules, and the starting values of the match rules) and the match rules themselves. Sending
    /// goes through the game's own friend challenge, with the rules added (see ChallengeRules).
    /// </summary>
    internal static class ChallengeMenu
    {
        private const string PanelName = "RevivalChallengeMenu";
        private static float nextCheck;
        private static bool dressedLogged, builtLogged;

        // menu state, kept while the game runs
        private static string friendId, friendName;
        private static int presetIndex;
        private static ChallengeRules.RuleSet values;
        private static string status = "";
        private static bool dirty = true;
        private static Sprite swords;

        private static bool Alive(UnityEngine.Object o) => (object)o != null && o.Pointer != IntPtr.Zero && o.m_CachedPtr != IntPtr.Zero;

        /// <summary>Modes offered as presets: those with decks of their own (Custom Test for testers).</summary>
        private static List<string> Presets()
        {
            var ids = new List<string>();
            foreach (var id in new[] { "RevivalClassic", "RevivalSkirmish" })
                if ((object)ChallengeRules.FindEvent(id) != null) ids.Add(id);
            if (AccountPage.IsTester && !string.IsNullOrEmpty(ServerSettings.LongGameEvent) && (object)ChallengeRules.FindEvent(ServerSettings.LongGameEvent) != null)
                ids.Add(ServerSettings.LongGameEvent);
            return ids;
        }

        private static string ModeName(string id)
        {
            var ev = ChallengeRules.FindEvent(id);
            return (object)ev == null ? id : TestersPage.Label(ev.TryCast<LiveOpsEvent>(), EventLabelReferenceType.Title, id);
        }

        /// <summary>Called every frame from the mod's update loop.</summary>
        internal static void Tick()
        {
            float now = Time.realtimeSinceStartup;
            bool slow = now >= nextCheck;
            if (slow) nextCheck = now + 0.25f;
            if (!slow && !dirty) return;
            try
            {
                var w = UnityEngine.Object.FindObjectOfType<SocialMenuWindow>();
                if (!Alive(w) || !w.isActiveAndEnabled) return;
                Dress(w);
                var tab = w.CurrentTab;
                bool onChallenge = (object)tab != null && (object)tab.TryCast<AlliancesTab>() != null;
                var panel = w.transform.Find(PanelName);
                if ((object)panel != null && panel.gameObject.activeSelf != onChallenge) panel.gameObject.SetActive(onChallenge);
                if (!onChallenge) return;
                var t = tab.transform;
                for (int i = 0; i < t.childCount; i++)
                    if (t.GetChild(i).gameObject.activeSelf) t.GetChild(i).gameObject.SetActive(false);
                if ((object)panel == null || dirty) Build(w, (object)panel == null ? null : panel.gameObject);
            }
            catch (Exception e) { RevivalMod.Log.Warning("[challenge] menu: " + e.Message); nextCheck = now + 5f; }
        }

        /// <summary>The ribbon: the Alliances button shown again as CHALLENGE, with crossed swords.</summary>
        private static void Dress(GameWindowWithTabs w)
        {
            var buttons = w.Components?.TabButtons?.options;
            if (buttons == null) return;
            if (!Alive(swords))
            {
                try
                {
                    var nav = UnityEngine.Object.FindObjectOfType<NavigationPanelController>();
                    var play = (object)nav == null ? null : nav.GetToggle(NavigationPanelToggleType.Main);
                    var img = (object)play == null ? null : play.transform.Find("Image");
                    if ((object)img != null) swords = img.GetComponent<Image>()?.sprite;
                }
                catch { }
            }
            for (int i = 0; i < buttons.Count; i++)
            {
                var b = buttons[i];
                if (b == null || (object)b.tab == null || (object)b.toggle == null) continue;
                if ((object)b.tab.TryCast<AlliancesTab>() == null) continue;
                var go = b.toggle.gameObject;
                if (!go.activeSelf) go.SetActive(true);
                // directly below Friends
                for (int j = 0; j < buttons.Count; j++)
                {
                    var fb = buttons[j];
                    if (fb == null || (object)fb.tab == null || (object)fb.toggle == null || (object)fb.tab.TryCast<FriendsTab>() == null) continue;
                    int want = fb.toggle.transform.GetSiblingIndex() + 1;
                    if (go.transform.parent == fb.toggle.transform.parent && go.transform.GetSiblingIndex() != want)
                        go.transform.SetSiblingIndex(want > go.transform.GetSiblingIndex() ? want - 1 : want);
                }
                foreach (var loc in go.GetComponentsInChildren<Il2CppI2.Loc.Localize>(true)) if (loc.enabled) loc.enabled = false;
                foreach (var label in go.GetComponentsInChildren<TMP_Text>(true)) if (label.text != "CHALLENGE") label.text = "CHALLENGE";
                var icon = go.transform.Find("Icon");
                var image = (object)icon == null ? null : icon.GetComponent<Image>();
                if ((object)image != null && Alive(swords) && (object)image.sprite != (object)swords) image.sprite = swords;
                if (!dressedLogged) { dressedLogged = true; RevivalMod.Log.Msg("[challenge] Social ribbon: Challenge tab shown"); }
            }
        }

        // ---------------------------------------------------------------- the menu

        private static void Build(GameWindowWithTabs w, GameObject old)
        {
            dirty = false;
            if ((object)old != null) UnityEngine.Object.Destroy(old);
            if ((object)TestersPage.font == null)
                try { var any = w.GetComponentInChildren<TMP_Text>(true); if ((object)any != null) TestersPage.font = any.font; } catch { }

            var presets = Presets();
            if (presets.Count == 0) return;
            presetIndex = Math.Clamp(presetIndex, 0, presets.Count - 1);
            string mode = presets[presetIndex];
            var ev = ChallengeRules.FindEvent(mode);
            if (values == null || values.EventId != mode) values = ChallengeRules.Preset(ev, mode);

            var go = new GameObject(PanelName);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(w.transform, false);
            rt.SetAsLastSibling();
            Place(w, rt);
            var area = go.transform;
            TestersPage.Box(area, "Back", new Color(0, 0, 0, 0.35f), 0, 0, 1, 1);

            // left: friends
            TestersPage.Text(area, "FriendsTitle", "Challenge a friend", 34, TextAlignmentOptions.Left, Color.white, 0.02f, 0.9f, 0.34f, 0.99f);
            var friends = Friends();
            int row = 0;
            foreach (var (id, name, state) in friends)
            {
                if (row >= 11) break;
                float top = 0.88f - row * 0.075f;
                bool picked = id == friendId;
                string hint = state == "online" ? "  (online)" : state == "playing" ? "  (in a match)" : "  (offline)";
                var b = TestersPage.Button(area, "Friend_" + id, "", 0.02f, top - 0.065f, 0.34f, top, picked ? new Color(0.35f, 0.25f, 0.08f, 1f) : TestersPage.Tile);
                TestersPage.Text(b.transform, "Name", name + hint, 24, TextAlignmentOptions.Left, state == "online" ? Color.white : TestersPage.Dim, 0.04f, 0, 0.98f, 1);
                string fid = id, fname = name;
                TestersPage.OnClick(b, () => { friendId = fid; friendName = fname; status = ""; dirty = true; });
                row++;
            }
            if (friends.Count == 0)
                TestersPage.Text(area, "NoFriends", "Add friends in the Friends tab first.", 24, TextAlignmentOptions.Left, TestersPage.Dim, 0.02f, 0.7f, 0.34f, 0.86f);

            // right: mode and rules
            float x0 = 0.38f, x1 = 0.98f;
            TestersPage.Text(area, "ModeTitle", "Mode", 26, TextAlignmentOptions.Left, TestersPage.Dim, x0, 0.92f, 0.5f, 0.99f);
            var prev = TestersPage.Button(area, "ModePrev", "<", 0.5f, 0.92f, 0.55f, 0.99f, TestersPage.Tile);
            TestersPage.Text(area, "Mode", ModeName(mode), 30, TextAlignmentOptions.Center, Color.white, 0.55f, 0.92f, 0.85f, 0.99f);
            var next = TestersPage.Button(area, "ModeNext", ">", 0.85f, 0.92f, 0.9f, 0.99f, TestersPage.Tile);
            TestersPage.OnClick(prev, () => { presetIndex = (presetIndex + presets.Count - 1) % presets.Count; values = null; dirty = true; });
            TestersPage.OnClick(next, () => { presetIndex = (presetIndex + 1) % presets.Count; values = null; dirty = true; });
            TestersPage.Text(area, "DeckRules", ChallengeRules.DeckRules(ev, mode) + ". You play your deck for this mode.", 20, TextAlignmentOptions.Left, TestersPage.Dim, x0, 0.86f, x1, 0.92f);

            var preset = ChallengeRules.Preset(ev, mode);
            int r = 0;
            foreach (var f in ChallengeRules.Fields)
            {
                float top = 0.84f - r * 0.06f;
                int v = values.Get(f.Key) ?? f.Min;
                bool changed = preset.Get(f.Key) != v;
                TestersPage.Text(area, "L_" + f.Key, f.Label, 22, TextAlignmentOptions.Left, changed ? new Color(1f, 0.85f, 0.45f, 1f) : Color.white, x0, top - 0.06f, 0.74f, top);
                var minus = TestersPage.Button(area, "M_" + f.Key, "-", 0.75f, top - 0.054f, 0.8f, top - 0.004f, TestersPage.Tile);
                TestersPage.Text(area, "V_" + f.Key, ChallengeRules.Show(f.Key, v), 24, TextAlignmentOptions.Center, Color.white, 0.8f, top - 0.06f, 0.9f, top);
                var plus = TestersPage.Button(area, "P_" + f.Key, "+", 0.9f, top - 0.054f, 0.95f, top - 0.004f, TestersPage.Tile);
                var field = f;
                TestersPage.OnClick(minus, () => Step(field, -1));
                TestersPage.OnClick(plus, () => Step(field, +1));
                r++;
            }

            float by = 0.84f - r * 0.06f - 0.015f;
            var reset = TestersPage.Button(area, "Reset", "Mode's own rules", x0, by - 0.075f, 0.62f, by, TestersPage.Tile);
            TestersPage.OnClick(reset, () => { values = null; status = ""; dirty = true; });
            var send = TestersPage.Button(area, "Send", friendId == null ? "Pick a friend" : "Challenge " + friendName, 0.64f, by - 0.075f, x1, by,
                                          friendId == null ? TestersPage.Tile : new Color(0.45f, 0.12f, 0.08f, 1f));
            TestersPage.OnClick(send, () => Send(mode, ev));
            if (!string.IsNullOrEmpty(status))
                TestersPage.Text(area, "Status", status, 22, TextAlignmentOptions.Left, TestersPage.Dim, x0, 0.0f, x1, by - 0.08f);
            if (!builtLogged) { builtLogged = true; RevivalMod.Log.Msg($"[challenge] menu built: {friends.Count} friend(s), presets {string.Join(", ", presets)}"); }
        }

        private static void Step(ChallengeRules.Field f, int dir)
        {
            if (values == null) return;
            int v = values.Get(f.Key) ?? f.Min;
            int next = Math.Clamp(v + dir * f.Step, f.Min, f.Max);
            // the starting hand cannot go past the hand limit (nor the second player's hand with its extra cards)
            if (f.Key == "startingHand" && next > (values.Get("handLimit") ?? 99)) return;
            if (f.Key == "secondPlayerExtraCards" && (values.Get("startingHand") ?? 0) + next > (values.Get("handLimit") ?? 99)) return;
            values.Values[f.Key] = next;
            ChallengeRules.CapHand(values.Values);       // a lower hand limit pulls the starting hand down with it
            dirty = true;
        }

        private static void Send(string mode, IPlayEvent ev)
        {
            try
            {
                if (friendId == null) { status = "Pick a friend on the left first."; dirty = true; return; }
                var manager = UnityEngine.Object.FindObjectOfType<ChallengeManager>();
                if ((object)manager == null) { status = "Challenges are not available right now."; dirty = true; return; }
                var rules = new ChallengeRules.RuleSet { EventId = mode, Values = new Dictionary<string, int>(values.Values) };
                ChallengeRules.Outgoing(friendId, rules);
                bool first = UnityEngine.Random.value < 0.5f;
                RevivalMod.Log.Msg($"[challenge] challenging {friendId} in {mode}");
                manager.StartChallengeWithUser(friendName ?? friendId, friendId, first, ev);
                status = $"Challenge sent to {friendName}. They see your rules when it arrives.";
                dirty = true;
            }
            catch (Exception e) { status = "Could not send the challenge: " + e.Message; dirty = true; RevivalMod.Log.Warning("[challenge] send: " + e); }
        }

        private static List<(string id, string name, string state)> Friends()
        {
            var list = new List<(string, string, string)>();
            try
            {
                var fl = PlayerDataManager.singletonManager?.friendsData?.friendList;
                if (fl != null)
                    for (int i = 0; i < fl.Count; i++)
                    {
                        var f = fl[i];
                        if ((object)f == null || string.IsNullOrEmpty(f.id)) continue;
                        list.Add((f.id, string.IsNullOrEmpty(f.name) ? f.id : f.name, GlobalChat.FriendStatus(f.id)));
                    }
            }
            catch { }
            int Rank(string s) => s == "online" ? 0 : s == "playing" ? 1 : 2;
            list.Sort((a, b) => Rank(a.Item3) != Rank(b.Item3) ? Rank(a.Item3).CompareTo(Rank(b.Item3)) : string.Compare(a.Item2, b.Item2, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        /// <summary>The menu's area: right of the inner ribbon, below the window's top bar.</summary>
        private static void Place(GameWindowWithTabs w, RectTransform panel)
        {
            var parent = panel.parent.TryCast<RectTransform>();
            if ((object)parent == null) return;
            var pr = parent.rect;
            float left = pr.xMin + 360, right = pr.xMax - 60, top = pr.yMax - pr.height * 0.17f, bottom = pr.yMin + pr.height * 0.05f;
            try
            {
                var rib = w.Components?.TabButtons?.GetComponent<RectTransform>();
                if ((object)rib != null)
                {
                    var corners = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(4);
                    rib.GetWorldCorners(corners);
                    left = parent.InverseTransformPoint(corners[2]).x + 28;
                }
            }
            catch { }
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(Mathf.Max(10, right - left), Mathf.Max(10, top - bottom));
            panel.anchoredPosition = new Vector2((left + right) / 2, (top + bottom) / 2) - pr.center;
        }

        // The Alliances tab's own start-up asks the server about alliances, which do not exist here.
        [HarmonyPatch(typeof(AlliancesTab), nameof(AlliancesTab.Start))]
        private static class NoAllianceStart { private static bool Prefix() => false; }

        [HarmonyPatch(typeof(AlliancesTab), nameof(AlliancesTab.ToFocus))]
        private static class NoAllianceFocus { private static bool Prefix() => false; }

        [HarmonyPatch(typeof(AlliancesTab), nameof(AlliancesTab.Refresh))]
        private static class NoAllianceRefresh { private static bool Prefix() => false; }
    }
}
