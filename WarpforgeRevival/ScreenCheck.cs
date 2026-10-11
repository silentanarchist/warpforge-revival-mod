using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace WarpforgeRevival
{
    /// <summary>
    /// Phones only, logging only: finds out what is really on screen while a black screen is being
    /// chased (Pixel 6 Pro, 2026-10-10: the game loads and plays its music, but the screen stays black).
    /// At set moments of a start-up (and a few seconds after each scene change) the picture the game
    /// has just drawn is read back, its brightness noted in the log and a small copy saved as
    /// UserData/WarpforgeRevival/screens/screen-NN.bmp (collected by "0 - get phone logs.bat").
    /// A black copy means the game itself draws black; a normal one while the phone shows black means
    /// the picture is lost between the game and the screen. Any full-screen dark layer of the menus
    /// (for example a fade that never fades back) is named in the log too.
    /// </summary>
    internal static class ScreenCheck
    {
        private static readonly float[] At = { 6f, 12f, 20f, 30f, 45f, 60f, 90f, 120f };
        private const int MaxShots = 14;
        private const int SmallW = 160, SmallH = 74;

        private static int nextAt, shots;
        private static float sceneCheckAt = -1f;
        private static string lastScene;
        private static bool due, broken;
        private static string reason;
        private static string dir;

        // ---------------------------------------------------------------- the loader's .NET

        /// <summary>True when the loader failed to load parts of .NET this start-up (the mod cannot work).</summary>
        internal static bool RuntimeBroken { get; private set; }
        private static string runtimeWhy;

        /// <summary>
        /// Once, before anything else: the parts of .NET the mod needs (encryption, the web client) can be
        /// loaded. On a Pixel 6 Pro one start-up in several failed to load them ("Method not found:
        /// SHA256.Create", "Failure has occurred while loading a type"); the mod then cannot reach the
        /// server and the game sat on a black loading screen. Now it says so on screen instead.
        /// </summary>
        internal static void CheckRuntime()
        {
            try { Probe(); }
            catch (Exception e)
            {
                RuntimeBroken = true;
                runtimeWhy = e.GetType().Name + ": " + e.Message;
                try { RevivalMod.Log.Error("[startup] the loader could not load parts of .NET this time (" + runtimeWhy + "); the mod cannot work. Close the game fully and start it again."); } catch { }
            }
        }

        // a method of its own: a missing part fails when this method is prepared, which the caller catches
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void Probe()
        {
            using (var sha = System.Security.Cryptography.SHA256.Create()) sha.ComputeHash(new byte[] { 1 });
            using (var h = new System.Net.Http.SocketsHttpHandler()) { }
            _ = System.Text.Json.JsonDocument.Parse("{}");
        }

        private static GUIStyle brokenStyle;

        private static void DrawBroken()
        {
            try { if (Event.current == null || Event.current.type != EventType.Repaint) return; } catch { return; }
            float w = Screen.width, h = Screen.height;
            brokenStyle ??= new GUIStyle(GUI.skin.label) { fontSize = Math.Max(24, (int)(h / 24)), wordWrap = true, alignment = TextAnchor.MiddleCenter };
            brokenStyle.normal.textColor = Color.white;
            GUI.Box(new Rect(w * 0.1f, h * 0.3f, w * 0.8f, h * 0.4f), "");
            GUI.Label(new Rect(w * 0.12f, h * 0.32f, w * 0.76f, h * 0.36f),
                "Warpforge Revival could not start properly this time (the mod loader failed to load part of .NET).\n\n" +
                "Close the game fully (swipe it away) and open it again.", brokenStyle);
        }

        private static string Scene()
        {
            try { return UnityEngine.SceneManagement.SceneManager.GetActiveScene().name; } catch { return "?"; }
        }

        // ---------------------------------------------------------------- black picture: find the cause
        // On the Pixel 6 Pro the game sometimes draws nothing but black once the menu loads (screenshots:
        // brightness 0.000, no layer on top) until the phone is locked and unlocked, which briefly changes the
        // drawing area's size (1440 -> 1441 -> 1440 lines). Resizing does bring the picture back, but it does not
        // say why it went. So when two checks in a row find the picture black while the game has focus, the
        // drawing setup is written to the log, and the likely causes are tried one at a time, each checked after
        // 1.5 s and undone if the picture stays black:
        //   1. Unity's drawing state cache thrown away (GpuState): invisible, one frame;
        //   2. the drawing area made 2 lines smaller and put back (what locking the phone does);
        //   3. HDR colour off.
        // Render scale and anti-aliasing changes were steps 1-2 until 0.12.57 and never helped (9 tries).
        // The first step that brings the picture back is logged as FOUND and kept. Once per start-up.
        private static int blackInRow, step = -1;
        private static bool stepsDone, menuStateLogged;
        private static float restoreAt = -1f, recheckAt = -1f;
        private static int fixW, fixH;
        private static int savedHdr = -1;
        private static readonly string[] StepNames = { "drawing state cache cleared", "drawing area 2 lines smaller for a moment", "HDR colour off" };

        /// <summary>Called every frame from the mod's update loop.</summary>
        internal static void Tick()
        {
#if ANDROID_PORT
            if (broken) return;
            try
            {
                float now = Time.realtimeSinceStartup;
                if (restoreAt > 0 && now >= restoreAt) { restoreAt = -1f; RestoreSize(); recheckAt = now + 1.5f; }
                if (due) return;
                if (recheckAt > 0 && now >= recheckAt) { recheckAt = -1f; due = true; reason = "check after trying a cause"; return; }
                if (shots >= MaxShots) return;
                string scene = Scene();
                if (scene != lastScene)
                {
                    lastScene = scene;
                    sceneCheckAt = now + 3f;
                }
                if (sceneCheckAt > 0 && now >= sceneCheckAt) { sceneCheckAt = -1f; due = true; reason = "3 s into scene " + scene; return; }
                if (nextAt < At.Length && now >= At[nextAt]) { due = true; reason = $"{At[nextAt]:0} s after start"; nextAt++; }
            }
            catch { }
#endif
        }

        /// <summary>Called from the mod's OnGUI: the picture is complete when the GUI repaints.</summary>
        internal static void OnGUI()
        {
            if (RuntimeBroken) { try { DrawBroken(); } catch { } return; }
#if ANDROID_PORT
            if (!due || broken) return;
            try { if (Event.current == null || Event.current.type != EventType.Repaint) return; } catch { return; }
            due = false;
            shots++;
            string scene = Scene();
            string picture = Picture(Math.Min(shots, 30));
            string layers = DarkLayers();
            bool focused = Application.isFocused;
            RevivalMod.Log.Msg($"[screen] {reason} (scene {scene}, {Screen.width}x{Screen.height}, focused {focused}): {picture}; {Cameras()}; {layers}");
            bool black = picture.StartsWith("picture BLACK");
            if (!black && !menuStateLogged && scene.StartsWith("MainMenu")) { menuStateLogged = true; RevivalMod.Log.Msg("[screen] drawing setup with a normal picture: " + State()); }
            if (step >= 0 && !stepsDone)
            {
                // the check after trying a cause
                if (!black) { stepsDone = true; RevivalMod.Log.Msg($"[screen] FOUND: the picture came back after step {step + 1} ({StepNames[step]}); kept. Drawing setup now: " + State()); blackInRow = 0; return; }
                RevivalMod.Log.Msg($"[screen] still black after step {step + 1} ({StepNames[step]}); undone");
                Undo(step);
                NextStep();
                return;
            }
            if (!black || !focused) { if (blackInRow > 0 && !black) RevivalMod.Log.Msg("[screen] the picture is back by itself"); blackInRow = 0; return; }
            blackInRow++;
            if (blackInRow < 2) { recheckAt = Time.realtimeSinceStartup + 2f; return; }      // look again before acting
            if (stepsDone || step >= 0) return;
            RevivalMod.Log.Msg("[screen] the picture is black; drawing setup: " + State());
            NextStep();
#endif
        }

#if ANDROID_PORT
        private static UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset Urp()
        {
            try
            {
                var rp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
                return (object)rp == null ? null : rp.TryCast<UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset>();
            }
            catch { return null; }
        }

        private static void NextStep()
        {
            var urp = Urp();
            while (++step < StepNames.Length)
            {
                try
                {
                    switch (step)
                    {
                        case 0:
                            if (!GpuState.Refresh("the picture is black")) continue;
                            break;
                        case 1:
                            fixW = Screen.width; fixH = Screen.height;
                            Screen.SetResolution(fixW, fixH - 2, true);      // the bool form: Screen.fullScreenMode is not available on phones
                            restoreAt = Time.realtimeSinceStartup + 0.5f;
                            RevivalMod.Log.Msg($"[screen] trying step {step + 1}: {StepNames[step]} ({fixW}x{fixH} -> {fixW}x{fixH - 2})");
                            return;
                        case 2:
                            if ((object)urp == null || !urp.supportsHDR) { RevivalMod.Log.Msg("[screen] step 3 skipped: HDR is already off"); continue; }
                            savedHdr = 1;
                            urp.supportsHDR = false;
                            break;
                    }
                    RevivalMod.Log.Msg($"[screen] trying step {step + 1}: {StepNames[step]}");
                    recheckAt = Time.realtimeSinceStartup + 1.5f;
                    return;
                }
                catch (Exception e) { RevivalMod.Log.Warning($"[screen] step {step + 1} ({StepNames[step]}) could not be tried: {e.Message}"); }
            }
            stepsDone = true;
            RevivalMod.Log.Warning("[screen] nothing tried brought the picture back; lock and unlock the phone");
        }

        private static void Undo(int which)
        {
            try
            {
                var urp = Urp();
                switch (which)
                {
                    case 2: if ((object)urp != null && savedHdr == 1) urp.supportsHDR = true; break;
                }
            }
            catch (Exception e) { RevivalMod.Log.Warning("[screen] could not undo a step: " + e.Message); }
        }

        private static void RestoreSize()
        {
            try { Screen.SetResolution(fixW, fixH, true); RevivalMod.Log.Msg($"[screen] drawing area back to {fixW}x{fixH}"); }
            catch (Exception e) { RevivalMod.Log.Warning("[screen] could not put the drawing area back: " + e.Message); }
        }

        /// <summary>Everything about how the game draws that could leave the picture black, each read on its own.</summary>
        private static string State()
        {
            var parts = new List<string>();
            void Add(string name, Func<object> read)
            {
                try { parts.Add(name + " " + (read() ?? "null")); } catch (Exception e) { parts.Add(name + " ?(" + e.GetType().Name + ")"); }
            }
            // Only engine calls that are real game methods are read here. QualitySettings (level, names, vsync) are
            // stripped from the game and recreated by the loader as engine calls; touching that class made the
            // loader's .NET abort ("mono_class_from_mono_type_internal: implement me", 0.12.52 on a Pixel 6 Pro).
            // The quality level is in the game's own log ("Graphics quality loaded ...").
            var urp = Urp();
            Add("pipeline", () => (object)urp == null ? "not URP" : urp.name);
            if ((object)urp != null)
            {
                Add("renderScale", () => urp.renderScale.ToString("0.00"));
                Add("msaa", () => urp.msaaSampleCount);
                Add("hdr", () => urp.supportsHDR);
                Add("depthTexture", () => urp.supportsCameraDepthTexture);
                Add("opaqueTexture", () => urp.supportsCameraOpaqueTexture);
                Add("upscaling", () => urp.upscalingFilter);
            }
            Add("screen", () => Screen.width + "x" + Screen.height);
            Add("display render", () => Display.main.renderingWidth + "x" + Display.main.renderingHeight);
            Add("display system", () => Display.main.systemWidth + "x" + Display.main.systemHeight);
            Add("activeRT", () => (object)RenderTexture.active == null ? "screen" : RenderTexture.active.name);
            Add("fps target", () => Application.targetFrameRate);
            try
            {
                foreach (var c in Camera.allCameras)
                {
                    if ((object)c == null) continue;
                    var cam = c;
                    Add("camera '" + cam.name + "'", () => $"enabled {cam.enabled}, depth {cam.depth}, rect {cam.rect}, pixels {cam.pixelRect}, clear {cam.clearFlags}, target {((object)cam.targetTexture == null ? "screen" : cam.targetTexture.name)}, hdr {cam.allowHDR}, msaa {cam.allowMSAA}, cull {cam.cullingMask}");
                }
            }
            catch (Exception e) { parts.Add("cameras ?(" + e.Message + ")"); }
            return string.Join("; ", parts);
        }
#endif

#if ANDROID_PORT
        private static string Picture(int n)
        {
            Texture2D full = null;
            try
            {
                int w = Screen.width, h = Screen.height;
                if (w <= 0 || h <= 0) return "no drawing area";
                full = new Texture2D(w, h, TextureFormat.RGB24, false);
                full.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
                // the small copy is written as a plain BMP by hand: Unity's own PNG encoder aborts the
                // loader's .NET on this phone (0.12.44 crashed at the first screenshot)
                int rowBytes = (SmallW * 3 + 3) & ~3;
                var bmp = new byte[54 + rowBytes * SmallH];
                double sum = 0;
                int lit = 0;
                float max = 0f;
                for (int y = 0; y < SmallH; y++)
                    for (int x = 0; x < SmallW; x++)
                    {
                        var c = full.GetPixel(x * w / SmallW, y * h / SmallH);
                        int o = 54 + y * rowBytes + x * 3;     // BMP rows run bottom-up, like the texture's
                        bmp[o] = (byte)(Mathf.Clamp01(c.b) * 255f);
                        bmp[o + 1] = (byte)(Mathf.Clamp01(c.g) * 255f);
                        bmp[o + 2] = (byte)(Mathf.Clamp01(c.r) * 255f);
                        float l = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
                        sum += l;
                        if (l > 0.08f) lit++;
                        if (l > max) max = l;
                    }
                string saved = "";
                try
                {
                    void Put(int at, int v) { bmp[at] = (byte)v; bmp[at + 1] = (byte)(v >> 8); bmp[at + 2] = (byte)(v >> 16); bmp[at + 3] = (byte)(v >> 24); }
                    bmp[0] = (byte)'B'; bmp[1] = (byte)'M';
                    Put(2, bmp.Length); Put(10, 54); Put(14, 40); Put(18, SmallW); Put(22, SmallH);
                    bmp[26] = 1; bmp[28] = 24; Put(34, rowBytes * SmallH);
                    dir ??= System.IO.Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "WarpforgeRevival", "screens");
                    Directory.CreateDirectory(dir);
                    File.WriteAllBytes(System.IO.Path.Combine(dir, $"screen-{n:00}.bmp"), bmp);
                    saved = $", saved screen-{n:00}.bmp";
                }
                catch (Exception e) { saved = ", not saved (" + e.Message + ")"; }
                double avg = sum / (SmallW * SmallH);
                int pct = (int)Math.Round(100.0 * lit / (SmallW * SmallH));
                return $"picture {(avg < 0.02 && max < 0.08 ? "BLACK" : "drawn")} (brightness {avg:0.000}, brightest {max:0.00}, {pct}% lit{saved})";
            }
            catch (Exception e)
            {
                broken = true;     // reading the picture back is not possible on this device: stop trying
                return "picture could not be read back: " + e.GetType().Name + ": " + e.Message;
            }
            finally
            {
                try { if ((object)full != null) UnityEngine.Object.Destroy(full); } catch { }
            }
        }

        private static string Cameras()
        {
            try
            {
                var names = new List<string>();
                foreach (var c in Camera.allCameras)
                    if ((object)c != null && c.isActiveAndEnabled) names.Add($"{c.name} (depth {c.depth}{((object)c.targetTexture != null ? ", into a texture" : "")})");
                return names.Count == 0 ? "NO active cameras" : "cameras: " + string.Join(", ", names);
            }
            catch (Exception e) { return "cameras unknown: " + e.Message; }
        }

        /// <summary>Full-screen, opaque, dark menu layers (the likely cover of a black screen).</summary>
        private static string DarkLayers()
        {
            try
            {
                var found = new List<(int order, string text)>();
                var corners = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(4);
                foreach (var canvas in UnityEngine.Object.FindObjectsOfType<Canvas>())
                {
                    if ((object)canvas == null || !canvas.isActiveAndEnabled || !canvas.isRootCanvas) continue;
                    if (canvas.renderMode == RenderMode.WorldSpace) continue;
                    foreach (var g in canvas.GetComponentsInChildren<UnityEngine.UI.Graphic>(false))
                    {
                        if ((object)g == null || !g.isActiveAndEnabled) continue;
                        var col = g.color;
                        float alpha = col.a;
                        try { var cr = g.canvasRenderer; if ((object)cr != null) alpha *= cr.GetAlpha(); } catch { }
                        if (alpha < 0.9f) continue;
                        if (0.2126f * col.r + 0.7152f * col.g + 0.0722f * col.b > 0.1f) continue;
                        g.rectTransform.GetWorldCorners(corners);
                        float wpx = Math.Abs(corners[2].x - corners[0].x), hpx = Math.Abs(corners[2].y - corners[0].y);
                        if (canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                        {
                            var cam = canvas.worldCamera;
                            if ((object)cam == null) continue;
                            var a = cam.WorldToScreenPoint(corners[0]);
                            var b = cam.WorldToScreenPoint(corners[2]);
                            wpx = Math.Abs(b.x - a.x); hpx = Math.Abs(b.y - a.y);
                        }
                        if (wpx < Screen.width * 0.9f || hpx < Screen.height * 0.9f) continue;
                        found.Add((canvas.sortingOrder, $"'{Where(g.transform)}' (canvas '{canvas.name}' order {canvas.sortingOrder}, alpha {alpha:0.00})"));
                    }
                }
                if (found.Count == 0) return "no full-screen dark layer";
                found.Sort((x, y) => y.order.CompareTo(x.order));
                var top = new List<string>();
                for (int i = 0; i < found.Count && i < 4; i++) top.Add(found[i].text);
                return $"full-screen dark layers ({found.Count}): " + string.Join(", ", top);
            }
            catch (Exception e) { return "layers unknown: " + e.Message; }
        }

        private static string Where(Transform t)
        {
            var parts = new List<string>();
            for (int i = 0; (object)t != null && i < 4; i++, t = t.parent) parts.Insert(0, t.name);
            return string.Join("/", parts);
        }
#endif
    }
}
