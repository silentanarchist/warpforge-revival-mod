#if ANDROID_PORT
using System;
using HarmonyLib;

namespace WarpforgeRevival
{
    /// <summary>
    /// The Android game's shop goes through Google Play billing, which a side-loaded copy cannot use.
    /// Report "no shop" the way the game itself does, as the PC mod does for the Steam shop.
    /// </summary>
    [HarmonyPatch(typeof(Il2Cpp.InAppPurchaseUnity), nameof(Il2Cpp.InAppPurchaseUnity.Init))]
    internal static class AndroidStore
    {
        private static bool Prefix(Il2Cpp.InAppPurchaseUnity __instance)
        {
            try
            {
                ((Il2Cpp.InAppPurchaseProxy)__instance).SetBillingNotSupported();
                RevivalMod.Log.Msg("[store] Google Play shop switched off");
            }
            catch (Exception e) { RevivalMod.Log.Warning("[store] switch-off failed: " + e.Message); }
            return false;
        }
    }
}
#endif
