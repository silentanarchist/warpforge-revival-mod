#if ANDROID_PORT
using System;
using HarmonyLib;
using Il2Cpp;

namespace WarpforgeRevival
{
    /// <summary>
    /// Phone notifications (Android test, 0.13.31). The game sets up notification channels for the
    /// publisher's reminders when its player data starts (PlayerDataManager.Awake -> InitializeNotifications);
    /// on newer Android creating that first channel makes the phone ask "Allow notifications?", and a
    /// revival server never sends any. That one call is skipped, so no channel is made and nothing asks.
    /// The only other use of the notification manager (cancelling everything when an account is deleted)
    /// is skipped while it was never set up. The game's own popup on the main menu asking the player to
    /// turn notifications on ("... responses from Support and news ...") is not shown either.
    ///
    /// These hooks are applied on their own, after the mod's other hooks and each inside its own try
    /// (Apply): 0.13.29 put a notification hook among the others, and when one hook cannot be applied the
    /// ones after it are not applied either - every game then stopped before signing in.
    /// </summary>
    internal static class NoNotifications
    {
        internal static void Apply(HarmonyLib.Harmony harmony)
        {
            Try(harmony, typeof(PlayerDataManager), "InitializeNotifications", nameof(SkipSetup));
            Try(harmony, typeof(Il2CppLocalNotificationHandler.GameNotificationsManager), "CancelAllNotifications", nameof(SkipCancel));
            // the game's own "Please enable notifications to receive responses from Support ..." popup on the main menu
            Try(harmony, typeof(MainMenuWindow), "ShowAllowNotificationsPopup", nameof(SkipAsk));
            Try(harmony, typeof(MainMenuWindow), "ShouldShowAskForNotificationByTime", nameof(NeverAsk));
        }

        private static void Try(HarmonyLib.Harmony harmony, Type type, string method, string prefix)
        {
            try
            {
                var target = AccessTools.Method(type, method);
                if (target == null) { RevivalMod.Log.Warning($"[notifications] {type.Name}.{method} not found; left as it is"); return; }
                harmony.Patch(target, prefix: new HarmonyMethod(typeof(NoNotifications), prefix));
                RevivalMod.Log.Msg($"[notifications] {type.Name}.{method} hooked");
            }
            catch (Exception e) { RevivalMod.Log.Warning($"[notifications] could not hook {type.Name}.{method}; left as it is: {e.Message}"); }
        }

        private static bool skipped;

        private static bool SkipSetup()
        {
            skipped = true;
            RevivalMod.Log.Msg("[notifications] the game's phone notifications are not set up (no permission prompt)");
            return false;
        }

        private static bool SkipAsk()
        {
            RevivalMod.Log.Msg("[notifications] the game's \"Please enable notifications\" popup is not shown");
            return false;
        }

        private static bool NeverAsk(ref bool __result)
        {
            __result = false;
            return false;
        }

        private static bool SkipCancel(Il2CppLocalNotificationHandler.GameNotificationsManager __instance)
        {
            try { return !skipped && (object)__instance != null && __instance.Initialized; } catch { return false; }
        }
    }
}
#endif
