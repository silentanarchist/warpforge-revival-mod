using System;
using HarmonyLib;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppTMPro;
using UnityEngine;

namespace WarpforgeRevival
{
    /// <summary>
    /// Settings (gear) > Account on a revival server: the game's e-mail login form is reused to link
    /// this game player to an account made on the Revival site (optional). The display name and the
    /// friend code are on the profile page; the helpers for them live here.
    /// </summary>
    internal static class AccountPage
    {
        /// <summary>Checks and saves a new display name; reports (saved, message).</summary>
        internal static void ChangeName(string typed, Action<bool, string> done)
        {
            var pd = PlayerDataManager.singletonManager;
            string wanted = string.Join(" ", (typed ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            if (wanted.Length < MinName || wanted.Length > MaxName) { done(false, $"Display name must be {MinName}-{MaxName} characters long."); return; }
            if (wanted == CurrentName(pd)) { done(false, "That is already your name."); return; }
            Action<bool, Il2CppPlayFab.PlayFabError> answered = (ok, error) =>
            {
                try
                {
                    if (ok)
                    {
                        pd.playerName = wanted;
                        try { PlayerDataManager.OnPlayerNameChanged?.Invoke(wanted); } catch { }
                        RevivalMod.Log.Msg($"[account] display name changed to '{wanted}'");
                        done(true, "Name saved.");
                    }
                    else
                    {
                        string why = (object)error != null ? error.ErrorMessage : null;
                        RevivalMod.Log.Msg($"[account] name change refused: {why}");
                        done(false, string.IsNullOrEmpty(why) ? "The server did not accept that name." : why);
                    }
                }
                catch (Exception e) { RevivalMod.Log.Warning("[account] " + e.Message); }
            };
            pd.SetPlayfabDisplayName(wanted, DelegateSupport.ConvertDelegate<Il2CppSystem.Action<bool, Il2CppPlayFab.PlayFabError>>(answered));
        }

        internal static string FriendCode(string playFabId)
        {
            if (string.IsNullOrEmpty(playFabId) || playFabId.Length < 8) return "";
            return (playFabId.Substring(0, 4) + "-" + playFabId.Substring(4, 4)).ToUpperInvariant();
        }

        private static void Hide(Component c)
        {
            if ((object)c != null && c.Pointer != IntPtr.Zero) c.gameObject.SetActive(false);
        }

        private static void SetPlaceholder(TMP_InputField input, string text)
        {
            var ph = input.placeholder;
            if ((object)ph == null) return;
            var label = ph.TryCast<TMP_Text>();
            if ((object)label != null) label.text = text;
        }

        private static void Label(Transform form, string child, string text)
        {
            var t = form.Find(child);
            if ((object)t == null) return;
            var loc = t.GetComponent<Il2CppI2.Loc.Localize>();
            if ((object)loc != null) loc.enabled = false;     // otherwise it puts "E-mail"/"Password" back
            var label = t.GetComponent<TMP_Text>();
            if ((object)label != null) label.text = text;
        }

        internal const int MinName = 3, MaxName = 20;

        internal static string CurrentName(PlayerDataManager pd)
        {
            string n = pd.playerName;
            if (string.IsNullOrEmpty(n)) n = pd.playerNameSocial;
            return n ?? "";
        }

        // ------------------------------------------------------------------ account link
        // Settings > Account: the game's e-mail login form, used to link this game player to an
        // account made on the Revival site (username + passphrase). Linking is optional; it lets the
        // server apply the account's permissions (in-game tester) to this player. The passphrase is
        // sent once to make the link and is not kept anywhere on this computer.

        private static readonly System.Net.Http.HttpClient Http = Net.Client(TimeSpan.FromSeconds(15));
        private static volatile string linkedName;          // null: not asked yet, "": not linked
        private static volatile bool tester, busy;
        private static bool described;

        /// <summary>Site account this player is linked to ("" when none or not known yet).</summary>
        internal static string LinkedAccount => linkedName ?? "";
        /// <summary>True when the linked site account is an in-game tester.</summary>
        internal static bool IsTester => tester;
        /// <summary>Whether the linked site account has a recovery code: null until the server has said (servers before 2026-10-16 do not).</summary>
        internal static bool? HasRecovery => hasRecovery;
        private static bool? hasRecovery;
        /// <summary>A recovery code was just made in the game (RecoveryCode).</summary>
        internal static void RecoveryMade() => hasRecovery = true;
        /// <summary>True once the server has answered whether this player is linked (and so whether it is a tester).</summary>
        internal static bool StatusKnown => linkedName != null;

        /// <summary>Called when the game has signed in: ask the server whether this player is linked.</summary>
        internal static void SignedIn()
        {
            StatusWithRetry(1);
            ProfileStats.Prefetch();       // your own record, ready before your profile is opened
        }

        // The answer decides whether this player sees tester-only things (the Testers button). Asked while the
        // game is busiest loading, the request sometimes failed to get through, and the button then stayed
        // away for the whole session (seen on a Pixel 6 Pro, 2026-10-10), so an unreachable server is asked again.
        private static void StatusWithRetry(int attempt) => Send("status", null, null, (ok, message) =>
        {
            if (ok || message != Unreachable || attempt >= 5) return;
            int wait = 2 * attempt;
            RevivalMod.Log.Msg($"[account] asking the server again in {wait} s (try {attempt + 1} of 5)");
            System.Threading.Tasks.Task.Delay(wait * 1000).ContinueWith(_ => PlayFabTransport.OnMainThread(() => StatusWithRetry(attempt + 1)));
        });

        private const string Unreachable = "Could not reach the server.";

        private static string Inner(Exception e)
        {
            string text = e.Message;
            for (var i = e.InnerException; i != null; i = i.InnerException) text += " / " + i.GetType().Name + ": " + i.Message;
            return text;
        }

        private static void Send(string op, string name, string passphrase, Action<bool, string> done)
        {
            string ticket = PlayFabTransport.SessionTicket;
            if (string.IsNullOrEmpty(ticket)) { done?.Invoke(false, "Not signed in to the server yet."); return; }
            string url = RevivalMod.Config.ServerUrl + "/playfab/Revival/AccountLink";
            System.Threading.Tasks.Task.Run(async () =>
            {
                bool ok = false;
                string message;
                try
                {
                    using var ms = new System.IO.MemoryStream();
                    using (var w = new System.Text.Json.Utf8JsonWriter(ms))
                    {
                        w.WriteStartObject();
                        w.WriteString("op", op);
                        if (name != null) w.WriteString("name", name);
                        if (passphrase != null) w.WriteString("passphrase", passphrase);
                        w.WriteEndObject();
                    }
                    using var msg = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, url)
                    {
                        Content = new System.Net.Http.StringContent(System.Text.Encoding.UTF8.GetString(ms.ToArray()), System.Text.Encoding.UTF8, "application/json")
                    };
                    msg.Headers.TryAddWithoutValidation("X-Authorization", ticket);
                    string login = op == "unlink" ? GameSignIn.Token() : null;      // the one saved sign-in to cancel
                    if (login != null) msg.Headers.TryAddWithoutValidation("X-Revival-Login", login);
                    System.Net.Http.HttpResponseMessage reply;
                    try { reply = await Http.SendAsync(msg); }
                    catch (System.Net.Http.HttpRequestException first)
                    {
                        // usually a kept-open connection the server had already closed: one more try on a new one
                        RevivalMod.Log.Msg("[account] " + op + ": sending failed (" + Inner(first) + "); trying once more");
                        await System.Threading.Tasks.Task.Delay(500);
                        using var again = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, url)
                        {
                            Content = new System.Net.Http.StringContent(System.Text.Encoding.UTF8.GetString(ms.ToArray()), System.Text.Encoding.UTF8, "application/json")
                        };
                        foreach (var h in msg.Headers) again.Headers.TryAddWithoutValidation(h.Key, h.Value);
                        reply = await Http.SendAsync(again);
                    }
                    using var replyOwned = reply;
                    using var doc = System.Text.Json.JsonDocument.Parse(await reply.Content.ReadAsStringAsync());
                    var root = doc.RootElement;
                    if (reply.IsSuccessStatusCode && root.TryGetProperty("data", out var data))
                    {
                        string linked = data.TryGetProperty("linked", out var l) ? l.GetString() ?? "" : "";
                        bool isTester = data.TryGetProperty("account", out var a) && a.ValueKind == System.Text.Json.JsonValueKind.Object &&
                                        a.TryGetProperty("tester", out var t) && t.ValueKind == System.Text.Json.JsonValueKind.True;
                        if (data.TryGetProperty("signedOut", out var so) && so.ValueKind == System.Text.Json.JsonValueKind.True)
                            GameSignIn.SignedOut(op == "unlink" && linked.Length > 0
                                ? "Signed out of " + linked + " on this device. Start the game again to sign in - with this account or another one."
                                : "This game was unlinked from its account. Start the game again and sign in.");
                        if (data.TryGetProperty("recovery", out var rc) && (rc.ValueKind == System.Text.Json.JsonValueKind.True || rc.ValueKind == System.Text.Json.JsonValueKind.False))
                            hasRecovery = linked.Length > 0 && rc.ValueKind == System.Text.Json.JsonValueKind.True;
                        bool changed = linked != linkedName || isTester != tester;
                        linkedName = linked;
                        tester = isTester;
                        ok = true;
                        message = linked.Length > 0 ? "Linked to " + linked + (isTester ? " (in-game tester)." : ".") : "Not linked to a site account.";
                        if (changed)
                        {
                            RevivalMod.Log.Msg("[account] " + message);
                            PlayFabTransport.OnMainThread(TestersTab.RoleChanged);      // the Testers button follows the role
                        }
                    }
                    else
                    {
                        message = root.TryGetProperty("errorMessage", out var e) ? e.GetString() : null;
                        if (string.IsNullOrEmpty(message)) message = "The server did not accept that (" + (int)reply.StatusCode + ").";
                        else message = char.ToUpperInvariant(message[0]) + message.Substring(1) + ".";
                        RevivalMod.Log.Msg("[account] " + op + " refused: " + message);
                    }
                }
                catch (Exception e)
                {
                    message = Unreachable;
                    RevivalMod.Log.Warning("[account] " + op + ": " + Inner(e));
                }
                if (done != null) PlayFabTransport.OnMainThread(() => done(ok, message));
            });
        }

