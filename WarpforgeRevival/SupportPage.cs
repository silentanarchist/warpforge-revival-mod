using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using Il2CppTMPro;
using UnityEngine;

namespace WarpforgeRevival
{
    /// <summary>
    /// Settings > Support pointed at the publisher's closed help pages. It becomes "Mod": the mod's
    /// version, a button ("Open") that opens the revival server's website (its home page) in the browser, and
    /// a "Check for updates" button (a copy of the first one) that asks the server for a newer mod
    /// now. The contact button, the support e-mail text and the privacy policy link are hidden.
    /// </summary>
    internal static class SupportPage
    {
        private const string TabName = "Mod", ButtonText = "Open", CheckText = "Check for updates", CheckName = "WFR Check for updates";
        private static string Intro => $"Warpforge Revival mod {RevivalMod.Version}. The website: design your own cards, manage your account and share decks.";

        private static bool reported;

        /// <summary>The website's home page: the site's address without the /creator part.</summary>
        private static string SiteHome()
        {
            string site = ServerSettings.CreatorUrl;
            try { return new Uri(site).GetLeftPart(UriPartial.Authority) + "/"; } catch { return site; }
        }

        private static bool Under(Transform t, Transform ancestor)
        {
            for (; (object)t != null; t = t.parent)
                if (t.Pointer == ancestor.Pointer) return true;
            return false;
        }

        private static void SetText(TMP_Text label, string text)
        {
            if ((object)label == null) return;
            var loc = label.GetComponent<Il2CppI2.Loc.Localize>();
            if ((object)loc != null) loc.enabled = false;        // otherwise the original wording comes back
            label.text = text;
        }

