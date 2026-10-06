using System;
using UnityEngine;
using UnityEngine.UI;

namespace WarpforgeRevival
{
    /// <summary>
    /// Long lists (avatars, titles) only scroll by wheel or drag, a few rows at a time. This makes
    /// the wheel move further per notch and adds a draggable scrollbar down the right edge.
    /// </summary>
    internal static class Scrolling
    {
        private const string BarName = "Revival Scrollbar";

        // The list cuts off everything drawn outside its own area, so a bar parented to the list is
        // invisible as soon as it sits beyond the list's edge. The bar is therefore attached to the
        // panel around the list (the first ancestor that does not clip) and lined up with the
        // list's right edge from there.
        private const float Gap = 24f, Width = 22f;

        private static bool Clips(Transform t) =>
            (object)t.GetComponent<Mask>() != null || (object)t.GetComponent<RectMask2D>() != null;

        /// <summary>The panel to attach the bar to: just above the outermost clipping object around the list.</summary>
        private static Transform Frame(ScrollRect scroll)
        {
            Transform top = null, t = scroll.transform;
            for (int i = 0; i < 5 && (object)t != null; i++, t = t.parent)
                if (Clips(t)) top = t;
            var frame = (object)top != null ? top.parent : scroll.transform.parent;
            return (object)frame != null ? frame : scroll.transform;
        }

        private static void Place(ScrollRect scroll, RectTransform barRt)
        {
            if ((object)barRt == null) return;
            var frame = barRt.parent;
            var list = scroll.GetComponent<RectTransform>();
            if ((object)frame == null || (object)list == null) return;
            var corners = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(4);
            list.GetWorldCorners(corners);                       // bottom-left, top-left, top-right, bottom-right
            Vector3 bottomRight = frame.InverseTransformPoint(corners[3]), topRight = frame.InverseTransformPoint(corners[2]);
            float height = Mathf.Abs(topRight.y - bottomRight.y);
            if (height < 1f) return;                             // the list has no size yet; placed again on the next refresh
            barRt.anchorMin = barRt.anchorMax = new Vector2(0.5f, 0.5f);
            barRt.pivot = new Vector2(0f, 0.5f);
            barRt.sizeDelta = new Vector2(Width, height - 8f);
            barRt.position = frame.TransformPoint(new Vector3(bottomRight.x + Gap, (topRight.y + bottomRight.y) / 2f, bottomRight.z));
            barRt.SetAsLastSibling();                            // drawn over the panel, not under it
        }

        private static bool described;

        private static string Describe(Transform t)
        {
            var rt = t.TryCast<RectTransform>();
            string size = (object)rt != null ? $" {rt.rect.width:0}x{rt.rect.height:0}" : "";
            return t.name + size + (Clips(t) ? " [clips]" : "") + ((object)t.GetComponent<Image>() != null ? " [image]" : "");
        }

        private static void Report(ScrollRect scroll, Transform frame)
        {
            if (described) return;
            described = true;
            try
            {
                var t = scroll.transform;
                for (int i = 0; i < 6 && (object)t != null; i++, t = t.parent)
                {
                    string kids = "";
                    for (int k = 0; k < t.childCount && k < 12; k++) kids += (k > 0 ? ", " : "") + Describe(t.GetChild(k));
                    RevivalMod.Log.Msg($"[scroll] {(i == 0 ? "list" : "parent " + i)}: {Describe(t)}{(t.Pointer == frame.Pointer ? "  <- bar attached here" : "")} | children: {kids}");
                }
            }
            catch (Exception e) { RevivalMod.Log.Msg("[scroll] " + e.Message); }
        }

        internal static void Improve(Transform content)
        {
            try
            {
                if ((object)content == null) return;
                var scroll = content.GetComponentInParent<ScrollRect>();
                if ((object)scroll == null) return;
                scroll.scrollSensitivity = 90f;
                scroll.inertia = true;

                var frame = Frame(scroll);
                var existing = (object)scroll.verticalScrollbar != null ? scroll.verticalScrollbar.transform : null;
                if ((object)existing != null && existing.name != BarName) return;      // the game gave this list its own bar
                if ((object)existing != null)
                {
                    if (existing.parent.Pointer != frame.Pointer) existing.SetParent(frame, false);
                    Place(scroll, existing.GetComponent<RectTransform>());
                    return;
                }

                var bar = new GameObject(BarName);
                var barRt = bar.AddComponent<RectTransform>();
                barRt.SetParent(frame, false);
                var track = bar.AddComponent<Image>();
                track.color = new Color(0f, 0f, 0f, 0.6f);

                var area = new GameObject("Sliding Area");
                var areaRt = area.AddComponent<RectTransform>();
                areaRt.SetParent(barRt, false);
                areaRt.anchorMin = Vector2.zero; areaRt.anchorMax = Vector2.one;
                areaRt.offsetMin = Vector2.zero; areaRt.offsetMax = Vector2.zero;

                var handle = new GameObject("Handle");
                var handleRt = handle.AddComponent<RectTransform>();
                handleRt.SetParent(areaRt, false);
                handleRt.offsetMin = Vector2.zero; handleRt.offsetMax = Vector2.zero;
                var handleImage = handle.AddComponent<Image>();
                handleImage.color = new Color(0.85f, 0.65f, 0.25f, 0.95f);

                var sb = bar.AddComponent<Scrollbar>();
                sb.handleRect = handleRt;
                sb.targetGraphic = handleImage;
                sb.direction = Scrollbar.Direction.BottomToTop;

                scroll.verticalScrollbar = sb;
                scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
                Place(scroll, barRt);
                Report(scroll, frame);
            }
            catch (Exception e) { RevivalMod.Log.Warning("[scroll] " + e.Message); }
        }
    }
}
