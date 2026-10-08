using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using Il2CppEverguild.LiveOps;
using UnityEngine;
using UnityEngine.UI;

namespace WarpforgeRevival
{
    /// <summary>
    /// Collection > Decks first asks which format to build for, one tall tile per format side by
    /// side. With three formats that row is crowded, so it is arranged like the game's own Play
    /// screen: Classic keeps its full-height tile, and Skirmish and Long Game share the next column,
    /// one above the other.
    /// </summary>
    internal static class DeckFormatPicker
    {
        private const string StackName = "RevivalFormatStack";
        private static int notes;

        private static string IdOf(SelectDeckGameModeMenuContainer tile, out IPlayEvent e)
        {
            e = null;
            try
            {
                var content = tile.Content;
                e = (object)content == null ? null : content.TryCast<IPlayEvent>();
                var d = (object)e == null ? null : e.GetBaseData();
                return (object)d == null ? "" : d.eventId ?? "";
            }
            catch { return ""; }
        }

        private static string Describe(Transform t)
        {
            var parts = new List<string>();
            foreach (var c in t.GetComponents<Component>())
                if ((object)c != null) parts.Add(c.GetIl2CppType().Name);
            var rt = t.TryCast<RectTransform>();
            string size = (object)rt == null ? "" : $" {rt.rect.width:0}x{rt.rect.height:0}";
            return $"'{t.name}'{size} [{string.Join(",", parts)}]";
        }

        [HarmonyPatch(typeof(SelectGameModeDeckTab), nameof(SelectGameModeDeckTab.Initialize))]
        private static class Built
        {
            private static void Postfix(SelectGameModeDeckTab __instance)
            {
                try { Arrange(__instance); }
                catch (Exception ex) { if (notes < 4) { notes++; RevivalMod.Log.Warning("[decks] format picker: " + ex); } }
            }
        }

