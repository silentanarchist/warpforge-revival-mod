using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using HarmonyLib;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WarpforgeRevival
{
    /// <summary>
    /// Friend requests (0.14.0). Adding someone no longer puts them on your list: it sends them a request, and both
    /// of you become friends when they accept. Social > Friends gets two buttons at the top, copied from the
    /// alliance page's General / Trophies pair: **Friends** (the game's own list) and **Requests** (requests waiting
    /// for you, with accept and decline, and the ones you sent, with cancel). Removing a friend takes you off each
    /// other's lists. The server keeps the lists and the requests (Revival/FriendRequests); the number waiting comes
    /// with every chat answer, so the Requests button shows it without asking separately.
    ///
    /// Built from copies of the game's own pieces when Friends first opens (FriendsLive.Opened calls Opened). One hook:
    /// PlayerDataManager.DeleteFriendFromList, so a removal goes to the server instead of the game uploading its
    /// whole list.
    /// </summary>
    internal static class FriendRequests
    {
        private const string HolderName = "Revival friend tabs";
        private static readonly ConcurrentQueue<Action> MainThread = new ConcurrentQueue<Action>();

        /// <summary>Requests waiting for this player, from the last chat answer (-1: not known yet).</summary>
        internal static volatile int Waiting = -1;
        private static int shownWaiting = -2;

        private static FriendsTab tab;
        private static Toggle friendsButton, requestsButton;
        private static TMP_Text requestsLabel, status;
        private static bool showingRequests, busy, hierarchyLogged;
        private static readonly List<GameObject> rows = new List<GameObject>();

        /// <summary>True while the Requests page is on screen (the friend list must not be redrawn over it).</summary>
        internal static bool ShowingRequests => showingRequests && Ui.Alive(tab);

        // ---------------------------------------------------------------- building

        /// <summary>Friends opened: build the two buttons once for this tab, and start on the Friends page.</summary>
        internal static void Opened(FriendsTab friends)
        {
            try
            {
                if ((object)tab == null || !Ui.Alive(tab) || tab.Pointer != friends.Pointer)
                {
                    tab = friends;
                    friendsButton = requestsButton = null;
                    Build(friends);
                }
                ShowFriends(false);
                Say("", true);
            }
            catch (Exception e) { RevivalMod.Log.Warning("[friends] requests page: " + e.Message); }
        }

        private static void Build(FriendsTab friends)
        {
            var root = friends.GetComponent<RectTransform>();
            if ((object)root == null || (object)root.Find(HolderName) != null) return;

            // the alliance page's General / Trophies buttons, in your own alliance's page (not the Challenge copy)
            AllianceMemberTab memberTab = null;
            AlliancesTab alliances = null;
            var window = friends.GetComponentInParent<GameWindowWithTabs>();
            if ((object)window != null)
                foreach (var t in window.GetComponentsInChildren<AlliancesTab>(true))
                    if ((object)t != null && !ChallengeMenu.IsChallengeTab(t)) { alliances = t; memberTab = t.allianceMemberVariant; break; }
            var general = (object)memberTab != null ? memberTab.generalButton : null;
            var trophies = (object)memberTab != null ? memberTab.trophiesButton : null;
            if ((object)general == null || (object)trophies == null)
            {
                RevivalMod.Log.Warning("[friends] requests page: the alliance page's General / Trophies buttons were not found");
                return;
            }

            var holder = new GameObject(HolderName);
            var hrt = holder.AddComponent<RectTransform>();
            holder.transform.SetParent(root, false);
            holder.AddComponent<LayoutElement>().ignoreLayout = true;
            hrt.anchorMin = Vector2.zero; hrt.anchorMax = Vector2.one;
            hrt.offsetMin = hrt.offsetMax = Vector2.zero;

            // each copy goes where its original sits on the alliance page, measured from that page's corner: both
            // pages fill the same part of the Social window
            var arect = alliances.GetComponent<RectTransform>();
            friendsButton = Copy(general, arect, hrt, "Friends");
            requestsButton = Copy(trophies, arect, hrt, "Requests");
            requestsLabel = requestsButton.GetComponentInChildren<TMP_Text>(true);
            friendsButton.onValueChanged.AddListener(DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction<bool>>(new Action<bool>(on => { if (on) ShowFriends(true); })));
            requestsButton.onValueChanged.AddListener(DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction<bool>>(new Action<bool>(on => { if (on) ShowRequests(); })));

            // the add-friend box sends a request (the server answers with what happened, shown under the box)
            var input = friends.inputFieldUserName;
            var add = friends.addButton;
            if ((object)add != null)
            {
                add.onClick = new Button.ButtonClickedEvent();
                add.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction>(new Action(Send)));
            }
            if ((object)input != null)
            {
                input.onSubmit = new TMP_InputField.SubmitEvent();
                input.onSubmit.AddListener(DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction<string>>(new Action<string>(_ => Send())));
                var lineGo = new GameObject("Friend request result");
                lineGo.transform.SetParent(input.transform, false);
                lineGo.AddComponent<LayoutElement>().ignoreLayout = true;
                status = lineGo.AddComponent<TextMeshProUGUI>();
                var sample = input.textComponent;
                if ((object)sample != null) { status.font = sample.font; status.fontSharedMaterial = sample.fontSharedMaterial; }
                status.fontSize = 22;
                status.enableWordWrapping = false;
                status.alignment = TextAlignmentOptions.TopLeft;
                var srt = status.rectTransform;
                srt.anchorMin = srt.anchorMax = new Vector2(0f, 0f);
                srt.pivot = new Vector2(0f, 1f);
                srt.sizeDelta = new Vector2(700f, 30f);
                srt.anchoredPosition = new Vector2(4f, -4f);
                status.text = "";
            }

            RevivalMod.Log.Msg($"[friends] requests page built: Friends tab {Size(root)}, alliance page {Size(arect)}, buttons at {friendsButton.transform.localPosition} and {requestsButton.transform.localPosition}");
            if (!hierarchyLogged) { hierarchyLogged = true; LogHierarchy(root, 0); }
        }

        private static string Size(RectTransform rt) { try { var r = rt.rect; return $"{r.width:0}x{r.height:0}"; } catch { return "?"; } }

        private static void LogHierarchy(Transform t, int depth)
        {
            if (depth > 5) return;
            try
            {
                var rt = t.GetComponent<RectTransform>();
                RevivalMod.Log.Msg($"[friends] {new string(' ', depth * 2)}{t.name} active={t.gameObject.activeSelf} " +
                                   ((object)rt != null ? $"pos={rt.anchoredPosition} size={rt.rect.width:0}x{rt.rect.height:0} anchors={rt.anchorMin}-{rt.anchorMax}" : ""));
                if (t.name.StartsWith("Friend") && depth >= 3) return;      // one list entry is enough
                for (int i = 0; i < t.childCount; i++) LogHierarchy(t.GetChild(i), depth + 1);
            }
            catch { }
        }

        private static Toggle Copy(Toggle original, RectTransform fromPage, RectTransform holder, string text)
        {
            var go = UnityEngine.Object.Instantiate(original.gameObject, holder, false);
            go.name = text;
            go.SetActive(true);
            (go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>()).ignoreLayout = true;
            var rt = go.GetComponent<RectTransform>();
            var ort = original.GetComponent<RectTransform>();
            // the original's place and size, in the alliance page's own coordinates
            Vector3 at = fromPage.InverseTransformPoint(ort.position);
            var size = ort.rect.size;
            Vector3 scale = new Vector3(ort.lossyScale.x / Math.Max(1e-6f, fromPage.lossyScale.x), ort.lossyScale.y / Math.Max(1e-6f, fromPage.lossyScale.y), 1f);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = ort.pivot;
            rt.sizeDelta = size;
            rt.localScale = scale;
            rt.localRotation = Quaternion.identity;
            // the holder fills the Friends tab: its centre is the tab's centre; the alliance page is measured from its own pivot
            var fr = fromPage.rect;
            var fromCentre = new Vector2(fr.center.x, fr.center.y);
            rt.anchoredPosition = new Vector2(at.x - fromCentre.x, at.y - fromCentre.y);
            var toggle = go.GetComponent<Toggle>();
            toggle.group = null;                                         // the alliance page's group stays with it
            toggle.onValueChanged = new Toggle.ToggleEvent();           // nothing of the alliance page comes along
            toggle.SetIsOnWithoutNotify(false);
            var label = go.GetComponentInChildren<TMP_Text>(true);
            if ((object)label != null) Ui.SetText(label, text);
            return toggle;
        }

        // ---------------------------------------------------------------- the two pages

        private static void ShowFriends(bool redraw)
        {
            showingRequests = false;
            if ((object)friendsButton != null) friendsButton.SetIsOnWithoutNotify(true);
            if ((object)requestsButton != null) requestsButton.SetIsOnWithoutNotify(false);
            ClearRows();
            Refresh(friendsButton); Refresh(requestsButton);
            if (redraw && Ui.Alive(tab)) { try { tab.FillInFriendsContainer(); } catch (Exception e) { RevivalMod.Log.Warning("[friends] " + e.Message); } }
        }

        private static void ShowRequests()
        {
            showingRequests = true;
            if ((object)friendsButton != null) friendsButton.SetIsOnWithoutNotify(false);
            if ((object)requestsButton != null) requestsButton.SetIsOnWithoutNotify(true);
            Refresh(friendsButton); Refresh(requestsButton);
            // the friend list's entries make way for the requests
            try
            {
                var container = tab.friendsContainer;
                for (int i = container.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(container.GetChild(i).gameObject);
            }
            catch { }
            ClearRows();
            AddText("Loading...");
            Load();
        }

        private static void Refresh(Toggle t)
        {
            try { var eg = t?.TryCast<EverguildToggle>(); if ((object)eg != null) eg.RefreshVisuals(); } catch { }
        }

        private static void ClearRows()
        {
            foreach (var r in rows) { try { if ((object)r != null) UnityEngine.Object.Destroy(r); } catch { } }
            rows.Clear();
        }

        private class Request { public string Id, Name, Code; }

        private static void Load()
        {
            Task.Run(async () =>
            {
                var incoming = new List<Request>();
                var outgoing = new List<Request>();
                string problem = null;
                try
                {
                    var reply = await Net.PostJson("/playfab/Revival/FriendRequests", Net.Json(w => w.WriteString("op", "list")), TimeSpan.FromSeconds(10), retry: true);
                    if (!reply.Ok) problem = reply.Unreachable ? "Could not reach the server" : reply.Error ?? "The server refused";
                    else
                    {
                        Read(reply.Data, "incoming", incoming);
                        Read(reply.Data, "outgoing", outgoing);
                    }
                }
                catch (Exception e) { problem = "Could not reach the server"; RevivalMod.Log.Warning("[friends] requests: " + e.Message); }
                MainThread.Enqueue(() =>
                {
                    if (!ShowingRequests) return;
                    Waiting = problem == null ? incoming.Count : Waiting;
                    ClearRows();
                    if (problem != null) { AddText(problem.TrimEnd('.')); return; }
                    if (incoming.Count == 0 && outgoing.Count == 0) { AddText("No friend requests. Add someone with their friend code above; they will see it here."); return; }
                    if (incoming.Count > 0) AddText("Waiting for your answer");
                    foreach (var r in incoming) AddRow(r, true);
                    if (outgoing.Count > 0) AddText("Sent by you, waiting for their answer");
                    foreach (var r in outgoing) AddRow(r, false);
                });
            });
        }

        private static void Read(JsonElement data, string key, List<Request> into)
        {
            if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty(key, out var list) || list.ValueKind != JsonValueKind.Array) return;
            foreach (var e in list.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object) continue;
                string id = e.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.String ? i.GetString() : null;
                if (string.IsNullOrEmpty(id)) continue;
                into.Add(new Request
                {
                    Id = id,
                    Name = e.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : "",
                    Code = e.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : "",
                });
            }
        }

        private static void AddText(string text)
        {
            try
            {
                var go = new GameObject("Requests note");
                go.transform.SetParent(tab.friendsContainer, false);
                var t = go.AddComponent<TextMeshProUGUI>();
                var sample = tab.inputFieldUserName?.textComponent;
                if ((object)sample != null) { t.font = sample.font; t.fontSharedMaterial = sample.fontSharedMaterial; }
                t.fontSize = 26;
                t.alignment = TextAlignmentOptions.MidlineLeft;
                t.enableWordWrapping = true;
                t.color = new Color(0.85f, 0.8f, 0.65f);
                t.text = text;
                var le = go.AddComponent<LayoutElement>();
                le.minHeight = le.preferredHeight = 50f;
                le.flexibleWidth = 1f;
                rows.Add(go);
            }
            catch (Exception e) { RevivalMod.Log.Warning("[friends] requests: " + e.Message); }
        }

        /// <summary>One request, drawn with the game's own friend entry: the name, and accept + decline (to you) or cancel
        /// (sent by you) in place of its challenge and remove buttons.</summary>
        private static void AddRow(Request r, bool toMe)
        {
            try
            {
                var prefab = tab.friendElementPrefab;
                var go = UnityEngine.Object.Instantiate(prefab.gameObject, tab.friendsContainer, false);
                go.name = "Request " + r.Code;
                rows.Add(go);
                var entry = go.GetComponent<FriendUIElementWarpforge>();
                var label = entry.userLabel;
                var challenge = entry.challengeButton;
                var remove = entry.deleteUserButton;
                var profile = entry.showProfileButton;
                try { if ((object)entry.connectedImage != null) entry.connectedImage.gameObject.SetActive(false); } catch { }
                try { if ((object)entry.notConnected != null) entry.notConnected.gameObject.SetActive(false); } catch { }
                UnityEngine.Object.Destroy(entry);                 // the entry's own buttons belong to a friend, not a request
                if ((object)label != null) Ui.SetText(label, string.IsNullOrEmpty(r.Code) ? r.Name : $"{r.Name}  <size=70%><color=#a89f86>{r.Code}</color></size>");
                if ((object)profile != null) profile.gameObject.SetActive(false);

                string id = r.Id, name = r.Name;
                if (toMe && (object)challenge != null)
                {
                    // accept: the add-friend button's look (+), in the challenge button's place
                    var add = tab.addButton;
                    var crt = challenge.GetComponent<RectTransform>();
                    var acc = UnityEngine.Object.Instantiate(add.gameObject, crt.parent, false);
                    acc.name = "Accept";
                    acc.transform.SetSiblingIndex(crt.GetSiblingIndex());
                    var art = acc.GetComponent<RectTransform>();
                    art.anchorMin = crt.anchorMin; art.anchorMax = crt.anchorMax; art.pivot = crt.pivot;
                    art.sizeDelta = crt.sizeDelta; art.anchoredPosition = crt.anchoredPosition; art.localScale = crt.localScale;
                    var le = acc.GetComponent<LayoutElement>();
                    var cle = challenge.GetComponent<LayoutElement>();
                    if ((object)le != null && (object)cle != null) { le.preferredWidth = cle.preferredWidth; le.preferredHeight = cle.preferredHeight; le.minWidth = cle.minWidth; le.minHeight = cle.minHeight; }
                    var b = acc.GetComponent<Button>();
                    b.onClick = new Button.ButtonClickedEvent();
                    b.interactable = true;
                    b.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction>(new Action(() => Answer("accept", id, name))));
                }
                if ((object)challenge != null) challenge.gameObject.SetActive(false);
                if ((object)remove != null)
                {
                    remove.onClick = new Button.ButtonClickedEvent();
                    remove.interactable = true;
                    string op = toMe ? "decline" : "cancel";
                    remove.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction>(new Action(() => Answer(op, id, name))));
                }
            }
            catch (Exception e) { RevivalMod.Log.Warning("[friends] request entry: " + e.Message); }
        }

        // ---------------------------------------------------------------- talking to the server

        private static void Answer(string op, string id, string name)
        {
            if (busy) return;
            busy = true;
            Task.Run(async () =>
            {
                string text; bool good = false;
                try
                {
                    var reply = await Net.PostJson("/playfab/Revival/FriendRequests", Net.Json(w => { w.WriteString("op", op); w.WriteString("id", id); }), TimeSpan.FromSeconds(10));
                    if (reply.Ok)
                    {
                        good = true;
                        text = op == "accept" ? $"You and {name} are now friends" : op == "decline" ? $"Declined {name}'s request" : $"Request to {name} cancelled";
                    }
                    else text = reply.Unreachable ? "Could not reach the server" : (reply.Error ?? "The server refused").TrimEnd('.');
                }
                catch (Exception e) { text = "Could not reach the server"; RevivalMod.Log.Warning("[friends] " + op + ": " + e.Message); }
                MainThread.Enqueue(() =>
                {
                    busy = false;
                    Say(text, good);
                    RevivalMod.Log.Msg($"[friends] {op} {id}: {text}");
                    if (good && op == "accept") FriendsLive.RefreshNow();
                    if (ShowingRequests) Load();
                });
            });
        }

        private static void Send()
        {
            try
            {
                if (busy || !Ui.Alive(tab)) return;
                var input = tab.inputFieldUserName;
                string code = ((object)input != null ? input.text : "")?.Trim() ?? "";
                if (code.Length == 0) { Say("Type a friend code first", false); return; }
                busy = true;
                Say("Sending...", true);
                Task.Run(async () =>
                {
                    string text; bool good = false, friendsNow = false;
                    try
                    {
                        var reply = await Net.PostJson("/playfab/Revival/FriendRequests", Net.Json(w => { w.WriteString("op", "send"); w.WriteString("code", code); }), TimeSpan.FromSeconds(10));
                        if (reply.Ok)
                        {
                            string name = reply.Data.TryGetProperty("name", out var n) ? n.GetString() : "";
                            if (string.IsNullOrEmpty(name)) name = "that player";
                            string result = reply.Data.TryGetProperty("result", out var rr) ? rr.GetString() : "";
                            good = true;
                            switch (result)
                            {
                                case "friends": text = $"You and {name} are now friends"; friendsNow = true; break;
                                case "already": text = $"{name} is already your friend"; break;
                                case "pending": text = $"Already sent to {name}; waiting for their answer"; break;
                                default: text = $"Friend request sent to {name}"; break;
                            }
                        }
                        else text = reply.Unreachable ? "Could not reach the server" : (reply.Error ?? "The server refused").TrimEnd('.');
                    }
                    catch (Exception e) { text = "Could not reach the server"; RevivalMod.Log.Warning("[friends] send: " + e.Message); }
                    MainThread.Enqueue(() =>
                    {
                        busy = false;
                        Say(text, good);
                        RevivalMod.Log.Msg("[friends] add: " + text);
                        if (good && Ui.Alive(tab)) { try { tab.inputFieldUserName.SetTextWithoutNotify(""); } catch { } }
                        if (friendsNow) FriendsLive.RefreshNow();
                        if (ShowingRequests) Load();
                    });
                });
            }
            catch (Exception e) { busy = false; RevivalMod.Log.Warning("[friends] send: " + e.Message); }
        }

        private static void Say(string text, bool good)
        {
            if ((object)status == null || !Ui.Alive(status)) return;
            status.text = text;
            status.color = good ? new Color(0.55f, 0.95f, 0.55f) : new Color(1f, 0.6f, 0.5f);
        }

        /// <summary>Removing a friend: the server takes you off each other's lists (instead of the game uploading its
        /// own copy of the list, which could drop a friend added since it was fetched). The list is fetched again.</summary>
        [HarmonyPatch(typeof(PlayerDataManager), nameof(PlayerDataManager.DeleteFriendFromList))]
        private static class Remove
        {
            private static bool Prefix(Friend friendToDelete)
            {
                try
                {
                    string id = friendToDelete?.id;
                    string name = friendToDelete?.name ?? "";
                    if (string.IsNullOrEmpty(id)) return false;
                    Task.Run(async () =>
                    {
                        string text; bool good = false;
                        try
                        {
                            var reply = await Net.PostJson("/playfab/Revival/FriendRequests", Net.Json(w => { w.WriteString("op", "remove"); w.WriteString("id", id); }), TimeSpan.FromSeconds(10));
                            good = reply.Ok;
                            text = good ? $"Removed {name} from your friends" : reply.Unreachable ? "Could not reach the server" : (reply.Error ?? "The server refused").TrimEnd('.');
                        }
                        catch (Exception e) { text = "Could not reach the server"; RevivalMod.Log.Warning("[friends] remove: " + e.Message); }
                        MainThread.Enqueue(() => { Say(text, good); RevivalMod.Log.Msg($"[friends] remove {id}: {text}"); FriendsLive.RefreshNow(); });
                    });
                }
                catch (Exception e) { RevivalMod.Log.Warning("[friends] remove: " + e.Message); }
                return false;
            }
        }

        /// <summary>Called every frame: answers from the server, and the number on the Requests button.</summary>
        internal static void Tick()
        {
            while (MainThread.TryDequeue(out var a)) { try { a(); } catch (Exception e) { RevivalMod.Log.Warning("[friends] " + e.Message); } }
            int now = Waiting;
            if (now == shownWaiting || (object)requestsLabel == null) return;
            bool more = now > shownWaiting && shownWaiting >= 0;
            shownWaiting = now;
            try { if (Ui.Alive(requestsLabel)) Ui.SetText(requestsLabel, now > 0 ? $"Requests ({now})" : "Requests"); } catch { }
            if (more && ShowingRequests) Load();          // a new request came in while the page is open
        }
    }
}
