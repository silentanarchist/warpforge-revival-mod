using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using Il2CppEverguild.LiveOps;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.UI;

namespace WarpforgeRevival
{
    /// <summary>
    /// The Testers window, behind the Testers button in the menu bar. It is a separate copy of the
    /// game's Collection window (made from the same window file the Collection button opens, with
    /// its own loading handle, so the game's Collection is never touched), with its inner ribbon set
    /// up for testers:
    ///  - Play: each tester-only mode's own Play-screen tile (for now Custom Test), made the way the
    ///    Play screen makes them; clicking one opens the mode's page. (The copy's Cosmetics tab, with
    ///    its content swapped for these tiles.)
    ///  - Decks: the copy's deck tab, opened straight on the Custom Test deck list (no format picker).
    ///    Back closes the window. The window is given the mode, so other modes can be passed later.
    ///  - Cards: the copy's card list. As a check that it is separate, Space Wolves are left out.
    /// The Styles tab is hidden. The normal Collection > Decks keeps every format, Custom Test included.
    /// </summary>
    internal static class TestersCollection
    {
        private const string CopyName = "RevivalTestersWindow", PanelName = "RevivalTestersPlay";
        private static CollectionScreen copy;
        private static GameObject sourcePrefab;
        private static Sprite playIcon;
        private static int hidden;
        private static float nextDress, closedAt;
        private static bool placedLogged, switchOffChanged, unlitLogged;
        private static IntPtr groupPtr;
        private static bool buttonsLogged, dressedLogged;

        private static bool Alive(UnityEngine.Object o) => (object)o != null && o.Pointer != IntPtr.Zero && o.m_CachedPtr != IntPtr.Zero;

        /// <summary>True when this window is the Testers window (not the normal Collection).</summary>
        internal static bool IsUnder(Transform t)
        {
            if (!Alive(copy)) return false;
            for (; (object)t != null; t = t.parent)
                if (t.Pointer == copy.transform.Pointer) return true;
            return false;
        }

        internal static bool IsCopy(GameWindow w) => Alive(copy) && (object)w != null && w.Pointer == copy.Pointer;

        internal static void Close()
        {
            try { if (Alive(copy) && copy.IsOpen()) WindowsManager.Instance.CloseWindow(copy); }
            catch (Exception e) { RevivalMod.Log.Warning("[testers] " + e.Message); }
        }

        /// <summary>Open the Testers window. False when it could not be made (the caller falls back to the plain page).</summary>
        internal static bool Open()
        {
            try
            {
                var window = Copy();
                if ((object)window == null) return false;
                var wm = WindowsManager.Instance;
                if ((object)wm == null) { RevivalMod.Log.Warning("[testers] window manager not ready"); return false; }
                if (window.IsOpen()) return true;
                wm.OpenWindow(window, DecksContext(), true, null);
                RevivalMod.Log.Msg("[testers] Testers window opened");
                return true;
            }
            catch (Exception e) { RevivalMod.Log.Warning("[testers] could not open the Testers window: " + e); return false; }
        }

        /// <summary>The mode the Decks tab shows: the first tester mode on this server (Custom Test for now).</summary>
        private static CollectionScreen.OpenDeckCollectionContext DecksContext()
        {
            foreach (var id in TestersPage.TesterModes())
            {
                var ev = TestersPage.Find(id);
                var playEvent = (object)ev == null ? null : ev.TryCast<IPlayEvent>();
                if ((object)playEvent == null) continue;
                return new CollectionScreen.OpenDeckCollectionContext { eventReference = playEvent };
            }
            RevivalMod.Log.Warning("[testers] no tester mode for the Decks tab");
            return null;
        }

