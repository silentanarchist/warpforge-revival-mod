#if !ANDROID_PORT   // Windows only; left out of the Android build
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace WarpforgeRevival
{
    /// <summary>
    /// Keeps the mod itself up to date from the revival server (GET /api/v1/mod/manifest).
    /// A newer build is downloaded into Mods\WarpforgeRevival and checked against the server's
    /// checksum. If the running copy is somewhere else (straight in Mods, say) it is removed after
    /// the update so the game never loads two copies. The game keeps the installed file open, so a small script swaps the files
    /// once the game has closed and then starts it again.
    /// AutoUpdate setting: Ask (default) asks in the game, once the main menu is up (like on
    /// Android), Auto updates without asking, Off never checks.
    /// </summary>
    internal static class Updater
    {
        private static volatile bool quitRequested;
        private static volatile string offered;          // a newer build waiting for the player's answer
        private static volatile string notice;           // something to tell the player in the game
        private static volatile bool staged;             // downloaded; the game restarts to use it
        private static bool asked;
        private static float nextTry, menuShownAt = -1f;

        /// <summary>Where MelonLoader loaded this mod from (set at start-up; empty when unknown).</summary>
        internal static string LoadedFrom = "";

        private const string FileName = "WarpforgeRevival.dll";
        private static bool SameFile(string a, string b) =>
            string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

        /// <summary>Called every frame on the main thread; closes the game once an update is staged.</summary>
        public static void Pump()
        {
            if (!quitRequested) return;
            quitRequested = false;
            RevivalMod.Log.Msg("[update] closing the game to finish the update");
            UnityEngine.Application.Quit();
        }

        private static string startServer, startData;

        private static bool UpdatesOff => (RevivalMod.Config.AutoUpdate ?? "Ask").Trim().Equals("Off", StringComparison.OrdinalIgnoreCase);

        public static void Start(string serverUrl, string dataDir)
        {
            startServer = serverUrl;
            startData = dataDir;
            if (UpdatesOff) return;
            string mode = (RevivalMod.Config.AutoUpdate ?? "Ask").Trim();
            Task.Run(() => Check(serverUrl, dataDir, mode.Equals("Auto", StringComparison.OrdinalIgnoreCase)));
        }

        /// <summary>The server refused to sign in because this mod is out of date: update without asking.</summary>
        internal static async Task<bool> UpdateNow()
        {
            if (UpdatesOff || startServer == null) return false;
            GameSignIn.Updating("This server needs a newer Warpforge Revival mod. Downloading it...");
            bool ready = await Check(startServer, startData, true);
            if (ready) GameSignIn.Updated("The new version is downloaded. The game closes and starts again to use it.", 3f);
            else GameSignIn.UpdateFailed();
            return ready;
        }

        /// <summary>Called when the main menu has been built (see MenuCleanup).</summary>
        internal static void MenuShown()
        {
            if (menuShownAt < 0f) menuShownAt = UnityEngine.Time.realtimeSinceStartup;
        }

        /// <summary>True a few seconds after the menu appeared, so the game's own start-up popups go first.</summary>
        private static bool MenuReady => menuShownAt >= 0f && UnityEngine.Time.realtimeSinceStartup - menuShownAt > 6f;

        /// <summary>Called every frame on the main thread: asks about, and reports on, an update once the menu is up.</summary>
        public static void Tick()
        {
            if (offered == null && notice == null) return;
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now < nextTry) return;
            nextTry = now + 2f;
            try
            {
                // Popups only work once the player is signed in and the menu has loaded.
                if (PlayFabTransport.SessionTicket == null || !MenuReady) return;
                var windows = Il2Cpp.SingletonBehaviour<Il2Cpp.WindowsManager>.Instance;
                if ((object)windows == null) return;
                if (notice != null)
                {
                    string text = notice;
                    notice = null;
                    if (staged)
                    {
                        Action restart = () => { RevivalMod.Log.Msg("[update] restarting the game for the update"); quitRequested = true; };
                        windows.ShowPopUp(text, false, false, "Restart now", Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(restart));
                    }
                    else
                        windows.ShowPopUp(text, false, true, "OK", Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Il2CppSystem.Action>((Action)(() => { })));
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
                        Task.Run(async () =>
                        {
                            bool ok = await Check(startServer, startData, true, true);
                            if (!ok && !staged) notice = "The Warpforge Revival mod update could not be downloaded. MelonLoader\\Latest.log in the game folder says why.";
                        });
                    };
                    Action no = () => RevivalMod.Log.Msg("[update] update declined");
                    windows.ShowPopUp(
                        $"A newer Warpforge Revival mod is available from your server.\n\nInstalled: {RevivalMod.Version}\nAvailable: {remote}\n\nInstall it now? The game closes and starts again.",
                        false, true, "Install", "Later",
                        Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(yes),
                        Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(no));
                }
            }
            catch (Exception e)
            {
                RevivalMod.Log.Warning("[update] could not show the update message: " + e.Message);
                offered = null; notice = null;
            }
        }

        /// <summary>True once a newer build is downloaded and the swap is arranged (the game then closes).</summary>
        private static async Task<bool> Check(string serverUrl, string dataDir, bool auto, bool answered = false)
        {
            try
            {
                // Updates always go to Mods\WarpforgeRevival\WarpforgeRevival.dll.
                string mods = MelonLoader.Utils.MelonEnvironment.ModsDirectory;
                string folder = Path.Combine(mods, "WarpforgeRevival");
                string dll = Path.Combine(folder, FileName);
                // Any other copy the game could load: the one running now, and one left straight in Mods.
                var old = new System.Collections.Generic.List<string>();
                foreach (string p in new[] { LoadedFrom, typeof(Updater).Assembly.Location, Path.Combine(mods, FileName) })
                    if (!string.IsNullOrEmpty(p) && File.Exists(p) && !SameFile(p, dll) && !old.Exists(o => SameFile(o, p))
                        && string.Equals(Path.GetFileName(p), FileName, StringComparison.OrdinalIgnoreCase))
                        old.Add(Path.GetFullPath(p));
                string fresh = dll + ".new";
                try { if (File.Exists(fresh)) File.Delete(fresh); } catch { }

                using var http = Net.Client(TimeSpan.FromSeconds(Math.Max(30, RevivalMod.Config.TimeoutSeconds)));
                using var manifest = JsonDocument.Parse(await http.GetStringAsync(serverUrl + "/api/v1/mod/manifest"));
                var root = manifest.RootElement;
                if (!root.TryGetProperty("available", out var av) || !av.GetBoolean()) return false;
                // "version" stays in the form older builds understand; "label" is the name shown to people
                string number = root.GetProperty("version").GetString();
                string remote = root.TryGetProperty("label", out var lb) && lb.ValueKind == JsonValueKind.String ? lb.GetString() : number;
                string sha = root.GetProperty("sha256").GetString();
                var rv = RevivalMod.Number(number); var lv = RevivalMod.Number(RevivalMod.Version);
                if (rv == null || lv == null || rv <= lv)
                {
                    RevivalMod.Log.Msg($"[update] mod is up to date (installed {RevivalMod.Version}, server has {remote})");
                    return false;
                }

                RevivalMod.Log.Msg($"[update] server has mod {remote} (installed {RevivalMod.Version})");
                if (!auto)
                {
                    offered = remote;                // asked in the game once the menu is up, see Tick
                    return false;
                }

                var data = await http.GetByteArrayAsync(serverUrl + root.GetProperty("url").GetString());
                string got = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
                if (got != sha) throw new InvalidDataException("downloaded mod does not match the server's checksum");
                Directory.CreateDirectory(folder);
                // MelonLoader only loads mods from a folder inside Mods when that folder holds a
                // manifest.json. Without one the updated mod would silently stop loading.
                string manifestFile = Path.Combine(folder, "manifest.json");
                if (!File.Exists(manifestFile))
                    File.WriteAllText(manifestFile,
                        "{\n  \"name\": \"WarpforgeRevival\",\n  \"description\": \"Warpforge Revival mod. MelonLoader needs this file to load mods from this folder - do not delete it.\"\n}\n");
                File.WriteAllBytes(fresh, data);

                // Swap the files after this process has exited, then start the game again.
                string exe = Environment.ProcessPath;
                int pid = Environment.ProcessId;
                string script = Path.Combine(dataDir, "apply-update.cmd");
                string log = Path.Combine(dataDir, "update.log");
                File.WriteAllText(script,
                    "@echo off\r\n" +
                    ":wait\r\n" +
                    $"tasklist /FI \"PID eq {pid}\" 2>nul | find \" {pid} \" >nul\r\n" +
                    "if not errorlevel 1 (\r\n  ping -n 2 127.0.0.1 >nul\r\n  goto wait\r\n)\r\n" +
                    $"move /y \"{fresh}\" \"{dll}\" >> \"{log}\" 2>&1\r\n" +
                    $"echo %date% %time% updated to {remote} (exit code %errorlevel%) >> \"{log}\"\r\n" +
                    // only once the new file is in place: drop the copies outside the folder
                    string.Concat(old.ConvertAll(o => $"if exist \"{dll}\" if not exist \"{fresh}\" del /q \"{o}\" >> \"{log}\" 2>&1\r\n")) +
                    $"start \"\" /d \"{Path.GetDirectoryName(exe)}\" \"{exe}\"\r\n");
                Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"\"{script}\"\"")
                {
                    UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = dataDir
                });
                RevivalMod.Log.Msg($"[update] mod {remote} downloaded; the game restarts to use it");
                staged = true;
                if (auto && !answered) quitRequested = true;
                else notice = $"Warpforge Revival mod {remote} is downloaded.\n\nThe game will now close and start again with the new version.";
                return true;
            }
            catch (Exception e)
            {
                RevivalMod.Log.Warning("[update] update check failed: " + e.Message);
                return false;
            }
        }
    }
}
#endif
