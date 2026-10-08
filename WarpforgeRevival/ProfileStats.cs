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
        private static readonly System.Net.Http.HttpClient Http = Net.Client(TimeSpan.FromSeconds(10));
        private static readonly Dictionary<string, string> Known = new Dictionary<string, string>();   // player id -> last text shown
        private static int asked;
        private static bool reported;

        private static bool Alive(TMP_Text t) => (object)t != null && t.Pointer != IntPtr.Zero && t.m_CachedPtr != IntPtr.Zero;

        internal static void Show(ProfileTab tab, string playerId)
        {
            if ((object)tab == null || string.IsNullOrEmpty(playerId)) return;
            var label = Panel(tab);
            if ((object)label == null) return;
            Place(tab, label);
            label.text = Known.TryGetValue(playerId, out var last) ? last : "";
            int mine = ++asked;
            Fetch(playerId, text => PlayFabTransport.OnMainThread(() =>
            {
                if (text != null) Known[playerId] = text;
                if (mine != asked || !Alive(label)) return;         // another profile was opened meanwhile
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

        /// <summary>Puts the panel to the right of the rank boxes, level with their top.</summary>
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
            rt.sizeDelta = new Vector2(width, Mathf.Max(200f, top - bottom));
            rt.position = space.TransformPoint(new Vector3(right + 45f, top, 0f));
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
            return $"{name}<pos=48%>{Num("won")} W<pos=72%>{Num("lost")} L" + (draws > 0 ? $"  {draws} D" : "");
        }

        private static void Fetch(string playerId, Action<string> done)
        {
            string ticket = PlayFabTransport.SessionTicket;
            if (string.IsNullOrEmpty(ticket)) { done(null); return; }
            string url = RevivalMod.Config.ServerUrl + "/playfab/Revival/PlayerStats";
            System.Threading.Tasks.Task.Run(async () =>
            {
                string text = null;
                try
                {
                    using var msg = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, url)
                    {
                        Content = new System.Net.Http.StringContent(JsonSerializer.Serialize(new Dictionary<string, string> { ["playerId"] = playerId }), Encoding.UTF8, "application/json")
                    };
                    msg.Headers.TryAddWithoutValidation("X-Authorization", ticket);
                    using var reply = await Http.SendAsync(msg);
                    using var doc = JsonDocument.Parse(await reply.Content.ReadAsStringAsync());
                    if (reply.IsSuccessStatusCode && doc.RootElement.TryGetProperty("data", out var data) && data.TryGetProperty("total", out var total))
                    {
                        var sb = new StringBuilder();
                        sb.Append("<size=125%><color=#F0B840>Record</color></size>\n");
                        sb.Append(Line("Overall", total)).Append('\n');
                        if (data.TryGetProperty("modes", out var modes) && modes.ValueKind == JsonValueKind.Array)
                            foreach (var m in modes.EnumerateArray())
                                sb.Append("<color=#C8C8C8>").Append(Line(m.TryGetProperty("name", out var n) ? n.GetString() : "?", m)).Append("</color>\n");
                        int skulls = total.TryGetProperty("skulls", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt32() : 0;
                        sb.Append("\n<color=#F0B840>Total skulls</color><pos=48%>").Append(skulls);
                        text = sb.ToString();
                    }
                }
                catch (Exception e) { RevivalMod.Log.Warning("[profile] record: " + e.Message); }
                done(text);
            });
        }
    }
}