        private static bool Usable(AccountTab tab) => (object)tab != null && tab.Pointer != IntPtr.Zero && tab.m_CachedPtr != IntPtr.Zero;

        private static void Apply(AccountTab tab)
        {
            try
            {
                if (!Usable(tab)) return;
                bool linked = !string.IsNullOrEmpty(linkedName);

                var user = tab.emailInput;
                var pass = tab.passwordInput;
                if ((object)user != null)
                {
                    user.gameObject.SetActive(true);
                    user.contentType = TMP_InputField.ContentType.Standard;
                    user.characterLimit = 30;
                    user.readOnly = linked;
                    user.interactable = !linked;
                    SetPlaceholder(user, "Site username");
                    if (linked) user.SetTextWithoutNotify(linkedName);
                    user.ForceLabelUpdate();
                    var form = user.transform.parent;
                    if ((object)form != null)
                    {
                        Label(form, "EmailText", "Username");
                        Label(form, "PasswordText", linked ? "" : "Password");
                    }
                }
                if ((object)pass != null)
                {
                    pass.gameObject.SetActive(!linked);          // nothing to type once linked
                    pass.contentType = TMP_InputField.ContentType.Password;
                    pass.characterLimit = 200;
                    pass.interactable = true;
                    SetPlaceholder(pass, "Password");
                    pass.ForceLabelUpdate();
                }

                Hide(tab.loginButton); Hide(tab.logoutButton); Hide(tab.switchAccountButton);
                Hide(tab.resetPasswordButton); Hide(tab.forgotPasswordButton); Hide(tab.deleteaccountButton);
                Hide(tab.newsletterLink); Hide(tab.twitchLink); Hide(tab.socialMediaLinks);

                var button = tab.registerButton;
                if ((object)button != null)
                {
                    button.gameObject.SetActive(true);
                    var group = button.transform.parent;
                    if ((object)group != null) group.gameObject.SetActive(true);
                    button.interactable = !busy;
                    var label = button.GetComponentInChildren<TMP_Text>(true);
                    if ((object)label != null)
                    {
                        var loc = label.GetComponent<Il2CppI2.Loc.Localize>();
                        if ((object)loc != null) loc.enabled = false;
                        label.text = !linked ? "Link account" : SignedInWithAccount ? "Sign out" : "Unlink account";
                    }
                }

                try { Arrange(tab, linked); } catch (Exception e) { RevivalMod.Log.Warning("[account] layout: " + e.Message); }

                if (!described)
                {
                    described = true;
                    RevivalMod.Log.Msg($"[account] page set up: {(linked ? "linked to " + linkedName : "not linked")}");
                }
            }
            catch (Exception e) { RevivalMod.Log.Warning("[account] " + e.Message); }
        }

