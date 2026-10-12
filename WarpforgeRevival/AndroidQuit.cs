#if ANDROID_PORT
using System;
using HarmonyLib;

namespace WarpforgeRevival
{
    /// <summary>
    /// Settings > General > Quit. The game's own quit (PlayerDataManager.QuitApplication: tells its help
    /// desk, leaves any match room, then a coroutine that hands over to Java) leaves the app half-closed
    /// on Android: still in the recent apps, and on some devices (an AYN Thor) hard to swipe away. After
    /// the game's own steps, close the app the way the updater does (AndroidUpdater.CloseApp: finish the
    /// screen, remove it from the recent apps, end the process).
    /// </summary>
    [HarmonyPatch(typeof(Il2Cpp.PlayerDataManager), nameof(Il2Cpp.PlayerDataManager.QuitApplication))]
    internal static class AndroidQuit
    {
        private static void Postfix()
        {
            try
            {
                RevivalMod.Log.Msg("[quit] the game's Quit button: closing the app for real");
                RevivalMod.QuitGame();
            }
            catch (Exception e) { RevivalMod.Log.Warning("[quit] " + e.Message); }
        }
    }
}
#endif
