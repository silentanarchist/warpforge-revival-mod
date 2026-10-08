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
    /// A newer build is downloaded next to the installed file, checked against the server's checksum
    /// and then put in its place; the game picks it up the next time it starts. Unlike on Windows no
    /// helper script is needed: a file that is in use can be replaced here.
    /// AutoUpdate setting: Ask (default) asks in the game first, Auto updates without asking, Off never checks.
    /// Only the mod is updated this way. The loader is part of the installed app and cannot replace itself.
    /// </summary>
    internal static class AndroidUpdater
    {
        private const string FileName = "WarpforgeRevival.Android.dll";

        /// <summary>Where MelonLoader loaded this mod from (set at start-up; empty when unknown).</summary>
        internal static string LoadedFrom = "";

        private static string server;
        private static bool auto;
        private static volatile string offered, offeredSha, offeredUrl;   // a newer build waiting for the player's answer
        private static volatile string notice;                            // something to tell the player
        private static bool asked;
        private static volatile bool installed;
        private static float nextTry;

        private static bool Newer(string remote, string installed)
        {
            var rv = RevivalMod.Number(remote); var lv = RevivalMod.Number(installed);
            return rv != null && lv != null && rv > lv;
        }

        private static bool UpdatesOff => (RevivalMod.Config.AutoUpdate ?? "Ask").Trim().Equals("Off", StringComparison.OrdinalIgnoreCase);

        public static void Start(string serverUrl)
        {
            string mode = (RevivalMod.Config.AutoUpdate ?? "Ask").Trim();
            server = serverUrl;
            if (UpdatesOff) return;
            auto = mode.Equals("Auto", StringComparison.OrdinalIgnoreCase);
            Task.Run(Check);
        }

        private static async Task Check()
        {
            try
            {
                using var http = Net.Client(TimeSpan.FromSeconds(Math.Max(30, RevivalMod.Config.TimeoutSeconds)));
                using var manifest = JsonDocument.Parse(await http.GetStringAsync(server + "/api/v1/mod/manifest?platform=android"));
                var root = manifest.RootElement;
                if (!root.TryGetProperty("available", out var av) || !av.GetBoolean())
                {
                    RevivalMod.Log.Msg("[update] the server has no Android mod to offer");
                    return;
                }
                // "version" stays in the form older builds understand; "label" is the name shown to people
                string number = root.GetProperty("version").GetString();
                string remote = root.TryGetProperty("label", out var lb) && lb.ValueKind == JsonValueKind.String ? lb.GetString() : number;
                if (!Newer(number, RevivalMod.Version))
                {
                    RevivalMod.Log.Msg($"[update] mod is up to date (installed {RevivalMod.Version}, server has {remote})");
                    return;
                }
                RevivalMod.Log.Msg($"[update] server has mod {remote} (installed {RevivalMod.Version})");
                offeredSha = root.GetProperty("sha256").GetString();
                offeredUrl = root.GetProperty("url").GetString();
                if (auto) await Install(remote);
                else offered = remote;     // asked on the main thread once the menu is up, see Tick
            }
            catch (Exception e)
            {
                RevivalMod.Log.Warning("[update] update check failed: " + e.Message);
            }
        }

        /// <summary>
        /// The server refused to sign in because this mod is out of date: download and install its
        /// build without asking, then close the game so the next start uses it.
        /// </summary>
        internal static async Task<bool> UpdateNow()
        {
            if (UpdatesOff || server == null) return false;
            GameSignIn.Updating("This server needs a newer Warpforge Revival mod. Downloading it...");
            string remote = null;
            try
            {
                using var http = Net.Client(TimeSpan.FromSeconds(Math.Max(30, RevivalMod.Config.TimeoutSeconds)));
                using var manifest = JsonDocument.Parse(await http.GetStringAsync(server + "/api/v1/mod/manifest?platform=android"));
                var root = manifest.RootElement;
                if (root.TryGetProperty("available", out var av) && av.GetBoolean())
                {
                    string number = root.GetProperty("version").GetString();
                    if (Newer(number, RevivalMod.Version))
                    {
                        remote = root.TryGetProperty("label", out var lb) && lb.ValueKind == JsonValueKind.String ? lb.GetString() : number;
                        offeredSha = root.GetProperty("sha256").GetString();
                        offeredUrl = root.GetProperty("url").GetString();
                    }
                }
            }
            catch (Exception e) { RevivalMod.Log.Warning("[update] update check failed: " + e.Message); }
            if (remote == null) { GameSignIn.UpdateFailed(); return false; }
            offered = null;
            asked = true;
            await Install(remote);
            notice = null;                       // told in the window below instead of the game's popup
            if (!installed) { GameSignIn.UpdateFailed(); return false; }
            GameSignIn.Updated($"Updated to {remote}. Open the game again to use it.", 5f);
            return true;
        }

        private static async Task Install(string remote)
        {
            try
            {
                string mods = MelonLoader.Utils.MelonEnvironment.ModsDirectory;
                string dll = !string.IsNullOrEmpty(LoadedFrom) && File.Exists(LoadedFrom)
                             && string.Equals(Path.GetFileName(LoadedFrom), FileName, StringComparison.OrdinalIgnoreCase)
                    ? Path.GetFullPath(LoadedFrom)
                    : Path.Combine(mods, FileName);
                string fresh = dll + ".new";

                using var http = Net.Client(TimeSpan.FromSeconds(120));
                var data = await http.GetByteArrayAsync(server + offeredUrl);
                string got = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
                if (got != offeredSha) throw new InvalidDataException("downloaded mod does not match the server's checksum");

                File.WriteAllBytes(fresh, data);
                // Put in place in one step; the running game keeps using the copy it already loaded.
                File.Move(fresh, dll, true);
                RevivalMod.Log.Msg($"[update] mod {remote} installed at {dll}; it is used from the next start of the game");
                installed = true;
                notice = $"Warpforge Revival mod updated to {remote}.\n\nThe game will now close. Open it again to use the new version.";
            }
            catch (Exception e)
            {
                RevivalMod.Log.Warning("[update] could not install the update: " + e.Message);
                notice = "The Warpforge Revival mod update could not be installed:\n" + e.Message;
            }
        }

        /// <summary>Called every frame on the main thread: asks about, and reports on, an update once the menu is up.</summary>
        public static void Tick()
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (closeAt > 0f && now >= closeAt) { closeAt = -1f; EndProcess(); return; }
            if (offered == null && notice == null) return;
            if (now < nextTry) return;
            nextTry = now + 2f;
            try
            {
                // Popups only work once the player is signed in and the menu has loaded.
                if (PlayFabTransport.SessionTicket == null || !MenuReady) return;
                var windows = SingletonBehaviour<WindowsManager>.Instance;
                if ((object)windows == null) return;

                if (notice != null)
                {
                    string text = notice;
                    notice = null;
                    if (installed)
                    {
                        // The game's own "Exit" only puts it in the background on a phone, and it would then
                        // carry on with the old build. Really end it, so the next start uses the new one.
                        Action close = () =>
                        {
                            RevivalMod.Log.Msg("[update] closing the game so the update is used on the next start");
                            CloseApp();
                        };
                        windows.ShowPopUp(text, false, false, "Close game", DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(close));
                    }
                    else
                        windows.ShowPopUp(text, false, true, "OK", DelegateSupport.ConvertDelegate<Il2CppSystem.Action>((Action)(() => { })));
                    return;
                }
                if (offered != null && !asked)
                {
                    asked = true;
                    string remote = offered;
                    offered = null;
                    Action yes = () =>
                    {
                        RevivalMod.Log.Msg("[update] update accepted");
                        Task.Run(() => Install(remote));
                    };
                    Action no = () => RevivalMod.Log.Msg("[update] update declined");
                    windows.ShowPopUp(
                        $"A newer Warpforge Revival mod is available from your server.\n\nInstalled: {RevivalMod.Version}\nAvailable: {remote}\n\nInstall it now?",
                        false, true, "Install", "Later",
                        DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(yes),
                        DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(no));
                }
            }
            catch (Exception e)
            {
                RevivalMod.Log.Warning("[update] could not show the update message: " + e.Message);
                offered = null; notice = null;
            }
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

        private static float menuShownAt = -1f;

        /// <summary>Called when the main menu has been built (see MenuCleanup).</summary>
        internal static void MenuShown()
        {
            if (menuShownAt < 0f) menuShownAt = UnityEngine.Time.realtimeSinceStartup;
        }

        /// <summary>True a few seconds after the menu appeared, so the game's own start-up popups go first.</summary>
        private static bool MenuReady => menuShownAt >= 0f && UnityEngine.Time.realtimeSinceStartup - menuShownAt > 6f;
    }
}
#endif