        // ---- "Open profile on website" button once linked (0.13.29; placed on the button's row in 0.13.31).
        private const string ProfileName = "WFR Open profile";
        private static void Arrange(AccountTab tab, bool linked)
        {
            // The page's own layout places the buttons (and puts them back whenever it is laid out again,
            // e.g. when the pointer moves over one), so the Sign out button is left where it puts it. The
            // "Open profile on website" button is kept out of that layout (ignoreLayout) and placed on the
            // same row, starting at the left edge of the username box - well away from Sign out on the right.
            // The message line is not part of that layout; it goes up just under the username box.
            var user = tab.emailInput;
            var button = tab.registerButton;
            if ((object)user == null || (object)button == null) return;
            var userRt = user.GetComponent<RectTransform>();
            var btnT = button.transform;
            if ((object)userRt == null || (object)btnT.parent == null) return;

            var profile = btnT.parent.Find(ProfileName);
            if ((object)profile == null && linked)
            {
                var go = UnityEngine.Object.Instantiate(button.gameObject, btnT.parent, false);
                go.name = ProfileName;
                profile = go.transform;
                var le = go.GetComponent<UnityEngine.UI.LayoutElement>() ?? go.AddComponent<UnityEngine.UI.LayoutElement>();
                le.ignoreLayout = true;
                var copy = go.GetComponent<UnityEngine.UI.Button>();
                if ((object)copy != null)
                {
                    copy.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();     // not the original's "link / sign out"
                    copy.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction>(new Action(OpenProfile)));
                }
                var label = go.GetComponentInChildren<TMP_Text>(true);
                if ((object)label != null)
                {
                    var loc = label.GetComponent<Il2CppI2.Loc.Localize>();
                    if ((object)loc != null) loc.enabled = false;
                    label.text = "Open profile on website";
                    label.enableAutoSizing = true;
                }
                RevivalMod.Log.Msg("[account] Open profile button added");
            }
            if ((object)profile == null) return;
            profile.gameObject.SetActive(linked);
            if (!linked) { RecoveryCode.Place(tab, false, userRt, button, ""); return; }
            // the message line (which the page's layout leaves alone) goes up just under the username box,
            // into the space the hidden password row left
            var msg = tab.errorMessage;
            var msgRt = (object)msg == null ? null : msg.GetComponent<RectTransform>();
            if ((object)msgRt != null)
            {
                var u = Corners(userRt);
                var m = Corners(msgRt);
                float target = u.bottom - (u.top - u.bottom) * 0.5f;
                if (Math.Abs(m.top - target) > 0.001f) msg.transform.position += new Vector3(0f, target - m.top, 0f);

                // and the row of buttons goes up under the message line. The row is the buttons' own holder, moved
                // as a whole (the page's layout arranges the buttons inside it, not the holder itself); it is only
                // moved when it holds nothing else of the page (not the username box nor the message line).
                var row = btnT.parent;
                var rowRt = row.GetComponent<RectTransform>();
                if ((object)rowRt != null && !Under(userRt.transform, row) && !Under(msg.transform, row))
                {
                    var brt = button.GetComponent<RectTransform>();
                    if ((object)brt != null)
                    {
                        var b = Corners(brt);
                        m = Corners(msgRt);
                        float lineH = Math.Max(0.0001f, (u.top - u.bottom) * 0.35f);
                        float want = m.bottom - lineH;                      // the buttons' top, a little under the text
                        if (Math.Abs(b.top - want) > 0.001f)
                        {
                            row.position += new Vector3(0f, want - b.top, 0f);
                            if (!rowLogged) { rowLogged = true; RevivalMod.Log.Msg("[account] button row '" + row.name + "' moved up under the message line"); }
                        }
                    }
                }
                else if (!rowLogged) { rowLogged = true; RevivalMod.Log.Msg("[account] button row not moved: its holder '" + row.name + "' also holds other parts of the page"); }
            }
            var prt = profile.GetComponent<RectTransform>();
            if ((object)prt == null) return;
            // same row as the button; its left edge on the username box's left edge
            float userLeft = Left(userRt), half = profile.position.x - Left(prt);
            profile.position = new Vector3(userLeft + half, btnT.position.y, btnT.position.z);
            var c = profile.GetComponent<UnityEngine.UI.Button>();
            if ((object)c != null) c.interactable = true;
            // under this row: password box and "Create recovery code" (0.13.35)
            try { RecoveryCode.Place(tab, true, userRt, button, linkedName ?? ""); }
            catch (Exception e) { RevivalMod.Log.Warning("[account] recovery code part: " + e.Message); }
        }