        /// <summary>The window, made the first time from the window file behind the menu bar's Collection button.</summary>
        private static CollectionScreen Copy()
        {
            if (Alive(copy)) return copy;
            copy = null;
            var nav = UnityEngine.Object.FindObjectOfType<NavigationPanelController>();
            var toggle = (object)nav == null ? null : nav.GetToggle(NavigationPanelToggleType.Collection);
            if ((object)toggle == null) { RevivalMod.Log.Warning("[testers] menu bar Collection button not found"); return null; }
            var opener = toggle.GetComponentInChildren<OpenWindowButton>(true);
            if ((object)opener == null) { RevivalMod.Log.Warning("[testers] Collection button has no window behind it"); return null; }
            var prefabRef = opener.windowToOpenPrefab;
            if ((object)prefabRef == null || !prefabRef.RuntimeKeyIsValid())
            {
                // a window that lives in the scene would have to be copied while in use; not done
                RevivalMod.Log.Warning("[testers] the Collection window is not a window file; Testers window not made");
                return null;
            }
            if (!Alive(sourcePrefab))
            {
                // our own handle on the file: the game's reference object (and its handle) is left alone
                var loaded = new AssetReference(prefabRef.AssetGUID).LoadAssetAsync<GameObject>().WaitForCompletion();
                if (!Alive(loaded) || (object)loaded.GetComponent<CollectionScreen>() == null)
                {
                    RevivalMod.Log.Warning("[testers] Collection window file not loaded");
                    return null;
                }
                sourcePrefab = loaded;
            }
            PlayIcon(nav);

            var placement = sourcePrefab.GetComponent<GameWindow>().WindowsPlacement;
            var anchor = WindowsManager.Instance.GetWindowAnchor(placement);
            var go = UnityEngine.Object.Instantiate(sourcePrefab, anchor, false);
            go.name = CopyName;
            copy = go.GetComponent<CollectionScreen>();
            copy.updateNavPanel = false;        // the menu bar's Collection button stays the normal Collection's
            RevivalMod.Log.Msg($"[testers] Testers window made from the Collection window file {prefabRef.AssetGUID}");
            return copy;
        }

        private static void PlayIcon(NavigationPanelController nav)
        {
            try
            {
                var play = nav.GetToggle(NavigationPanelToggleType.Main);
                var img = (object)play == null ? null : play.transform.Find("Image");
                var image = (object)img == null ? null : img.GetComponent<Image>();
                if ((object)image != null) playIcon = image.sprite;
            }
            catch { }
        }

        private static WindowTabBase TabOf<T>(GameWindowWithTabs w) where T : WindowTabBase
        {
            var tabs = w.tabs;
            if (tabs == null) return null;
            for (int i = 0; i < tabs.Count; i++)
            {
                var t = tabs[i];
                if ((object)t != null && (object)t.TryCast<T>() != null) return t;
            }
            return null;
        }

        /// <summary>The inner ribbon: Play, Decks, Collection; Styles hidden.</summary>
        private static void Dress(GameWindowWithTabs w)
        {
            var buttons = w.Components?.TabButtons?.options;
            if (buttons == null) return;
            var order = new List<string>();
            for (int i = 0; i < buttons.Count; i++)
            {
                var b = buttons[i];
                if (b == null || (object)b.tab == null || (object)b.toggle == null) continue;
                var t = b.toggle.transform;
                if (!buttonsLogged) { buttonsLogged = true; LogHierarchy(t, 0); }
                if ((object)b.tab.TryCast<AlternateArtCardCollectionTab>() != null) { if (t.gameObject.activeSelf) t.gameObject.SetActive(false); continue; }
                if ((object)b.tab.TryCast<CardbackCollectionTab>() != null) { Relabel(t, "PLAY", playIcon); Place(t, 0); order.Add("Play"); }
                else if ((object)b.tab.TryCast<SelectDecksTab>() != null) { Relabel(t, "DECKS", null); Place(t, 1); order.Add("Decks"); }
                else if ((object)b.tab.TryCast<CardCollectionTab>() != null) { Relabel(t, "CARDS", null); Place(t, 2); order.Add("Cards"); }
            }
            if (!dressedLogged) { dressedLogged = true; RevivalMod.Log.Msg($"[testers] Testers window ribbon: {string.Join(", ", order)}"); }
        }

