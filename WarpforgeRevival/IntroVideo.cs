using System;
using System.IO;
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

        // ---------------------------------------------------------------- the loading-screen video
        // Playing the loading screen's H.264 clip goes through the phone's hardware decoder and the screen
        // surface, and on a Pixel 6 Pro that path leaves the picture black (0.12.52-0.12.55 logs). A VP8
        // WebM copy of the same clip is decoded by Unity itself, in software, and never touches that path.
        // "7 - fix loading video.bat" makes that copy from the game's own files and puts it on the phone
        // as UserData/WarpforgeRevival/content/intro.webm. When that file exists, the loading screen's
        // player is pointed at it instead of the built-in clip; otherwise the clip plays as it always did.
        // Deleting the file puts the original back.
        private static float nextLook;
        private static string webmPath;
        private static bool webmChecked, webmExists, swapped, swapLogged;

        private static string WebmPath()
        {
            if (!webmChecked)
            {
                webmChecked = true;
                try
                {
                    webmPath = Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "WarpforgeRevival", "content", "intro.webm");
                    webmExists = File.Exists(webmPath) && new FileInfo(webmPath).Length > 100_000;
                    RevivalMod.Log.Msg(webmExists ? "[video] loading-screen video: the WebM copy on this phone will be used (content/intro.webm)"
                                                  : "[video] loading-screen video: the game's own clip (no content/intro.webm on this phone)");
                }
                catch (Exception e) { RevivalMod.Log.Warning("[video] " + e.Message); }
            }
            return webmExists ? webmPath : null;
        }

        // After the loading scene is gone (its video player destroyed with it), Unity's drawing state cache
        // is refreshed a few times over the next seconds (see GpuState), covering the scene unload, the
        // transition and the menu's first draws. Cheap, invisible, and so far the only thing short of a
        // surface rebuild that could stop the black menu.
        private static string lastScene;
        private static int refreshesLeft;
        private static float refreshAt;

        internal static void Tick()
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (refreshesLeft > 0 && now >= refreshAt)
            {
                refreshesLeft--;
                refreshAt = now + 0.75f;
                if (!GpuState.Refresh($"{3 - refreshesLeft} of 3 after the loading scene closed")) refreshesLeft = 0;
            }
            if (now < nextLook) return;
            nextLook = now + 0.25f;
            try
            {
                string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                if (scene != lastScene)
                {
                    if (lastScene == "Intro") { refreshesLeft = 3; refreshAt = now + 0.25f; }
                    lastScene = scene;
                }
                if (scene != "Intro") { swapped = false; return; }
                if (swapped) return;
                string webm = WebmPath();
                if (webm == null) { swapped = true; return; }
                foreach (var vp in UnityEngine.Object.FindObjectsOfType<UnityEngine.Video.VideoPlayer>())
                {
                    if ((object)vp == null) continue;
                    if (vp.source == UnityEngine.Video.VideoSource.Url) { swapped = true; continue; }
                    bool wasPlaying = vp.isPlaying;
                    vp.Stop();
                    vp.source = UnityEngine.Video.VideoSource.Url;
                    vp.url = webm;
                    vp.Play();
                    swapped = true;
                    if (!swapLogged) { swapLogged = true; RevivalMod.Log.Msg($"[video] '{vp.name}' now plays the WebM copy{(wasPlaying ? " (the clip had started)" : "")}"); }
                }
            }
            catch (Exception e) { nextLook = now + 5f; swapped = true; RevivalMod.Log.Warning("[video] could not switch the loading-screen video: " + e.Message); }
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
