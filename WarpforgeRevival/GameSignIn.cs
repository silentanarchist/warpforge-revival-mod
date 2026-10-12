using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using UnityEngine;

namespace WarpforgeRevival
{
    /// <summary>
    /// Signing in with an account from the server's website.
    ///
    /// The game signs in by itself (Steam, the phone's device id, or the saved id when Steam is off).
    /// A server can ask for more: "requireAccount" in its settings. The mod then sends the saved
    /// game login (UserData/WarpforgeRevival/game-login.txt) with every sign-in, and when there is
    /// none - or the server no longer accepts it - shows a small window asking for the account's
    /// name and password. The server answers a new game login, which is saved to that file, and
    /// the sign-in goes ahead. The password itself is never stored.
    ///
    /// The window is drawn with Unity's simple built-in GUI (OnGUI), because it has to appear while
    /// the game is still loading, before any of the game's own menus exist.
    /// </summary>
    internal static class GameSignIn
    {
        private const string Prefix = "wfr-login-";
        private static readonly HttpClient Http = Net.Client(TimeSpan.FromSeconds(20));

        private static string FilePath => Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "WarpforgeRevival", "game-login.txt");

        /// <summary>The saved game login, or null.</summary>
        internal static string Token()
        {
            try
            {
                if (!File.Exists(FilePath)) return null;
                string text = File.ReadAllText(FilePath).Trim();
                if (text.StartsWith(Prefix) && text.Length > 40 && text.Length < 200) return text;
                RevivalMod.Log.Warning("[signin] game-login.txt is not a game login - ignored");
            }
            catch (Exception e) { RevivalMod.Log.Warning("[signin] could not read game-login.txt: " + e.Message); }
            return null;
        }

