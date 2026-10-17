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
    /// Social > Challenge: challenge a friend with rules of your choosing, on a tab of its own in the Social window
    /// (0.13.52; before, the unused Alliances tab was taken over, and the page was swapped in on a 0.25 s timer).
    ///
    /// How the tab is added (decompiled 2026-10-16): GameWindowWithTabs.SetupTabs runs once per window, before it is
    /// shown; it wires up every button in the ribbon's list (TabButtons.Initialize) and then sets up the tabs in the
    /// window's tab list. In a prefix the Alliances tab is copied (its class does nothing on its own: Start and
    /// Refresh, which load alliances, are blocked for the copy only) and a copy of its button is added to the ribbon
    /// with the game's own TabButtons.AssignTabButton, so the game wires its click like any other. In a postfix the
    /// copy is set up and added to the tab list after the game has filed its tabs by class, so the game's lookups
    /// (GetTab&lt;AlliancesTab&gt;) still find the real Alliances tab. From then on the game opens and closes the
    /// Challenge tab itself; the page lives inside it and is drawn when the tab is picked (the window's OnTabChanged
    /// event), in the same frame. The button's
    /// label, icon and place come from MenuLayout ("Challenge"). The real Alliances tab is the game's own again
    /// (hidden by MenuLayout until alliances work on the server).
    ///
    /// The page: a friend to challenge, a game mode as the preset (its deck rules, and the starting values of the
    /// match rules) and the match rules themselves. Sending goes through the game's own friend challenge, with the
    /// rules added (see ChallengeRules).
    /// </summary>
    internal static class ChallengeMenu
    {
        private const string PanelName = "RevivalChallengeMenu", TabName = "RevivalChallengeTab";
        private static SocialMenuWindow window;
        private static AlliancesTab tab;
        private static bool builtLogged;

        // menu state, kept while the game runs
        private static string friendId, friendName;
        private static int presetIndex;
        private static ChallengeRules.RuleSet values;
        private static string status = "";
        private static bool dirty = true;

        private static bool Alive(UnityEngine.Object o) => (object)o != null && o.Pointer != IntPtr.Zero && o.m_CachedPtr != IntPtr.Zero;

        /// <summary>Modes offered as presets: Classic and Skirmish (the two the game's own challenge can carry).</summary>
        private static List<string> Presets()
        {
            var ids = new List<string>();
            foreach (var id in new[] { "RevivalClassic", "RevivalSkirmish" })
                if ((object)ChallengeRules.FindEvent(id) != null) ids.Add(id);
            // Custom Test is left out for now: the friend's game picks its deck from the game's own Classic /
            // Skirmish choice, so a Custom Test challenge would pit a Custom Test deck against a Classic one.
            return ids;
        }

        private static string ModeName(string id)
        {
            var ev = ChallengeRules.FindEvent(id);
            return (object)ev == null ? id : TestersPage.Label(ev.TryCast<LiveOpsEvent>(), EventLabelReferenceType.Title, id);
        }

        /// <summary>True for the Challenge tab (the copy), not the real Alliances tab.</summary>
        internal static bool IsChallengeTab(UnityEngine.Object t) => Alive(tab) && (object)t != null && t.Pointer == tab.Pointer;

        /// <summary>Called every frame: redraws the page after a click on it (picking a friend, a mode, a rule).</summary>
        internal static void Tick()
        {
            if (!dirty) return;
            try
            {
                if (!Alive(window) || !Alive(tab) || !tab.gameObject.activeInHierarchy) return;
                Build(window, tab.transform);
            }
            catch (Exception e) { RevivalMod.Log.Warning("[challenge] menu: " + e.Message); dirty = false; }
        }

        // ---------------------------------------------------------------- the tab

        [HarmonyPatch(typeof(GameWindowWithTabs), nameof(GameWindowWithTabs.SetupTabs))]
        private static class AddTab
        {
            /// <summary>Before the game wires up the ribbon: the tab and its button.</summary>
            private static void Prefix(GameWindowWithTabs __instance)
            {
                try
                {
                    var social = __instance.TryCast<SocialMenuWindow>();
                    if ((object)social == null) return;
                    var alliances = __instance.GetComponentInChildren<AlliancesTab>(true);
                    var buttons = __instance.Components?.TabButtons;
                    if ((object)alliances == null || (object)buttons == null || buttons.options == null)
                    { RevivalMod.Log.Warning("[challenge] Social window has no Alliances tab or ribbon to copy; no Challenge tab"); return; }
                    TabButtons.TabButton source = null;
                    for (int i = 0; i < buttons.options.Count; i++)
                    {
                        var b = buttons.options[i];
                        if (b != null && (object)b.tab != null && b.tab.Pointer == alliances.Pointer && (object)b.toggle != null) { source = b; break; }
                    }
                    if (source == null) { RevivalMod.Log.Warning("[challenge] the Alliances tab has no button to copy; no Challenge tab"); return; }

                    // Both copies are made under an inactive holder, so nothing in them (the alliance pages, the
                    // button's alliances switch) starts up before it is taken out.
                    var holder = new GameObject("RevivalChallengeHolder");
                    holder.SetActive(false);

                    // the tab: a copy of the Alliances tab with its alliance pages taken out
                    var tabGo = UnityEngine.Object.Instantiate(alliances.gameObject, holder.transform, false);
                    tabGo.name = TabName;
                    var copyTab = tabGo.GetComponent<AlliancesTab>();
                    for (int i = tabGo.transform.childCount - 1; i >= 0; i--)
                        UnityEngine.Object.DestroyImmediate(tabGo.transform.GetChild(i).gameObject);
                    tabGo.SetActive(false);
                    tabGo.transform.SetParent(alliances.transform.parent, false);
                    tabGo.transform.SetSiblingIndex(alliances.transform.GetSiblingIndex() + 1);

                    // the button: a copy of the Alliances button, without its alliances switch
                    var buttonGo = UnityEngine.Object.Instantiate(source.toggle.gameObject, holder.transform, false);
                    buttonGo.name = "Challenge Tab Button";
                    foreach (var fc in buttonGo.GetComponents<FeatureChecker>()) UnityEngine.Object.DestroyImmediate(fc);
                    buttonGo.SetActive(true);
                    buttonGo.transform.SetParent(source.toggle.transform.parent, false);
                    UnityEngine.Object.Destroy(holder);
                    var toggle = buttonGo.GetComponent<EverguildToggle>();
                    if ((object)toggle == null) { UnityEngine.Object.Destroy(buttonGo); UnityEngine.Object.Destroy(tabGo); RevivalMod.Log.Warning("[challenge] copied button is not a tab button"); return; }
                    toggle.isOn = false;
                    buttons.AssignTabButton(copyTab, toggle);       // wired up by the game in TabButtons.Initialize, next

                    window = social;
                    tab = copyTab;
                    MenuLayout.Name(copyTab, "Challenge");
                }
                catch (Exception e) { RevivalMod.Log.Warning("[challenge] could not add the Challenge tab: " + e); }
            }

            /// <summary>After the game has filed its tabs: the copy set up, listed, closed, and its page drawn.</summary>
            private static void Postfix(GameWindowWithTabs __instance)
            {
                try
                {
                    if (!Alive(tab) || !Alive(window) || __instance.Pointer != window.Pointer) return;
                    tab.Setup(__instance);
                    var tabs = __instance.tabs;
                    if (tabs != null && !tabs.Contains(tab)) tabs.Add(tab);     // after the game filed them by class
                    if (tab.gameObject.activeSelf) tab.gameObject.SetActive(false);
                    var onChange = Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Il2CppSystem.Action<WindowTabBase>>(new Action<WindowTabBase>(TabChanged));
                    __instance.OnTabChanged = __instance.OnTabChanged == null
                        ? onChange
                        : Il2CppSystem.Delegate.Combine(__instance.OnTabChanged, onChange).Cast<Il2CppSystem.Action<WindowTabBase>>();
                    Build(window, tab.transform);
                    RevivalMod.Log.Msg("[challenge] Challenge tab added to the Social window");
                }
                catch (Exception e) { RevivalMod.Log.Warning("[challenge] Challenge tab set-up: " + e.Message); }
            }
        }

        /// <summary>The window's own "tab changed" event (it lights the ribbon): the page is drawn again when the
        /// Challenge tab is picked, in the same frame (the friend list may have changed). No hook: 0.13.52 hooked
        /// WindowTabBase.TryOpenTab, which crashed the Android loader (its code starts 8 bytes before a page end).</summary>
        private static void TabChanged(WindowTabBase to)
        {
            try { if (IsChallengeTab(to) && Alive(window)) Build(window, tab.transform); }
            catch (Exception e) { RevivalMod.Log.Warning("[challenge] menu: " + e.Message); }
        }

        // ---------------------------------------------------------------- the menu

        private static void Build(GameWindowWithTabs w, Transform host)
        {
            dirty = false;
            var old = host.Find(PanelName);
            if ((object)old != null) { old.gameObject.SetActive(false); old.name = PanelName + " (old)"; UnityEngine.Object.Destroy(old.gameObject); }
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
            rt.SetParent(host, false);
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
            const float rowH = 0.049f;     // 14 rules (0.13.33 added carried-over mana) and the buttons fit below the mode
            foreach (var f in ChallengeRules.Fields)
            {
                float top = 0.84f - r * rowH;
                int v = values.Get(f.Key) ?? f.Min;
                bool changed = preset.Get(f.Key) != v;
                TestersPage.Text(area, "L_" + f.Key, f.Label, 21, TextAlignmentOptions.Left, changed ? new Color(1f, 0.85f, 0.45f, 1f) : Color.white, x0, top - rowH, 0.74f, top);
                TestersPage.Text(area, "V_" + f.Key, ChallengeRules.Show(f.Key, v), 22, TextAlignmentOptions.Center, Color.white, 0.8f, top - rowH, 0.9f, top);
                var field = f;
                // a button that would not change anything (at the lowest or highest value) is left out
                if (Next(field, -1) != v)
                {
                    var minus = TestersPage.Button(area, "M_" + f.Key, "-", 0.75f, top - rowH + 0.005f, 0.8f, top - 0.004f, TestersPage.Tile);
                    TestersPage.OnClick(minus, () => Step(field, -1));
                }
                if (Next(field, +1) != v)
                {
                    var plus = TestersPage.Button(area, "P_" + f.Key, "+", 0.9f, top - rowH + 0.005f, 0.95f, top - 0.004f, TestersPage.Tile);
                    TestersPage.OnClick(plus, () => Step(field, +1));
                }
                r++;
            }

            float by = 0.84f - r * rowH - 0.015f;
            var reset = TestersPage.Button(area, "Reset", "Mode's own rules", x0, by - 0.075f, 0.62f, by, TestersPage.Tile);
            TestersPage.OnClick(reset, () => { values = null; status = ""; dirty = true; });
            var send = TestersPage.Button(area, "Send", friendId == null ? "Pick a friend" : "Challenge " + friendName, 0.64f, by - 0.075f, x1, by,
                                          friendId == null ? TestersPage.Tile : new Color(0.45f, 0.12f, 0.08f, 1f));
            TestersPage.OnClick(send, () => Send(mode, ev));
            if (!string.IsNullOrEmpty(status))
                TestersPage.Text(area, "Status", status, 22, TextAlignmentOptions.Left, TestersPage.Dim, x0, 0.0f, x1, by - 0.08f);
            if (!builtLogged) { builtLogged = true; RevivalMod.Log.Msg($"[challenge] menu built: {friends.Count} friend(s), presets {string.Join(", ", presets)}"); }
        }

        /// <summary>What one press of - (dir -1) or + (dir +1) would set; the same value when the press would do nothing.</summary>
        private static int Next(ChallengeRules.Field f, int dir)
        {
            int v = values?.Get(f.Key) ?? f.Min;
            int next = Math.Clamp(v + dir * f.Step, f.Min, f.Max);
            // the starting hand cannot go past the hand limit (nor the second player's hand with its extra cards)
            if (f.Key == "startingHand" && next > (values?.Get("handLimit") ?? 99)) return v;
            if (f.Key == "secondPlayerExtraCards" && (values?.Get("startingHand") ?? 0) + next > (values?.Get("handLimit") ?? 99)) return v;
            return next;
        }

        private static void Step(ChallengeRules.Field f, int dir)
        {
            if (values == null) return;
            int v = values.Get(f.Key) ?? f.Min;
            int next = Next(f, dir);
            if (next == v) return;
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
                // who goes first: the rules' choice (1 the challenger, 2 the friend), otherwise a coin toss as before;
                // MatchOptions makes sure of it on both sides once the match starts
                int fp = rules.Get("firstPlayer") ?? 0;
                bool first = fp == 1 ? true : fp == 2 ? false : UnityEngine.Random.value < 0.5f;
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

        /// <summary>The menu's area: right of the inner ribbon, below the window's top bar (worked out in the
        /// window's space, then set wherever the page sits, here inside the Challenge tab).</summary>
        private static void Place(GameWindowWithTabs w, RectTransform panel)
        {
            var parent = w.GetComponent<RectTransform>();
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
            // size in the window's units, scaled to the page's own parent (the same canvas: normally 1)
            float scale = 1f;
            var host = panel.parent;
            if ((object)host != null && host.lossyScale.x > 0.0001f && parent.lossyScale.x > 0.0001f) scale = parent.lossyScale.x / host.lossyScale.x;
            panel.sizeDelta = new Vector2(Mathf.Max(10, right - left), Mathf.Max(10, top - bottom)) * scale;
            panel.position = parent.TransformPoint(new Vector3((left + right) / 2, (top + bottom) / 2, 0));
        }

        // The copy's own alliance loading is blocked; the real Alliances tab runs as the game made it.
        // (ToFocus is not patched: in this build it is a single jump into Refresh, and a hook on a 4-byte function
        // is the one thing that made the Android loader place code at an address of its own choosing.)

        [HarmonyPatch(typeof(AlliancesTab), nameof(AlliancesTab.Start))]
        private static class NoAllianceStart { private static bool Prefix(AlliancesTab __instance) => !IsChallengeTab(__instance); }

        [HarmonyPatch(typeof(AlliancesTab), nameof(AlliancesTab.Refresh))]
        private static class NoAllianceRefresh { private static bool Prefix(AlliancesTab __instance) => !IsChallengeTab(__instance); }
    }
}
