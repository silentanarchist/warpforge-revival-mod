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
    /// AutoUpdate setting: Ask (default) shows a Yes/No box first, Auto updates without asking, Off never checks.
    /// </summary>
    internal static class Updater
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

        private static volatile bool quitRequested;

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

        public static void Start(string serverUrl, string dataDir)
        {
            string mode = (RevivalMod.Config.AutoUpdate ?? "Ask").Trim();
            if (mode.Equals("Off", StringComparison.OrdinalIgnoreCase)) return;
            Task.Run(() => Check(serverUrl, dataDir, mode.Equals("Auto", StringComparison.OrdinalIgnoreCase)));
        }

        private static async Task Check(string serverUrl, string dataDir, bool auto)
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

                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Max(30, RevivalMod.Config.TimeoutSeconds)) };
                using var manifest = JsonDocument.Parse(await http.GetStringAsync(serverUrl + "/api/v1/mod/manifest"));
                var root = manifest.RootElement;
                if (!root.TryGetProperty("available", out var av) || !av.GetBoolean()) return;
                // "version" stays in the form older builds understand; "label" is the name shown to people
                string number = root.GetProperty("version").GetString();
                string remote = root.TryGetProperty("label", out var lb) && lb.ValueKind == JsonValueKind.String ? lb.GetString() : number;
                string sha = root.GetProperty("sha256").GetString();
                var rv = RevivalMod.Number(number); var lv = RevivalMod.Number(RevivalMod.Version);
                if (rv == null || lv == null || rv <= lv)
                {
                    RevivalMod.Log.Msg($"[update] mod is up to date (installed {RevivalMod.Version}, server has {remote})");
                    return;
                }

                RevivalMod.Log.Msg($"[update] server has mod {remote} (installed {RevivalMod.Version})");
                if (!auto)
                {
                    // 0x4 Yes/No, 0x20 question icon, 0x40000 topmost, 0x10000 set foreground; 6 = Yes
                    int answer = MessageBoxW(IntPtr.Zero,
                        $"A newer Warpforge Revival mod is available from your server.\n\n" +
                        $"Installed: {RevivalMod.Version}\nAvailable: {remote}\nServer: {serverUrl}\n\n" +
                        "Install it now? The game will close and start again.",
                        "Warpforge Revival update", 0x4 | 0x20 | 0x40000 | 0x10000);
                    if (answer != 6) { RevivalMod.Log.Msg("[update] update declined"); return; }
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
                RevivalMod.Log.Msg($"[update] mod {remote} downloaded; restarting the game");
                quitRequested = true;
            }
            catch (Exception e)
            {
                RevivalMod.Log.Warning("[update] update check failed: " + e.Message);
            }
        }
    }
}
#endif
