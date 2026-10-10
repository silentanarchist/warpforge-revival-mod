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
    /// The page behind the Testers button: a full-screen panel with tabs. For now one tab, Play,
    /// listing the game modes only testers play (the Custom Test mode). The server keeps those modes
    /// off everyone's Play screen ("showInMainMenu": false in GameModes.json); picking one here opens
    /// the mode's own page, the same one its Play-screen tile would open.
    /// Built from plain UI pieces with the game's own font, so it does not depend on any window's layout.
    /// </summary>
    internal static class TestersPage
    {
        private const string Name = "RevivalTestersPage";
        private static GameObject root;
        private static TMP_FontAsset font;

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

            // tab bar: one tab for now, drawn as selected
            var tab = Box(panel, "TabPlay", Tile, 0.04f, 0.7f, 0.2f, 0.78f);
            Box(tab, "Underline", Line, 0, 0, 1, 0.06f);
            Text(tab, "Label", "Play", 28, TextAlignmentOptions.Center, Color.white, 0, 0, 1, 1);
            Box(panel, "TabLine", new Color(1, 1, 1, 0.08f), 0.04f, 0.695f, 0.96f, 0.7f);

            var modes = TesterModes();
            int shown = 0;
            foreach (var id in modes)
            {
                var ev = Find(id);
                if ((object)ev == null) { RevivalMod.Log.Warning($"[testers] mode {id} is not on this server"); continue; }
                string title = Label(ev, EventLabelReferenceType.Title, id);
                float top = 0.64f - shown * 0.2f;
                var tile = Button(panel, "Mode_" + id, "", 0.04f, top - 0.17f, 0.6f, top, Tile);
                Box(tile.transform, "Edge", Line, 0, 0, 0.008f, 1);
                Text(tile.transform, "Name", title, 34, TextAlignmentOptions.Left, Color.white, 0.05f, 0.45f, 0.95f, 0.92f);
                Text(tile.transform, "Hint", "Open this mode's page: pick a deck, play, or build decks for it.", 20, TextAlignmentOptions.Left, Dim, 0.05f, 0.1f, 0.95f, 0.45f);
                var picked = ev;
                OnClick(tile, () => Play(picked, id));
                shown++;
            }
            if (shown == 0)
                Text(panel, "Empty", "No tester modes are set up on this server right now.", 26, TextAlignmentOptions.Left, Dim, 0.04f, 0.5f, 0.9f, 0.62f);
            RevivalMod.Log.Msg($"[testers] page opened: {shown} mode(s) on the Play tab");
        }

        internal static void Close()
        {
            if ((object)root != null && root.Pointer != IntPtr.Zero && root.m_CachedPtr != IntPtr.Zero) UnityEngine.Object.Destroy(root);
            root = null;
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
