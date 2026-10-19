using System;
using System.Collections.Generic;
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
    /// Report a player: Profile > battle log has a REPORT button (beside the opponent's name) on matches against
    /// another player. It opens a small form (reason, a note, "send my recent game log") and sends it to
    /// the server (POST /playfab/Revival/PlayerReport), which keeps both players' recordings of that
    /// match and the server's own record of it for the admins (website > Moderation > Reported players).
    /// </summary>
    internal static class PlayerReports
    {
        private const string ButtonName = "RevivalReport";
        private static readonly string[] ReasonIds = { "cheating", "unsportsmanlike", "stalling", "other" };
        // made each time they are drawn, so they follow the player's language (only the ids are sent)
        private static string[] ReasonNames() => new[]
        {
            Loc.T("playerReport.reason.cheating", "Cheating"), Loc.T("playerReport.reason.unsportsmanlike", "Unsportsmanlike"),
            Loc.T("playerReport.reason.stalling", "Stalling / AFK"), Loc.T("bugReport.cat.other", "Other"),
        };

        // ---------------------------------------------------------------- the button (from ReplayCodes.Place)

        /// <summary>Makes the REPORT button (ReplayCodes.Arrange places it next to the opponent's name); null when the match was not against a player.</summary>
        /// <summary>Hides the row's REPORT button (the row is being reused for a match without one).</summary>
        internal static void Hide(Transform parent)
        {
            var old = (object)parent == null ? null : parent.Find(ButtonName);
            if ((object)old != null && old.gameObject.activeSelf) old.gameObject.SetActive(false);
        }

        /// <summary>The row's REPORT button, made once per row and updated when the row is reused (0.13.55); null when
        /// the match was not against another player.</summary>
        internal static RectTransform Place(BattleLogItem item, BattleLogMatch match, RectTransform code, Transform parent, float w, float h)
        {
            var existing = parent.Find(ButtonName);
            string enemy = match.enemyId ?? "";
            if (enemy.Length != 16 || enemy == "AI" || !string.IsNullOrEmpty(match.botAltid)) { Hide(parent); return null; }     // only real players

            RectTransform rt;
            Button b;
            if ((object)existing != null)
            {
                rt = existing.GetComponent<RectTransform>();
                b = existing.GetComponent<Button>();
                if (!existing.gameObject.activeSelf) existing.gameObject.SetActive(true);
                rt.sizeDelta = new Vector2(w, h);
                b.onClick.RemoveAllListeners();
            }
            else
            {
                var go = new GameObject(ButtonName);
                rt = go.AddComponent<RectTransform>();
                rt.SetParent(parent, false);
                go.AddComponent<LayoutElement>().ignoreLayout = true;
                rt.anchorMin = rt.anchorMax = code.anchorMin;
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(w, h);
                var img = go.AddComponent<Image>();
                img.color = new Color(0f, 0f, 0f, 0.28f);
                var edge = go.AddComponent<Outline>();
                edge.effectColor = new Color(0.75f, 0.3f, 0.25f, 0.55f);
                edge.effectDistance = new Vector2(1.5f, -1.5f);
                b = go.AddComponent<Button>();
                b.targetGraphic = img;
                var label = new GameObject("Label");
                var lrt = label.AddComponent<RectTransform>();
                lrt.SetParent(rt, false);
                lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.offsetMin = lrt.offsetMax = Vector2.zero;
                var t = label.AddComponent<TextMeshProUGUI>();
                var font = item.GetComponentInChildren<TMP_Text>(true);
                if ((object)font != null) t.font = font.font;
                t.text = Loc.T("playerReport.button", "REPORT");
                t.fontSize = Mathf.Clamp(h * 0.3f, 12f, 24f);
                t.characterSpacing = 3f;
                t.alignment = TextAlignmentOptions.Center;
                t.color = new Color(0.85f, 0.45f, 0.38f, 1f);
                t.raycastTarget = false;
                t.enableAutoSizing = true; t.fontSizeMin = 9f; t.fontSizeMax = t.fontSize;
            }
            rt.localPosition = code.localPosition;
            string who = enemy, name = match.enemyName ?? "", index = match.recordingIndex ?? "";
            b.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction>(new Action(() => Form.Show(who, name, index))));
            return rt;
        }

        internal static void OnGUI()
        {
            try { if (Form.Open) Form.Draw(); }
            catch (Exception e) { RevivalMod.Log.Warning("[report] form: " + e.Message); Form.Open = false; }
        }

        // ---------------------------------------------------------------- the form

        private static class Form
        {
            internal static bool Open;
            private static string player, name, index, note = "", status = "";
            private static int reason = -1;
            private static bool sendLog = true, busy, done, problem;
            private static GUIStyle box, label, small, field, button, picked;
            private static Texture2D shade, panel, pickedBack;
            private static float sc = 1f;
            private static Rect R(float x, float y, float w, float h) => new Rect(x * sc, y * sc, w * sc, h * sc);


            internal static void Show(string who, string whoName, string matchIndex)
            {
                player = who; name = string.IsNullOrEmpty(whoName) ? who : whoName; index = matchIndex;
                note = ""; status = ""; reason = -1; sendLog = true; busy = done = problem = false;
                Open = true;
                RevivalMod.Log.Msg($"[report] report form opened for {who} (match {matchIndex})");
            }

            private static void Styles()
            {
                if (box == null || (object)panel == null)
                {
                    shade = Ui.Solid(new Color(0f, 0f, 0f, 0.6f));
                    panel = Ui.Solid(new Color(0.09f, 0.05f, 0.05f, 0.97f));
                    pickedBack = Ui.Solid(new Color(0.45f, 0.14f, 0.1f, 1f));
                    box = new GUIStyle(GUI.skin.box); box.normal.background = panel;
                    label = new GUIStyle(GUI.skin.label) { wordWrap = true }; label.normal.textColor = new Color(0.95f, 0.9f, 0.85f);
                    small = new GUIStyle(label); small.normal.textColor = new Color(0.75f, 0.68f, 0.62f);
                    field = new GUIStyle(GUI.skin.textField) { wordWrap = true };
                    button = new GUIStyle(GUI.skin.button);
                    picked = new GUIStyle(GUI.skin.button); picked.normal.background = pickedBack; picked.hover.background = pickedBack;
                }
                sc = Ui.GuiScale();
                label.fontSize = Mathf.RoundToInt(20 * sc); small.fontSize = Mathf.RoundToInt(15 * sc);
                field.fontSize = Mathf.RoundToInt(19 * sc); button.fontSize = picked.fontSize = Mathf.RoundToInt(18 * sc);
            }

            internal static void Draw()
            {
                Styles();
                float w = Screen.width / sc, h = Screen.height / sc;
                GUI.DrawTexture(R(0, 0, w, h), shade);
                float pw = 720, ph = 470, x = (w - pw) / 2, y = Mathf.Max(6, (h - ph) / 2 - 40);
                GUI.Box(R(x, y, pw, ph), "", box);
                GUI.Box(R(x, y, pw, ph), "", box);
                float cx = x + 30, cw = pw - 60, cy = y + 18;
                GUI.Label(R(cx, cy, cw, 40), Loc.T("playerReport.heading", "Report {name}", ("name", name)), label); cy += 46;
                if (done)
                {
                    GUI.Label(R(cx, cy, cw, 160), status, label);
                    if (GUI.Button(R(cx + cw - 160, y + ph - 66, 160, 48), Loc.T("common.close", "Close"), button)) Open = false;
                    return;
                }
                GUI.Label(R(cx, cy, cw, 30), Loc.T("playerReport.why", "Why?"), small); cy += 30;
                float bw = (cw - 30) / 4f;
                string[] reasonNames = ReasonNames();
                for (int i = 0; i < reasonNames.Length; i++)
                    if (GUI.Button(R(cx + i * (bw + 10), cy, bw, 44), reasonNames[i], reason == i ? picked : button)) reason = i;
                cy += 56;
                GUI.Label(R(cx, cy, cw, 30), Loc.T("playerReport.whatHappened", "What happened (optional)"), small); cy += 30;
                note = GUI.TextField(R(cx, cy, cw, 70), note ?? "", 500, field); cy += 82;
                if (GUI.Button(R(cx, cy, 34, 34), sendLog ? "X" : "", button)) sendLog = !sendLog;
                if (GUI.Button(R(cx + 40, cy, cw - 40, 34), Loc.T("playerReport.sendLog", "Send my recent game log too (helps the admins check)"), small)) sendLog = !sendLog;
                cy += 42;
                GUI.Label(R(cx, cy, cw, 50), Loc.T("playerReport.note", "The admins see both players' recordings of this match and the server's own record of it. False reports can get your own account looked at."), small);
                cy += 56;
                var keep = GUI.color;
                if (problem) GUI.color = new Color(1f, 0.6f, 0.5f);
                GUI.Label(R(cx, cy, cw, 30), busy ? Loc.T("common.sending", "Sending...") : status, small);
                GUI.color = keep;
                GUI.enabled = !busy;
                if (GUI.Button(R(cx, y + ph - 66, 200, 48), Loc.T("playerReport.send", "Send report"), button)) Send();
                if (GUI.Button(R(cx + cw - 160, y + ph - 66, 160, 48), Loc.T("common.cancel", "Cancel"), button)) Open = false;
                GUI.enabled = true;
            }

            private static void Send()
            {
                if (reason < 0) { status = Loc.T("playerReport.pickReason", "Pick a reason first."); problem = true; return; }
                string ticket = PlayFabTransport.SessionTicket;
                if (string.IsNullOrEmpty(ticket)) { status = Loc.T("account.notSignedInYet", "Not signed in to the server yet."); problem = true; return; }
                busy = true; problem = false;
                var body = new Dictionary<string, string> { ["player"] = player, ["recordingIndex"] = index, ["reason"] = ReasonIds[reason], ["note"] = note ?? "" };
                if (sendLog)
                {
                    string log = BugReport.RecentLog() ?? "";
                    if (log.Length > 200000) log = log.Substring(log.Length - 200000);
                    body["log"] = log;
                }
                System.Threading.Tasks.Task.Run(async () =>
                {
                    string id = null, error = null;
                    try
                    {
                        var reply = await Net.PostJson("/playfab/Revival/PlayerReport", JsonSerializer.Serialize(body),
                                                       TimeSpan.FromSeconds(Math.Max(20, RevivalMod.Config.TimeoutSeconds)));
                        if (reply.Ok && reply.Data.ValueKind == JsonValueKind.Object && reply.Data.TryGetProperty("id", out var i)) id = i.GetString();
                        else error = reply.Error;
                    }
                    catch (Exception e) { error = Loc.T("common.unreachableDot", "Could not reach the server."); RevivalMod.Log.Warning("[report] " + e.Message); }
                    PlayFabTransport.OnMainThread(() =>
                    {
                        busy = false;
                        if (id != null)
                        {
                            done = true;
                            status = Loc.T("playerReport.sent", "Report {id} sent. Thank you - the admins will look at the match.", ("id", id));
                            RevivalMod.Log.Msg($"[report] report {id} sent about {player}");
                        }
                        else { problem = true; status = error ?? Loc.T("playerReport.notSent", "The report was not sent."); RevivalMod.Log.Msg("[report] not sent: " + error); }
                    });
                });
            }
        }
    }
}
