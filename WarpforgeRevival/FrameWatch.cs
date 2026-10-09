using System;
using UnityEngine;

namespace WarpforgeRevival
{
    /// <summary>
    /// Notes in the log what the screen is going through, to track down screens that stay black or
    /// freeze (seen on a Pixel 6 after leaving the game while it switched from loading to the menu,
    /// and on PC after long tab-outs): stretches with no frames, the game losing or getting back
    /// focus, and the drawing area changing size. Logging only; nothing is changed.
    /// </summary>
    internal static class FrameWatch
    {
        private const float GapSeconds = 1.5f;

        private static float lastFrame;
        private static bool focused = true, started;
        private static int width, height;

        private static string Scene()
        {
            try { return UnityEngine.SceneManagement.SceneManager.GetActiveScene().name; } catch { return "?"; }
        }

        /// <summary>Called every frame from the mod's update loop.</summary>
        internal static void Tick()
        {
            try
            {
                float now = Time.realtimeSinceStartup;
                bool f = Application.isFocused;
                int w = Screen.width, h = Screen.height;
                if (!started)
                {
                    started = true; lastFrame = now; focused = f; width = w; height = h;
                    return;
                }
                float gap = now - lastFrame;
                lastFrame = now;
                if (gap >= GapSeconds)
                    RevivalMod.Log.Msg($"[frames] no frame for {gap:0.0} s (scene {Scene()}, focused {f})");
                if (f != focused)
                {
                    focused = f;
                    RevivalMod.Log.Msg($"[frames] game {(f ? "has focus again" : "lost focus")} (scene {Scene()}, {w}x{h})");
                }
                if (w != width || h != height)
                {
                    RevivalMod.Log.Msg($"[frames] drawing area {width}x{height} -> {w}x{h} (scene {Scene()})");
                    width = w; height = h;
                }
            }
            catch { }
        }
    }
}
