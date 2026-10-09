using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using Il2CppTMPro;
using UnityEngine;

namespace WarpforgeRevival
{
    /// <summary>
    /// Settings > Support pointed at the publisher's closed help pages. It becomes "Website": one
    /// button that opens the revival server's site (the card creator) in the browser. The contact
    /// button, the support e-mail text and the privacy policy link are hidden.
    /// </summary>
    internal static class SupportPage
    {
        private const string TabName = "Website", ButtonText = "Card Creator",
            Intro = "Design your own cards for Warpforge Revival, manage your account and share decks.";

        private static bool reported;

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
            faq.Url = ServerSettings.CreatorUrl;

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

            // The phone layout of this page keeps the button and its introduction switched off and
            // shows its own wording instead (which was just hidden with the rest of the publisher's
            // text). Make sure the two things this page is for can be seen.
            int shown = Show(faqT, root);
            if ((object)intro != null) shown += Show(intro.transform, root);
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
            RevivalMod.Log.Msg($"[support] page now opens {ServerSettings.CreatorUrl}; heading was '{oldTitle}', side tab labels renamed: {renamed}; texts found: {string.Join(" | ", seen)}");
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
