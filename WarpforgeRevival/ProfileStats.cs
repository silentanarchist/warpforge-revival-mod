using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Il2Cpp;
using Il2CppTMPro;
using UnityEngine;

namespace WarpforgeRevival
{
    /// <summary>
    /// Profile page: a "Record" panel in the empty space beside the rank boxes, with wins and
    /// losses overall and per mode and the total skulls taken. The numbers come from the revival
    /// server (POST /playfab/Revival/PlayerStats), for the player whose profile is open.
    /// </summary>
    internal static class ProfileStats
    {
        private const string PanelName = "Revival Record";
        private static readonly Dictionary<string, string> Known = new Dictionary<string, string>();   // player id -> last text shown
        private static int asked;
        private static bool reported;


        internal static void Show(ProfileTab tab, string playerId)
        {
            if ((object)tab == null || string.IsNullOrEmpty(playerId)) return;
            var label = Panel(tab);
            if ((object)label == null) return;
            Place(tab, label);
            label.text = Known.TryGetValue(playerId.ToUpperInvariant(), out var last) ? last : "";
            int mine = ++asked;
            Fetch(playerId, text => PlayFabTransport.OnMainThread(() =>
            {
                if (text != null) Known[playerId.ToUpperInvariant()] = text;
                if (mine != asked || !Ui.Alive(label)) return;         // another profile was opened meanwhile
                label.text = text ?? "";
                Place(tab, label);
            }));
        }

        private static TextMeshProUGUI Panel(ProfileTab tab)
        {
            var root = tab.transform;
            var old = root.Find(PanelName);
            if ((object)old != null) return old.GetComponent<TextMeshProUGUI>();

            var go = new GameObject(PanelName);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(root, false);
            var label = go.AddComponent<TextMeshProUGUI>();
            label.raycastTarget = false;
            label.richText = true;
            label.enableWordWrapping = false;
            label.alignment = TextAlignmentOptions.TopLeft;
            label.color = Color.white;
            // Same lettering as the rank boxes beside it.
            TMP_Text like = null;
            if ((object)tab.rankingSection != null) like = tab.rankingSection.GetComponentInChildren<TMP_Text>(true);
            if ((object)like != null)
            {
                label.font = like.font;
                label.fontSharedMaterial = like.fontSharedMaterial;
                label.fontSize = Mathf.Clamp(like.fontSize * 0.72f, 20f, 40f);
            }
            else label.fontSize = 28f;
            var layout = go.AddComponent<UnityEngine.UI.LayoutElement>();
            layout.ignoreLayout = true;                             // never pushes the page's own boxes around
            return label;
        }

        private static bool Corners(Component c, Transform space, ref float left, ref float right, ref float top, ref float bottom)
        {
            if ((object)c == null) return false;
            var rt = c.GetComponent<RectTransform>();
            if ((object)rt == null || rt.rect.width < 1f) return false;
            var w = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(4);
            rt.GetWorldCorners(w);
            for (int i = 0; i < 4; i++)
            {
                var p = space.InverseTransformPoint(w[i]);
                left = Mathf.Min(left, p.x); right = Mathf.Max(right, p.x);
                top = Mathf.Max(top, p.y); bottom = Mathf.Min(bottom, p.y);
            }
            return true;
        }

        /// <summary>Puts the panel to the right of the rank boxes: under the Warlord Mastery box when
        /// that is showing, otherwise level with the rank boxes' top.</summary>
        private static void Place(ProfileTab tab, TMP_Text label)
        {
            var rank = tab.rankingSection;
            if ((object)rank == null) return;
            var space = tab.transform;
            float left = float.MaxValue, right = float.MinValue, top = float.MinValue, bottom = float.MaxValue;
            bool any = Corners(rank.currentRankDisplay, space, ref left, ref right, ref top, ref bottom);
            any |= Corners(rank.highestRankDisplay, space, ref left, ref right, ref top, ref bottom);
            if (!any && !Corners(rank, space, ref left, ref right, ref top, ref bottom)) return;   // no size yet; placed again when the numbers arrive

            float width = 560f;
            float pl = float.MaxValue, pr = float.MinValue, pt = float.MinValue, pb = float.MaxValue;
            if (Corners(tab, space, ref pl, ref pr, ref pt, ref pb) && pr - right > 300f) width = Mathf.Min(pr - right - 70f, 680f);

            var rt = label.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0f, 1f);
            // With the Warlord Mastery box showing, the record goes under it, in that box's column.
            var mastery = tab.GetComponentInChildren<WarlordMasteryContainer>(false);
            float ml = float.MaxValue, mr = float.MinValue, mt = float.MinValue, mb = float.MaxValue;
            if ((object)mastery != null && Corners(mastery, space, ref ml, ref mr, ref mt, ref mb) && mb - bottom > 120f)
            {
                rt.sizeDelta = new Vector2(Mathf.Min(mr - ml - 20f, 680f), mb - 30f - bottom);
                rt.position = space.TransformPoint(new Vector3(ml + 20f, mb - 30f, 0f));
            }
            else
            {
                rt.sizeDelta = new Vector2(width, Mathf.Max(200f, top - bottom));
                rt.position = space.TransformPoint(new Vector3(right + 45f, top, 0f));
            }
            // Drawn just after the rank boxes, not last: windows the page opens over itself (the
            // name change box, for one) come later in the page and must stay on top of the record.
            Transform boxes = rank.transform;
            while ((object)boxes.parent != null && boxes.parent.Pointer != space.Pointer) boxes = boxes.parent;
            if ((object)boxes.parent != null)
            {
                int at = boxes.GetSiblingIndex();
                rt.SetSiblingIndex(rt.GetSiblingIndex() > at ? at + 1 : at);
            }

