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
    ///  - Collection: the copy's card list. As a check that it is separate, Space Wolves are left out.
    /// The Styles tab is hidden. The normal Collection > Decks keeps every format, Custom Test included.
    /// </summary>
    internal static class TestersCollection
    {
        private const string CopyName = "RevivalTestersWindow", PanelName = "RevivalTestersPlay";
        private static CollectionScreen copy;
        private static GameObject sourcePrefab;
        private static Sprite playIcon;
        private static int hidden;
        private static float nextDress;
        private static bool buttonsLogged, dressedLogged;

        private static bool Alive(UnityEngine.Object o) => (object)o != null && o.Pointer != IntPtr.Zero && o.m_CachedPtr != IntPtr.Zero;

        /// <summary>True when this window is the Testers window (not the normal Collection).</summary>
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
                else if ((object)b.tab.TryCast<CardCollectionTab>() != null) { Relabel(t, "COLLECTION", null); Place(t, 2); order.Add("Collection"); }
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

        /// <summary>Called every frame: while the Play tab is showing, its cosmetics content is swapped for the tester modes.</summary>
        internal static void Tick()
        {
            try
            {
                if (!Alive(copy) || !copy.isActiveAndEnabled) return;
                float now = Time.realtimeSinceStartup;
                if (now >= nextDress) { nextDress = now + 0.25f; Dress(copy); }   // the game re-labels its tab buttons; keep ours
                var tab = copy.CurrentTab;
                if ((object)tab == null || (object)tab.TryCast<CardbackCollectionTab>() == null) return;
                var t = tab.transform;
                var panel = t.Find(PanelName);
                for (int i = 0; i < t.childCount; i++)
                {
                    var child = t.GetChild(i);
                    if ((object)panel != null && child.Pointer == panel.Pointer) continue;
                    if (child.gameObject.activeSelf) child.gameObject.SetActive(false);
                }
                // the cosmetics list and its "owned" switch, wherever the window keeps them
                var cardbacks = tab.TryCast<CardbackCollectionTab>();
                var display = cardbacks.display;
                if ((object)display != null && display.gameObject.activeSelf) display.gameObject.SetActive(false);
                var owned = cardbacks.ownedToggle;
                if ((object)owned != null && owned.gameObject.activeSelf) owned.gameObject.SetActive(false);
                if ((object)panel == null) BuildPlay(t);
                else FitTiles(panel);
            }
            catch (Exception e) { RevivalMod.Log.Warning("[testers] play tab: " + e.Message); }
        }

        /// <summary>The Play tab: each tester mode's own Play-screen tile, made the way the Play screen makes them.</summary>
        private static void BuildPlay(Transform tab)
        {
            var panel = new GameObject(PanelName);
            var rt = panel.AddComponent<RectTransform>();
            rt.SetParent(tab, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

            var row = new GameObject("Tiles");
            var rowRt = row.AddComponent<RectTransform>();
            rowRt.SetParent(rt, false);
            rowRt.anchorMin = rowRt.anchorMax = rowRt.pivot = new Vector2(0, 0.5f);
            rowRt.anchoredPosition = new Vector2(40, 0);
            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 30;
            layout.childAlignment = TextAnchor.MiddleLeft;
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
                    if ((object)trt != null) { trt.anchorMin = trt.anchorMax = trt.pivot = new Vector2(0, 0.5f); }
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

        /// <summary>Tiles are as tall as on the Play screen; scaled down if the tab is shorter.</summary>
        private static void FitTiles(Transform panel)
        {
            var row = panel.Find("Tiles");
            var area = panel.GetComponent<RectTransform>();
            var rowRt = (object)row == null ? null : row.GetComponent<RectTransform>();
            if ((object)rowRt == null || (object)area == null) return;
            float h = rowRt.rect.height, w = rowRt.rect.width;
            if (h <= 1 || w <= 1) return;
            float scale = Mathf.Min(1f, area.rect.height * 0.9f / h, (area.rect.width - 80) / w);
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