        private static void Place(Transform t, int index) { if (t.GetSiblingIndex() != index) t.SetSiblingIndex(index); }

        private static void Relabel(Transform button, string text, Sprite icon)
        {
            foreach (var loc in button.GetComponentsInChildren<Il2CppI2.Loc.Localize>(true)) loc.enabled = false;
            foreach (var label in button.GetComponentsInChildren<TMP_Text>(true)) if (label.text != text) label.text = text;
            if ((object)icon == null) return;
            var img = button.Find("Icon");
            var image = (object)img == null ? null : img.GetComponent<Image>();
            if ((object)image != null && (object)image.sprite != (object)icon) image.sprite = icon;
        }

        private static void LogHierarchy(Transform t, int depth)
        {
            if (depth > 3) return;
            var names = new System.Text.StringBuilder();
            foreach (var c in t.GetComponents<Component>())
                if ((object)c != null) names.Append(c.GetIl2CppType().Name).Append(' ');
            RevivalMod.Log.Msg($"[testers] tab button {new string(' ', depth * 2)}{t.name}: {names}");
            for (int i = 0; i < t.childCount; i++) LogHierarchy(t.GetChild(i), depth + 1);
        }

        /// <summary>Called every frame: menu-bar highlight, our tab labels, and the Play tab's tiles.</summary>
        internal static void Tick()
        {
            try
            {
                float now = Time.realtimeSinceStartup;
                bool slow = now >= nextDress;
                if (slow) { nextDress = now + 0.25f; Highlight(); DropWhenClosed(now); }
                if (!Alive(copy) || !copy.isActiveAndEnabled) return;
                if (slow) Dress(copy);           // the game re-labels its tab buttons; keep ours
                var tab = copy.CurrentTab;
                bool onPlay = (object)tab != null && (object)tab.TryCast<CardbackCollectionTab>() != null;
                var panel = copy.transform.Find(PanelName);
                if ((object)panel != null && panel.gameObject.activeSelf != onPlay) panel.gameObject.SetActive(onPlay);
                if (!onPlay) return;
                var t = tab.transform;
                for (int i = 0; i < t.childCount; i++)
                {
                    var child = t.GetChild(i);
                    if (child.gameObject.activeSelf) child.gameObject.SetActive(false);
                }
                // the cosmetics list and its "owned" switch, wherever the window keeps them
                var cardbacks = tab.TryCast<CardbackCollectionTab>();
                var display = cardbacks.display;
                if ((object)display != null && display.gameObject.activeSelf) display.gameObject.SetActive(false);
                var owned = cardbacks.ownedToggle;
                if ((object)owned != null && owned.gameObject.activeSelf) owned.gameObject.SetActive(false);
                if ((object)panel == null) BuildPlay(copy.transform);
                else if (slow) { PlacePanel(panel.GetComponent<RectTransform>()); FitTiles(panel); }
            }
            catch (Exception e) { RevivalMod.Log.Warning("[testers] play tab: " + e.Message); }
        }

        /// <summary>
        /// Once closed, the Testers window is deleted (after a moment, so its closing animation finishes),
        /// so it never stays loaded into a match. Opening it again makes a fresh one from the loaded file.
        /// </summary>
        private static void DropWhenClosed(float now)
        {
            if (!Alive(copy)) { copy = null; closedAt = 0; return; }
            bool open;
            try
            {
                open = copy.IsOpen() || copy.CurrentState != WindowState.Closed;
                // still in the window manager's list (for example under the deck editor): keep it
                var list = WindowsManager.Instance?.openWindows;
                if (!open && list != null)
                    for (int i = 0; i < list.Count; i++)
                        if ((object)list[i] != null && list[i].Pointer == copy.Pointer) { open = true; break; }
            }
            catch { open = true; }
            if (open) { closedAt = 0; return; }
            if (closedAt == 0) { closedAt = now; return; }
            if (now - closedAt < 1.5f) return;
            UnityEngine.Object.Destroy(copy.gameObject);
            copy = null;
            closedAt = 0;
            RevivalMod.Log.Msg("[testers] Testers window closed and removed");
        }

