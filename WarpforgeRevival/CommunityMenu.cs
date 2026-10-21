using System;
using System.Collections.Generic;
using System.Text.Json;
using Il2Cpp;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WarpforgeRevival
{
    /// <summary>
    /// Social > Community (0.14.28): a tab of the mod's own in the Social window (SocialTabs), with
    ///  - Replays: "Watch a replay code" (moved here from Settings > Mod; plays the code on the clipboard) and the
    ///    replays shared in Discord with the bot's /replay command (Revival/SharedReplays), each with a Watch button;
    ///  - News: what the server's content/files/news.json says (the owner writes it: {"discord": url, "items":
    ///    [{date, title, text, link?}]}), newest first, and a Discord button at the bottom when the file names one.
    /// Both lists are fetched when the tab opens (at most once a minute) and drawn when they arrive.
    /// </summary>
    internal static class CommunityMenu
    {
        private const string PanelName = "RevivalCommunityMenu", TabName = "RevivalCommunityTab";
        private static readonly SocialTabs.Entry entry = SocialTabs.Register(new SocialTabs.Entry
        {
            Name = "Community", TabName = TabName, ButtonName = "Community Tab Button",
            Build = (w, host) => Build(w, host),
        });

        private sealed class Shared { public string Code, Own, Enemy, OwnHero, EnemyHero, Result, Mode, Played, By, Comment; }
        private sealed class Item { public string Date, Title, Text, Link; }

        private static List<Shared> shared = new List<Shared>();
        private static List<Item> news = new List<Item>();
        private static string discordUrl;
        private static float fetchedAt = -1000f;
        private static bool fetching, fetchFailed;
        private static string watchStatus = "";
        private static string codeText = "";                  // what is typed in the code box, kept across redraws
        private static bool dirty;
        private static int drawnLoc;
        private static readonly Dictionary<string, float> scrollAt = new Dictionary<string, float>();
        private static readonly List<RectTransform> scrollContents = new List<RectTransform>();
        private static int replaceBars;

        /// <summary>Called every frame: redraws the page after a change (new data, a language change).</summary>
        internal static void Tick()
        {
            if (drawnLoc != Loc.Version) { drawnLoc = Loc.Version; dirty = true; }
            if (replaceBars > 0 && --replaceBars == 0)
                foreach (var c in scrollContents)
                {
                    if (!Ui.Alive(c)) continue;
                    Scrolling.Improve(c);
                    try
                    {
                        var sr = c.GetComponentInParent<ScrollRect>();
                        if ((object)sr != null && scrollAt.TryGetValue(sr.name, out float at)) sr.verticalNormalizedPosition = at;
                    }
                    catch { }
                }
            if (!dirty) return;
            try
            {
                var w = SocialTabs.Window; var tab = entry.Tab;
                if (!Ui.Alive(w) || !Ui.Alive(tab) || !tab.gameObject.activeInHierarchy) return;
                Build(w, tab.transform);
            }
            catch (Exception e) { RevivalMod.Log.Warning("[community] " + e.Message); dirty = false; }
        }

        // ---------------------------------------------------------------- the page

        private static void Build(GameWindowWithTabs w, Transform host)
        {
            dirty = false;
            Fetch();
            scrollContents.Clear();
            var old = host.Find(PanelName);
            if ((object)old != null)
            {
                try { foreach (var sr in old.GetComponentsInChildren<ScrollRect>(true)) scrollAt[sr.name] = sr.verticalNormalizedPosition; } catch { }
                old.gameObject.SetActive(false); old.name = PanelName + " (old)"; UnityEngine.Object.Destroy(old.gameObject);
            }
            if ((object)TestersPage.font == null)
                try { var any = w.GetComponentInChildren<TMP_Text>(true); if ((object)any != null) TestersPage.font = any.font; } catch { }

            var go = new GameObject(PanelName);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(host, false);
            rt.SetAsLastSibling();
            Place(w, rt);
            var area = go.transform;
            TestersPage.Box(area, "Back", new Color(0, 0, 0, 0.35f), 0, 0, 1, 1);

            // ---- left: replays
            const float lx0 = 0.02f, lx1 = 0.4f;
            TestersPage.Text(area, "ReplaysTitle", Loc.T("community.replays", "Replays"), 34, TextAlignmentOptions.Left, Color.white, lx0, 0.9f, lx1, 0.99f);
            // a box to paste or type a code into, and a Watch button (0.14.29; before, the code came from the clipboard)
            TestersPage.Text(area, "WatchTitle", Loc.T("modTab.watchReplay", "Watch a replay code"), 24, TextAlignmentOptions.Left, TestersPage.Dim, lx0, 0.8f, lx1, 0.86f);
            CodeBox(w, area, lx0, 0.72f, lx1 - 0.1f, 0.78f);
            var watch = TestersPage.Button(area, "WatchCode", Loc.T("community.watch", "Watch"), lx1 - 0.09f, 0.72f, lx1, 0.78f, new Color(0.45f, 0.12f, 0.08f, 1f));
            TestersPage.OnClick(watch, WatchTyped);
            if (!string.IsNullOrEmpty(watchStatus))
                TestersPage.Text(area, "WatchHint", watchStatus, 20, TextAlignmentOptions.Left, TestersPage.Dim, lx0, 0.66f, lx1, 0.71f);

            TestersPage.Text(area, "SharedTitle", Loc.T("community.shared", "Shared in Discord"), 26, TextAlignmentOptions.Left, TestersPage.Dim, lx0, 0.58f, lx1, 0.64f);
            const float sharedRow = 0.14f;
            var list = ScrollArea(area, "SharedList", lx0, 0.02f, lx1, 0.57f, Math.Max(1, shared.Count) * sharedRow, out float listH);
            if (shared.Count == 0)
                TestersPage.Text(list, "None", fetching ? Loc.T("community.loading", "Loading...") : fetchFailed ? Loc.T("common.unreachableDot", "Could not reach the server.")
                                 : Loc.T("community.noShared", "Nothing shared yet. Share a replay code in Discord with /replay and it shows here."),
                                 22, TextAlignmentOptions.Left, TestersPage.Dim, 0.02f, 0f, 0.98f, 1f);
            int r = 0;
            foreach (var s in shared)
            {
                float top = 1f - r * sharedRow / listH, bottom = top - (sharedRow - 0.012f) / listH;
                var row = TestersPage.Button(list, "Shared_" + s.Code, "", 0f, bottom, 0.78f, top, TestersPage.Tile);
                string vs = Loc.T("community.versus", "{a} vs {b}", ("a", s.Own), ("b", s.Enemy));
                string line2 = string.Join(" · ", Parts(HeroName(s.OwnHero) + " / " + HeroName(s.EnemyHero), s.Mode, Day(s.Played), s.By.Length > 0 ? Loc.T("community.sharedBy", "shared by {name}", ("name", s.By)) : null));
                TestersPage.Text(row.transform, "Line1", vs + (s.Result == "won" ? "  " + Loc.T("community.wonBy", "({name} won)", ("name", s.Own)) : s.Result == "lost" ? "  " + Loc.T("community.wonBy", "({name} won)", ("name", s.Enemy)) : ""),
                                 22, TextAlignmentOptions.Left, Color.white, 0.03f, 0.5f, 0.98f, 0.98f);
                TestersPage.Text(row.transform, "Line2", line2 + (s.Comment.Length > 0 ? "\n" + s.Comment : ""), 17, TextAlignmentOptions.Left, TestersPage.Dim, 0.03f, 0.02f, 0.98f, 0.5f);
                var play = TestersPage.Button(list, "Watch_" + s.Code, Loc.T("community.watch", "Watch"), 0.8f, bottom, 1f, top, new Color(0.45f, 0.12f, 0.08f, 1f));
                string code = s.Code;
                Action go2 = () =>
                {
                    ReplayCodes.WatchStatus = text => { watchStatus = text ?? ""; dirty = true; };
                    ReplayCodes.Watch(code.Replace("-", ""));
                };
                TestersPage.OnClick(row, go2);
                TestersPage.OnClick(play, go2);
                r++;
            }

            // ---- right: news
            const float nx0 = 0.44f, nx1 = 0.98f;
            TestersPage.Text(area, "NewsTitle", Loc.T("community.news", "News"), 34, TextAlignmentOptions.Left, Color.white, nx0, 0.9f, nx1, 0.99f);
            bool discord = !string.IsNullOrEmpty(discordUrl);
            float newsBottom = discord ? 0.12f : 0.02f;
            var feed = ScrollArea(area, "NewsList", nx0, newsBottom, nx1, 0.88f, NewsHeight(), out float feedH);
            if (news.Count == 0)
                TestersPage.Text(feed, "None", fetching ? Loc.T("community.loading", "Loading...") : fetchFailed ? Loc.T("common.unreachableDot", "Could not reach the server.") : Loc.T("community.noNews", "No news yet."),
                                 22, TextAlignmentOptions.Left, TestersPage.Dim, 0.02f, 0f, 0.98f, 1f);
            float y = 1f;
            foreach (var n in news)
            {
                float h = ItemHeight(n) / feedH;
                float titleH = 0.05f / feedH, dateH = 0.035f / feedH;
                TestersPage.Text(feed, "T", n.Title, 25, TextAlignmentOptions.Left, new Color(1f, 0.85f, 0.45f, 1f), 0.02f, y - titleH, 0.98f, y);
                TestersPage.Text(feed, "D", Day(n.Date), 17, TextAlignmentOptions.Left, TestersPage.Dim, 0.02f, y - titleH - dateH, 0.98f, y - titleH);
                var body = TestersPage.Text(feed, "B", n.Text, 20, TextAlignmentOptions.TopLeft, Color.white, 0.02f, y - h + (n.Link != null ? 0.045f / feedH : 0.01f / feedH), 0.98f, y - titleH - dateH);
                if (n.Link != null)
                {
                    var open = TestersPage.Button(feed, "L", Loc.T("community.open", "Open"), 0.02f, y - h + 0.005f / feedH, 0.22f, y - h + 0.04f / feedH, TestersPage.Tile);
                    string url = n.Link;
                    TestersPage.OnClick(open, () => { try { Ui.OpenUrl(url); } catch (Exception e) { RevivalMod.Log.Warning("[community] open: " + e.Message); } });
                }
                y -= h;
            }
            if (discord)
            {
                var b = TestersPage.Button(area, "Discord", Loc.T("community.discord", "Join the Discord"), nx0, 0.02f, nx1, 0.1f, new Color(0.35f, 0.39f, 0.95f, 1f));
                TestersPage.OnClick(b, () => { try { Ui.OpenUrl(discordUrl); } catch (Exception e) { RevivalMod.Log.Warning("[community] discord: " + e.Message); } });
            }
            Enlarge(go.transform);
        }

        /// <summary>The code box: a copy of the Friends tab's add-friend box (the game's own look and keyboard handling).</summary>
        private static void CodeBox(GameWindowWithTabs w, Transform area, float x0, float y0, float x1, float y1)
        {
            try
            {
                FriendsTab friends = null;
                foreach (var f in w.GetComponentsInChildren<FriendsTab>(true)) { friends = f; break; }
                var input = (object)friends == null ? null : friends.inputFieldUserName;
                if ((object)input == null) return;
                var boxGo = UnityEngine.Object.Instantiate(input.gameObject, area, false);
                boxGo.name = "Replay code box";
                foreach (var le in boxGo.GetComponents<LayoutElement>()) le.ignoreLayout = true;
                var rt = boxGo.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(x0, y0); rt.anchorMax = new Vector2(x1, y1);
                rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
                var box = boxGo.GetComponent<TMP_InputField>();
                if ((object)box != null)
                {
                    box.onSubmit = new TMP_InputField.SubmitEvent();
                    box.onEndEdit = new TMP_InputField.SubmitEvent();
                    box.onValueChanged = new TMP_InputField.OnChangeEvent();
                    box.characterLimit = 12;
                    box.SetTextWithoutNotify(codeText ?? "");
                    var ph = box.placeholder?.TryCast<TMP_Text>();
                    if ((object)ph != null) Ui.SetText(ph, Loc.T("community.codePlaceholder", "Replay code"));
                    box.onValueChanged.AddListener(Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction<string>>(new Action<string>(t => codeText = t)));
                    box.onSubmit.AddListener(Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction<string>>(new Action<string>(_ => WatchTyped())));
                }
                boxGo.SetActive(true);
            }
            catch (Exception e) { RevivalMod.Log.Warning("[community] code box: " + e.Message); }
        }

        private static void WatchTyped()
        {
            string code = ReplayCodes.Normalize(codeText ?? "");
            ReplayCodes.WatchStatus = text => { watchStatus = text ?? ""; dirty = true; };
            if (code.Length == 0) { watchStatus = Loc.T("community.typeCode", "Paste or type a replay code first."); dirty = true; return; }
            ReplayCodes.Watch(code);
        }

        private static string[] Parts(params string[] xs) => Array.FindAll(xs, x => !string.IsNullOrEmpty(x));

        private static string Day(string iso)
        {
            if (string.IsNullOrEmpty(iso)) return "";
            try { return DateTime.Parse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind).ToLocalTime().ToString("d"); }
            catch { return iso.Length >= 10 ? iso.Substring(0, 10) : iso; }
        }

        private static string HeroName(string id)
        {
            if (string.IsNullOrEmpty(id)) return "?";
            try
            {
                var d = PlayerDataManager.singletonManager?.warlordsObjWithId;
                if (d != null && d.ContainsKey(id)) { var c = d[id]; if ((object)c != null && !string.IsNullOrEmpty(c.cardName)) return c.cardName; }
            }
            catch { }
            return id;
        }

        // news items: a title line, a date line, the text (height by its length), an Open button when linked
        private static float ItemHeight(Item n)
        {
            int lines = 1 + (n.Text?.Length ?? 0) / 60 + (n.Text ?? "").Split('\n').Length - 1;
            return 0.05f + 0.035f + 0.034f * Math.Max(1, lines) + (n.Link != null ? 0.05f : 0.015f);
        }

        private static float NewsHeight() { float h = 0; foreach (var n in news) h += ItemHeight(n); return h; }

        private static void Enlarge(Transform panel)
        {
            const float scale = 1.7f;
            foreach (var t in panel.GetComponentsInChildren<TMP_Text>(true))
            {
                float size = t.fontSize;
                t.enableAutoSizing = true;
                t.fontSizeMin = size * 0.8f;
                t.fontSizeMax = size * scale;
                t.fontSize = size * scale;
            }
        }

        /// <summary>The page's area (the same as the Challenge page's).</summary>
        private static void Place(GameWindowWithTabs w, RectTransform panel)
        {
            var parent = w.GetComponent<RectTransform>();
            if ((object)parent == null) return;
            var pr = parent.rect;
            float left = pr.xMin + 360, right = pr.xMax - 60, top = pr.yMax - pr.height * 0.09f, bottom = pr.yMin + pr.height * 0.015f;
            try
            {
                var rib = w.Components?.TabButtons?.GetComponent<RectTransform>();
                if ((object)rib != null)
                {
                    var corners = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(4);
                    rib.GetWorldCorners(corners);
                    left = parent.InverseTransformPoint(corners[2]).x + 28;
                }
            }
            catch { }
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
            float scale = 1f;
            var host = panel.parent;
            if ((object)host != null && host.lossyScale.x > 0.0001f && parent.lossyScale.x > 0.0001f) scale = parent.lossyScale.x / host.lossyScale.x;
            panel.sizeDelta = new Vector2(Mathf.Max(10, right - left), Mathf.Max(10, top - bottom)) * scale;
            panel.position = parent.TransformPoint(new Vector3((left + right) / 2, (top + bottom) / 2, 0));
        }

        private static Transform ScrollArea(Transform panel, string name, float x0, float y0, float x1, float y1, float contentFrac, out float contentH)
        {
            float viewH = y1 - y0;
            contentH = Math.Max(viewH, contentFrac);
            var view = TestersPage.Box(panel, name, new Color(0f, 0f, 0f, 0.18f), x0, y0, x1, y1);
            view.gameObject.AddComponent<RectMask2D>();
            var scroll = view.gameObject.AddComponent<ScrollRect>();
            var content = new GameObject("Content");
            var crt = content.AddComponent<RectTransform>();
            crt.SetParent(view, false);
            crt.anchorMin = new Vector2(0f, 1f); crt.anchorMax = new Vector2(1f, 1f); crt.pivot = new Vector2(0.5f, 1f);
            float panelH = panel.GetComponent<RectTransform>().rect.height;
            crt.sizeDelta = new Vector2(0f, panelH * contentH);
            crt.anchoredPosition = Vector2.zero;
            scroll.content = crt;
            scroll.viewport = view.GetComponent<RectTransform>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 60f;
            if (scrollAt.TryGetValue(name, out float at)) scroll.verticalNormalizedPosition = at;
            if (contentH > viewH + 0.0001f)
            {
                Scrolling.Improve(crt);
                scrollContents.Add(crt);
                replaceBars = 2;
            }
            return crt;
        }

        // ---------------------------------------------------------------- the data

        private static void Fetch()
        {
            if (fetching || Time.realtimeSinceStartup - fetchedAt < 60f) return;
            fetching = true; fetchFailed = false;
            System.Threading.Tasks.Task.Run(async () =>
            {
                List<Shared> gotShared = null; List<Item> gotNews = null; string gotDiscord = null; bool failed = false;
                try
                {
                    var reply = await Net.PostJson("/playfab/Revival/SharedReplays", "{}", TimeSpan.FromSeconds(10), retry: true);
                    if (reply.Ok && reply.Data.ValueKind == JsonValueKind.Object && reply.Data.TryGetProperty("replays", out var arr) && arr.ValueKind == JsonValueKind.Array)
                    {
                        gotShared = new List<Shared>();
                        foreach (var e in arr.EnumerateArray())
                        {
                            if (e.ValueKind != JsonValueKind.Object) continue;
                            string S(string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : "";
                            if (S("code").Length == 0) continue;
                            gotShared.Add(new Shared { Code = S("code"), Own = S("ownName"), Enemy = S("enemyName"), OwnHero = S("ownHero"), EnemyHero = S("enemyHero"),
                                                       Result = S("result"), Mode = S("mode"), Played = S("played").Length > 0 ? S("played") : S("created"), By = S("by"), Comment = S("comment") });
                        }
                    }
                    else failed = true;
                }
                catch (Exception e) { failed = true; RevivalMod.Log.Warning("[community] shared replays: " + e.Message); }
                try
                {
                    using var http = Net.Client(TimeSpan.FromSeconds(10));
                    string text = await http.GetStringAsync(RevivalMod.Config.ServerUrl + "/api/v1/content/files/news.json");
                    using var doc = JsonDocument.Parse(text);
                    var root = doc.RootElement;
                    gotNews = new List<Item>();
                    if (root.ValueKind == JsonValueKind.Object)
                    {
                        if (root.TryGetProperty("discord", out var d) && d.ValueKind == JsonValueKind.String && d.GetString().StartsWith("https://", StringComparison.Ordinal)) gotDiscord = d.GetString();
                        if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                            foreach (var e in items.EnumerateArray())
                            {
                                if (e.ValueKind != JsonValueKind.Object || gotNews.Count >= 50) continue;
                                string S(string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : "";
                                if (S("title").Length == 0 && S("text").Length == 0) continue;
                                string link = S("link");
                                gotNews.Add(new Item { Date = S("date"), Title = S("title"), Text = S("text"), Link = link.StartsWith("https://", StringComparison.Ordinal) ? link : null });
                            }
                    }
                }
                catch (System.Net.Http.HttpRequestException e) { RevivalMod.Log.Msg("[community] no news file (" + e.Message + ")"); gotNews = new List<Item>(); }
                catch (Exception e) { failed = true; RevivalMod.Log.Warning("[community] news: " + e.Message); }
                PlayFabTransport.OnMainThread(() =>
                {
                    fetching = false; fetchFailed = failed; fetchedAt = Time.realtimeSinceStartup;
                    if (gotShared != null) shared = gotShared;
                    if (gotNews != null) { news = gotNews; discordUrl = gotDiscord; }
                    dirty = true;
                });
            });
        }
    }
}