        private static void Arrange(SelectGameModeDeckTab tab)
        {
            var tiles = tab.currentSelectDeckContainers;
            var anchor = tab.contentAnchor;
            if (tiles == null || (object)anchor == null) return;

            SelectDeckGameModeMenuContainer classic = null, skirmish = null, longGame = null;
            var seen = new List<string>();
            for (int i = 0; i < tiles.Count; i++)
            {
                var tile = tiles[i];
                if ((object)tile == null) continue;
                string id = IdOf(tile, out var e);
                seen.Add(id);
                if (LongGame.IsLongEvent(e)) longGame = tile;
                else if (id.IndexOf("Skirmish", StringComparison.OrdinalIgnoreCase) >= 0) skirmish = tile;
                else if (id.IndexOf("Classic", StringComparison.OrdinalIgnoreCase) >= 0) classic = tile;
            }

            bool report = notes < 2;
            if (report)
            {
                notes++;
                RevivalMod.Log.Msg($"[decks] format picker: formats {string.Join(", ", seen)}; holder {Describe(anchor)}");
                for (int i = 0; i < tiles.Count; i++)
                    if ((object)tiles[i] != null) RevivalMod.Log.Msg($"[decks]   tile {Describe(tiles[i].transform)}");
            }
            if ((object)classic == null || (object)skirmish == null || (object)longGame == null)
            {
                if (report) RevivalMod.Log.Msg("[decks] format picker left as it is (it needs Classic, Skirmish and Long Game to rearrange)");
                return;
            }

            // The game's own way of doing this, where the holder supports it: a grid whose items say
            // how many cells they take (the Play screen works like that).
            var grid = anchor.GetComponent<FlexibleGridLayout>();
            var cSize = classic.GetComponent<FlexibleLayoutSizeOption>();
            var sSize = skirmish.GetComponent<FlexibleLayoutSizeOption>();
            var lSize = longGame.GetComponent<FlexibleLayoutSizeOption>();
            if ((object)grid != null && (object)cSize != null && (object)sSize != null && (object)lSize != null)
            {
                sSize.sizeX = 1; sSize.sizeY = 1;
                lSize.sizeX = 1; lSize.sizeY = 1;
                try { classic.transform.SetSiblingIndex(0); skirmish.transform.SetSiblingIndex(1); longGame.transform.SetSiblingIndex(2); } catch { }
                LayoutRebuilder.MarkLayoutForRebuild(anchor.TryCast<RectTransform>());
                if (report) RevivalMod.Log.Msg($"[decks] format picker: Classic {cSize.sizeX}x{cSize.sizeY}, Skirmish and Long Game 1x1 in the game's grid");
                return;
            }

            // Otherwise: a column of our own in the row, holding the two tiles at half height.
            var classicT = classic.transform;
            // (the game clears the holder each time the picker is built, but a cleared object lingers
            // until the end of the frame, so an earlier column is renamed out of the way, never reused)
            var old = anchor.Find(StackName);
            if ((object)old != null) old.name = StackName + "-old";
            var stack = new GameObject(StackName);
            stack.AddComponent<RectTransform>();
            stack.transform.SetParent(anchor, false);
            var stackRect = stack.GetComponent<RectTransform>();
            var classicRect = classicT.TryCast<RectTransform>();

            float spacing = 10f;
            var row = anchor.GetComponent<HorizontalOrVerticalLayoutGroup>();
            if ((object)row != null) spacing = row.spacing;

            // Each stacked tile keeps its own full size and is shown smaller as a whole, so its art
            // and label keep their proportions (a tile simply made shorter squashes its art).
            float w = 0, h = 0;
            if ((object)classicRect != null) { w = classicRect.rect.width; h = classicRect.rect.height; }
            if (w < 2 || h < 2) { var sd = (object)classicRect != null ? classicRect.sizeDelta : Vector2.zero; w = sd.x; h = sd.y; }
            if (w < 2 || h < 2) { w = 506; h = 803; }
            float gap = Mathf.Min(spacing, h * 0.04f);
            float scale = (h - gap) / 2f / h;

            var mine = stack.AddComponent<LayoutElement>();
            mine.minWidth = w * scale; mine.preferredWidth = w * scale; mine.flexibleWidth = 0;
            mine.minHeight = h; mine.preferredHeight = h; mine.flexibleHeight = 0;
            if ((object)classicRect != null)
            {
                stackRect.anchorMin = classicRect.anchorMin; stackRect.anchorMax = classicRect.anchorMax;
                stackRect.pivot = classicRect.pivot;
            }
            stackRect.sizeDelta = new Vector2(w * scale, h);

            int slot = 0;
            foreach (var tile in new[] { skirmish, longGame })
            {
                // a holder of our own carries the size change, so the tile's own press effect is untouched
                var holder = new GameObject("RevivalFormatSlot");
                var hr = holder.AddComponent<RectTransform>();
                holder.transform.SetParent(stack.transform, false);
                hr.anchorMin = hr.anchorMax = new Vector2(0.5f, 1f);
                hr.pivot = new Vector2(0.5f, 1f);
                hr.sizeDelta = new Vector2(w, h);
                hr.anchoredPosition = new Vector2(0, -slot * (h * scale + gap));
                hr.localScale = new Vector3(scale, scale, 1f);

                tile.transform.SetParent(holder.transform, false);
                var tr = tile.transform.TryCast<RectTransform>();
                if ((object)tr != null)
                {
                    tr.anchorMin = tr.anchorMax = tr.pivot = new Vector2(0.5f, 0.5f);
                    tr.sizeDelta = new Vector2(w, h);
                    tr.anchoredPosition = Vector2.zero;
                }
                slot++;
            }
            try { classicT.SetSiblingIndex(0); stack.transform.SetSiblingIndex(1); } catch { }
            LayoutRebuilder.MarkLayoutForRebuild(anchor.TryCast<RectTransform>());
            if (report) RevivalMod.Log.Msg($"[decks] format picker: Classic full size, Skirmish over Long Game in a column of our own (tile {w:0}x{h:0}, shown at {scale:0.00})");
        }
    }
}
