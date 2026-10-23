using System;
using System.IO;
using System.Text;
using System.Text.Json;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WarpforgeRevival
{
    /// <summary>
    /// Settings > Account, once linked: make a recovery code for the site account without the website
    /// (POST /playfab/Revival/GameRecoveryCode with the account's password; same code and limits as the
    /// Profile page). Below the Open profile / Sign out row: a password box, then "Create recovery code".
    /// The code is saved to a file straight away (Downloads when the game can write there, otherwise the
    /// mod's UserData folder) and shown hidden (for streamers) with Show and Copy buttons. It is never
    /// written to the log.
    /// </summary>
    internal static class RecoveryCode
    {
        private const string PassName = "WFR Recovery password", MakeName = "WFR Recovery make",
                             ShowName = "WFR Recovery show", CopyName = "WFR Recovery copy", TextName = "WFR Recovery text";
        private static string code, savedTo, status = "", account;
        private static bool shown, busy, placedLogged;
        private static TMP_InputField passBox;
        private static TMP_Text line;
        private static TMP_Text showLabel;

        private static void PutAt(Transform t, float left, float top)
        {
            var rt = t.GetComponent<RectTransform>();
            if ((object)rt == null) return;
            var b = Ui.WorldBox(rt);
            t.position += new Vector3(left - b.left, top - b.top, 0f);
        }

        private static GameObject CloneButton(Button template, Transform parent, string name, string text, Action click)
        {
            var go = UnityEngine.Object.Instantiate(template.gameObject, parent, false);
            go.name = name;
            go.SetActive(true);
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
            var b = go.GetComponent<Button>();
            if ((object)b != null)
            {
                b.onClick = new Button.ButtonClickedEvent();
                b.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction>(new Action(() =>
                {
                    try { click(); } catch (Exception e) { RevivalMod.Log.Warning("[account] recovery code: " + e.Message); }
                })));
                b.interactable = true;
            }
            var label = go.GetComponentInChildren<TMP_Text>(true);
            if ((object)label != null)
            {
                Ui.SetText(label, text);
                label.enableAutoSizing = true;
            }
            return go;
        }

        /// <summary>Called from AccountPage.Arrange: builds (once) and places the recovery code part under the button row.</summary>
        internal static void Place(AccountTab tab, bool linked, RectTransform userRt, Button rowButton, string linkedName)
        {
            var form = userRt.parent;
            var row = rowButton.transform.parent;
            if ((object)form == null || (object)row == null) return;
            var passT = form.Find(PassName);
            var makeT = row.Find(MakeName);
            if ((object)passT == null && linked)
            {
                var template = tab.passwordInput;
                if ((object)template == null) return;
                var go = UnityEngine.Object.Instantiate(template.gameObject, form, false);
                go.name = PassName;
                go.SetActive(true);
                var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
                le.ignoreLayout = true;
                passBox = go.GetComponent<TMP_InputField>();
                if ((object)passBox != null)
                {
                    passBox.contentType = TMP_InputField.ContentType.Password;
                    passBox.characterLimit = 200;
                    passBox.readOnly = false;
                    passBox.interactable = true;
                    passBox.SetTextWithoutNotify("");
                    var ph = passBox.placeholder?.TryCast<TMP_Text>();
                    if ((object)ph != null) { Loc.Bind(ph, () => Loc.T("recovery.passwordHint", "Password (for a recovery code)")); Ui.BrighterHint(ph); }
                }
                passT = go.transform;

                var make = CloneButton(rowButton, row, MakeName, Loc.T("recovery.create", "Create recovery code"), Make);
                makeT = make.transform;
                Loc.Bind(make.GetComponentInChildren<TMP_Text>(true), () => Loc.T("recovery.create", "Create recovery code"));
                var show = CloneButton(rowButton, row, ShowName, Loc.T("recovery.show", "Show"), Toggle);
                showLabel = show.GetComponentInChildren<TMP_Text>(true);
                var copy = CloneButton(rowButton, row, CopyName, Loc.T("recovery.copy", "Copy"), Copy);
                Loc.Bind(copy.GetComponentInChildren<TMP_Text>(true), () => Loc.T("recovery.copy", "Copy"));

                var tgo = new GameObject(TextName);
                var trt = tgo.AddComponent<RectTransform>();
                trt.SetParent(form, false);
                tgo.AddComponent<LayoutElement>().ignoreLayout = true;
                trt.sizeDelta = new Vector2(userRt.rect.width * 1.9f, userRt.rect.height * 1.6f);
                var t = tgo.AddComponent<TextMeshProUGUI>();
                var font = tab.errorMessage;
                if ((object)font != null) { t.font = font.font; t.fontSize = font.fontSize * 0.8f; t.color = font.color; }
                else t.fontSize = 22;
                t.enableWordWrapping = true;
                t.alignment = TextAlignmentOptions.TopLeft;
                t.raycastTarget = false;
                line = t;
            }
            if ((object)passT == null || (object)makeT == null) return;
            account = linkedName;
            var showT = row.Find(ShowName); var copyT = row.Find(CopyName); var textT = form.Find(TextName);
            foreach (var x in new[] { passT, makeT, showT, copyT, textT }) if ((object)x != null) x.gameObject.SetActive(linked);
            if (!linked) return;

            // under the button row: the password box (the username box's width), then the buttons, then the line
            var u = Ui.WorldBox(userRt);
            var r = Ui.WorldBox(rowButton.GetComponent<RectTransform>());
            float gap = (u.top - u.bottom) * 0.35f;
            PutAt(passT, u.left, r.bottom - gap * 1.4f);
            var p = Ui.WorldBox(passT.GetComponent<RectTransform>());
            PutAt(makeT, u.left, p.bottom - gap);
            var m = Ui.WorldBox(makeT.GetComponent<RectTransform>());
            if ((object)showT != null) { PutAt(showT, m.right + gap, m.top); showT.gameObject.SetActive(code != null); }
            if ((object)copyT != null && (object)showT != null) { var s = Ui.WorldBox(showT.GetComponent<RectTransform>()); PutAt(copyT, s.right + gap, m.top); copyT.gameObject.SetActive(code != null); }
            if ((object)textT != null)
            {
                // the line wraps inside the page: from the username box's left edge to the furthest right of the
                // username box and the Copy button (it was 1.9 x the box's width and ran off the page in Spanish, 0.14.65)
                float right = u.right;
                if ((object)copyT != null) right = Math.Max(right, Ui.WorldBox(copyT.GetComponent<RectTransform>()).right);
                var trt = textT.GetComponent<RectTransform>();
                float scale = Math.Abs(trt.lossyScale.x) > 1e-5f ? Math.Abs(trt.lossyScale.x) : 1f;
                trt.sizeDelta = new Vector2((right - u.left) / scale, userRt.rect.height * 4f);
                PutAt(textT, u.left, m.bottom - gap * 0.6f);
            }
            var mb = makeT.GetComponent<Button>();
            if ((object)mb != null) mb.interactable = !busy;
            Refresh();
            if (!placedLogged)
            {
                placedLogged = true;
                RevivalMod.Log.Msg($"[account] recovery code part placed: password box top {p.top:0.00}, button bottom {m.bottom:0.00} (button row bottom {r.bottom:0.00})");
            }
        }

        private static string Masked(string c) => System.Text.RegularExpressions.Regex.Replace(c ?? "", "[0-9A-Za-z]", "*");

        private static void Refresh()
        {
            try
            {
                if ((object)line != null && line.Pointer != IntPtr.Zero)
                {
                    string text = status;
                    if (code != null)
                        text = Loc.T("recovery.code", "Recovery code: {code}", ("code", "<b>" + (shown ? code : Masked(code)) + "</b>")) + "\n" +
                               (savedTo != null ? Loc.T("recovery.savedTo", "Saved to {path}", ("path", savedTo)) + "\n" : "") +
                               Loc.T("recovery.onlyHere", "It is shown only on this screen. Anyone with it can take over your account; no admin will ever ask for it.");
                    line.text = text;
                }
                if ((object)showLabel != null && showLabel.Pointer != IntPtr.Zero) showLabel.text = shown ? Loc.T("recovery.hide", "Hide") : Loc.T("recovery.show", "Show");
            }
            catch { }
        }

        private static void Toggle() { shown = !shown; Refresh(); }

        private static void Copy()
        {
            if (code == null) return;
            GUIUtility.systemCopyBuffer = code;
            try { UIMessageController.Instance?.ShowMessage(Loc.T("recovery.copied", "Recovery code copied"), false); } catch { }
        }

        private static void Make()
        {
            if (busy) return;
            string pass = (object)passBox != null ? passBox.text : "";
            if (string.IsNullOrEmpty(pass)) { status = Loc.T("recovery.needPassword", "Enter your account's password first."); Refresh(); return; }
            string ticket = PlayFabTransport.SessionTicket;
            if (string.IsNullOrEmpty(ticket)) { status = Loc.T("account.notSignedInYet", "Not signed in to the server yet."); Refresh(); return; }
            busy = true; status = Loc.T("recovery.making", "Making a recovery code…"); Refresh();
            System.Threading.Tasks.Task.Run(async () =>
            {
                string made = null, name = null, at = null, error = null;
                try
                {
                    var body = JsonSerializer.Serialize(new System.Collections.Generic.Dictionary<string, string> { ["passphrase"] = pass });
                    var reply = await Net.PostJson("/playfab/Revival/GameRecoveryCode", body, TimeSpan.FromSeconds(Math.Max(20, RevivalMod.Config.TimeoutSeconds)));
                    var d = reply.Data;
                    if (reply.Ok && d.ValueKind == JsonValueKind.Object && d.TryGetProperty("recoveryCode", out var c))
                    {
                        made = c.GetString();
                        name = d.TryGetProperty("name", out var n) ? n.GetString() : account;
                        at = d.TryGetProperty("at", out var a) ? a.GetString() : "";
                    }
                    else error = reply.Error;
                }
                catch (Exception e) { error = Loc.T("common.unreachableDot", "Could not reach the server."); RevivalMod.Log.Warning("[account] recovery code: " + e.Message); }
                PlayFabTransport.OnMainThread(() =>
                {
                    busy = false;
                    if (made == null)
                    {
                        RevivalMod.Log.Msg("[account] recovery code refused: " + error);
                        status = error ?? Loc.T("recovery.notMade", "No recovery code was made.");
                        Refresh();
                        return;
                    }
                    if ((object)passBox != null && passBox.Pointer != IntPtr.Zero) passBox.SetTextWithoutNotify("");
                    code = made; shown = false;
                    AccountPage.RecoveryMade();
                    savedTo = Save(name ?? account ?? "account", made, at ?? "");
                    RevivalMod.Log.Msg("[account] recovery code made" + (savedTo != null ? " and saved to a file" : "; the file could not be saved"));
                    status = "";
                    Refresh();
                    // the Show / Copy buttons appear next to the button now that there is a code
                    try { var tab = UnityEngine.Object.FindObjectOfType<AccountTab>(); if ((object)tab != null) AccountPage.Rearrange(tab); } catch { }
                });
            });
        }

        /// <summary>Writes the same file the website downloads. Returns where it went, or null.</summary>
        private static string Save(string name, string c, string at)
        {
            string safe = System.Text.RegularExpressions.Regex.Replace(name, @"[^\w.'-]+", "_");
            string file = $"warpforge-recovery-{safe}.txt";
            string text = Loc.T("recovery.fileTitle", "Warpforge Revival - account recovery code") + "\r\n\r\n" +
                          Loc.T("recovery.fileUser", "Username: {name}", ("name", name)) + "\r\n" + Loc.T("recovery.code", "Recovery code: {code}", ("code", c)) + "\r\n" +
                          Loc.T("recovery.fileMade", "Made: {date}", ("date", at.Length >= 10 ? at.Substring(0, 10) : at)) + "\r\n\r\n" +
                          Loc.T("recovery.fileHow", "If you forget your password: open the website, choose \"Forgot your password?\" and enter your username, this code and a new password.") + "\r\n" +
                          Loc.T("recovery.fileOnce", "The code works once. Making a new one cancels this one. Anyone with it can take over your account, so keep this file private.") + "\r\n";
            foreach (var dir in Places())
            {
                try
                {
                    if (string.IsNullOrEmpty(dir)) continue;
                    Directory.CreateDirectory(dir);
                    string path = Path.Combine(dir, file);
                    File.WriteAllText(path, text);
                    if (File.Exists(path)) return path;
                }
                catch (Exception e) { RevivalMod.Log.Msg("[account] recovery file not written to one place: " + e.GetType().Name); }
            }
            return null;
        }

        private static System.Collections.Generic.IEnumerable<string> Places()
        {
#if ANDROID_PORT
            yield return "/storage/emulated/0/Download";
#else
            string home = null;
            try { home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); } catch { }
            if (!string.IsNullOrEmpty(home)) yield return Path.Combine(home, "Downloads");
#endif
            yield return Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "WarpforgeRevival");
        }
    }
}
