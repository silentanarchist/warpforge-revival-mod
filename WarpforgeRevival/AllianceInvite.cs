using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WarpforgeRevival
{
    /// <summary>
    /// "Invite by friend code" on the alliance page (0.13.66): a box and an Invite button to the right of
    /// "Members: n/30", for Moderators and up. The game invites only from a player's profile; here a pasted friend
    /// code (or name) is enough. The server (Revival/AllianceInvite) finds the player and applies the game's own
    /// invite rules; the answer shows on a line under the box.
    ///
    /// Built once, when the Social window is built (AllianceConfig.SocialBuilt), from copies of the Friends tab's
    /// add-friend box and button; placed and shown when Alliances opens in an alliance (AllianceConfig.TabOpened),
    /// one frame later so the page's layout has settled. No hooks of its own.
    /// </summary>
    internal static class AllianceInvite
    {
        private const string HolderName = "Revival invite by code";
        private static readonly HttpClient Http = Net.Client(TimeSpan.FromSeconds(10));
        private static readonly ConcurrentQueue<Action> MainThread = new ConcurrentQueue<Action>();

        private static AllianceView view;
        private static GameObject holder;
        private static TMP_InputField box;
        private static Button button;
        private static TMP_Text status;
        private static int placeIn = -1;              // frames until placing; -1: nothing to do
        private static bool busy, builtLogged, placedLogged;

        /// <summary>The Social window was built: make the box (hidden until Alliances opens in an alliance).</summary>
        internal static void Build(GameWindowWithTabs window)
        {
            try
            {
                AllianceView target = null;
                foreach (var v in window.GetComponentsInChildren<AllianceView>(true))
                    if ((object)v != null && !ChallengeMenu.IsChallengeTab(v.GetComponentInParent<AlliancesTab>(true))) { target = v; break; }
                FriendsTab friends = null;
                foreach (var f in window.GetComponentsInChildren<FriendsTab>(true)) { friends = f; break; }
                if ((object)target == null || (object)friends == null) return;
                var input = friends.inputFieldUserName;
                var add = friends.addButton;
                if ((object)input == null || (object)add == null) return;
                if ((object)target.transform.Find(HolderName) != null) return;

                view = target;
                holder = new GameObject(HolderName);
                var hrt = holder.AddComponent<RectTransform>();
                holder.transform.SetParent(target.transform, false);
                holder.AddComponent<LayoutElement>().ignoreLayout = true;
                hrt.sizeDelta = Vector2.zero;

                var boxGo = UnityEngine.Object.Instantiate(input.gameObject, holder.transform, false);
                boxGo.name = "Friend code";
                (boxGo.GetComponent<LayoutElement>() ?? boxGo.AddComponent<LayoutElement>()).ignoreLayout = true;
                box = boxGo.GetComponent<TMP_InputField>();
                if ((object)box != null)
                {
                    box.onSubmit = new TMP_InputField.SubmitEvent();
                    box.onEndEdit = new TMP_InputField.SubmitEvent();
                    box.onValueChanged = new TMP_InputField.OnChangeEvent();
                    box.characterLimit = 24;
                    box.SetTextWithoutNotify("");
                    var ph = box.placeholder?.TryCast<TMP_Text>();
                    if ((object)ph != null)
                    {
                        var loc = ph.GetComponent<Il2CppI2.Loc.Localize>();
                        if ((object)loc != null) loc.enabled = false;
                        ph.text = "Friend code to invite";
                    }
                    box.onSubmit.AddListener(DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction<string>>(new Action<string>(_ => Send())));
                }

                var btnGo = UnityEngine.Object.Instantiate(add.gameObject, holder.transform, false);
                btnGo.name = "Invite";
                (btnGo.GetComponent<LayoutElement>() ?? btnGo.AddComponent<LayoutElement>()).ignoreLayout = true;
                button = btnGo.GetComponent<Button>();
                if ((object)button != null)
                {
                    button.onClick = new Button.ButtonClickedEvent();       // nothing of "add friend" comes along
                    button.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction>(new Action(Send)));
                    button.interactable = true;
                }
                var label = btnGo.GetComponentInChildren<TMP_Text>(true);
                if ((object)label != null)
                {
                    var loc = label.GetComponent<Il2CppI2.Loc.Localize>();
                    if ((object)loc != null) loc.enabled = false;
                    label.text = "Invite";
                    label.enableAutoSizing = true;
                }

                var lineGo = new GameObject("Invite result");
                lineGo.transform.SetParent(holder.transform, false);
                status = lineGo.AddComponent<TextMeshProUGUI>();
                var sample = (object)box != null ? box.textComponent : null;
                if ((object)sample != null) { status.font = sample.font; status.fontSharedMaterial = sample.fontSharedMaterial; }
                status.fontSize = 24;
                status.enableWordWrapping = false;
                status.alignment = TextAlignmentOptions.Left;
                status.text = "";
                lineGo.GetComponent<RectTransform>().sizeDelta = new Vector2(700, 34);

                holder.SetActive(false);
                if (!builtLogged) { builtLogged = true; RevivalMod.Log.Msg("[alliances] invite-by-friend-code box built with the Social window"); }
            }
            catch (Exception e) { RevivalMod.Log.Warning("[alliances] invite box: " + e.Message); }
        }

        /// <summary>Alliances opened: show the box (next frame) when you may invite, hide it otherwise.</summary>
        internal static void Opened(AlliancesManager manager)
        {
            if ((object)holder == null) return;
            try
            {
                var me = (object)manager != null && manager.IsInGroup ? manager.CurrentPlayerData : null;
                bool may = (object)me != null && (int)me.role >= 1;           // Moderator and up, as on the profile
                holder.SetActive(false);
                if (may) placeIn = 1;
            }
            catch (Exception e) { RevivalMod.Log.Warning("[alliances] invite box: " + e.Message); }
        }

        private static (float left, float right, float top, float bottom) Corners(RectTransform rt)
        {
            var c = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(4);
            rt.GetWorldCorners(c);
            return (Math.Min(c[0].x, c[2].x), Math.Max(c[0].x, c[2].x), Math.Max(c[0].y, c[1].y), Math.Min(c[0].y, c[1].y));
        }

        private static void Place()
        {
            if ((object)view == null || view.m_CachedPtr == IntPtr.Zero || (object)holder == null) return;
            // "Members: n/30": the page's text that starts with "Members"
            TMP_Text members = null;
            foreach (var t in view.GetComponentsInChildren<TMP_Text>(false))
            {
                if ((object)t == null || t.transform.IsChildOf(holder.transform)) continue;
                var s = t.text;
                if (!string.IsNullOrEmpty(s) && s.TrimStart().StartsWith("Members", StringComparison.OrdinalIgnoreCase)) { members = t; break; }
            }
            if ((object)members == null) { RevivalMod.Log.Warning("[alliances] invite box: no \"Members\" line on the alliance page"); return; }
            holder.SetActive(true);
            var mrt = members.rectTransform;
            float scale = mrt.lossyScale.x == 0 ? 1f : mrt.lossyScale.x;
            // where the words end (the line's box may be much wider than its text)
            var b = members.textBounds;
            var textEnd = mrt.TransformPoint(new Vector3(b.max.x, b.center.y, 0f));
            var mc = Corners(mrt);
            float midY = textEnd.y, gap = 40f * scale;

            var boxRt = box.GetComponent<RectTransform>();
            var btnRt = button.GetComponent<RectTransform>();
            float h = Math.Max(48f, Math.Min(64f, (mc.top - mc.bottom) / scale + 8f));
            boxRt.sizeDelta = new Vector2(380f, h);
            btnRt.sizeDelta = new Vector2(170f, h);
            PutLeftMid(boxRt, textEnd.x + gap, midY);
            var bc = Corners(boxRt);
            PutLeftMid(btnRt, bc.right + 16f * scale, midY);
            var srt = status.rectTransform;
            var sc0 = Corners(srt);
            srt.position += new Vector3((bc.left) - sc0.left, (bc.bottom - 4f * scale) - sc0.top, 0f);
            if (!placedLogged) { placedLogged = true; RevivalMod.Log.Msg("[alliances] invite-by-friend-code box placed next to the members count"); }
        }

        private static void PutLeftMid(RectTransform rt, float left, float midY)
        {
            var c = Corners(rt);
            rt.position += new Vector3(left - c.left, midY - (c.top + c.bottom) / 2f, 0f);
        }

        private static void Say(string text, bool good)
        {
            if ((object)status == null) return;
            status.text = text;
            status.color = good ? new Color(0.55f, 0.95f, 0.55f) : new Color(1f, 0.6f, 0.5f);
        }

        private static void Send()
        {
            try
            {
                if (busy || (object)box == null) return;
                string code = (box.text ?? "").Trim();
                if (code.Length == 0) { Say("Paste a friend code first", false); return; }
                string session = PlayFabTransport.SessionTicket;
                if (string.IsNullOrEmpty(session)) { Say("Not signed in", false); return; }
                busy = true;
                Say("Inviting...", true);
                Task.Run(async () =>
                {
                    string text; bool good = false;
                    try
                    {
                        using var msg = new HttpRequestMessage(HttpMethod.Post, RevivalMod.Config.ServerUrl + "/playfab/Revival/AllianceInvite")
                        {
                            Content = new StringContent(JsonSerializer.Serialize(new { code }), Encoding.UTF8, "application/json")
                        };
                        msg.Headers.TryAddWithoutValidation("X-Authorization", session);
                        using var reply = await Http.SendAsync(msg);
                        using var doc = JsonDocument.Parse(await reply.Content.ReadAsStringAsync());
                        var root = doc.RootElement;
                        if (reply.IsSuccessStatusCode && root.TryGetProperty("data", out var data))
                        {
                            string name = data.TryGetProperty("name", out var n) ? n.GetString() : "";
                            text = "Invited " + (string.IsNullOrEmpty(name) ? "that player" : name);
                            good = true;
                        }
                        else text = root.TryGetProperty("errorMessage", out var em) ? em.GetString() : "The server refused the invitation";
                    }
                    catch (Exception e) { text = "Could not reach the server"; RevivalMod.Log.Warning("[alliances] invite by friend code: " + e.Message); }
                    MainThread.Enqueue(() =>
                    {
                        busy = false;
                        Say(text, good);
                        if (good && (object)box != null) box.SetTextWithoutNotify("");
                        RevivalMod.Log.Msg("[alliances] invite by friend code: " + text);
                    });
                });
            }
            catch (Exception e) { busy = false; RevivalMod.Log.Warning("[alliances] invite by friend code: " + e.Message); }
        }

        /// <summary>Called every frame: the deferred placing, and answers from the server.</summary>
        internal static void Tick()
        {
            while (MainThread.TryDequeue(out var a)) { try { a(); } catch (Exception e) { RevivalMod.Log.Warning("[alliances] " + e.Message); } }
            if (placeIn < 0) return;
            if (placeIn-- > 0) return;
            try { Place(); } catch (Exception e) { RevivalMod.Log.Warning("[alliances] invite box: " + e.Message); }
        }
    }
}