        /// <summary>
        /// The menu bar lights the button of the window type on screen, so with the Testers window up it lit
        /// Collection. While the Testers window is the one on screen, Collection is switched off (without
        /// opening anything) and the Testers button lit instead.
        /// </summary>
        private static void Highlight()
        {
            bool showing = false;
            try
            {
                var wm = WindowsManager.Instance;
                var current = (object)wm == null ? null : wm.CurrentWindow;
                showing = Alive(copy) && copy.isActiveAndEnabled && (object)current != null && current.Pointer == copy.Pointer;
                var nav = UnityEngine.Object.FindObjectOfType<NavigationPanelController>();
                var col = (object)nav == null ? null : nav.GetToggle(NavigationPanelToggleType.Collection);
                var toggle = (object)col == null ? null : col.toggle;
                var group = (object)toggle == null ? null : toggle.group;
                if (showing && (object)toggle != null)
                {
                    // the bar's buttons are a group that keeps one button on; let it have none while Testers is up
                    if ((object)group != null && !group.allowSwitchOff)
                    {
                        if (!switchOffChanged) { switchOffChanged = true; groupPtr = group.Pointer; }
                        group.allowSwitchOff = true;
                    }
                    if (toggle.isOn)
                    {
                        toggle.SetIsOnWithoutNotify(false);
                        toggle.RefreshVisuals();
                        if (!unlitLogged) { unlitLogged = true; RevivalMod.Log.Msg($"[testers] menu bar: Collection unlit ({(toggle.isOn ? "still on" : "off")})"); }
                    }
                }
                else if (!showing && switchOffChanged && (object)group != null && group.Pointer == groupPtr)
                {
                    group.allowSwitchOff = false;    // back to the game's own setting
                    switchOffChanged = false;
                }
            }
            catch (Exception e) { RevivalMod.Log.Warning("[testers] highlight: " + e.Message); }
            TestersTab.Lit(showing);
        }

        /// <summary>Where the Play screen puts its tiles: right of the inner ribbon, same top and bottom as the Play screen's tiles.</summary>
        private static void PlacePanel(RectTransform panel)
        {
            var parent = panel.parent.TryCast<RectTransform>();
            if ((object)parent == null) return;
            var pr = parent.rect;
            float left = pr.xMin + 200, right = pr.xMax - 40, top = pr.yMax - pr.height * 0.17f, bottom = pr.yMin + pr.height * 0.045f;
            var corners = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(4);
            var ribbon = copy.Components?.TabButtons;
            var rib = (object)ribbon == null ? null : ribbon.GetComponent<RectTransform>();
            if ((object)rib != null)
            {
                rib.GetWorldCorners(corners);
                left = parent.InverseTransformPoint(corners[2]).x + 28;
            }
            // the Play screen's own tiles give the top and bottom
            try
            {
                var menu = WindowsManager.Instance.BaseMenu;
                var main = (object)menu == null ? null : menu.TryCast<MainMenuWindow>();
                var tiles = (object)main == null ? null : main.currentLiveOpContainers;
                if (tiles != null && tiles.Count > 0)
                {
                    float t = float.MinValue, b = float.MaxValue, r = float.MinValue;
                    for (int i = 0; i < tiles.Count; i++)
                    {
                        var tile = tiles[i];
                        var trt = (object)tile == null ? null : tile.GetComponent<RectTransform>();
                        if ((object)trt == null) continue;
                        trt.GetWorldCorners(corners);
                        t = Mathf.Max(t, parent.InverseTransformPoint(corners[1]).y);
                        b = Mathf.Min(b, parent.InverseTransformPoint(corners[0]).y);
                        r = Mathf.Max(r, parent.InverseTransformPoint(corners[2]).x);
                    }
                    if (t > b + 10) { top = t; bottom = b; right = Mathf.Max(right, r); }
                }
            }
            catch { }
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
            var size = new Vector2(Mathf.Max(10, right - left), Mathf.Max(10, top - bottom));
            var centre = new Vector2((left + right) / 2, (top + bottom) / 2) - pr.center;
            if ((panel.sizeDelta - size).sqrMagnitude > 1 || (panel.anchoredPosition - centre).sqrMagnitude > 1)
            {
                panel.sizeDelta = size;
                panel.anchoredPosition = centre;
                if (!placedLogged) { placedLogged = true; RevivalMod.Log.Msg($"[testers] Play tab area {size.x:0}x{size.y:0}"); }
            }
        }

