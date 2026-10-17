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
        private static bool placedLogged;
        private static readonly Regex Dashed = new Regex(@"\b([0-9A-Za-z]{4})-([0-9A-Za-z]{4})\b");
        private static readonly Regex Code = new Regex(@"^[0-9A-HJKMNP-TV-Z]{8}$");

        /// <summary>What watching a code is doing, for the button that started it (Settings > Mod).</summary>
        internal static Action<string> WatchStatus = _ => { };

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

        /// <summary>
        /// The row's CODE button (and REPORT beside it). Since 0.13.55 each row keeps its buttons: the battle log
        /// reuses rows as it scrolls, and the buttons are only updated (match, shown or not, place) instead of being
        /// destroyed and made again. They are placed once the row is laid out, in the same frame; a second look
        /// on the next frame only logs (and corrects) if anything moved.
        /// </summary>
        private static void Place(BattleLogItem item, BattleLogMatch match)
        {
            var replay = item.replayButton;
            if ((object)replay == null || (object)match == null) return;
            var rb = replay.GetComponent<RectTransform>();
            var parent = rb == null ? null : rb.parent;
            if ((object)parent == null) return;
            var existing = parent.Find(ButtonName);
            bool wanted = replay.gameObject.activeSelf && !string.IsNullOrEmpty(match.recordingIndex);
            if (!wanted)
            {
                if ((object)existing != null && existing.gameObject.activeSelf) existing.gameObject.SetActive(false);
                PlayerReports.Hide(parent);
                return;
            }

            // a box a little shorter than the replay button, kept out of any layout; Arrange puts it between
            // the mode's icon and the player's own name (owner, 2026-10-16), REPORT beside the opponent's name
            float h = Mathf.Max(rb.rect.height * 0.8f, 34f), w = Mathf.Max(h * 2.1f, 80f);
            RectTransform rt;
            Button b;
            TMP_Text t;
            if ((object)existing != null)
            {
                rt = existing.GetComponent<RectTransform>();
                b = existing.GetComponent<Button>();
                t = existing.GetComponentInChildren<TMP_Text>(true);
                if (!existing.gameObject.activeSelf) existing.gameObject.SetActive(true);
                rt.sizeDelta = new Vector2(w, h);
                if ((object)t != null) t.text = "CODE";
                b.onClick.RemoveAllListeners();
            }
            else
            {
                var go = new GameObject(ButtonName);
                rt = go.AddComponent<RectTransform>();
                rt.SetParent(parent, false);
                go.AddComponent<LayoutElement>().ignoreLayout = true;
                rt.anchorMin = rt.anchorMax = rb.anchorMin == rb.anchorMax ? rb.anchorMin : new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(w, h);
                // see-through, with a thin gold edge, so it sits on the row's own background
                var img = go.AddComponent<Image>();
                img.color = new Color(0f, 0f, 0f, 0.28f);
                var edge = go.AddComponent<Outline>();
                edge.effectColor = new Color(0.79f, 0.64f, 0.29f, 0.55f);
                edge.effectDistance = new Vector2(1.5f, -1.5f);
                b = go.AddComponent<Button>();
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
                var tm = label.AddComponent<TextMeshProUGUI>();
                if ((object)font != null) tm.font = font.font;
                tm.text = "CODE";
                tm.fontSize = Mathf.Clamp(h * 0.32f, 13f, 26f);
                tm.characterSpacing = 4f;
                tm.alignment = TextAlignmentOptions.Center;
                tm.color = new Color(0.79f, 0.64f, 0.29f, 1f);           // the game's gold
                tm.raycastTarget = false;
                tm.enableAutoSizing = true; tm.fontSizeMin = 9f; tm.fontSizeMax = tm.fontSize;
                t = tm;
            }
            Vector3 lp = rb.localPosition;
            rt.localPosition = new Vector3(lp.x - w, lp.y, lp.z);          // until Arrange has measured the names
            RectTransform report = null;
            try { report = PlayerReports.Place(item, match, rt, parent, w, h); }
            catch (Exception e) { RevivalMod.Log.Warning("[report] battle log button: " + e.Message); }

            // lay the row out now, so the names can be measured and the buttons placed before it is drawn
            try { var row = item.GetComponent<RectTransform>(); if ((object)row != null) LayoutRebuilder.ForceRebuildLayoutImmediate(row); } catch { }
            Arrange(item, parent, rt, report, w);
            Vector3 codeAt = rt.localPosition, reportAt = Ui.Alive(report) ? report.localPosition : Vector3.zero;
            PlayFabTransport.OnMainThread(() =>
            {
                // the check: placed again once the row has been laid out by the game; logged if anything moved
                Arrange(item, parent, rt, report, w);
                if (movedLogged || !Ui.Alive(rt)) return;
                bool moved = (rt.localPosition - codeAt).sqrMagnitude > 4f || (Ui.Alive(report) && (report.localPosition - reportAt).sqrMagnitude > 4f);
                if (moved) { movedLogged = true; RevivalMod.Log.Warning($"[replay code] battle log buttons moved after the row's layout (CODE {codeAt.x:0} -> {rt.localPosition.x:0}); please send this log"); }
            });
            string index = match.recordingIndex;
            var shownLabel = t;
            b.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction>(new Action(() => Ask(index, shownLabel))));
            if (!placedLogged)
            {
                placedLogged = true;
                RevivalMod.Log.Msg($"[replay code] battle log: CODE button placed, replay button {rb.rect.width:0}x{h:0} at {lp.x:0},{lp.y:0} in {parent.name}");
            }
        }

        private static bool movedLogged;

        // ---------------------------------------------------------------- where the buttons go


        /// <summary>A box in parent's space: the text actually drawn (not its whole box), or null.</summary>
        private static Rect? TextSpan(TMP_Text text, Transform parent)
        {
            if (!Ui.Alive(text) || !text.gameObject.activeInHierarchy || string.IsNullOrEmpty(text.text)) return null;
            text.ForceMeshUpdate(false, false);
            var b = text.textBounds;
            if (b.size.x <= 0f) return RectSpan(text.rectTransform, parent);
            Vector3 a = parent.InverseTransformPoint(text.transform.TransformPoint(b.min));
            Vector3 c = parent.InverseTransformPoint(text.transform.TransformPoint(b.max));
            return Rect.MinMaxRect(Math.Min(a.x, c.x), Math.Min(a.y, c.y), Math.Max(a.x, c.x), Math.Max(a.y, c.y));
        }

        private static Rect? RectSpan(RectTransform rt, Transform parent)
        {
            if (!Ui.Alive(rt) || !rt.gameObject.activeInHierarchy) return null;
            var c = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(4);
            rt.GetWorldCorners(c);
            Vector3 a = parent.InverseTransformPoint(c[0]), z = parent.InverseTransformPoint(c[2]);
            return Rect.MinMaxRect(Math.Min(a.x, z.x), Math.Min(a.y, z.y), Math.Max(a.x, z.x), Math.Max(a.y, z.y));
        }

        /// <summary>Puts a button centred in the gap from left to right (at most width wide, at least 60%), at height y.</summary>
        private static void PutIn(RectTransform b, float left, float right, float y, float width)
        {
            float room = right - left, w = Mathf.Clamp(room, width * 0.6f, width);
            float x = (left + right) / 2f;
            b.sizeDelta = new Vector2(w, b.sizeDelta.y);
            b.localPosition = new Vector3(x, y, b.localPosition.z);
        }

        private static bool arrangedLogged;

        /// <summary>
        /// CODE between the mode's icon (left column) and the player's own name; REPORT between the opponent's
        /// name and the opponent's rank icon (owner, 2026-10-16), both at the height of the player names.
        /// </summary>
        private static void Arrange(BattleLogItem item, Transform parent, RectTransform code, RectTransform report, float width)
        {
            try
            {
                if (!Ui.Alive(item) || !Ui.Alive(code)) return;
                float pad = Math.Max(8f, width * 0.08f);
                var me = item.playerInfo;
                Rect? myName = Ui.Alive(me) ? TextSpan(me.playerName, parent) : null;
                Rect? myIcon = Ui.Alive(me) && Ui.Alive(me.ratingImage) ? RectSpan(me.ratingImage.rectTransform, parent) : null;
                Rect? mode = TextSpan(item.mode, parent);
                if (myName.HasValue)
                {
                    float right = myName.Value.xMin - pad;
                    float left = myIcon.HasValue && myIcon.Value.xMax < right ? myIcon.Value.xMax + pad
                               : mode.HasValue && mode.Value.xMax < right ? mode.Value.xMax + pad : right - width;
                    PutIn(code, left, right, myName.Value.center.y, width);
                }
                Rect? theirName = null, theirIcon = null;
                if (Ui.Alive(report))
                {
                    var them = item.enemyInfo;
                    theirName = Ui.Alive(them) ? TextSpan(them.playerName, parent) : null;
                    theirIcon = Ui.Alive(them) && Ui.Alive(them.ratingImage) ? RectSpan(them.ratingImage.rectTransform, parent) : null;
                    if (theirName.HasValue)
                    {
                        float left = theirName.Value.xMax + pad;
                        float right = theirIcon.HasValue && theirIcon.Value.xMin > left ? theirIcon.Value.xMin - pad : left + width;
                        PutIn(report, left, right, theirName.Value.center.y, width);
                    }
                }
                if (!arrangedLogged && myName.HasValue)
                {
                    arrangedLogged = true;
                    RevivalMod.Log.Msg($"[replay code] battle log layout: my name {myName}, my icon {myIcon}, mode {mode}, their name {theirName}, their icon {theirIcon}; CODE at {code.localPosition.x:0},{code.localPosition.y:0}");
                }
            }
            catch (Exception e) { RevivalMod.Log.Warning("[replay code] arranging the battle log buttons: " + e.Message); }
        }

        /// <summary>The button's own text says what happened (COPIED for a moment), then goes back to CODE.</summary>
        private static void Flash(TMP_Text label, string text, float seconds)
        {
            try
            {
                if ((object)label == null || label.Pointer == IntPtr.Zero) return;
                label.text = text;
                if (seconds <= 0) return;
                System.Threading.Tasks.Task.Delay((int)(seconds * 1000)).ContinueWith(_ => PlayFabTransport.OnMainThread(() =>
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
            Flash(label, "...", 0);
            System.Threading.Tasks.Task.Run(async () =>
            {
                string code = null, error = null;
                try
                {
                    var reply = await Net.PostJson("/playfab/Revival/ReplayCode",
                        JsonSerializer.Serialize(new Dictionary<string, string> { ["recordingIndex"] = index }),
                        TimeSpan.FromSeconds(Math.Max(20, RevivalMod.Config.TimeoutSeconds)), retry: true);     // the same code again: no harm
                    if (reply.Ok && reply.Data.ValueKind == JsonValueKind.Object && reply.Data.TryGetProperty("code", out var c)) code = c.GetString();
                    else error = reply.Unreachable ? "could not reach the server" : reply.Error;
                }
                catch (Exception e) { error = "could not reach the server"; RevivalMod.Log.Warning("[replay code] " + e.Message); }
                PlayFabTransport.OnMainThread(() =>
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
