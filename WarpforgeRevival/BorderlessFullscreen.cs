#if !ANDROID_PORT
using System;
using System.Runtime.InteropServices;
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
    /// The game is built with Unity's "visible in background" off, so even borderless it minimizes
    /// itself when it loses focus (alt-tab). When that happens it is shown again behind the window
    /// the player switched to, without taking focus, the way borderless games usually behave.
    /// </summary>
    internal static class BorderlessFullscreen
    {
        private static bool startChecked, restoreLogged, wasFocused = true, restoredThisTime;
        private static float nextCheck, nextRestore, lostAt;
        private static IntPtr window;

        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int cmd);
        private const int SW_SHOWNOACTIVATE = 4;

        /// <summary>Called every frame from the mod's update loop.</summary>
        internal static void Tick()
        {
            KeepVisible();
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

        /// <summary>Borderless fullscreen and minimized while another program has focus: show it again, behind, unfocused.</summary>
        private static void KeepVisible()
        {
            float now = Time.realtimeSinceStartup;
            bool focused = Application.isFocused;
            if (focused) { wasFocused = true; return; }
            if (wasFocused) { wasFocused = false; lostAt = now; restoredThisTime = false; }
            // only the minimize that comes with losing focus; a minimize the player asks for later
            // (Win+D, the taskbar) is left alone
            if (restoredThisTime || now - lostAt > 2f || now < nextRestore) return;
            nextRestore = now + 0.25f;
            try
            {
                if (Screen.fullScreenMode != FullScreenMode.FullScreenWindow) return;
                if (window == IntPtr.Zero) window = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                if (window == IntPtr.Zero || !IsIconic(window)) return;
                ShowWindow(window, SW_SHOWNOACTIVATE);
                restoredThisTime = true;
                if (!restoreLogged) { restoreLogged = true; RevivalMod.Log.Msg("[screen] minimized on alt-tab: shown again behind the other window"); }
            }
            catch (Exception e) { nextRestore = now + 60f; RevivalMod.Log.Warning("[screen] " + e.Message); }
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