        /// <summary>The Play tab: each tester mode's own Play-screen tile, made the way the Play screen makes them.</summary>
        private static void BuildPlay(Transform tab)
        {
            var panel = new GameObject(PanelName);
            var rt = panel.AddComponent<RectTransform>();
            rt.SetParent(tab, false);           // the window itself, over its tabs
            rt.SetAsLastSibling();
            PlacePanel(rt);

            var row = new GameObject("Tiles");
            var rowRt = row.AddComponent<RectTransform>();
            rowRt.SetParent(rt, false);
            rowRt.anchorMin = rowRt.anchorMax = rowRt.pivot = new Vector2(0, 1);
            rowRt.anchoredPosition = Vector2.zero;
            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 22;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = layout.childControlHeight = false;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            var fit = row.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            int shown = 0;
            foreach (var id in TestersPage.TesterModes())
            {
                var ev = TestersPage.Find(id);
                if ((object)ev == null) { RevivalMod.Log.Warning($"[testers] mode {id} is not on this server"); continue; }
                try
                {
                    var provider = ev.TryCast<IContainerProvider>();
                    var tile = (object)provider == null ? null
                        : ContainerBuilder<LiveopMenuContainer>.MakeContainer(provider, EventComponentReferenceType.MainMenuContainer, row.transform);
                    if ((object)tile == null) { RevivalMod.Log.Warning($"[testers] {id} has no Play-screen tile"); continue; }
                    // a tile under a layout group: the layout places it, its own size is kept
                    var trt = tile.GetComponent<RectTransform>();
                    if ((object)trt != null) { trt.anchorMin = trt.anchorMax = trt.pivot = new Vector2(0, 1); }
                    var size = tile.gameObject.GetComponent<LayoutElement>() ?? tile.gameObject.AddComponent<LayoutElement>();
                    if ((object)trt != null) { size.preferredWidth = trt.rect.width; size.preferredHeight = trt.rect.height; }
                    RevivalMod.Log.Msg($"[testers] Play tab tile for {id}: {tile.name} {(object)trt?.rect.width ?? 0:0}x{(object)trt?.rect.height ?? 0:0}");
                    shown++;
                }
                catch (Exception e) { RevivalMod.Log.Warning($"[testers] tile for {id}: {e.Message}"); }
            }
            if (shown == 0)
                TestersPage.Text(panel.transform, "Empty", "No tester modes are set up on this server right now.", 26, TextAlignmentOptions.Left, TestersPage.Dim, 0.05f, 0.6f, 0.9f, 0.75f);
            RevivalMod.Log.Msg($"[testers] Play tab: {shown} mode(s)");
        }

        /// <summary>Tiles keep the Play screen's size; scaled down only if the area is smaller.</summary>
        private static void FitTiles(Transform panel)
        {
            var row = panel.Find("Tiles");
            var area = panel.GetComponent<RectTransform>();
            var rowRt = (object)row == null ? null : row.GetComponent<RectTransform>();
            if ((object)rowRt == null || (object)area == null) return;
            float h = rowRt.rect.height, w = rowRt.rect.width;
            if (h <= 1 || w <= 1) return;
            float scale = Mathf.Min(1f, area.rect.height / h, area.rect.width / w);
            if (scale > 0 && Mathf.Abs(rowRt.localScale.x - scale) > 0.01f) rowRt.localScale = new Vector3(scale, scale, 1);
        }

