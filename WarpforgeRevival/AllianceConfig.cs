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
    /// Also (0.13.57): Social > Alliances > Join could show the layout's placeholder invitations ("Alliance Name Bla
    /// Bla", 17/20). JoinAllianceMenu keeps one "loading" flag for both of its lists; FillGroupInvitations returns at
    /// once while it is set (before it clears the placeholder rows and asks the server), and the open-alliance list
    /// sets it too. The flag is cleared just before the invitation list is filled, so it always runs.
    /// </summary>
    internal static class AllianceConfig
    {
        private static bool invitationsLogged;

        [HarmonyPatch(typeof(JoinAllianceMenu), nameof(JoinAllianceMenu.FillGroupInvitations))]
        private static class Invitations
        {
            private static void Prefix(JoinAllianceMenu __instance)
            {
                try
                {
                    if ((object)__instance == null || !__instance.loading) return;
                    __instance.loading = false;
                    if (!invitationsLogged)
                    {
                        invitationsLogged = true;
                        RevivalMod.Log.Msg("[alliances] invitations: the Join page's loading flag was still set; cleared so the list is filled");
                    }
                }
                catch (Exception e) { RevivalMod.Log.Warning("[alliances] invitations: " + e.Message); }
            }
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
