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
        private static readonly ConcurrentQueue<Action> MainThread = new ConcurrentQueue<Action>();

        private static AllianceView view;
        private static TMP_Text membersLine;
        private static Vector2 addSize;                // the Friends tab's add button, as drawn there          // the General page's "Members: n/30" (AllianceMemberList.memberLabel)
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
                // the General sub-tab of your own alliance's page (AlliancesTab's member variant), not the page that
                // shows other alliances from the Join list
                AllianceMemberTab memberTab = null;
                foreach (var t in window.GetComponentsInChildren<AlliancesTab>(true))
                    if ((object)t != null && !SocialTabs.IsCopy(t)) { memberTab = t.allianceMemberVariant; break; }
                var target = (object)memberTab != null ? memberTab.generalView : null;
                var list = (object)target != null ? target.memberList : null;
                var line = (object)list != null ? list.memberLabel : null;
                FriendsTab friends = null;
                foreach (var f in window.GetComponentsInChildren<FriendsTab>(true)) { friends = f; break; }
                if ((object)line == null || (object)friends == null) { RevivalMod.Log.Warning("[alliances] invite box: the alliance page's General tab or the Friends tab was not found"); return; }
                var input = friends.inputFieldUserName;
                var add = friends.addButton;
                if ((object)input == null || (object)add == null) return;
                if ((object)line.transform.Find(HolderName) != null) return;

                view = target;
                membersLine = line;
                holder = new GameObject(HolderName);
                var hrt = holder.AddComponent<RectTransform>();
                holder.transform.SetParent(line.transform, false);
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
                        Ui.SetText(ph, Loc.T("alliance.invitePlaceholder", "Friend code to invite"));
                    }
                    box.onSubmit.AddListener(DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction<string>>(new Action<string>(_ => Send())));
                }

                try { var ar = add.GetComponent<RectTransform>().rect; addSize = new Vector2(ar.width, ar.height); } catch { addSize = Vector2.zero; }
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
                    Ui.SetText(label, Loc.T("alliance.inviteButton", "Invite"));
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
                // the General button (also pressed by the game when the page opens): place it each time General shows
                var general = memberTab.generalButton;
                if ((object)general != null)
                    general.onValueChanged.AddListener(DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction<bool>>(new Action<bool>(on =>
                    {
                        if (on) Opened(SingletonBehaviour<AlliancesManager>.Instance);
                    })));
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
                if (may) placeIn = 5;
            }
            catch (Exception e) { RevivalMod.Log.Warning("[alliances] invite box: " + e.Message); }
        }

        private static void Place()
        {
            if ((object)holder == null) return;
            var members = membersLine;
            if ((object)members == null || members.m_CachedPtr == IntPtr.Zero) return;
            // the box goes on the "Members" line itself (as its child), in that line's own coordinates, to the right of
            // where its words end; so it moves with the line and is drawn with it
            var mrt = members.rectTransform;
            if (holder.transform.parent != mrt) holder.transform.SetParent(mrt, false);
            holder.SetActive(true);
            var hrt = holder.GetComponent<RectTransform>();
            hrt.anchorMin = hrt.anchorMax = hrt.pivot = new Vector2(0.5f, 0.5f);
            hrt.anchoredPosition = Vector2.zero;
            hrt.localScale = Vector3.one;
            hrt.localRotation = Quaternion.identity;
            var r = mrt.rect;
            float words = members.GetPreferredValues(members.text ?? "").x;
            if (!(words > 0f) || words > r.width) words = Math.Min(260f, r.width);
            float end;
            var align = members.horizontalAlignment;
            if (align == HorizontalAlignmentOptions.Right) end = r.xMax;
            else if (align == HorizontalAlignmentOptions.Center) end = r.center.x + words / 2f;
            else end = r.xMin + words;
            float x = end - r.center.x + 40f;                 // in the holder's coordinates (the line's centre)
            float h = Math.Max(48f, Math.Min(64f, r.height + 8f));
            // raised a little so the result line under it stays above the member list
            float y = h / 2f + 22f;
            // the add button as big as on the Friends tab (square, its border round the icon)
            var bs = addSize.x > 8f && addSize.y > 8f ? addSize : new Vector2(h + 16f, h + 16f);
            Lay(box.GetComponent<RectTransform>(), new Vector2(380f, h), new Vector2(0f, 0.5f), new Vector2(x, y));
            Lay(button.GetComponent<RectTransform>(), bs, new Vector2(0f, 0.5f), new Vector2(x + 396f, y));
            Lay(status.rectTransform, new Vector2(700f, 34f), new Vector2(0f, 1f), new Vector2(x, y - h / 2f - 4f));
            if (!placedLogged)
                RevivalMod.Log.Msg($"[alliances] invite box: \"{members.text}\" line {r.width:0}x{r.height:0}, words end at {end:0}, box at x {x:0}, button {bs.x:0}x{bs.y:0}");
            if (!placedLogged) { placedLogged = true; RevivalMod.Log.Msg("[alliances] invite-by-friend-code box placed next to the members count"); }
        }

        private static void Lay(RectTransform rt, Vector2 size, Vector2 pivot, Vector2 at)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = pivot;
            rt.sizeDelta = size;
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;
            rt.anchoredPosition = at;
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
                if (code.Length == 0) { Say(Loc.T("alliance.pasteCode", "Paste a friend code first"), false); return; }
                string session = PlayFabTransport.SessionTicket;
                if (string.IsNullOrEmpty(session)) { Say(Loc.T("common.notSignedIn", "Not signed in"), false); return; }
                busy = true;
                Say(Loc.T("alliance.inviting", "Inviting..."), true);
                Task.Run(async () =>
                {
                    string text; bool good = false;
                    try
                    {
                        var reply = await Net.PostJson("/playfab/Revival/AllianceInvite", JsonSerializer.Serialize(new { code }), TimeSpan.FromSeconds(10));
                        if (reply.Ok)
                        {
                            string name = reply.Data.ValueKind == JsonValueKind.Object && reply.Data.TryGetProperty("name", out var n) ? n.GetString() : "";
                            text = Loc.T("alliance.invited", "Invited {name}", ("name", string.IsNullOrEmpty(name) ? Loc.T("common.thatPlayer", "that player") : name));
                            good = true;
                        }
                        else text = reply.Unreachable ? Loc.T("common.unreachable", "Could not reach the server") : (reply.Error ?? Loc.T("alliance.inviteRefused", "The server refused the invitation")).TrimEnd('.');
                    }
                    catch (Exception e) { text = Loc.T("common.unreachable", "Could not reach the server"); RevivalMod.Log.Warning("[alliances] invite by friend code: " + e.Message); }
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
