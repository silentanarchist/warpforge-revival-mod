using System;
using HarmonyLib;
using Il2Cpp;
using Il2CppEverguild.LiveOps.Config;

namespace WarpforgeRevival
{
    /// <summary>
    /// Alliances (2026-10-16): the price of creating an alliance (title data AlliancesConfig.createAlliancePrice)
    /// names its currency by asset id, and the currency assets were in Everguild's lost cloud bundle, so the id is
    /// empty and Social > Alliances > Create failed (CreateAllianceMenu.Initialize: NullReferenceException; empty
    /// language and privacy lists, the layout's placeholder price). Right after the game unpacks its configs at
    /// sign-in, a price without a currency gets the mod's stand-in for "free" (Currencies), keeping the config's
    /// amount (0 on the revival server, so creating an alliance costs nothing).
    ///
    /// Also (0.13.58): Social > Alliances showed the layout's placeholder invitations ("Alliance Name Bla Bla",
    /// 17/20; Dismiss on one threw a NullReferenceException). Opening the tab only fills the open-alliance list; the
    /// invitation list was filled (and its placeholder rows deleted) only when Join was clicked. Now, the build-once
    /// way: when the Social window is built (MenuLayout's SetupTabs postfix) the placeholder rows are deleted and the
    /// empty section hidden; when Alliances is opened (the window's own "tab changed" event) the current alliance is
    /// fetched and the tab's own Refresh opens the right page (see TabOpened; 0.13.59). (0.13.57 hooked
    /// FillGroupInvitations instead; 0.13.58 only filled the invitation list.)
    /// </summary>
    internal static class AllianceConfig
    {
        private static bool builtLogged;

        /// <summary>The Social window has just been built (before it is shown): the Join page's placeholder invitations
        /// go, and opening Alliances fills the real invitation list.</summary>
        internal static void SocialBuilt(GameWindowWithTabs window)
        {
            try
            {
                JoinAllianceMenu join = null;
                foreach (var j in window.GetComponentsInChildren<JoinAllianceMenu>(true))
                    if ((object)j != null && !ChallengeMenu.IsChallengeTab(j.GetComponentInParent<AlliancesTab>(true))) { join = j; break; }
                if ((object)join == null) return;
                int removed = 0;
                var holder = join.invitationHolder;
                if ((object)holder != null)
                    for (int i = holder.childCount - 1; i >= 0; i--)
                    {
                        var row = holder.GetChild(i).gameObject;
                        row.SetActive(false);
                        UnityEngine.Object.Destroy(row);
                        removed++;
                    }
                if ((object)join.invitationList != null) join.invitationList.SetActive(false);   // shown again when there are real ones
                // neither page shows until the tab's own Refresh has picked one (see TabOpened)
                var alliances = join.GetComponentInParent<AlliancesTab>(true);
                if ((object)alliances != null)
                {
                    if ((object)alliances.allianceNotMemberVariant != null) alliances.allianceNotMemberVariant.gameObject.SetActive(false);
                    if ((object)alliances.allianceMemberVariant != null) alliances.allianceMemberVariant.gameObject.SetActive(false);
                }
                var onChange = Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Il2CppSystem.Action<WindowTabBase>>(new Action<WindowTabBase>(TabOpened));
                window.OnTabChanged = window.OnTabChanged == null
                    ? onChange
                    : Il2CppSystem.Delegate.Combine(window.OnTabChanged, onChange).Cast<Il2CppSystem.Action<WindowTabBase>>();
                if (!builtLogged) { builtLogged = true; RevivalMod.Log.Msg($"[alliances] Join page set up as the Social window was built: {removed} placeholder invitation(s) removed"); }
            }
            catch (Exception e) { RevivalMod.Log.Warning("[alliances] Join page: " + e.Message); }
        }

        private static bool openedLogged;

