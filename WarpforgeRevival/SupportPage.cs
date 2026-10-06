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

            faq.Initialize(ServerSettings.CreatorUrl);

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
            foreach (var label in root.GetComponentsInChildren<TMP_Text>(true))
            {
                if ((object)label == null) continue;
                bool inFaq = Under(label.transform, faqT);
                bool isIntro = (object)intro != null && label.Pointer == intro.Pointer;
                seen.Add($"{label.name}='{label.text}'" + (inFaq ? " [button]" : isIntro ? " [intro]" : ""));
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
            }

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

            if (reported) return;
            reported = true;
            RevivalMod.Log.Msg($"[support] page now opens {ServerSettings.CreatorUrl}; heading was '{oldTitle}', side tab labels renamed: {renamed}; texts found: {string.Join(" | ", seen)}");
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
