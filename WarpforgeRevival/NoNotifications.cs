using System;
using HarmonyLib;
using Il2Cpp;

namespace WarpforgeRevival
{
    /// <summary>
    /// Phone notifications (0.13.29). The game set up its notification channels at start-up for the
    /// publisher's reminders (rewards ready and the like). On newer Android versions creating that first
    /// channel makes the phone ask "Allow Warpforge to send you notifications?", and a revival server never
    /// sends any. So the game's notification manager is not set up at all, and its send / cancel calls
    /// are skipped while it is not.
    /// </summary>
    internal static class NoNotifications
    {
        private static bool logged;

        [HarmonyPatch(typeof(Il2CppLocalNotificationHandler.GameNotificationsManager), nameof(Il2CppLocalNotificationHandler.GameNotificationsManager.Initialize))]
        private static class NoSetup
        {
            private static bool Prefix()
            {
                if (!logged) { logged = true; RevivalMod.Log.Msg("[notifications] the game's phone notifications are not set up (no permission prompt)"); }
                return false;
            }
        }

        [HarmonyPatch(typeof(Il2CppLocalNotificationHandler.GameNotificationsManager), nameof(Il2CppLocalNotificationHandler.GameNotificationsManager.SendNotification))]
        private static class NoSend
        {
            private static bool Prefix(Il2CppLocalNotificationHandler.GameNotificationsManager __instance) => SetUp(__instance);
        }

        [HarmonyPatch(typeof(Il2CppLocalNotificationHandler.GameNotificationsManager), nameof(Il2CppLocalNotificationHandler.GameNotificationsManager.CancelAllNotifications))]
        private static class NoCancel
        {
            private static bool Prefix(Il2CppLocalNotificationHandler.GameNotificationsManager __instance) => SetUp(__instance);
        }

        private static bool SetUp(Il2CppLocalNotificationHandler.GameNotificationsManager m)
        {
            try { return (object)m != null && m.Initialized && m.Platform != null; } catch { return false; }
        }
    }
}
