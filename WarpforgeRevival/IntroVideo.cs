using System;
using System.IO;
using HarmonyLib;
using Il2Cpp;

namespace WarpforgeRevival
{
    /// <summary>
    /// Phones only. The loading screen's video, and stopping videos cleanly before a scene change.
    ///
    /// The black menu on a Pixel 6 Pro (2026-10-10, 4 starts in about 20): the picture stays black after
    /// the loading screen while the game reports drawing normally; only rebuilding the screen surface
    /// (a resize, or locking the phone) brings it back. Stopping the video before the scene change
    /// (0.12.54), a WebM copy of the clip (0.12.57) and refreshing Unity's drawing state (0.12.58) did
    /// not stop it. The decoder's teardown error turned out to appear in good starts too. What is left
    /// to test is whether the video is involved at all, so the LoadingVideo setting has three modes:
    ///   Normal  - the game's own player, drawing straight onto the camera's far plane;
    ///   Texture - the player draws into a texture, shown through a plain UI image on the loading
    ///             screen's canvas, so the camera's far-plane video path is never used (candidate fix);
    ///   Off     - the video is stopped as soon as it starts (the test: the loading screen stays dark).
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
            DropTexture();
        }

        // ---------------------------------------------------------------- Texture mode
        private static UnityEngine.RenderTexture texture;
        private static UnityEngine.GameObject textureImage;

        private static string Mode()
        {
            try { return (RevivalMod.Config.LoadingVideo ?? "").Trim(); } catch { return ""; }
        }

        private static void DropTexture()
        {
            try
            {
                if ((object)textureImage != null) { UnityEngine.Object.Destroy(textureImage); textureImage = null; }
                if ((object)texture != null) { texture.Release(); UnityEngine.Object.Destroy(texture); texture = null; }
            }
            catch { textureImage = null; texture = null; }
        }

        /// <summary>Points the player at a texture and shows that texture through a UI image behind the loading screen's text.</summary>
        private static bool ThroughTexture(UnityEngine.Video.VideoPlayer vp)
        {
            UnityEngine.Canvas canvas = null;
            foreach (var c in UnityEngine.Object.FindObjectsOfType<UnityEngine.Canvas>())
            {
                if ((object)c == null || !c.isActiveAndEnabled) continue;
                try { if (c.gameObject.scene.name != "Intro") continue; } catch { continue; }
                if ((object)c.rootCanvas != null && c.rootCanvas.GetInstanceID() != c.GetInstanceID()) continue;
                canvas = c; break;
            }
            if ((object)canvas == null) { RevivalMod.Log.Warning("[video] Texture mode: no canvas in the loading scene; the game's own player is left as it is"); return false; }

            int w = 1440, h = 810;
            try { var clip = vp.clip; if ((object)clip != null && clip.width > 0 && clip.height > 0) { w = (int)clip.width; h = (int)clip.height; } } catch { }
            DropTexture();
            texture = new UnityEngine.RenderTexture(w, h, 0);
            texture.name = "WarpforgeRevival intro video";
            texture.Create();

            var go = new UnityEngine.GameObject("WarpforgeRevival intro video");
            go.layer = canvas.gameObject.layer;
            go.transform.SetParent(canvas.transform, false);
            go.transform.SetAsFirstSibling();                       // drawn first: behind the loading text
            var img = go.AddComponent<UnityEngine.UI.RawImage>();
            img.texture = texture;
            img.raycastTarget = false;
            // Fit inside the screen keeping the clip's shape, centred (what the game's player does: FitInside).
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new UnityEngine.Vector2(0.5f, 0.5f);
            rt.pivot = new UnityEngine.Vector2(0.5f, 0.5f);
            var size = ((UnityEngine.RectTransform)canvas.transform).rect.size;
            float scale = Math.Min(size.x / w, size.y / h);
            rt.sizeDelta = new UnityEngine.Vector2(w * scale, h * scale);
            rt.anchoredPosition = UnityEngine.Vector2.zero;
            textureImage = go;

            vp.Stop();
            vp.renderMode = UnityEngine.Video.VideoRenderMode.RenderTexture;
            vp.targetTexture = texture;
            vp.targetCamera = null;
            return true;
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
                string mode = Mode();
                string webm = WebmPath();
                if (webm == null && !mode.Equals("Texture", StringComparison.OrdinalIgnoreCase) && !mode.Equals("Off", StringComparison.OrdinalIgnoreCase)) { swapped = true; return; }
                foreach (var vp in UnityEngine.Object.FindObjectsOfType<UnityEngine.Video.VideoPlayer>())
                {
                    if ((object)vp == null) continue;
                    if (vp.source == UnityEngine.Video.VideoSource.Url) { swapped = true; continue; }
                    bool wasPlaying = vp.isPlaying;
                    if (mode.Equals("Off", StringComparison.OrdinalIgnoreCase))
                    {
                        vp.Stop();
                        try { vp.targetCamera = null; } catch { }
                        swapped = true;
                        if (!swapLogged) { swapLogged = true; RevivalMod.Log.Msg($"[video] '{vp.name}' stopped: LoadingVideo = Off (test mode, the loading screen stays dark)"); }
                        continue;
                    }
                    bool viaTexture = mode.Equals("Texture", StringComparison.OrdinalIgnoreCase) && ThroughTexture(vp);
                    if (webm != null)
                    {
                        vp.Stop();
                        vp.source = UnityEngine.Video.VideoSource.Url;
                        vp.url = webm;
                    }
                    vp.Play();
                    swapped = true;
                    if (!swapLogged)
                    {
                        swapLogged = true;
                        RevivalMod.Log.Msg($"[video] '{vp.name}' plays {(webm != null ? "the WebM copy" : "the game's clip")}{(viaTexture ? " through a texture (LoadingVideo = Texture)" : "")}{(wasPlaying ? " (the clip had started)" : "")}");
                    }
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
