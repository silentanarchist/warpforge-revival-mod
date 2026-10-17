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
    /// empty section hidden, and the window's own "tab changed" event fills the invitation list when Alliances is
    /// opened (the game's JoinAllianceMenu.FillGroupInvitations, called, not hooked; its shared "loading" flag is
    /// cleared first, since the open-alliance list sets it). (0.13.57 hooked FillGroupInvitations instead.)
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
                var menu = join;
                var onChange = Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Il2CppSystem.Action<WindowTabBase>>(new Action<WindowTabBase>(to =>
                {
                    try
                    {
                        if ((object)to == null || (object)to.TryCast<AlliancesTab>() == null || ChallengeMenu.IsChallengeTab(to)) return;
                        if ((object)menu == null || menu.m_CachedPtr == IntPtr.Zero) return;
                        menu.loading = false;            // shared with the open-alliance list, which the tab fills as it opens
                        menu.FillGroupInvitations();
                    }
                    catch (Exception e) { RevivalMod.Log.Warning("[alliances] invitations: " + e.Message); }
                }));
                window.OnTabChanged = window.OnTabChanged == null
                    ? onChange
                    : Il2CppSystem.Delegate.Combine(window.OnTabChanged, onChange).Cast<Il2CppSystem.Action<WindowTabBase>>();
                if (!builtLogged) { builtLogged = true; RevivalMod.Log.Msg($"[alliances] Join page set up as the Social window was built: {removed} placeholder invitation(s) removed"); }
            }
            catch (Exception e) { RevivalMod.Log.Warning("[alliances] Join page: " + e.Message); }
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