        /// <summary>Places the page's added parts again (RecoveryCode, after a code was made).</summary>
        internal static void Rearrange(AccountTab tab) => Arrange(tab, !string.IsNullOrEmpty(linkedName));

        private static bool rowLogged;

        private static bool Under(Transform t, Transform ancestor)
        {
            for (; (object)t != null; t = t.parent) if (t.Pointer == ancestor.Pointer) return true;
            return false;
        }

        private static (float top, float bottom) Corners(RectTransform rt)
        {
            var c = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(4);
            rt.GetWorldCorners(c);
            return (Math.Max(c[0].y, c[1].y), Math.Min(c[0].y, c[1].y));
        }

        private static float Left(RectTransform rt)
        {
            var c = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(4);
            rt.GetWorldCorners(c);
            return Math.Min(c[0].x, c[2].x);
        }

        private static void OpenProfile()
        {
            try
            {
                string site = ServerSettings.CreatorUrl;
                string url;
                try { url = new Uri(site).GetLeftPart(UriPartial.Authority) + "/profile"; } catch { url = site; }
                RevivalMod.Log.Msg("[account] opening the website profile: " + url);
                Application.OpenURL(url);
            }
            catch (Exception e) { RevivalMod.Log.Warning("[account] open profile: " + e.Message); }
        }

