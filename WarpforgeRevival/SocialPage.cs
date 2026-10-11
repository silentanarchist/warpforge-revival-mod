using System;
using HarmonyLib;
using Il2Cpp;
using Il2CppTMPro;
using UnityEngine;

namespace WarpforgeRevival
{
    /// <summary>
    /// Social on a revival server: the friend list (worded for adding people by friend code or name)
    /// and the Challenge tab, which takes the place of the unused Alliances tab (see ChallengeMenu).
    /// </summary>
    internal static class SocialPage
    {
        private static WindowTabBase Find<T>(GameWindowWithTabs w) where T : WindowTabBase
        {
            var tabs = w.tabs;
            if (tabs == null) return null;
            for (int i = 0; i < tabs.Count; i++)
            {
                var t = tabs[i];
                if ((object)t != null && (object)t.TryCast<T>() != null) return t;
            }
            return null;
        }

        private static void HideAlliances(GameWindowWithTabs w)
        {
            try
            {
                if ((object)w == null || (object)w.TryCast<SocialMenuWindow>() == null) return;
                var buttons = w.Components?.TabButtons?.options;
                if (buttons != null)
                    for (int i = 0; i < buttons.Count; i++)
                    {
                        var b = buttons[i];
                        if (b == null || (object)b.tab == null || (object)b.toggle == null) continue;
                        // the Alliances tab is the Challenge tab now (ChallengeMenu); it is shown, not hidden
                        if ((object)b.tab.TryCast<AlliancesTab>() != null && !b.toggle.gameObject.activeSelf) b.toggle.gameObject.SetActive(true);
                    }
            }
            catch (Exception e) { RevivalMod.Log.Warning("[social] " + e.Message); }
        }

        /// <summary>Collection: the Styles tab (alternate card art) has no content on a revival server.</summary>
        private static void HideStyles(GameWindowWithTabs w)
        {
            try
            {
                if ((object)w == null || (object)w.TryCast<CollectionScreen>() == null) return;
                var buttons = w.Components?.TabButtons?.options;
                if (buttons == null) return;
                for (int i = 0; i < buttons.Count; i++)
                {
                    var b = buttons[i];
                    if (b == null || (object)b.tab == null || (object)b.toggle == null) continue;
                    if ((object)b.tab.TryCast<AlternateArtCardCollectionTab>() != null) b.toggle.gameObject.SetActive(false);
                }
            }
            catch (Exception e) { RevivalMod.Log.Warning("[collection] " + e.Message); }
        }

        [HarmonyPatch(typeof(GameWindowWithTabs), nameof(GameWindowWithTabs.GetStartingTab))]
        private static class StartOnFriends
        {
            private static void Postfix(GameWindowWithTabs __instance, ref WindowTabBase __result)
            {
                try
                {
                    if ((object)__instance.TryCast<SocialMenuWindow>() == null) return;
                    var friends = Find<FriendsTab>(__instance);
                    if ((object)friends != null) __result = friends;
                }
                catch (Exception e) { RevivalMod.Log.Warning("[social] " + e.Message); }
            }
        }

        [HarmonyPatch(typeof(GameWindowWithTabs), nameof(GameWindowWithTabs.OpenTabs))]
        private static class AfterOpenTabs { private static void Postfix(GameWindowWithTabs __instance) { HideAlliances(__instance); } }

        [HarmonyPatch(typeof(GameWindowWithTabs), nameof(GameWindowWithTabs.Open))]
        private static class AfterOpen { private static void Postfix(GameWindowWithTabs __instance) { HideAlliances(__instance); } }

        /// <summary>Friends tab wording: add by friend code, no "instant duel" button.</summary>
        internal static void DressFriendsTab(FriendsTab tab)
        {
            try
            {
                var input = tab.inputFieldUserName;
                if ((object)input != null)
                {
                    var ph = input.placeholder;
                    if ((object)ph != null)
                    {
                        var loc = ph.GetComponent<Il2CppI2.Loc.Localize>();
                        if ((object)loc != null) loc.enabled = false;
                        var text = ph.TryCast<TMP_Text>();
                        if ((object)text != null) text.text = "Enter friend code";
                    }
                    var panel = input.transform.parent;
                    var title = (object)panel != null ? panel.Find("Search Player") : null;
                    if ((object)title != null)
                    {
                        var loc = title.GetComponent<Il2CppI2.Loc.Localize>();
                        if ((object)loc != null) loc.enabled = false;
                        var text = title.GetComponent<TMP_Text>();
                        if ((object)text != null) text.text = "Add friend";
                    }
                }
                var duel = tab.instantDuelButton;
                if ((object)duel != null) duel.gameObject.SetActive(false);
            }
            catch (Exception e) { RevivalMod.Log.Warning("[social] " + e.Message); }
        }
    }
}
