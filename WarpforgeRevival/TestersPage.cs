using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppEverguild.LiveOps;
using Il2CppInterop.Runtime;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WarpforgeRevival
{
    /// <summary>
    /// The page behind the Testers button: a full-screen panel with tabs. Play lists the game modes only
    /// testers play (the Custom Test mode); Collection opens a separate test copy of the Collection screen;
    /// Decks opens that copy on one tester mode's deck list (see TestersCollection). The server keeps those modes
    /// off everyone's Play screen ("showInMainMenu": false in GameModes.json); picking one here opens
    /// the mode's own page, the same one its Play-screen tile would open.
    /// Built from plain UI pieces with the game's own font, so it does not depend on any window's layout.
    /// </summary>
    internal static class TestersPage
    {
        private const string Name = "RevivalTestersPage";
        private static GameObject root;
        private static TMP_FontAsset font;
        private static Transform content;
        private static readonly string[] Tabs = { "Play", "Collection", "Decks" };
        private static readonly Button[] tabButtons = new Button[3];
        private static int current;                       // the tab shown, kept while the game runs

        private static readonly Color Back = new Color(0.04f, 0.05f, 0.07f, 0.94f);
        private static readonly Color Panel = new Color(0.11f, 0.12f, 0.15f, 1f);
        private static readonly Color Line = new Color(0.79f, 0.64f, 0.29f, 1f);           // the game's gold
        private static readonly Color Tile = new Color(0.16f, 0.18f, 0.22f, 1f);
        private static readonly Color Dim = new Color(0.66f, 0.64f, 0.6f, 1f);

        /// <summary>The modes on the Play tab: event ids. For now the Custom Test mode (the server's long-game event).</summary>
        private static List<string> TesterModes()
        {
            var ids = new List<string>();
            if (!string.IsNullOrEmpty(ServerSettings.LongGameEvent)) ids.Add(ServerSettings.LongGameEvent);
            return ids;
        }

        internal static bool IsOpen => (object)root != null && root.Pointer != IntPtr.Zero && root.m_CachedPtr != IntPtr.Zero && root.activeSelf;

        internal static void Open(TMP_FontAsset gameFont)
        {
            if ((object)gameFont != null) font = gameFont;
            Close();
            root = new GameObject(Name);
            UnityEngine.Object.DontDestroyOnLoad(root);
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5000;                       // over the menu, under nothing the player needs
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            root.AddComponent<GraphicRaycaster>();

            var shade = Box(root.transform, "Shade", Back, 0, 0, 1, 1);           // also stops clicks reaching the menu
            var panel = Box(shade, "Panel", Panel, 0.12f, 0.1f, 0.88f, 0.9f);
            Box(panel, "TopLine", Line, 0, 0.995f, 1, 1);

            Text(panel, "Title", "Testers", 46, TextAlignmentOptions.Left, Color.white, 0.04f, 0.86f, 0.6f, 0.97f);
            Text(panel, "Note", "Modes and tools for testers. Not shown to other players.", 22, TextAlignmentOptions.Left, Dim, 0.04f, 0.8f, 0.8f, 0.87f);
            var close = Button(panel, "Close", "Close", 0.82f, 0.88f, 0.97f, 0.96f, Tile);
            OnClick(close, Close);

            // tab bar
            content = Box(panel, "Content", new Color(0, 0, 0, 0), 0, 0, 1, 0.69f);
            Box(panel, "TabLine", new Color(1, 1, 1, 0.08f), 0.04f, 0.695f, 0.96f, 0.7f);
            for (int i = 0; i < Tabs.Length; i++)
            {
                int index = i;
                var tab = Button(panel, "Tab" + Tabs[i], "", 0.04f + i * 0.17f, 0.7f, 0.2f + i * 0.17f, 0.78f, Tile);
                tabButtons[i] = tab;
                Text(tab.transform, "Label", Tabs[i], 28, TextAlignmentOptions.Center, Color.white, 0, 0, 1, 1);
                OnClick(tab, () => ShowTab(index));
            }
            ShowTab(current);
            RevivalMod.Log.Msg($"[testers] page opened on the {Tabs[current]} tab");
        }

        private static void ShowTab(int index)
        {
            current = index;
            for (int i = 0; i < tabButtons.Length; i++)
            {
                var b = tabButtons[i];
                if ((object)b == null) continue;
                var under = b.transform.Find("Underline");
                if (i == index && (object)under == null) Box(b.transform, "Underline", Line, 0, 0, 1, 0.06f);
                if (i != index && (object)under != null) UnityEngine.Object.Destroy(under.gameObject);
            }
            for (int i = content.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(content.GetChild(i).gameObject);
            if (index == 0) PlayTab();
            else if (index == 1) CollectionTab();
            else DecksTab();
        }

        /// <summary>A tile in the tab's content area; row 0 at the top.</summary>
        private static void AddTile(int row, string name, string title, string hint, Action click)
        {
            float top = 0.92f - row * 0.29f;
            var tile = Button(content, name, "", 0.04f, top - 0.25f, 0.6f, top, Tile);
            Box(tile.transform, "Edge", Line, 0, 0, 0.008f, 1);
            Text(tile.transform, "Name", title, 34, TextAlignmentOptions.Left, Color.white, 0.05f, 0.45f, 0.95f, 0.92f);
            Text(tile.transform, "Hint", hint, 20, TextAlignmentOptions.Left, Dim, 0.05f, 0.1f, 0.95f, 0.45f);
            OnClick(tile, click);
        }

        private static void Empty(string text) =>
            Text(content, "Empty", text, 26, TextAlignmentOptions.Left, Dim, 0.04f, 0.6f, 0.9f, 0.85f);

        private static void PlayTab()
        {
            int shown = 0;
            foreach (var id in TesterModes())
            {
                var ev = Find(id);
                if ((object)ev == null) { RevivalMod.Log.Warning($"[testers] mode {id} is not on this server"); continue; }
                var picked = ev;
                AddTile(shown++, "Mode_" + id, Label(ev, EventLabelReferenceType.Title, id),
                     "Open this mode's page: pick a deck, play, or build decks for it.", () => Play(picked, id));
            }
            if (shown == 0) Empty("No tester modes are set up on this server right now.");
        }

        private static void CollectionTab()
        {
            AddTile(0, "TestCollection", "Collection (test copy)",
                 "A separate copy of the Collection screen. Space Wolves are left out on purpose, to show it is separate.",
                 () => { Close(); TestersCollection.OpenCards(); });
        }

        private static void DecksTab()
        {
            int shown = 0;
            foreach (var id in TesterModes())
            {
                var ev = Find(id);
                if ((object)ev == null) continue;
                var picked = ev;
                string title = Label(ev, EventLabelReferenceType.Title, id);
                AddTile(shown++, "Decks_" + id, title + " decks",
                     "Build and edit decks for " + title + " only.", () => { Close(); TestersCollection.OpenDecks(picked, id); });
            }
            if (shown == 0) Empty("No tester modes with decks on this server right now.");
        }

        internal static void Close()
        {
            if ((object)root != null && root.Pointer != IntPtr.Zero && root.m_CachedPtr != IntPtr.Zero) UnityEngine.Object.Destroy(root);
            root = null;
            content = null;
            for (int i = 0; i < tabButtons.Length; i++) tabButtons[i] = null;
        }

        private static LiveOpsEvent Find(string id)
        {
            try
            {
                var found = LiveOpsManager.GetEvent(id);
                return (object)found == null ? null : found.TryCast<LiveOpsEvent>();
            }
            catch (Exception e) { RevivalMod.Log.Warning($"[testers] {id}: {e.Message}"); return null; }
        }

        private static string Label(LiveOpsEvent ev, EventLabelReferenceType type, string fallback)
        {
            try
            {
                string s = LiveOpsAssetUtility.GetLabel(ev.TryCast<ILiveOps>(), type, (Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppSystem.Object>)null);
                return string.IsNullOrEmpty(s) ? fallback : s;
            }
            catch { return fallback; }
        }

        private static void Play(LiveOpsEvent ev, string id)
        {
            Close();                                          // the mode's page opens on the game's own canvas, under this one
            try
            {
                RevivalMod.Log.Msg($"[testers] opening {id}");
                ev.OpenWindow();
            }
            catch (Exception e) { RevivalMod.Log.Warning($"[testers] could not open {id}: {e.Message}"); }
        }

        // ---- small UI helpers (anchors are fractions of the parent) ----
        private static RectTransform Place(GameObject go, Transform parent, float x0, float y0, float x1, float y1)
        {
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>() ?? go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(x0, y0); rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            return rt;
        }

        private static Transform Box(Transform parent, string name, Color color, float x0, float y0, float x1, float y1)
        {
            var go = new GameObject(name);
            go.AddComponent<RectTransform>();
            Place(go, parent, x0, y0, x1, y1);
            var img = go.AddComponent<Image>();
            img.color = color;
            return go.transform;
        }

        private static TextMeshProUGUI Text(Transform parent, string name, string text, float size, TextAlignmentOptions align, Color color,
                                            float x0, float y0, float x1, float y1)
        {
            var go = new GameObject(name);
            go.AddComponent<RectTransform>();
            Place(go, parent, x0, y0, x1, y1);
            var t = go.AddComponent<TextMeshProUGUI>();
            if ((object)font != null) t.font = font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.enableWordWrapping = true;
            t.raycastTarget = false;
            return t;
        }

        private static Button Button(Transform parent, string name, string label, float x0, float y0, float x1, float y1, Color color)
        {
            var box = Box(parent, name, color, x0, y0, x1, y1);
            var b = box.gameObject.AddComponent<Button>();
            b.targetGraphic = box.GetComponent<Image>();
            var colors = b.colors;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            b.colors = colors;
            if (!string.IsNullOrEmpty(label)) Text(box, "Label", label, 26, TextAlignmentOptions.Center, Color.white, 0, 0, 1, 1);
            return b;
        }

        private static void OnClick(Button b, Action action)
        {
            b.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction>(new Action(() =>
            {
                try { action(); }
                catch (Exception e) { RevivalMod.Log.Warning("[testers] " + e.Message); }
            })));
        }
    }
}
