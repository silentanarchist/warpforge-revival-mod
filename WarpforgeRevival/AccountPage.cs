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

        private static readonly System.Net.Http.HttpClient Http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        private static volatile string linkedName;          // null: not asked yet, "": not linked
        private static volatile bool tester, busy;
        private static bool described;

        /// <summary>Site account this player is linked to ("" when none or not known yet).</summary>
        internal static string LinkedAccount => linkedName ?? "";
        /// <summary>True when the linked site account is an in-game tester.</summary>
        internal static bool IsTester => tester;

        /// <summary>Called when the game has signed in: ask the server whether this player is linked.</summary>
        internal static void SignedIn() => Send("status", null, null, null);

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
                    using var reply = await Http.SendAsync(msg);
                    using var doc = System.Text.Json.JsonDocument.Parse(await reply.Content.ReadAsStringAsync());
                    var root = doc.RootElement;
                    if (reply.IsSuccessStatusCode && root.TryGetProperty("data", out var data))
                    {
                        string linked = data.TryGetProperty("linked", out var l) ? l.GetString() ?? "" : "";
                        bool isTester = data.TryGetProperty("account", out var a) && a.ValueKind == System.Text.Json.JsonValueKind.Object &&
                                        a.TryGetProperty("tester", out var t) && t.ValueKind == System.Text.Json.JsonValueKind.True;
                        bool changed = linked != linkedName || isTester != tester;
                        linkedName = linked;
                        tester = isTester;
                        ok = true;
                        message = linked.Length > 0 ? "Linked to " + linked + (isTester ? " (in-game tester)." : ".") : "Not linked to a site account.";
                        if (changed) RevivalMod.Log.Msg("[account] " + message);
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
                    message = "Could not reach the server.";
                    RevivalMod.Log.Warning("[account] " + op + ": " + e.Message);
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
                        label.text = linked ? "Unlink account" : "Link account";
                    }
                }

                if (!described)
                {
                    described = true;
                    RevivalMod.Log.Msg($"[account] page set up: {(linked ? "linked to " + linkedName : "not linked")}");
                }
            }
            catch (Exception e) { RevivalMod.Log.Warning("[account] " + e.Message); }
        }

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

        [HarmonyPatch(typeof(AccountTab), nameof(AccountTab.Refresh))]
        private static class OnRefresh
        {
            private static void Postfix(AccountTab __instance) => Apply(__instance);
        }

        [HarmonyPatch(typeof(AccountTab), nameof(AccountTab.OnOpen))]
        private static class OnOpen
        {
            private static void Postfix(AccountTab __instance)
            {
                var tab = __instance;
                Apply(tab);
                Say(tab, Status());
                // the link may have been changed from the site since the game started
                Send("status", null, null, (ok, message) => { Apply(tab); Say(tab, ok ? Status() : message, !ok); });
            }
        }

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
                    Say(tab, linked ? "Unlinking..." : "Linking...");
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