        /// <summary>
        /// True when this game signs in with a site account (a saved game login): the account and
        /// its game player are then one thing, and the button only signs this computer or phone out.
        /// </summary>
        private static bool SignedInWithAccount => GameSignIn.Token() != null;

        private static string Status()
        {
            if (linkedName == null) return "Checking the link...";
            if (linkedName.Length == 0) return "Optional: link an account made on the Revival site. Enter its username and password.";
            return "Linked to " + linkedName + (tester ? " - in-game tester." : ".");
        }

        private static void Say(AccountTab tab, string text, bool problem = false)
        {
            try
            {
                if (!Usable(tab)) return;
                var msg = tab.errorMessage;
                if ((object)msg == null) return;
                msg.gameObject.SetActive(true);
                msg.color = problem ? new Color(1f, 0.25f, 0.2f) : new Color(0.75f, 0.85f, 0.8f);
                msg.text = text;
            }
            catch { }
        }

        // ---------------------------------------------------------------- when the tab comes into view
        private static AccountTab seen;
        private static int seenFrames;
        private static bool seenReported;

        /// <summary>Told about the tab as soon as the Settings window is built (see SupportPage).</summary>
        internal static void Remember(AccountTab tab)
        {
            if (!Usable(tab)) return;
            // the button row's phone/PC layout, settled at set-up (0.13.55): see MenuLayout.SettlePlatformLayout
            int settled = MenuLayout.SettlePlatformLayout(tab);
            if (settled > 0) RevivalMod.Log.Msg($"[account] phone/PC layout settled at set-up ({settled})");
            // (the page itself is filled in by the Refresh / OnOpen postfixes, in the frame the game opens it)
            seen = tab;
            seenFrames = 0;
        }