        // Ribbon set up once the window's tabs exist.
        [HarmonyPatch(typeof(GameWindowWithTabs), nameof(GameWindowWithTabs.OpenTabs))]
        private static class AfterOpenTabs
        {
            private static void Postfix(GameWindowWithTabs __instance)
            {
                try { if (IsCopy(__instance)) Dress(__instance); }
                catch (Exception e) { RevivalMod.Log.Warning("[testers] ribbon: " + e.Message); }
            }
        }

        // The window opens on Play.
        [HarmonyPatch(typeof(GameWindowWithTabs), nameof(GameWindowWithTabs.GetStartingTab))]
        private static class StartingTab
        {
            private static void Postfix(GameWindowWithTabs __instance, ref WindowTabBase __result)
            {
                try
                {
                    if (!IsCopy(__instance)) return;
                    Dress(__instance);
                    var tab = TabOf<CardbackCollectionTab>(__instance);
                    if ((object)tab != null) __result = tab;
                }
                catch (Exception e) { RevivalMod.Log.Warning("[testers] " + e.Message); }
            }
        }

        // The separation check: the Testers window's card list leaves out Space Wolves.
        [HarmonyPatch(typeof(CardCollectionTab), "GetCollection")]
        private static class NoSpaceWolves
        {
            private static void Postfix(CardCollectionTab __instance, ref Il2CppSystem.Collections.Generic.List<RawCardScript> __result)
            {
                try
                {
                    if (__result == null || !IsCopy(__instance.Window)) return;
                    var kept = new Il2CppSystem.Collections.Generic.List<RawCardScript>();
                    int left = 0;
                    for (int i = 0; i < __result.Count; i++)
                    {
                        var c = __result[i];
                        if ((object)c != null && c.cardArmy == CardArmy.SpaceWolves) { left++; continue; }
                        kept.Add(c);
                    }
                    __result = kept;
                    if (left != hidden) { hidden = left; RevivalMod.Log.Msg($"[testers] Testers collection: {kept.Count} cards shown, {left} Space Wolves left out"); }
                }
                catch (Exception e) { RevivalMod.Log.Warning("[testers] " + e.Message); }
            }
        }

        // Decks skips the format picker: straight to the tester mode's deck list.
        [HarmonyPatch(typeof(SelectDecksTab), nameof(SelectDecksTab.ChooseTabToOpen))]
        private static class DecksStraightToMode
        {
            private static void Postfix(SelectDecksTab __instance)
            {
                try
                {
                    if (!IsCopy(__instance.Window)) return;
                    foreach (var id in TestersPage.TesterModes())
                    {
                        var ev = TestersPage.Find(id);
                        var own = (object)ev == null ? null : ev.TryCast<IOwnDeckPlayEvent>();
                        if ((object)own == null) continue;
                        __instance.OnClickDeckCollection(own);
                        RevivalMod.Log.Msg($"[testers] Decks tab: {id} decks");
                        return;
                    }
                    RevivalMod.Log.Warning("[testers] Decks tab: no tester mode with decks");
                }
                catch (Exception e) { RevivalMod.Log.Warning("[testers] decks: " + e.Message); }
            }
        }

        // Back from the Custom Test deck list closes the window instead of showing the format picker.
        [HarmonyPatch(typeof(SelectDecksTab), nameof(SelectDecksTab.BackButtonClicked))]
        private static class BackCloses
        {
            private static bool Prefix(SelectDecksTab __instance)
            {
                try
                {
                    if (!IsCopy(__instance.Window)) return true;
                    Close();
                    return false;
                }
                catch (Exception e) { RevivalMod.Log.Warning("[testers] " + e.Message); return true; }
            }
        }
    }
}