            if (reported) return;
            reported = true;
            var order = new StringBuilder();
            for (int i = 0; i < space.childCount; i++) order.Append(i == 0 ? "" : ", ").Append(space.GetChild(i).name);
            RevivalMod.Log.Msg("[profile] page parts in drawing order: " + order);
            RevivalMod.Log.Msg($"[profile] record panel: rank boxes x {left:0}..{right:0}, y {bottom:0}..{top:0}; page right edge {(pr > float.MinValue ? pr.ToString("0") : "unknown")}; panel width {width:0}, font {label.fontSize:0}");
        }

        private static string Line(string name, JsonElement row)
        {
            int Num(string k) => row.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
            int draws = Num("draw");
            // W / L / D as words the translators can shorten their own way; the layout tags stay out of the texts
            return name + "<pos=48%>" + Loc.T("profile.won", "{count} W", ("count", Num("won"))) + "<pos=72%>" + Loc.T("profile.lost", "{count} L", ("count", Num("lost")))
                   + (draws > 0 ? "  " + Loc.T("profile.draws", "{count} D", ("count", draws)) : "");
        }

        /// <summary>Right after sign-in: your own record, so your profile opens with it (0.13.55).</summary>
        internal static void Prefetch()
        {
            Fetch("", text => { }, (id, text) => PlayFabTransport.OnMainThread(() =>
            {
                if (text == null || string.IsNullOrEmpty(id)) return;
                Known[id.ToUpperInvariant()] = text;
                RevivalMod.Log.Msg("[profile] own record loaded at sign-in");
            }));
        }

        private static void Fetch(string playerId, Action<string> done, Action<string, string> answered = null)
        {
            string ticket = PlayFabTransport.SessionTicket;
            if (string.IsNullOrEmpty(ticket)) { done(null); return; }
            System.Threading.Tasks.Task.Run(async () =>
            {
                string text = null, answeredId = null;
                try
                {
                    var reply = await Net.PostJson("/playfab/Revival/PlayerStats",
                        JsonSerializer.Serialize(new Dictionary<string, string> { ["playerId"] = playerId }), TimeSpan.FromSeconds(10), retry: true);
                    var data = reply.Data;
                    if (reply.Ok && data.ValueKind == JsonValueKind.Object && data.TryGetProperty("total", out var total))
                    {
                        var sb = new StringBuilder();
                        sb.Append("<size=125%><color=#F0B840>").Append(Loc.T("profile.record", "Record")).Append("</color></size>\n");
                        sb.Append(Line(Loc.T("profile.overall", "Overall"), total)).Append('\n');
                        if (data.TryGetProperty("modes", out var modes) && modes.ValueKind == JsonValueKind.Array)
                            foreach (var m in modes.EnumerateArray())
                                sb.Append("<color=#C8C8C8>").Append(Line(m.TryGetProperty("name", out var n) ? n.GetString() : "?", m)).Append("</color>\n");
                        int skulls = total.TryGetProperty("skulls", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt32() : 0;
                        sb.Append("\n<color=#F0B840>").Append(Loc.T("profile.totalSkulls", "Total skulls")).Append("</color><pos=48%>").Append(skulls);
                        text = sb.ToString();
                        if (answered != null && data.TryGetProperty("playerId", out var pid) && pid.ValueKind == JsonValueKind.String) answeredId = pid.GetString();
                    }
                }
                catch (Exception e) { RevivalMod.Log.Warning("[profile] record: " + e.Message); }
                done(text);
                answered?.Invoke(answeredId, text);
            });
        }
    }
}