        /// <summary>
        /// Called every frame. The game lays this tab out again whenever it is opened - on a phone
        /// quite differently from a PC - and that can undo the changes made above. So each time the
        /// tab comes into view, wait two frames for the layout to finish and apply them once more.
        /// </summary>
        internal static void Tick()
        {
            if ((object)seen == null) return;
            try
            {
                if (!Usable(seen)) { seen = null; return; }
                if (!seen.gameObject.activeInHierarchy) { seenFrames = 0; return; }
                if (seenFrames < 0) return;
                if (++seenFrames < 3) return;
                seenFrames = -1;
                var tab = seen;
                Apply(tab);
                Say(tab, Status());
                if (seenReported) return;
                seenReported = true;
                string State(Component c) => (object)c == null ? "missing" : c.gameObject.activeInHierarchy ? "on screen" : "off";
                RevivalMod.Log.Msg("[account] tab opened: username box " + State(tab.emailInput) + ", password box " + State(tab.passwordInput)
                    + ", link button " + State(tab.registerButton) + ", message line " + State(tab.errorMessage)
                    + ", sign-in button " + State(tab.loginButton) + ", sign-out button " + State(tab.logoutButton));
            }
            catch (Exception e)
            {
                seen = null;
                RevivalMod.Log.Warning("[account] " + e.Message);
            }
        }

        private static void Opened(AccountTab tab)
        {
            Apply(tab);
            Say(tab, Status());
            // the link may have been changed from the site since the game started
            // if this re-check fails but the link is already known (from the sign-in), keep showing it
            Send("status", null, null, (ok, message) => { Apply(tab); bool keep = !ok && StatusKnown; Say(tab, ok || keep ? Status() : message, !ok && !keep); });
        }

#if ANDROID_PORT
        // On the phone "open the page" is a single jump into "refresh the page" - too short to hook
        // safely (see NoShortHooks) - so opening is noticed through the refresh it always causes.
        private static float lastOpened = -100f;

        [HarmonyPatch(typeof(AccountTab), nameof(AccountTab.Refresh))]
        private static class OnRefresh
        {
            private static void Postfix(AccountTab __instance)
            {
                float now = Time.realtimeSinceStartup;
                if (now - lastOpened < 3f) { Apply(__instance); return; }
                lastOpened = now;
                Opened(__instance);
            }
        }
#else
        [HarmonyPatch(typeof(AccountTab), nameof(AccountTab.Refresh))]
        private static class OnRefresh
        {
            private static void Postfix(AccountTab __instance) => Apply(__instance);
        }

        [HarmonyPatch(typeof(AccountTab), nameof(AccountTab.OnOpen))]
        private static class OnOpen
        {
            private static void Postfix(AccountTab __instance) => Opened(__instance);
        }
#endif

        // The page's main button links or unlinks.
        [HarmonyPatch(typeof(AccountTab), nameof(AccountTab.Register))]
        private static class LinkOrUnlink
        {
            private static bool Prefix(AccountTab __instance)
            {
                var tab = __instance;
                try
                {
                    if (busy) return false;
                    bool linked = !string.IsNullOrEmpty(linkedName);
                    string name = null, pass = null;
                    if (!linked)
                    {
                        name = (tab.emailInput.text ?? "").Trim();
                        pass = tab.passwordInput.text ?? "";
                        if (name.Length == 0 || pass.Length == 0) { Say(tab, "Enter your site username and password.", true); return false; }
                    }
                    busy = true;
                    Apply(tab);
                    bool signOut = linked && SignedInWithAccount;
                    Say(tab, !linked ? "Linking..." : signOut ? "Signing out..." : "Unlinking...");
                    Send(linked ? "unlink" : "link", name, pass, (ok, message) =>
                    {
                        busy = false;
                        try { if (Usable(tab) && (object)tab.passwordInput != null) tab.passwordInput.SetTextWithoutNotify(""); } catch { }
                        if (ok && !linked) { try { tab.emailInput.SetTextWithoutNotify(linkedName ?? ""); } catch { } }
                        if (ok && linked) { try { tab.emailInput.SetTextWithoutNotify(""); } catch { } }
                        Apply(tab);
                        Say(tab, ok ? Status() : message, !ok);
                    });
                }
                catch (Exception e)
                {
                    busy = false;
                    RevivalMod.Log.Warning("[account] " + e);
                    Say(tab, "Could not do that.", true);
                }
                return false;
            }
        }

        // Friend codes contain a dash; the server decides whether a name or code exists.
        [HarmonyPatch(typeof(SupportMethods), nameof(SupportMethods.IsValidFriendName))]
        private static class AnyFriendName
        {
            private static bool Prefix(string friendName, ref bool __result)
            {
                __result = !string.IsNullOrWhiteSpace(friendName);
                return false;
            }
        }
    }
}
