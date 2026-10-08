#if ANDROID_PORT
using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Il2Cpp;
using Il2CppInterop.Runtime;

namespace WarpforgeRevival
{
    /// <summary>
    /// Keeps the Android mod up to date from the revival server (GET /api/v1/mod/manifest?platform=android).
    ///
    /// One way of updating, the same as on Windows: when the server has a newer build - seen by the
    /// check at start-up, or because the server refuses to sign in an outdated mod - the game's
    /// sign-in window says so while the game loads, the build is downloaded, checked against the
    /// server's checksum and put in place, and the game closes; the next start uses it. Unlike on
    /// Windows no helper script is needed: a file that is in use can be replaced here.
    /// AutoUpdate setting: Off never checks; anything else updates.
    /// Only the mod is updated this way. The loader is part of the installed app and cannot replace itself.
    /// </summary>
    internal static class AndroidUpdater
    {
        private const string FileName = "WarpforgeRevival.Android.dll";

        /// <summary>Where MelonLoader loaded this mod from (set at start-up; empty when unknown).</summary>
        internal static string LoadedFrom = "";

        private static string server;
        private static Task<bool> running;
        private static readonly object Gate = new object();

        private static bool UpdatesOff => (RevivalMod.Config.AutoUpdate ?? "").Trim().Equals("Off", StringComparison.OrdinalIgnoreCase);

        public static void Start(string serverUrl)
        {
            server = serverUrl;
            if (UpdatesOff) return;
            Task.Run(async () =>
            {
                try
                {
                    var m = await Manifest();
                    if (m == null) { RevivalMod.Log.Msg("[update] the server has no Android mod to offer"); return; }
                    if (!m.Value.newer)
                    {
                        RevivalMod.Log.Msg($"[update] mod is up to date (installed {RevivalMod.Version}, server has {m.Value.label})");
                        return;
                    }
                    RevivalMod.Log.Msg($"[update] server has mod {m.Value.label} (installed {RevivalMod.Version})");
                    await Run("A newer Warpforge Revival mod is available from the server. Downloading it...");
                }
                catch (Exception e) { RevivalMod.Log.Warning("[update] update check failed: " + e.Message); }
            });
        }

        /// <summary>The server refused to sign in because this mod is out of date: update now.</summary>
        internal static Task<bool> UpdateNow() => Run("This server needs a newer Warpforge Revival mod. Downloading it...");

        /// <summary>One update at a time; a failed one may be tried again.</summary>
        private static Task<bool> Run(string message)
        {
            if (UpdatesOff || server == null) return Task.FromResult(false);
            lock (Gate)
            {
                if (running == null || (running.IsCompleted && !running.Result))
                    running = Task.Run(() => Update(message));
                return running;
            }
        }

        private struct Offer { public bool newer; public string label, sha, url; }

        private static async Task<Offer?> Manifest()
        {
            using var http = Net.Client(TimeSpan.FromSeconds(Math.Max(30, RevivalMod.Config.TimeoutSeconds)));
            using var manifest = JsonDocument.Parse(await http.GetStringAsync(server + "/api/v1/mod/manifest?platform=android"));
            var root = manifest.RootElement;
            if (!root.TryGetProperty("available", out var av) || !av.GetBoolean()) return null;
            // "version" stays in the form older builds understand; "label" is the name shown to people
            string number = root.GetProperty("version").GetString();
            var rv = RevivalMod.Number(number); var lv = RevivalMod.Number(RevivalMod.Version);
            return new Offer
            {
                newer = rv != null && lv != null && rv > lv,
                label = root.TryGetProperty("label", out var lb) && lb.ValueKind == JsonValueKind.String ? lb.GetString() : number,
                sha = root.GetProperty("sha256").GetString(),
                url = root.GetProperty("url").GetString()
            };
        }

