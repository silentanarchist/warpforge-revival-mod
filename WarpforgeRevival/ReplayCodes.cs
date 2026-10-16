using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HarmonyLib;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WarpforgeRevival
{
    /// <summary>
    /// Replay codes: share one match's replay as a short code (e.g. K7M2-QX9P).
    ///  - Profile > battle log: each match with a replay gets a CODE button next to its replay button.
    ///    It asks the server for the match's code (POST /playfab/Revival/ReplayCode; the same code
    ///    every time) and copies it to the clipboard.
    ///  - Settings > Mod: "Watch a replay code" reads a code from the clipboard and plays it through the
    ///    game's own replay loading (PlayerDataManager.RecoverMatchRecording with the code as the record
    ///    name; the server answers with the copy kept for that code), exactly like the battle log's
    ///    replay button, so the replay's own rules are used (Replays).
    /// </summary>
    internal static class ReplayCodes
    {
        private const string ButtonName = "RevivalReplayCode";
        private static readonly ConcurrentQueue<Action> Main = new ConcurrentQueue<Action>();
        private static bool placedLogged;
        private static readonly Regex Dashed = new Regex(@"\b([0-9A-Za-z]{4})-([0-9A-Za-z]{4})\b");
        private static readonly Regex Code = new Regex(@"^[0-9A-HJKMNP-TV-Z]{8}$");

        /// <summary>What watching a code is doing, for the button that started it (Settings > Mod).</summary>
        internal static Action<string> WatchStatus = _ => { };

        internal static void Tick()
        {
            while (Main.TryDequeue(out var a))
            {
                try { a(); } catch (Exception e) { RevivalMod.Log.Warning("[replay code] " + e.Message); }
            }
        }

        /// <summary>A code typed or pasted in any of the forgiving forms, as the 8 characters the server keeps; "" if none.</summary>
        internal static string Normalize(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            var m = Dashed.Match(text);
            string t = m.Success ? m.Groups[1].Value + m.Groups[2].Value : Regex.Replace(text, @"[\s-]", "");
            t = t.ToUpperInvariant().Replace('O', '0').Replace('I', '1').Replace('L', '1');
            return Code.IsMatch(t) ? t : "";
        }

        internal static string Show(string code) => code.Length == 8 ? code.Substring(0, 4) + "-" + code.Substring(4) : code;

        private static void Notice(string text)
        {
            try
            {
                var n = UIMessageController.Instance;
                if ((object)n != null) n.ShowMessage(text, false);
            }
            catch { }
        }

        // ---------------------------------------------------------------- battle log: CODE button

        [HarmonyPatch(typeof(BattleLogItem), nameof(BattleLogItem.Initialize))]
        private static class LogItem
        {
            private static void Postfix(BattleLogItem __instance, BattleLogMatch match)
            {
                try { Place(__instance, match); }
                catch (Exception e) { RevivalMod.Log.Warning("[replay code] battle log button: " + e.Message); }
            }
        }

        private static void Place(BattleLogItem item, BattleLogMatch match)
        {
            var replay = item.replayButton;
            if ((object)replay == null || (object)match == null) return;
            var rb = replay.GetComponent<RectTransform>();
            var parent = rb == null ? null : rb.parent;
            if ((object)parent == null) return;
            var old = parent.Find(ButtonName);
            if ((object)old != null) UnityEngine.Object.Destroy(old.gameObject);
            if (!replay.gameObject.activeSelf || string.IsNullOrEmpty(match.recordingIndex)) return;

            // a box of the replay button's height, its own width, left of the replay button; kept out of any layout
            float h = rb.rect.height, w = Mathf.Max(h * 1.9f, 80f), gap = Mathf.Max(6f, h * 0.12f);
            var go = new GameObject(ButtonName);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            go.AddComponent<LayoutElement>().ignoreLayout = true;
            rt.anchorMin = rt.anchorMax = rb.anchorMin == rb.anchorMax ? rb.anchorMin : new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            Vector3 lp = rb.localPosition;
            float left = lp.x + rb.rect.xMin * rb.localScale.x;
            float midY = lp.y + rb.rect.center.y * rb.localScale.y;
            rt.localPosition = new Vector3(left - gap - w / 2f, midY, lp.z);
            // see-through, with a thin gold edge, so it sits on the row's own background
            var img = go.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.28f);
            var edge = go.AddComponent<Outline>();
            edge.effectColor = new Color(0.79f, 0.64f, 0.29f, 0.55f);
            edge.effectDistance = new Vector2(1.5f, -1.5f);
            var b = go.AddComponent<Button>();
            b.targetGraphic = img;
            var colors = b.colors;
            colors.highlightedColor = new Color(1.6f, 1.4f, 1.0f, 1f);
            colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            b.colors = colors;
            var font = item.GetComponentInChildren<TMP_Text>(true);
            var label = new GameObject("Label");
            var lrt = label.AddComponent<RectTransform>();
            lrt.SetParent(rt, false);
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.offsetMin = lrt.offsetMax = Vector2.zero;
            var t = label.AddComponent<TextMeshProUGUI>();
            if ((object)font != null) t.font = font.font;
            t.text = "CODE";
            t.fontSize = Mathf.Clamp(h * 0.32f, 13f, 26f);
            t.characterSpacing = 4f;
            t.alignment = TextAlignmentOptions.Center;
            t.color = new Color(0.79f, 0.64f, 0.29f, 1f);           // the game's gold
            t.raycastTarget = false;
            string index = match.recordingIndex;
            b.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction>(new Action(() => Ask(index, t))));
            if (!placedLogged)
            {
                placedLogged = true;
                RevivalMod.Log.Msg($"[replay code] battle log: CODE button placed, replay button {rb.rect.width:0}x{h:0} at {lp.x:0},{lp.y:0} in {parent.name}");
            }
        }

        /// <summary>The button's own text says what happened (COPIED for a moment), then goes back to CODE.</summary>
        private static void Flash(TMP_Text label, string text, float seconds)
        {
            try
            {
                if ((object)label == null || label.Pointer == IntPtr.Zero) return;
                label.text = text;
                if (seconds <= 0) return;
                System.Threading.Tasks.Task.Delay((int)(seconds * 1000)).ContinueWith(_ => Main.Enqueue(() =>
                {
                    try { if ((object)label != null && label.Pointer != IntPtr.Zero && label.text == text) label.text = "CODE"; } catch { }
                }));
            }
            catch { }
        }

        private static void Ask(string index, TMP_Text label)
        {
            string ticket = PlayFabTransport.SessionTicket;
            if (string.IsNullOrEmpty(ticket)) { Notice("Sign in first"); return; }
            string url = RevivalMod.Config.ServerUrl + "/playfab/Revival/ReplayCode";
            Flash(label, "...", 0);
            System.Threading.Tasks.Task.Run(async () =>
            {
                string code = null, error = null;
                try
                {
                    using var http = Net.Client(TimeSpan.FromSeconds(Math.Max(20, RevivalMod.Config.TimeoutSeconds)));
                    using var msg = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, url)
                    {
                        Content = new System.Net.Http.StringContent(JsonSerializer.Serialize(new Dictionary<string, string> { ["recordingIndex"] = index }), Encoding.UTF8, "application/json")
                    };
                    msg.Headers.TryAddWithoutValidation("X-Authorization", ticket);
                    using var reply = await http.SendAsync(msg);
                    using var doc = JsonDocument.Parse(await reply.Content.ReadAsStringAsync());
                    var root = doc.RootElement;
                    if (reply.IsSuccessStatusCode && root.TryGetProperty("data", out var data) && data.TryGetProperty("code", out var c))
                        code = c.GetString();
                    else
                        error = root.TryGetProperty("errorMessage", out var m) ? m.GetString() : "the server answered " + (int)reply.StatusCode;
                }
                catch (Exception e) { error = "could not reach the server"; RevivalMod.Log.Warning("[replay code] " + e.Message); }
                Main.Enqueue(() =>
                {
                    if (!string.IsNullOrEmpty(code))
                    {
                        GUIUtility.systemCopyBuffer = code;
                        RevivalMod.Log.Msg($"[replay code] {code} copied for match {index}");
                        Flash(label, "COPIED", 2.5f);
                        Notice($"Replay code {code} copied");
                    }
                    else
                    {
                        RevivalMod.Log.Warning($"[replay code] no code for match {index}: {error}");
                        Flash(label, "NO CODE", 3f);
                        Notice("No replay code: " + error);
                    }
                });
            });
        }

        // ---------------------------------------------------------------- Social > Challenge: watch a code

        /// <summary>Plays the replay whose code is on the clipboard.</summary>
        internal static void WatchFromClipboard()
        {
            string text = "";
            try { text = GUIUtility.systemCopyBuffer ?? ""; } catch { }
            string code = Normalize(text);
            if (code.Length == 0)
            {
                WatchStatus("Copy a replay code first");
                
                return;
            }
            Watch(code);
        }

        internal static void Watch(string code)
        {
            var pd = PlayerDataManager.singletonManager;
            if ((object)pd == null) { WatchStatus("Not signed in yet.");  return; }
            WatchStatus($"Loading {Show(code)}...");
            
            RevivalMod.Log.Msg($"[replay code] watching {code}");
            Action<BattleRecordData> found = record =>
            {
                try
                {
                    if ((object)record == null)
                    {
                        WatchStatus($"No replay for {Show(code)}");
                        
                        return;
                    }
                    WatchStatus("");
                    
                    var mm = Il2CppEverguild.MatchMakerManager.Instance;
                    if ((object)mm == null) { WatchStatus("Replay could not start");  return; }
                    mm.StartReplayMatch(record, 1f);
                }
                catch (Exception e) { RevivalMod.Log.Warning("[replay code] start: " + e); WatchStatus("Replay could not start");  }
            };
            Action<string> failed = e =>
            {
                RevivalMod.Log.Warning($"[replay code] {code}: {e}");
                WatchStatus($"No replay for {Show(code)}");
                
            };
            pd.RecoverMatchRecording(code, DelegateSupport.ConvertDelegate<Il2CppSystem.Action<BattleRecordData>>(found),
                                     DelegateSupport.ConvertDelegate<Il2CppSystem.Action<string>>(failed), "", "");
        }
    }
}