        /// <summary>
        /// Hides something for good. The window switches its rows back on when the tab opens, so
        /// switching the object off is not enough: everything it draws is turned off as well, which
        /// also stops it catching clicks.
        /// </summary>
        private static void Hide(Component c)
        {
            if ((object)c == null) return;
            foreach (var g in c.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
                if ((object)g != null) g.enabled = false;
            foreach (var b in c.GetComponentsInChildren<UnityEngine.UI.Selectable>(true))
                if ((object)b != null) b.enabled = false;
            c.gameObject.SetActive(false);
        }

        /// <summary>Switches on everything between a part of the page and the page itself. Returns how many were off.</summary>
        private static int Show(Transform t, Transform root)
        {
            int n = 0;
            for (; (object)t != null && t.Pointer != root.Pointer; t = t.parent)
                if (!t.gameObject.activeSelf) { t.gameObject.SetActive(true); n++; }
            return n;
        }

        private static void Apply(SupportTab tab)
        {
            var faq = tab.faqButton;
            if ((object)faq == null) return;
            var root = tab.transform;
            var faqT = faq.transform;

            // Everything that pointed at the publisher goes.
            Hide(tab.contactButton);
            Hide(tab.privacyPolicyButton);
            Hide(tab.termsOfServiceButton);
            Hide(tab.supportButton);

            // The game's own set-up has already given the button its click handler (which opens Url);
            // only the address changes. Initialize would add a second handler: two browser tabs per click.
            faq.Url = SiteHome();

            // The line introducing the button: its nearest earlier neighbour that holds text and no button.
            TMP_Text intro = null;
            var parent = faqT.parent;
            if ((object)parent != null)
                for (int i = faqT.GetSiblingIndex() - 1; i >= 0 && (object)intro == null; i--)
                {
                    var c = parent.GetChild(i);
                    if ((object)c.GetComponentInChildren<UrlButton>(true) != null || (object)c.GetComponentInChildren<EverguildButton>(true) != null) continue;
                    intro = c.GetComponentInChildren<TMP_Text>(true);
                }

            // The page heading is the first text on the page; every other loose line of text is the
            // publisher's ("Do you need help from us?", the support e-mail) and is hidden.
            TMP_Text title = null;
            string oldTitle = null;
            var seen = new List<string>();
            var gone = new List<Transform>();
            foreach (var label in root.GetComponentsInChildren<TMP_Text>(true))
            {
                if ((object)label == null) continue;
                if ((object)check != null && check.Pointer != IntPtr.Zero && Under(label.transform, check)) continue;
                bool inFaq = Under(label.transform, faqT);
                bool isIntro = (object)intro != null && label.Pointer == intro.Pointer;
                seen.Add($"{label.name}='{label.text}'" + (inFaq ? " [button]" : isIntro ? " [intro]" : "")
                    + (label.gameObject.activeInHierarchy ? "" : " (not shown)"));
                if (inFaq) { SetText(label, ButtonText); continue; }
                if (isIntro) { SetText(label, Intro); continue; }
                if ((object)title == null && (object)label.GetComponentInParent<EverguildButton>() == null)
                {
                    title = label;
                    oldTitle = label.text;
                    SetText(label, TabName);
                    continue;
                }
                Hide(label);
                gone.Add(label.transform);
            }

            // What was hidden often sits in a row with its own icon (the link arrow beside the old
            // privacy policy text). Hide the whole row: the largest part of the page around it that
            // holds none of the three things this page keeps.
            foreach (var extra in new Component[] { tab.contactButton, tab.privacyPolicyButton, tab.termsOfServiceButton, tab.supportButton })
                if ((object)extra != null) gone.Add(extra.transform);
            int rows = 0;
            foreach (var t in gone)
            {
                var row = t;
                while ((object)row.parent != null && row.parent.Pointer != root.Pointer && row.Pointer != root.Pointer
                       && !Under(faqT, row.parent)
                       && ((object)intro == null || !Under(intro.transform, row.parent))
                       && ((object)title == null || !Under(title.transform, row.parent)))
                    row = row.parent;
                if (row.Pointer == t.Pointer || row.Pointer == root.Pointer) continue;
                Hide(row);
                rows++;
            }
            if (rows > 0 && !reported) RevivalMod.Log.Msg($"[support] hid {rows} leftover row(s) around the publisher's links");

            AddCheckButton(faq, faqT);
            AddReportButton(faq, faqT);

            // The phone layout of this page keeps the button and its introduction switched off and
            // shows its own wording instead (which was just hidden with the rest of the publisher's
            // text). Make sure the two things this page is for can be seen.
            int shown = Show(faqT, root);
            if ((object)intro != null) shown += Show(intro.transform, root);
            if ((object)check != null) shown += Show(check, root);
            ReportVisible(root);
            if (shown > 0 && !reported) RevivalMod.Log.Msg($"[support] switched on {shown} hidden part(s) of the page so the button and its text show");

            // The tab's button down the side of the window carries the same word as the heading.
            int renamed = 0;
            var menu = tab.GetComponentInParent<SettingsMenu>();
            if ((object)menu != null && !string.IsNullOrEmpty(oldTitle))
            {
                string term = null;
                var titleLoc = title.GetComponent<Il2CppI2.Loc.Localize>();
                if ((object)titleLoc != null) term = titleLoc.mTerm;
                foreach (var label in menu.GetComponentsInChildren<TMP_Text>(true))
                {
                    if ((object)label == null || Under(label.transform, root)) continue;
                    var loc = label.GetComponent<Il2CppI2.Loc.Localize>();
                    bool same = label.text == oldTitle || ((object)loc != null && !string.IsNullOrEmpty(term) && loc.mTerm == term);
                    if (!same) continue;
                    SetText(label, TabName);
                    renamed++;
                }
            }

            // the Login tab of the same window needs the same care (see AccountPage.Tick)
            try
            {
                var window = tab.GetComponentInParent<SettingsMenu>();
                if ((object)window != null) AccountPage.Remember(window.GetComponentInChildren<AccountTab>(true));
            }
            catch (Exception e) { RevivalMod.Log.Warning("[account] " + e.Message); }

            // remembered for Tick: on a phone the layout changes again when the tab is opened
            page = tab; pageRoot = root; pageButton = faqT; pageIntro = (object)intro != null ? intro.transform : null;
            settledFrames = 0;

            if (reported) return;
            reported = true;
            RevivalMod.Log.Msg($"[support] page now opens {SiteHome()}; heading was '{oldTitle}', side tab labels renamed: {renamed}; texts found: {string.Join(" | ", seen)}");
        }

        // ---- Check for updates
        private static Transform check;
        private static TMP_Text checkLabel;
        private static bool checking;

        /// <summary>A copy of the website button under it, which checks the server for a newer mod.</summary>
        private static void AddCheckButton(UrlButton faq, Transform faqT)
        {
            var parent = faqT.parent;
            if ((object)parent == null) return;
            var old = parent.Find(CheckName);
            if ((object)old != null) { check = old; return; }        // set up already (the page was set up again)
            var go = UnityEngine.Object.Instantiate(faq.gameObject, parent, false);
            go.name = CheckName;
            check = go.transform;
            check.SetSiblingIndex(faqT.GetSiblingIndex() + 1);
            // the copy must not open the website: its UrlButton goes (its click handler was added in
            // code by the game, so the copy never had it), and the button gets the check instead
            var url = go.GetComponent<UrlButton>();
            if ((object)url != null) UnityEngine.Object.DestroyImmediate(url);
            // no layout group to stack them: put the copy below by hand
            if ((object)parent.GetComponent<UnityEngine.UI.LayoutGroup>() == null)
            {
                var rt = check.Cast<RectTransform>();
                var src = faqT.Cast<RectTransform>();
                rt.anchoredPosition = src.anchoredPosition - new Vector2(0f, src.rect.height + 24f);
            }
            checkLabel = go.GetComponentInChildren<TMP_Text>(true);
            SetText(checkLabel, CheckText);
            var button = go.GetComponent<UnityEngine.UI.Button>() ?? go.GetComponentInChildren<UnityEngine.UI.Button>(true);
            if ((object)button == null) { RevivalMod.Log.Warning("[support] the update button has no button to click"); return; }
            button.onClick.AddListener(Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction>(new Action(OnCheck)));
            RevivalMod.Log.Msg("[support] Check for updates button added" + ((object)parent.GetComponent<UnityEngine.UI.LayoutGroup>() == null ? " (placed by hand)" : ""));
        }

        private static void Label(string text)
        {
            try { if ((object)checkLabel != null && checkLabel.Pointer != IntPtr.Zero) SetText(checkLabel, text); } catch { }
        }

        private static void OnCheck()
        {
            if (checking) return;
            checking = true;
            Label("Checking...");
            System.Threading.Tasks.Task.Run(async () =>
            {
                string answer;
                try { answer = await RevivalMod.CheckForUpdates(); }
                catch (Exception e) { RevivalMod.Log.Warning("[update] check button: " + e.Message); answer = "Could not reach the server."; }
                PlayFabTransport.OnMainThread(() =>
                {
                    Label(answer ?? "Updating...");
                    if (answer != null) RevivalMod.Log.Msg("[update] check button: " + answer);
                    var at = Time.realtimeSinceStartup;
                    resetAt = answer != null ? at + 4f : 0f;
                    checking = answer == null;      // while updating, the button stays busy
                });
            });
        }

        private static float resetAt;

        // ---- Report a bug (testers): a second copy, under Check for updates. Outside matches this is
        // where testers start a report (F8 also works on PC; in a match there is a tab on the screen edge).
        private const string ReportName = "WFR Report a bug";
        private static Transform report;

        private static void AddReportButton(UrlButton faq, Transform faqT)
        {
            var parent = faqT.parent;
            if ((object)parent == null || (object)check == null) return;
            var old = parent.Find(ReportName);
            if ((object)old != null) { report = old; return; }
            var go = UnityEngine.Object.Instantiate(faq.gameObject, parent, false);
            go.name = ReportName;
            report = go.transform;
            report.SetSiblingIndex(check.GetSiblingIndex() + 1);
            var url = go.GetComponent<UrlButton>();
            if ((object)url != null) UnityEngine.Object.DestroyImmediate(url);
            if ((object)parent.GetComponent<UnityEngine.UI.LayoutGroup>() == null)
            {
                var rt = report.Cast<RectTransform>();
                var src = check.Cast<RectTransform>();
                rt.anchoredPosition = src.anchoredPosition - new Vector2(0f, src.rect.height + 24f);
            }
            SetText(go.GetComponentInChildren<TMP_Text>(true), "Report a bug");
            var button = go.GetComponent<UnityEngine.UI.Button>() ?? go.GetComponentInChildren<UnityEngine.UI.Button>(true);
            if ((object)button == null) { RevivalMod.Log.Warning("[support] the report button has no button to click"); return; }
            button.onClick.AddListener(Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction>(new Action(BugReport.Request)));
            report.gameObject.SetActive(AccountPage.IsTester);
        }

        /// <summary>The report button only for testers (switched on by the phone layout fix like the others).</summary>
        private static void ReportVisible(Transform root)
        {
            try
            {
                if ((object)report == null || report.Pointer == IntPtr.Zero) return;
                if (AccountPage.IsTester) Show(report, root);
                else if (report.gameObject.activeSelf) report.gameObject.SetActive(false);
            }
            catch { }
        }

        private static SupportTab page;
        private static Transform pageRoot, pageButton, pageIntro;
        private static int settledFrames;
        private static bool tickReported;

        /// <summary>
        /// Called every frame. The phone layout switches the button and its introduction off when the
        /// tab is opened (after the set-up above has run) and shows its own wording, which this page
        /// hides. So each time the tab comes into view, wait two frames for the layout to finish and
        /// then switch the page's two parts back on.
        /// </summary>
        internal static void Tick()
        {
            if (resetAt > 0f && Time.realtimeSinceStartup >= resetAt) { resetAt = 0f; checking = false; Label(CheckText); }
            if ((object)page == null) return;
            try
            {
                if (page.Pointer == IntPtr.Zero || page.m_CachedPtr == IntPtr.Zero) { page = null; return; }   // the window was closed for good
                if (!page.gameObject.activeInHierarchy) { settledFrames = 0; return; }
                if (settledFrames < 0) return;                  // done for this opening of the tab
                if (++settledFrames < 3) return;
                settledFrames = -1;
                int shown = Show(pageButton, pageRoot);
                if ((object)pageIntro != null) shown += Show(pageIntro, pageRoot);
                if ((object)check != null && check.Pointer != IntPtr.Zero) shown += Show(check, pageRoot);
                ReportVisible(pageRoot);
                if (!tickReported)
                {
                    tickReported = true;
                    var states = new List<string>();
                    foreach (var label in pageRoot.GetComponentsInChildren<TMP_Text>(true))
                        if ((object)label != null)
                            states.Add($"{label.name}='{(label.text ?? "").Replace("\n", " ")}' {(label.gameObject.activeInHierarchy ? "on screen" : "off")}{(label.enabled ? "" : " (hidden by the mod)")}");
                    RevivalMod.Log.Msg($"[support] tab opened: switched on {shown} part(s) the layout had turned off; texts now: {string.Join(" | ", states)}");
                }
            }
            catch (Exception e)
            {
                page = null;
                RevivalMod.Log.Warning("[support] " + e.Message);
            }
        }

        [HarmonyPatch(typeof(SupportTab), nameof(SupportTab.OnSetup))]
        private static class OnSetup
        {
            private static void Postfix(SupportTab __instance)
            {
                try { Apply(__instance); }
                catch (Exception e) { RevivalMod.Log.Warning("[support] " + e.Message); }
            }
        }
    }
}