        /// <summary>
        /// The Alliances tab was picked (the window's own OnTabChanged event, no hook). The game decides between
        /// "your alliance" and the Join page in AlliancesTab.Refresh, which it only runs when alliance data arrives at
        /// sign-in, and on the revival server that start-up stops early ("Disabling Alliances before tutorial ends")
        /// without asking for the current alliance. So: ask for it (AlliancesManager.GetCurrentGroup, fresh; storing
        /// it also sets the player's member record, which is what IsInGroup checks), then let the tab's own Refresh
        /// open the right page: your alliance, or the real Join page with invitations and open alliances.
        /// </summary>
        private static void TabOpened(WindowTabBase to)
        {
            try
            {
                if ((object)to == null || ChallengeMenu.IsChallengeTab(to)) return;
                var tab = to.TryCast<AlliancesTab>();
                if ((object)tab == null) return;
                var manager = SingletonBehaviour<AlliancesManager>.Instance;
                if ((object)manager == null) { lastState = null; tab.Refresh(); return; }
                var done = Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Il2CppSystem.Action<Group>>(new Action<Group>(group =>
                {
                    try
                    {
                        if ((object)tab == null || tab.m_CachedPtr == IntPtr.Zero) return;
                        lastState = null;          // opening always refreshes (see ShouldRefresh)
                        tab.Refresh();
                        if (!openedLogged)
                        {
                            openedLogged = true;
                            RevivalMod.Log.Msg("[alliances] Alliances opened: " + ((object)group == null ? "not in an alliance, Join page" : "in '" + group.Name + "', alliance page"));
                        }
                    }
                    catch (Exception e) { RevivalMod.Log.Warning("[alliances] opening the tab: " + e.Message); }
                }));
                manager.GetCurrentGroup(done, true);
            }
            catch (Exception e) { RevivalMod.Log.Warning("[alliances] opening the tab: " + e.Message); }
        }

        // ---------------------------------------------------------------- the refresh loop (0.13.60)
        private static string lastState;
        private static float lastRefreshAt = -100f;
        private static int skipped;

        /// <summary>
        /// Whether the Alliances tab's Refresh should run (called from the hook ChallengeMenu already has on it, no new
        /// hook). The game's own chain: the alliance page's set-up asks for the alliance (GetCurrentGroup), storing the
        /// answer fires the "alliance updated" event, the tab (subscribed in its Start) runs Refresh, which sets the
        /// page up again and asks again: on the revival server every answer is a fresh copy, so it never settled
        /// (0.13.59: about 30 refreshes a second; the page was rebuilt before the edit button could be used). A
        /// Refresh within 5 s of the last one, with nothing about the alliance changed, is skipped; joining, leaving,
        /// edits and member changes still refresh.
        /// </summary>
        internal static bool ShouldRefresh()
        {
            try
            {
                var manager = SingletonBehaviour<AlliancesManager>.Instance;
                string state = "?";
                if ((object)manager != null)
                {
                    var g = manager.IsInGroup ? manager.CurrentCachedGroupData : null;
                    state = (object)g == null ? (manager.IsInGroup ? "member" : "none")
                        : string.Join("|", g.GroupId, g.Name, g.Desc, g.MemberCount, g.LeaderId, (int)g.Privacy, g.Region, g.MinElo);
                }
                float now = UnityEngine.Time.realtimeSinceStartup;
                if (state == lastState && now - lastRefreshAt < 5f)
                {
                    if (++skipped == 1) RevivalMod.Log.Msg("[alliances] repeated refresh of an unchanged alliance skipped (the game's refresh loop)");
                    return false;
                }
                lastState = state;
                lastRefreshAt = now;
                return true;
            }
            catch (Exception e) { RevivalMod.Log.Warning("[alliances] refresh: " + e.Message); return true; }
        }

        [HarmonyPatch(typeof(ConfigManager), nameof(ConfigManager.Unpack))]
        private static class AfterUnpack
        {
            private static void Postfix()
            {
                try
                {
                    var config = ConfigManager.GetConfig<AlliancesConfig>();
                    if (config == null) { RevivalMod.Log.Warning("[alliances] no AlliancesConfig after the configs were unpacked"); return; }
                    var price = config.createAlliancePrice;
                    var currency = price?.currency;
                    bool missing = price == null || currency == null
                                   || (string.IsNullOrEmpty(currency.TargetId) && currency.reference == null);
                    if (!missing) return;
                    int amount = price?.amount ?? 0;
                    config.createAlliancePrice = new Price(Currencies.Get(GameCurrency.free), amount);
                    RevivalMod.Log.Msg($"[alliances] creating an alliance: price had no currency; set to the stand-in 'free' currency, amount {amount}");
                }
                catch (Exception e) { RevivalMod.Log.Warning("[alliances] alliance config: " + e.Message); }
            }
        }
    }
}
