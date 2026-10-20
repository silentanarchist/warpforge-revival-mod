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
    ///
    /// 0.14.17: a player can also be picked by friend code (a box under the friend list; the server finds the player
    /// and lets the two games exchange the challenge although they are not friends), and the game's own duel popup
    /// (Classic / Skirmish on a profile or the alliance member list), whose challenge cannot carry the mode or any
    /// rules, now opens this page with that player and mode picked instead.
    /// </summary>
    internal static class ChallengeMenu
    {
        private const string PanelName = "RevivalChallengeMenu", TabName = "RevivalChallengeTab";
        private static SocialMenuWindow window;
        private static AlliancesTab tab;
        private static bool builtLogged;

        // menu state, kept while the game runs
        private static string friendId, friendName;
        private static string codeId, codeName, codeState;    // the player found by friend code (shown under the list)
        private static TMP_InputField codeBox;
        private static string codeText = "";                  // what was typed, kept across redraws
        private static bool codeBusy;
        private static int presetIndex;
        private static ChallengeRules.RuleSet values;
        private static string status = "";
        private static bool dirty = true;


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
            // the two presets carry the game's own mode names (the server's event titles are English only)
            if (id == "RevivalClassic") return Loc.Game("MainMenu/GameMode/Classic", "Classic");
            if (id == "RevivalSkirmish") return Loc.Game("MainMenu/GameMode/Skirmish", "Skirmish");
            var ev = ChallengeRules.FindEvent(id);
            return (object)ev == null ? id : TestersPage.Label(ev.TryCast<LiveOpsEvent>(), EventLabelReferenceType.Title, id);
        }

        /// <summary>True for the Challenge tab (the copy), not the real Alliances tab.</summary>
        internal static bool IsChallengeTab(UnityEngine.Object t) => Ui.Alive(tab) && (object)t != null && t.Pointer == tab.Pointer;

        private static int drawnLoc;
        private static readonly List<RectTransform> scrollContents = new List<RectTransform>();
        private static int replaceBars;
        /// <summary>
        /// The page's text larger than first drawn (x1.7). A text that would not fit its place shrinks back, down to its
        /// first size at the least.
        /// </summary>
        private static void Enlarge(Transform panel)
        {
            const float scale = 1.7f;
            foreach (var t in panel.GetComponentsInChildren<TMP_Text>(true))
            {
                float size = t.fontSize;
                t.enableAutoSizing = true;
                t.fontSizeMin = size;
                t.fontSizeMax = size * scale;
                t.fontSize = size * scale;
            }
        }

        /// <summary>Called every frame: redraws the page after a click on it (picking a friend, a mode, a rule) or a change of language.</summary>
        internal static void Tick()
        {
            if (drawnLoc != Loc.Version) { drawnLoc = Loc.Version; dirty = true; }     // the language changed: draw it again in the new one
            if (openSince >= 0) FinishOpen();
            if (replaceBars > 0 && --replaceBars == 0)
                foreach (var c in scrollContents) if (Ui.Alive(c)) Scrolling.Improve(c);
            if (!dirty) return;
            try
            {
                if (!Ui.Alive(window) || !Ui.Alive(tab) || !tab.gameObject.activeInHierarchy) return;
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
                    MenuLayout.StripBadges(buttonGo);            // not the alliance invitations' count
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
                    if (!Ui.Alive(tab) || !Ui.Alive(window) || __instance.Pointer != window.Pointer) return;
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
            try { if (IsChallengeTab(to) && Ui.Alive(window)) Build(window, tab.transform); }
            catch (Exception e) { RevivalMod.Log.Warning("[challenge] menu: " + e.Message); }
        }

        // ---------------------------------------------------------------- the menu

        private static void Build(GameWindowWithTabs w, Transform host)
        {
            dirty = false;
            scrollContents.Clear();
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
            TestersPage.Text(area, "FriendsTitle", Loc.T("challenge.title", "Challenge a friend"), 34, TextAlignmentOptions.Left, Color.white, 0.02f, 0.9f, 0.34f, 0.99f);
            var friends = Friends();
            // the list scrolls on its own (0.14.17): rows of a fixed height inside a scroll area with a bar
            const float friendRow = 0.075f, friendGap = 0.01f;
            var list = ScrollArea(area, "FriendList", 0.02f, 0.3f, 0.34f, 0.88f, Math.Max(1, friends.Count) * friendRow, out float listH);
            int row = 0;
            foreach (var (id, name, state) in friends)
            {
                float top = 1f - row * friendRow / listH, bottom = top - (friendRow - friendGap) / listH;
                bool picked = id == friendId;
                string hint = "  (" + (state == "online" ? Loc.T("challenge.online", "online") : state == "playing" ? Loc.T("challenge.playing", "in a match") : Loc.Game("MainMenu/General/Offline", "Offline")) + ")";
                var b = TestersPage.Button(list, "Friend_" + id, "", 0f, bottom, 1f, top, picked ? new Color(0.35f, 0.25f, 0.08f, 1f) : TestersPage.Tile);
                TestersPage.Text(b.transform, "Name", name + hint, 24, TextAlignmentOptions.Left, state == "online" ? Color.white : TestersPage.Dim, 0.04f, 0, 0.98f, 1);
                string fid = id, fname = name;
                TestersPage.OnClick(b, () => { friendId = fid; friendName = fname; status = ""; dirty = true; });
                row++;
            }
            if (friends.Count == 0)
                TestersPage.Text(list, "NoFriends", Loc.T("challenge.noFriends", "Add friends in the Friends tab first."), 24, TextAlignmentOptions.Left, TestersPage.Dim, 0f, 0f, 1f, 1f);
            CodeBox(w, area);

            // right: mode and rules
            float x0 = 0.38f, x1 = 0.98f;
            TestersPage.Text(area, "ModeTitle", Loc.T("challenge.mode", "Mode"), 26, TextAlignmentOptions.Left, TestersPage.Dim, x0, 0.92f, 0.5f, 0.99f);
            var prev = TestersPage.Button(area, "ModePrev", "<", 0.5f, 0.92f, 0.55f, 0.99f, TestersPage.Tile);
            TestersPage.Text(area, "Mode", ModeName(mode), 30, TextAlignmentOptions.Center, Color.white, 0.55f, 0.92f, 0.85f, 0.99f);
            var next = TestersPage.Button(area, "ModeNext", ">", 0.85f, 0.92f, 0.9f, 0.99f, TestersPage.Tile);
            TestersPage.OnClick(prev, () => { presetIndex = (presetIndex + presets.Count - 1) % presets.Count; values = null; dirty = true; });
            TestersPage.OnClick(next, () => { presetIndex = (presetIndex + 1) % presets.Count; values = null; dirty = true; });
            TestersPage.Text(area, "DeckRules", ChallengeRules.DeckRules(ev, mode) + " " + Loc.T("challenge.yourDeck", "You play your deck for this mode."), 20, TextAlignmentOptions.Left, TestersPage.Dim, x0, 0.86f, x1, 0.92f);

            var preset = ChallengeRules.Preset(ev, mode);
            // the rules scroll on their own (0.14.17), so more can be added and the text is larger; the buttons stay at the bottom
            const float ruleRow = 0.066f;
            var rules = ScrollArea(area, "RuleList", x0, 0.17f, 0.96f, 0.85f, ChallengeRules.Fields.Length * ruleRow, out float rulesH);
            int r = 0;
            foreach (var f in ChallengeRules.Fields)
            {
                float top = 1f - r * ruleRow / rulesH, bottom = top - ruleRow / rulesH, pad = 0.004f / rulesH;
                int v = values.Get(f.Key) ?? f.Min;
                bool changed = preset.Get(f.Key) != v;
                TestersPage.Text(rules, "L_" + f.Key, f.Text(), 26, TextAlignmentOptions.Left, changed ? new Color(1f, 0.85f, 0.45f, 1f) : Color.white, 0.01f, bottom, 0.6f, top);
                TestersPage.Text(rules, "V_" + f.Key, ChallengeRules.Show(f.Key, v), 27, TextAlignmentOptions.Center, Color.white, 0.7f, bottom, 0.87f, top);
                var field = f;
                // a button that would not change anything (at the lowest or highest value) is left out
                if (Next(field, -1) != v)
                {
                    var minus = TestersPage.Button(rules, "M_" + f.Key, "-", 0.61f, bottom + pad, 0.69f, top - pad, TestersPage.Tile);
                    TestersPage.OnClick(minus, () => Step(field, -1));
                }
                if (Next(field, +1) != v)
                {
                    var plus = TestersPage.Button(rules, "P_" + f.Key, "+", 0.88f, bottom + pad, 0.96f, top - pad, TestersPage.Tile);
                    TestersPage.OnClick(plus, () => Step(field, +1));
                }
                r++;
            }

            const float by = 0.1f;
            var reset = TestersPage.Button(area, "Reset", Loc.T("challenge.reset", "Mode's own rules"), x0, by - 0.08f, 0.62f, by, TestersPage.Tile);
            TestersPage.OnClick(reset, () => { values = null; status = ""; dirty = true; });
            var send = TestersPage.Button(area, "Send", friendId == null ? Loc.T("challenge.pickFriend", "Pick a friend") : Loc.T("challenge.send", "Challenge {name}", ("name", friendName)), 0.64f, by - 0.08f, x1, by,
                                          friendId == null ? TestersPage.Tile : new Color(0.45f, 0.12f, 0.08f, 1f));
            TestersPage.OnClick(send, () => Send(mode, ev));
            if (!string.IsNullOrEmpty(status))
                TestersPage.Text(area, "Status", status, 22, TextAlignmentOptions.Left, TestersPage.Dim, x0, by + 0.01f, x1, 0.16f);
            Enlarge(go.transform);
            if (!builtLogged) { builtLogged = true; RevivalMod.Log.Msg($"[challenge] menu built: {friends.Count} friend(s), presets {string.Join(", ", presets)}"); }
        }

        /// <summary>
        /// A scroll area (wheel, drag, and a bar down its right edge) at the given place in the panel. Its content is
        /// contentFrac of the panel's height tall (at least the area itself); rows are placed in it with anchors as
        /// fractions of the content height, which is returned in contentH (in the same panel-height units).
        /// </summary>
        private static Transform ScrollArea(Transform panel, string name, float x0, float y0, float x1, float y1, float contentFrac, out float contentH)
        {
            float viewH = y1 - y0;
            contentH = Math.Max(viewH, contentFrac);
            var view = TestersPage.Box(panel, name, new Color(0f, 0f, 0f, 0.18f), x0, y0, x1, y1);
            view.gameObject.AddComponent<RectMask2D>();
            var scroll = view.gameObject.AddComponent<ScrollRect>();
            var content = new GameObject("Content");
            var crt = content.AddComponent<RectTransform>();
            crt.SetParent(view, false);
            crt.anchorMin = new Vector2(0f, 1f); crt.anchorMax = new Vector2(1f, 1f); crt.pivot = new Vector2(0.5f, 1f);
            float panelH = panel.GetComponent<RectTransform>().rect.height;
            crt.sizeDelta = new Vector2(0f, panelH * contentH);
            crt.anchoredPosition = Vector2.zero;
            scroll.content = crt;
            scroll.viewport = view.GetComponent<RectTransform>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 60f;
            if (contentH > viewH + 0.0001f)
            {
                // the bar sits just outside the area's right edge (Scrolling attaches it to the panel around the list);
                // placed again next frame, once the new objects have their sizes
                Scrolling.Improve(crt);
                scrollContents.Add(crt);
                replaceBars = 2;
            }
            return crt;
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
                if (friendId == null) { status = Loc.T("challenge.pickFriendFirst", "Pick a friend on the left first."); dirty = true; return; }
                var manager = UnityEngine.Object.FindObjectOfType<ChallengeManager>();
                if ((object)manager == null) { status = Loc.T("challenge.unavailable", "Challenges are not available right now."); dirty = true; return; }
                var rules = new ChallengeRules.RuleSet { EventId = mode, Values = new Dictionary<string, int>(values.Values) };
                ChallengeRules.Outgoing(friendId, rules);
                // who goes first: the rules' choice (1 the challenger, 2 the friend), otherwise a coin toss as before;
                // MatchOptions makes sure of it on both sides once the match starts
                int fp = rules.Get("firstPlayer") ?? 0;
                bool first = fp == 1 ? true : fp == 2 ? false : UnityEngine.Random.value < 0.5f;
                RevivalMod.Log.Msg($"[challenge] challenging {friendId} in {mode}");
                manager.StartChallengeWithUser(friendName ?? friendId, friendId, first, ev);
                status = Loc.T("challenge.sent", "Challenge sent to {name}. They see your rules when it arrives.", ("name", friendName));
                dirty = true;
            }
            catch (Exception e) { status = Loc.T("challenge.sendFailed", "Could not send the challenge: {error}", ("error", e.Message)); dirty = true; RevivalMod.Log.Warning("[challenge] send: " + e); }
        }

        /// <summary>Under the friend list: the player found by friend code (if any), a box for a code and a Find button.</summary>
        private static void CodeBox(GameWindowWithTabs w, Transform area)
        {
            try
            {
                const float top = 0.27f;        // found player 0.205-0.27, label 0.14-0.19, box 0.07-0.13
                TestersPage.Text(area, "CodeTitle", Loc.T("challenge.byCode", "Or by friend code"), 24, TextAlignmentOptions.Left, TestersPage.Dim, 0.02f, top - 0.13f, 0.34f, top - 0.08f);
                if (codeId != null)
                {
                    bool picked = codeId == friendId;
                    string hint = "  (" + (codeState == "online" ? Loc.T("challenge.online", "online") : codeState == "playing" ? Loc.T("challenge.playing", "in a match") : Loc.Game("MainMenu/General/Offline", "Offline")) + ")";
                    var b = TestersPage.Button(area, "Friend_code", "", 0.02f, top - 0.065f, 0.34f, top, picked ? new Color(0.35f, 0.25f, 0.08f, 1f) : TestersPage.Tile);
                    TestersPage.Text(b.transform, "Name", codeName + hint, 24, TextAlignmentOptions.Left, codeState == "online" ? Color.white : TestersPage.Dim, 0.04f, 0, 0.98f, 1);
                    TestersPage.OnClick(b, () => { friendId = codeId; friendName = codeName; status = ""; dirty = true; });
                }
                // the box: a copy of the Friends tab's add-friend box (the game's own look and keyboard handling)
                FriendsTab friends = null;
                foreach (var f in w.GetComponentsInChildren<FriendsTab>(true)) { friends = f; break; }
                var input = (object)friends == null ? null : friends.inputFieldUserName;
                if ((object)input == null) return;
                var boxGo = UnityEngine.Object.Instantiate(input.gameObject, area, false);
                boxGo.name = "Friend code box";
                foreach (var le in boxGo.GetComponents<LayoutElement>()) le.ignoreLayout = true;
                var rt = boxGo.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.02f, top - 0.2f); rt.anchorMax = new Vector2(0.24f, top - 0.14f);
                rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
                codeBox = boxGo.GetComponent<TMP_InputField>();
                if ((object)codeBox != null)
                {
                    codeBox.onSubmit = new TMP_InputField.SubmitEvent();
                    codeBox.onEndEdit = new TMP_InputField.SubmitEvent();
                    codeBox.onValueChanged = new TMP_InputField.OnChangeEvent();
                    codeBox.characterLimit = 24;
                    codeBox.SetTextWithoutNotify(codeText ?? "");
                    var ph = codeBox.placeholder?.TryCast<TMP_Text>();
                    if ((object)ph != null) Ui.SetText(ph, Loc.T("challenge.codePlaceholder", "Friend code"));
                    codeBox.onValueChanged.AddListener(Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction<string>>(new Action<string>(t => codeText = t)));
                    codeBox.onSubmit.AddListener(Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction<string>>(new Action<string>(_ => FindByCode())));
                }
                boxGo.SetActive(true);
                var find = TestersPage.Button(area, "FindCode", codeBusy ? "..." : Loc.T("challenge.find", "Find"), 0.25f, top - 0.2f, 0.34f, top - 0.14f, TestersPage.Tile);
                TestersPage.OnClick(find, FindByCode);
            }
            catch (Exception e) { RevivalMod.Log.Warning("[challenge] friend code box: " + e.Message); }
        }

        private static void FindByCode()
        {
            string code = (codeText ?? "").Trim();
            if (codeBusy) return;
            if (code.Length == 0) { status = Loc.T("challenge.typeCode", "Type a friend code first."); dirty = true; return; }
            codeBusy = true;
            status = ""; dirty = true;
            System.Threading.Tasks.Task.Run(async () =>
            {
                string id = null, name = null, state = null, error = null;
                try
                {
                    var reply = await Net.PostJson("/playfab/Revival/ChallengeByCode", Net.Json(w => w.WriteString("code", code)), TimeSpan.FromSeconds(10));
                    var d = reply.Data;
                    if (reply.Ok && d.ValueKind == System.Text.Json.JsonValueKind.Object && d.TryGetProperty("id", out var i))
                    {
                        id = i.GetString();
                        name = d.TryGetProperty("name", out var n) ? n.GetString() : id;
                        state = d.TryGetProperty("state", out var st) ? st.GetString() : "offline";
                    }
                    else error = reply.Error ?? Loc.T("challenge.noSuchCode", "No player with that friend code.");
                }
                catch (Exception e) { error = e.Message; }
                PlayFabTransport.OnMainThread(() =>
                {
                    codeBusy = false;
                    if (id != null)
                    {
                        codeId = id; codeName = name; codeState = state;
                        friendId = id; friendName = name;
                        RevivalMod.Log.Msg($"[challenge] friend code found: {id} ({state})");
                    }
                    else status = error;
                    dirty = true;
                });
            });
        }

        /// <summary>
        /// The game's own duel popup (Classic / Skirmish on a profile or an alliance member): its challenge can carry
        /// neither the mode nor any rules (the friend's game plays it as a default challenge), so it opens this page
        /// instead, with that player and mode picked.
        /// </summary>
        private static float openSince = -1f;
        private static bool pendingSkirmish;

        private static void FromDuelPopup(DuelPopupWindow popup, bool skirmish)
        {
            try
            {
                var data = popup.playerDuelData;
                string id = (object)data == null ? null : data.PlayerId, name = (object)data == null ? null : data.PlayerName;
                if (string.IsNullOrEmpty(id)) { RevivalMod.Log.Warning("[challenge] duel popup without a player"); return; }
                codeId = friendId = id; codeName = friendName = string.IsNullOrEmpty(name) ? id : name; codeState = GlobalChat.FriendStatus(id);
                pendingSkirmish = skirmish;
                status = ""; dirty = true;
                try { popup.OnCloseButtonPressed(); } catch { try { popup.Close(); } catch { } }
                RevivalMod.Log.Msg($"[challenge] duel popup: opening the Challenge page for {id} ({(skirmish ? "Skirmish" : "Classic")})");
                OpenPage();
            }
            catch (Exception e) { RevivalMod.Log.Warning("[challenge] duel popup: " + e.Message); }
        }

        private static void OpenPage()
        {
            openSince = Time.realtimeSinceStartup;
            try
            {
                if (Ui.Alive(window) && window.IsOpen()) { FinishOpen(); return; }
                var wm = WindowsManager.Instance;
                SocialMenuWindow social = null;
                foreach (var m in Resources.FindObjectsOfTypeAll<SocialMenuWindow>())
                    if ((object)m != null && m.gameObject.scene.IsValid()) { social = m; break; }
                if ((object)social != null) { wm.OpenWindow(social, false, null); return; }
                foreach (var b in UnityEngine.Object.FindObjectsOfType<OpenWindowButton>())
                {
                    var scene = b.windowToOpenScene;
                    if ((object)scene != null && (object)scene.TryCast<SocialMenuWindow>() != null) { b.OpenWindow(); return; }
                }
                RevivalMod.Log.Warning("[challenge] the Social window was not found");
                openSince = -1f;
            }
            catch (Exception e) { RevivalMod.Log.Warning("[challenge] could not open the Social window: " + e.Message); openSince = -1f; }
        }

        /// <summary>Once the Social window is open: switch to the Challenge tab with the picked mode.</summary>
        private static void FinishOpen()
        {
            if (openSince < 0) return;
            if (Ui.Alive(window) && window.IsOpen() && Ui.Alive(tab))
            {
                openSince = -1f;
                var presets = Presets();
                int want = presets.IndexOf(pendingSkirmish ? "RevivalSkirmish" : "RevivalClassic");
                if (want >= 0 && want != presetIndex) { presetIndex = want; values = null; }
                dirty = true;
                try { window.ChangeTab(tab); } catch (Exception e) { RevivalMod.Log.Warning("[challenge] could not switch to the Challenge tab: " + e.Message); }
                return;
            }
            if (Time.realtimeSinceStartup - openSince > 5f) { openSince = -1f; RevivalMod.Log.Warning("[challenge] the Social window did not open"); }
        }

        [HarmonyPatch(typeof(DuelPopupWindow), nameof(DuelPopupWindow.OnClassicButtonPressed))]
        private static class DuelClassic { private static bool Prefix(DuelPopupWindow __instance) { FromDuelPopup(__instance, false); return false; } }

        [HarmonyPatch(typeof(DuelPopupWindow), nameof(DuelPopupWindow.OnSkirmishButtonPressed))]
        private static class DuelSkirmish { private static bool Prefix(DuelPopupWindow __instance) { FromDuelPopup(__instance, true); return false; } }

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
            float left = pr.xMin + 360, right = pr.xMax - 60, top = pr.yMax - pr.height * 0.09f, bottom = pr.yMin + pr.height * 0.015f;   // 0.14.7: taller, for the larger text
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
        private static class NoAllianceRefresh
        {
            // the real tab: AllianceConfig.ShouldRefresh breaks the game's refresh loop (0.13.60)
            private static bool Prefix(AlliancesTab __instance) => !IsChallengeTab(__instance) && AllianceConfig.ShouldRefresh();
        }
    }
}