        /// <summary>Downloads the newer build, puts it in place, and lets the sign-in window close the game.</summary>
        private static async Task<bool> Update(string message)
        {
            GameSignIn.Updating(message);
            try
            {
                var offer = await Manifest();
                if (offer == null || !offer.Value.newer) throw new InvalidOperationException("the server has no newer mod to offer");
                string remote = offer.Value.label;
                string mods = MelonLoader.Utils.MelonEnvironment.ModsDirectory;
                string dll = !string.IsNullOrEmpty(LoadedFrom) && File.Exists(LoadedFrom)
                             && string.Equals(Path.GetFileName(LoadedFrom), FileName, StringComparison.OrdinalIgnoreCase)
                    ? Path.GetFullPath(LoadedFrom)
                    : Path.Combine(mods, FileName);
                string fresh = dll + ".new";

                using var http = Net.Client(TimeSpan.FromSeconds(120));
                var data = await http.GetByteArrayAsync(server + offer.Value.url);
                string got = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
                if (got != offer.Value.sha) throw new InvalidDataException("downloaded mod does not match the server's checksum");

                File.WriteAllBytes(fresh, data);
                // Put in place in one step; the running game keeps using the copy it already loaded.
                File.Move(fresh, dll, true);
                RevivalMod.Log.Msg($"[update] mod {remote} installed at {dll}; it is used from the next start of the game");
                // the sign-in window counts down and closes the app for real (CloseApp)
                GameSignIn.Updated($"Updated to {remote}. Open the game again to use it.", 5f);
                return true;
            }
            catch (Exception e)
            {
                RevivalMod.Log.Warning("[update] could not update the mod: " + e.Message);
                GameSignIn.UpdateFailed();
                return false;
            }
        }

        /// <summary>Called every frame on the main thread: the fallback that ends the process after CloseApp.</summary>
        public static void Tick()
        {
            if (closeAt > 0f && UnityEngine.Time.realtimeSinceStartup >= closeAt) { closeAt = -1f; EndProcess(); }
        }

        // ---- closing the app for real ----
        // Ending the process alone is not enough: Android keeps the app's screen in its list of recent
        // apps and starts it again from that leftover state, and that restart has been seen to break
        // the game (errors from its own code before login). So first ask Android to finish the screen
        // and remove it from the recent apps, the way a real exit does, and only then end the process.
        private static float closeAt = -1f;

        [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "getpid")] private static extern int getpid();
        [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "kill")] private static extern int kill(int pid, int sig);

        internal static void CloseApp()
        {
            try
            {
                var none = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<UnityEngine.jvalue>(0);
                IntPtr player = UnityEngine.AndroidJNI.FindClass("com/unity3d/player/UnityPlayer");
                IntPtr field = UnityEngine.AndroidJNI.GetStaticFieldID(player, "currentActivity", "Landroid/app/Activity;");
                IntPtr activity = UnityEngine.AndroidJNI.GetStaticObjectField(player, field);
                IntPtr activityClass = UnityEngine.AndroidJNI.FindClass("android/app/Activity");
                IntPtr finish = UnityEngine.AndroidJNI.GetMethodID(activityClass, "finishAndRemoveTask", "()V");
                if (activity == IntPtr.Zero || finish == IntPtr.Zero) throw new Exception("the app's screen was not found");
                UnityEngine.AndroidJNI.CallVoidMethod(activity, finish, none);
                RevivalMod.Log.Msg("[update] asked Android to close the app's screen and remove it from recent apps");
            }
            catch (Exception e)
            {
                try { UnityEngine.AndroidJNI.ExceptionClear(); } catch { }
                RevivalMod.Log.Warning("[update] could not close the app's screen the normal way (" + e.Message + "); ending the process only");
            }
            // End the process a moment later, from a plain background thread. (This used to be done
            // from the game's own per-frame update, but once the screen is closed the game is paused
            // and no frames run: the process stayed alive, and was only ended when the player opened
            // the app again - which looked like the game closing by itself shortly after starting.)
            var ender = new System.Threading.Thread(() =>
            {
                System.Threading.Thread.Sleep(800);
                EndProcess();
            });
            ender.IsBackground = true;
            ender.Start();
            closeAt = UnityEngine.Time.realtimeSinceStartup + 3.0f;      // fallback, should the thread not get to it
        }

        private static void EndProcess()
        {
            RevivalMod.Log.Msg("[update] ending the process");
            try { kill(getpid(), 9); } catch { }
            try { System.Diagnostics.Process.GetCurrentProcess().Kill(); } catch { }
            Environment.Exit(0);
        }
    }
}
#endif
