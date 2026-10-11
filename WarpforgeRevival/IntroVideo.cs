using System;
using HarmonyLib;
using Il2Cpp;

namespace WarpforgeRevival
{
    /// <summary>
    /// Phones only. Stops the game's videos cleanly before it leaves a scene.
    ///
    /// The black menu on a Pixel 6 Pro (2026-10-10): the loading screen plays a video through the phone's
    /// hardware decoder (Exynos H.264 there). When the game switches to the menu it unloads the loading
    /// scene with the video still playing, so the decoder is torn down mid-frame ("allocateed 1 buffer after
    /// stop", "Tried to deallocate non dequeued buffer", "deallocate() ... was not successful"). In every run
    /// with that failed teardown the picture went black from that moment (5 of 5), and in runs where the
    /// decoder shut down cleanly it did not (4 of 5). Changing the render settings did not bring it back;
    /// only rebuilding the screen surface did (a resize, or locking the phone). Other phones use other
    /// decoders, which is why only that one showed it.
    ///
    /// So as soon as the game starts a scene change, every playing video is stopped first: the decoder then
    /// shuts down in order while its scene and picture still exist, and the scene is unloaded afterwards.
    /// The loading screen is behind the game's own fade at that point, so nothing visible changes.
    /// </summary>
    internal static class IntroVideo
    {
#if ANDROID_PORT
        internal static void StopVideos(string why)
        {
            try
            {
                int stopped = 0;
                var names = new System.Collections.Generic.List<string>();
                foreach (var vp in UnityEngine.Object.FindObjectsOfType<UnityEngine.Video.VideoPlayer>())
                {
                    if ((object)vp == null) continue;
                    bool playing = false;
                    try { playing = vp.isPlaying; } catch { }
                    if (!playing) continue;
                    vp.Stop();
                    stopped++;
                    names.Add(vp.name);
                }
                if (stopped > 0) RevivalMod.Log.Msg($"[video] stopped {stopped} playing video(s) before {why}: {string.Join(", ", names)}");
            }
            catch (Exception e) { RevivalMod.Log.Warning("[video] could not stop the videos: " + e.Message); }
        }

        // ---------------------------------------------------------------- test: no loading-screen video
        // The 0.12.54 runs showed the black picture can start while the loading video is still playing (21:16:39,
        // with the video stopped only at 21:16:46), and the decoder's teardown failed even after a proper Stop. So
        // the stop above does not prevent it. Test (0.12.55): on phones the loading screen's video is not played at
        // all, to see whether the black screens stop. If they do, the video path is the cause and this stays.
        private static float nextLook;
        private static int intoScene;

        internal static void Tick()
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now < nextLook) return;
            nextLook = now + 0.25f;
            try
            {
                string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                if (scene != "Intro") return;
                foreach (var vp in UnityEngine.Object.FindObjectsOfType<UnityEngine.Video.VideoPlayer>())
                {
                    if ((object)vp == null || !vp.isPlaying) continue;
                    vp.Stop();
                    intoScene++;
                    RevivalMod.Log.Msg($"[video] loading-screen video '{vp.name}' not played (test: does the black screen stop without it?) [{intoScene}]");
                }
            }
            catch (Exception e) { nextLook = now + 5f; RevivalMod.Log.Warning("[video] " + e.Message); }
        }

        [HarmonyPatch(typeof(EverguildSceneManager), nameof(EverguildSceneManager.LoadScene), new[] { typeof(string), typeof(string) })]
        private static class LoadFromTo
        {
            private static void Prefix(string from, string to) => StopVideos($"the scene change {from} -> {to}");
        }

        [HarmonyPatch(typeof(EverguildSceneManager), nameof(EverguildSceneManager.LoadScene), new[] { typeof(string) })]
        private static class LoadOne
        {
            private static void Prefix(string scene) => StopVideos("loading the scene " + scene);
        }

        [HarmonyPatch(typeof(EverguildSceneManager), nameof(EverguildSceneManager.UnloadScene))]
        private static class Unload
        {
            private static void Prefix(string scene) => StopVideos("unloading the scene " + scene);
        }
#endif
    }
}