        private static void Save(string token)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, token);
            }
            catch (Exception e) { RevivalMod.Log.Warning("[signin] could not save the game login: " + e.Message); }
        }

        /// <summary>Removes a saved game login the server no longer accepts.</summary>
        internal static void Forget()
        {
            try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch { }
        }

        /// <summary>
        /// What to do with the server's answer to a sign-in: null to hand it to the game as it is,
        /// otherwise the message to show in the sign-in window before trying again.
        /// </summary>
        internal static string NeedsSignIn(string body)
        {
            if (string.IsNullOrEmpty(body) || body.Length > 2000 || !body.Contains("\"error\"")) return null;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(body);
                var root = doc.RootElement;
                string error = root.TryGetProperty("error", out var e) ? e.GetString() : null;
                string message = root.TryGetProperty("errorMessage", out var m) ? m.GetString() : null;
                switch (error)
                {
                    case "AccountRequired": return message ?? "This server needs you to sign in.";
                    case "InvalidGameLoginFile": Forget(); return message ?? "Your saved sign-in is no longer valid. Sign in again.";
                    case "AccountNotApproved": Forget(); return message ?? "Your account is waiting for an admin to approve it.";
                }
            }
            catch { }
            return null;
        }

        // ------------------------------------------------------------------ the window
        private static volatile bool open;
        /// <summary>The sign-in or closing window is up (the bug-report tab stays away meanwhile).</summary>
        internal static bool Busy => open || closing;
        private static TaskCompletionSource<string> waiting;
        private static string userName = "", password = "", password2 = "", message = "", serverNote = "";
        private static bool busy, problem, drawn;
        // Creating an account: the password twice and a tick box agreeing to the server's Terms and
        // Privacy notice (its /terms page, open to everyone). The same tick box appears when signing
        // in to an account that has not agreed to the current version yet (the server answers
        // "TermsNotAgreed"); "termsBox" tells the server this mod can show it.
        private static bool creating, agreed, termsAsked;
        private static float askedAt;

        /// <summary>
        /// Shows the window (from any thread) and waits for a new game login; null when the window
        /// could not be shown, in which case the game gets the server's answer as it was.
        /// </summary>
        internal static Task<string> Ask(string why)
        {
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            PlayFabTransport.OnMainThread(() =>
            {
                waiting?.TrySetResult(null);
                waiting = tcs;
                message = why ?? "";
                problem = false;
                busy = false;
                password = "";
                password2 = "";
                creating = false;
                agreed = false;
                termsAsked = false;
                drawn = false;
                askedAt = Time.realtimeSinceStartup;
                serverNote = ServerName() + (RevivalMod.Config.Secure ? "" : "  -  this connection is NOT encrypted");
                open = true;
                RevivalMod.Log.Msg("[signin] the server asks for an account: " + message);
            });
            return tcs.Task;
        }

        /// <summary>The server's Terms and Privacy notice: /terms on the website (the "creatorUrl" setting's site when known).</summary>
        internal static string TermsUrl()
        {
            try
            {
                string site = ServerSettings.CreatorUrl;
                if (!string.IsNullOrEmpty(site)) return new Uri(site).GetLeftPart(UriPartial.Authority) + "/terms";
            }
            catch { }
            return RevivalMod.Config.ServerUrl.TrimEnd('/') + "/terms";
        }

        private static void OpenTerms()
        {
            string url = TermsUrl();
            try { OpenUrl(url); RevivalMod.Log.Msg("[signin] opened " + url); }
            catch (Exception e)
            {
                RevivalMod.Log.Warning("[signin] could not open the terms page: " + e.Message);
                Say("Open " + url + " in a browser to read them.", false);
            }
        }

        // Kept apart so that a game build missing Application.OpenURL fails inside OpenTerms' try, not in Draw.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void OpenUrl(string url) => Application.OpenURL(url);

        private static string ServerName()
        {
            try { return new Uri(RevivalMod.Config.ServerUrl).Host; } catch { return RevivalMod.Config.ServerUrl; }
        }

        /// <summary>Called every frame from OnUpdate: gives up if the window never gets drawn.</summary>
        // ------------------------------------------------------------------ signed out while playing
        // When the server ends this game's session (the account was unlinked, its password changed,
        // or it was banned) the game cannot carry on: the window says why and the game closes.
        // The next start asks to sign in again.
        private static volatile bool closing;
        private static string closingWhy = "", closingTitle = "Signed out";
        private static bool closingButton = true;
        private static float closeAt;
        private const float CloseAfter = 20f;

        /// <summary>From any thread: the server signed this game out.</summary>
        internal static void SignedOut(string why)
        {
            if (closing) return;
            closing = true;
            Forget();
            PlayFabTransport.OnMainThread(() =>
            {
                closingWhy = string.IsNullOrEmpty(why) ? "You were signed out by the server." : why;
                closeAt = Time.realtimeSinceStartup + CloseAfter;
                RevivalMod.Log.Msg("[signin] signed out by the server: " + closingWhy);
            });
        }

        // ------------------------------------------------------------------ out of date
        /// <summary>True when the server refused a sign-in because this mod is not the one it hands out.</summary>
        internal static bool OutOfDate(string body)
        {
            if (string.IsNullOrEmpty(body) || body.Length > 2000 || !(body.Contains("\"ModOutOfDate\"") || body.Contains("\"ModChanged\""))) return false;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(body);
                string e = doc.RootElement.TryGetProperty("error", out var v) ? v.GetString() : null;
                return e == "ModOutOfDate" || e == "ModChanged";
            }
            catch { return false; }
        }

        /// <summary>From any thread: shows "Updating" while the update downloads (no closing yet).</summary>
        internal static void Updating(string text)
        {
            closing = true;
            PlayFabTransport.OnMainThread(() =>
            {
                closingTitle = "Updating the mod";
                closingWhy = text;
                closingButton = false;
                closeAt = 0f;
            });
        }

        /// <summary>From any thread: the update is in place; the game closes after a short wait.</summary>
        internal static void Updated(string text, float seconds)
        {
            closing = true;
            PlayFabTransport.OnMainThread(() =>
            {
                closingTitle = "Mod updated";
                closingWhy = text;
                closingButton = false;          // "Close now" froze the game on every system; the countdown closes it
                closeAt = Time.realtimeSinceStartup + seconds;
            });
        }

        /// <summary>From any thread: the update could not be installed; the game goes on to show the server's message.</summary>
        internal static void UpdateFailed()
        {
            PlayFabTransport.OnMainThread(() => { closing = false; closeAt = 0f; });
        }

        /// <summary>Spots the server's "signed out" answer to any request.</summary>
        internal static void Watch(string body)
        {
            if (closing || string.IsNullOrEmpty(body) || body.Length > 2000 || !body.Contains("\"SignedOut\"")) return;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.TryGetProperty("error", out var e) && e.GetString() == "SignedOut")
                    SignedOut(root.TryGetProperty("errorMessage", out var m) ? m.GetString() : null);
            }
            catch { }
        }

        private static void DrawClosing()
        {
            float left = Mathf.Max(0f, closeAt - Time.realtimeSinceStartup);
            if (closeAt > 0 && left <= 0f) { RevivalMod.QuitGame(); return; }
            float scale = Scale();
            float w = Screen.width / scale, h = Screen.height / scale;
            GUI.DrawTexture(R(0, 0, w, h), shade);
            float pw = 640, ph = 300, x = (w - pw) / 2, y = (h - ph) / 2;
            GUI.Box(R(x, y, pw, ph), "", box);
            GUI.Label(R(x + 40, y + 24, pw - 80, 34), closingTitle, label);
            GUI.Label(R(x + 40, y + 70, pw - 80, 90), closingWhy, small);
            if (closeAt > 0)
                GUI.Label(R(x + 40, y + 160, pw - 80, 30), "The game closes in " + Mathf.CeilToInt(left) + " s.", small);
            if (closingButton && GUI.Button(R(x + 40, y + 210, 220, 50), "Close now", button)) RevivalMod.QuitGame();
        }

        private static float lastFrame;

        internal static void Tick()
        {
            // The countdown only runs while the game is drawing. The game can stall for seconds while it
            // loads; time spent stalled does not count, so the message is on screen for the full wait
            // (a phone once closed straight after a stall, without the message ever being seen).
            float now = Time.realtimeSinceStartup;
            float gap = lastFrame > 0f ? now - lastFrame : 0f;
            lastFrame = now;
            if (closing && closeAt > 0 && gap > 0.5f) closeAt += gap;
            // closes even if the window cannot be drawn on this device
            if (closing && closeAt > 0 && Time.realtimeSinceStartup > closeAt + 1f) { RevivalMod.QuitGame(); return; }
            if (!open || drawn || Time.realtimeSinceStartup - askedAt < 10f) return;
            RevivalMod.Log.Error("[signin] the sign-in window could not be shown on this device");
            Close(null);
        }

        private static void Close(string token)
        {
            open = false;
            password = "";
            password2 = "";
            var tcs = waiting;
            waiting = null;
            tcs?.TrySetResult(token);
        }

        private static GUIStyle box, label, small, field, button;
        private static Texture2D shade, panel;

        private static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            t.hideFlags = HideFlags.HideAndDontSave;
            return t;
        }

        // Laid out on a 1280 x 720 page and scaled to the screen by hand: the phone's game build lacks
        // parts of Unity's GUI that would do this (GUI.matrix, GUI.depth), and using them fails there.
        private static float sc = 1f;
        private static Rect R(float x, float y, float w, float h) => new Rect(x * sc, y * sc, w * sc, h * sc);

        private static float Scale()
        {
            sc = Mathf.Max(0.5f, Mathf.Min(Screen.width / 1280f, Screen.height / 720f));
            label.fontSize = Mathf.RoundToInt(22 * sc);
            small.fontSize = Mathf.RoundToInt(16 * sc);
            field.fontSize = Mathf.RoundToInt(24 * sc);
            button.fontSize = Mathf.RoundToInt(22 * sc);
            return sc;
        }

        private static void Styles()
        {
            if (box != null) return;
            shade = Solid(new Color(0f, 0f, 0f, 0.75f));
            panel = Solid(new Color(0.05f, 0.11f, 0.09f, 0.97f));
            box = new GUIStyle(GUI.skin.box);
            box.normal.background = panel;
            label = new GUIStyle(GUI.skin.label) { fontSize = 22, wordWrap = true };
            label.normal.textColor = new Color(0.85f, 0.95f, 0.9f);
            small = new GUIStyle(label) { fontSize = 16 };
            small.normal.textColor = new Color(0.6f, 0.75f, 0.7f);
            field = new GUIStyle(GUI.skin.textField) { fontSize = 24 };
            button = new GUIStyle(GUI.skin.button) { fontSize = 22 };
        }

        /// <summary>Called from the mod's OnGUI.</summary>
        internal static void Draw()
        {
            if (closing)
            {
                try { Styles(); DrawClosing(); }
                catch (Exception e) { RevivalMod.Log.Error("[signin] " + e.Message); RevivalMod.QuitGame(); }
                return;
            }
            if (!open) return;
            try
            {
                Styles();
                drawn = true;
                // Lay out on a 1280 x 720 page scaled to the screen, so it reads the same on a phone.
                float scale = Scale();
                float w = Screen.width / scale, h = Screen.height / scale;
                GUI.DrawTexture(R(0, 0, w, h), shade);

                bool showTerms = creating || termsAsked;
                float pw = 640, ph = (creating ? 560 : 440) + (termsAsked && !creating ? 64 : 0);
                float x = (w - pw) / 2, y = Mathf.Max(10, (h - ph) / 2 - 60);
                GUI.Box(R(x, y, pw, ph), "", box);
                float cx = x + 40, cw = pw - 80, cy = y + 24;
                GUI.Label(R(cx, cy, cw, 34), (creating ? "Create an account on " : "Sign in to ") + ServerName(), label); cy += 40;
                GUI.Label(R(cx, cy, cw, 48), creating ? "Pick a username (5 to 30 letters, digits, . ' - or _) and a password (at least 6 characters, no spaces)."
                                                      : "Use the account you made on the server's website, or create one here.", small); cy += 50;

                GUI.Label(R(cx, cy, 140, 40), "Username", label);
                GUI.SetNextControlName("wfrName");
                userName = GUI.TextField(R(cx + 150, cy, cw - 150, 42), userName ?? "", 30, field); cy += 54;
                GUI.Label(R(cx, cy, 140, 40), "Password", label);
                password = GUI.PasswordField(R(cx + 150, cy, cw - 150, 42), password ?? "", '*', 200, field); cy += 54;
                if (creating)
                {
                    GUI.Label(R(cx, cy, 150, 40), "Again", label);
                    password2 = GUI.PasswordField(R(cx + 150, cy, cw - 150, 42), password2 ?? "", '*', 200, field); cy += 54;
                }
                if (showTerms)
                {
                    // a button drawn as a tick box: plain buttons work on every build of the game
                    if (GUI.Button(R(cx, cy, 44, 44), agreed ? "X" : "", button)) agreed = !agreed;
                    if (GUI.Button(R(cx + 52, cy, cw - 52 - 130, 44), "I am 16 or older and agree to the Terms and Privacy notice", small)) agreed = !agreed;
                    if (GUI.Button(R(cx + cw - 120, cy, 120, 44), "Read", button)) OpenTerms();
                    cy += 54;
                }
                cy += 4;

                var keepColor = GUI.color;
                if (problem) GUI.color = new Color(1f, 0.55f, 0.5f);
                GUI.Label(R(cx, cy, cw, 52), busy ? (creating ? "Creating the account..." : "Signing in...") : message, small);
                GUI.color = keepColor;
                cy += 58;

                GUI.enabled = !busy;
                if (creating)
                {
                    bool match = (password ?? "").Length > 0 && password == password2;
                    GUI.enabled = !busy && agreed && match;
                    if (GUI.Button(R(cx, cy, 230, 50), "Create account", button)) Submit(true);
                    GUI.enabled = !busy;
                    if (GUI.Button(R(cx + 245, cy, 140, 50), "Back", button)) { creating = false; password2 = ""; agreed = false; Say("", false); }
                    if (!busy && message.Length == 0 && (password2 ?? "").Length > 0 && !match) Say("The two passwords do not match.", true);
                }
                else
                {
                    if (GUI.Button(R(cx, cy, 170, 50), "Sign in", button)) Submit(false);
                    if (GUI.Button(R(cx + 185, cy, 230, 50), "Create account", button)) { creating = true; agreed = false; password2 = ""; Say("", false); }
                }
                if (GUI.Button(R(cx + cw - 140, cy, 140, 50), "Quit", button)) RevivalMod.QuitGame();
                GUI.enabled = true;
                cy += 62;
                GUI.Label(R(cx, cy, cw, 30), serverNote, small);
                if (problem && message == "The two passwords do not match." && password == password2) Say("", false);

                var e = Event.current;
                if (!busy && !creating && e != null && e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)) Submit(false);
            }
            catch (Exception e)
            {
                RevivalMod.Log.Error("[signin] window: " + e.Message);
                Close(null);
            }
        }

        private static void Submit(bool create)
        {
            string name = (userName ?? "").Trim(), pass = password ?? "";
            if (name.Length == 0 || pass.Length == 0) { Say("Enter your username and password.", true); return; }
            if (create && pass != password2) { Say("The two passwords do not match.", true); return; }
            if ((create || termsAsked) && !agreed) { Say("Tick the box to agree to the Terms and Privacy notice.", true); return; }
            bool agree = (create || termsAsked) && agreed;
            busy = true;
            string url = RevivalMod.Config.ServerUrl + "/playfab/Revival/GameAccountSignIn";
            Task.Run(async () =>
            {
                string token = null, why, error = null;
                try
                {
                    using var ms = new MemoryStream();
                    using (var w = new System.Text.Json.Utf8JsonWriter(ms))
                    {
                        w.WriteStartObject();
                        w.WriteString("name", name);
                        w.WriteString("passphrase", pass);
                        w.WriteBoolean("create", create);
                        w.WriteBoolean("termsBox", true);
                        if (agree) w.WriteBoolean("agreeTerms", true);
                        w.WriteEndObject();
                    }
                    using var msg = new HttpRequestMessage(HttpMethod.Post, url)
                    {
                        Content = new StringContent(System.Text.Encoding.UTF8.GetString(ms.ToArray()), System.Text.Encoding.UTF8, "application/json")
                    };
                    msg.Headers.TryAddWithoutValidation("X-Revival-Mod", RevivalMod.Version);
                    using var reply = await Http.SendAsync(msg);
                    using var doc = System.Text.Json.JsonDocument.Parse(await reply.Content.ReadAsStringAsync());
                    var root = doc.RootElement;
                    if (reply.IsSuccessStatusCode && root.TryGetProperty("data", out var data) && data.TryGetProperty("token", out var t))
                    {
                        token = t.GetString();
                        why = null;
                    }
                    else
                    {
                        error = root.TryGetProperty("error", out var ec) ? ec.GetString() : null;
                        why = root.TryGetProperty("errorMessage", out var e) ? e.GetString() : null;
                        why = string.IsNullOrEmpty(why) ? "The server did not accept that (" + (int)reply.StatusCode + ")." : char.ToUpperInvariant(why[0]) + why.Substring(1) + ".";
                    }
                }
                catch (Exception e)
                {
                    why = "Could not reach the server.";
                    RevivalMod.Log.Warning("[signin] " + e.GetType().Name + ": " + e.Message);
                }
                PlayFabTransport.OnMainThread(() =>
                {
                    busy = false;
                    if (token != null)
                    {
                        Save(token);
                        creating = false;
                        termsAsked = false;
                        RevivalMod.Log.Msg("[signin] signed in as " + name + (create ? " (new account)" : ""));
                        Close(token);
                    }
                    else if (error == "TermsNotAgreed" && !create)
                    {
                        termsAsked = true;
                        agreed = false;
                        Say("The Terms and Privacy notice have changed. Read them, tick the box and sign in again.", true);
                    }
                    else Say(why, true);
                });
            });
        }

        private static void Say(string text, bool bad)
        {
            message = text ?? "";
            problem = bad;
        }
    }
}
