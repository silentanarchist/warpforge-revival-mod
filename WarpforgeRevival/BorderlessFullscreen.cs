#if !ANDROID_PORT
using System;
using HarmonyLib;
using Il2Cpp;
using UnityEngine;

namespace WarpforgeRevival
{
    /// <summary>
    /// PC only. The game's "Fullscreen" (Settings > window mode) can end up as exclusive fullscreen,
    /// which minimizes the game on alt-tab. Fullscreen is made borderless instead (Unity's
    /// FullScreenWindow): a window the size of the screen, so alt-tab just switches away.
    /// Both when the player picks Fullscreen, and at start-up if the game comes up exclusive
    /// (Unity remembers the last mode in the registry). Windowed stays windowed.
    /// </summary>
    internal static class BorderlessFullscreen
    {
        private static bool startChecked;
        private static float nextCheck;

        /// <summary>Called every frame from the mod's update loop; checks a few times after start-up.</summary>
        internal static void Tick()
        {
            if (startChecked) return;
            float now = Time.realtimeSinceStartup;
            if (now < nextCheck) return;
            nextCheck = now + 2f;
            try
            {
                var mode = Screen.fullScreenMode;
                if (mode == FullScreenMode.ExclusiveFullScreen)
                {
                    var r = Screen.currentResolution;
                    Screen.SetResolution(r.width, r.height, FullScreenMode.FullScreenWindow);
                    RevivalMod.Log.Msg($"[screen] exclusive fullscreen switched to borderless ({r.width}x{r.height})");
                }
                else RevivalMod.Log.Msg($"[screen] window mode at start: {mode}");
                startChecked = true;
            }
            catch (Exception e) { startChecked = true; RevivalMod.Log.Warning("[screen] " + e.Message); }
        }

        // The game's own switch: SetFullScreen(true) asked for the player-settings default, which can be exclusive.
        [HarmonyPatch(typeof(ResolutionManager), nameof(ResolutionManager.SetFullScreen))]
        private static class Switch
        {
            private static bool Prefix(bool fullScreen)
            {
                try
                {
                    if (!fullScreen) return true;                // windowed: the game's own code
                    var r = Screen.currentResolution;
                    Screen.SetResolution(r.width, r.height, FullScreenMode.FullScreenWindow);
                    RevivalMod.Log.Msg($"[screen] fullscreen (borderless) {r.width}x{r.height}");
                    return false;
                }
                catch (Exception e) { RevivalMod.Log.Warning("[screen] " + e.Message); return true; }
            }
        }
    }
}
#endif
