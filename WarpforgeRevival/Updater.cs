#if !ANDROID_PORT   // Windows only; left out of the Android build
using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace WarpforgeRevival
{
    /// <summary>
    /// Keeps the mod itself up to date from the revival server (GET /api/v1/mod/manifest).
    ///
    /// One way of updating, the same as on phones: when the server has a newer build - seen by the
    /// check at start-up, or because the server refuses to sign in an outdated mod - the game's
    /// sign-in window says so while the game loads, the build is downloaded and checked against the
    /// server's checksum, and the game closes and starts again with it.
    ///
    /// Updates go to Mods\WarpforgeRevival\WarpforgeRevival.dll. If the running copy is somewhere
    /// else (straight in Mods, say) it is removed after the update so the game never loads two
    /// copies. The game keeps the installed file open, so a small script swaps the files once the
    /// game has closed and then starts it again.
    /// AutoUpdate setting: Off never checks; anything else updates.
    /// </summary>
    internal static class Updater
    {
        /// <summary>Where MelonLoader loaded this mod from (set at start-up; empty when unknown).</summary>
        internal static string LoadedFrom = "";

        private const string FileName = "WarpforgeRevival.dll";
        private static string server, dataDir;
        private static Task<bool> running;
        private static readonly object Gate = new object();

        private static bool SameFile(string a, string b) =>
            string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

        private static bool UpdatesOff => (RevivalMod.Config.AutoUpdate ?? "").Trim().Equals("Off", StringComparison.OrdinalIgnoreCase);

        public static void Start(string serverUrl, string data)
        {
            server = serverUrl;
            dataDir = data;
            if (UpdatesOff) return;
            Task.Run(async () =>
            {
                try
                {
                    var m = await Manifest();
                    if (m == null) return;
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
            using var manifest = JsonDocument.Parse(await http.GetStringAsync(server + "/api/v1/mod/manifest"));
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

        /// <summary>Downloads the newer build, arranges the swap, and lets the sign-in window close the game.</summary>
        private static async Task<bool> Update(string message)
        {
            GameSignIn.Updating(message);
            try
            {
                var offer = await Manifest();
                if (offer == null || !offer.Value.newer) throw new InvalidOperationException("the server has no newer mod to offer");
                string remote = offer.Value.label;

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

                using var http = Net.Client(TimeSpan.FromSeconds(120));
                var data = await http.GetByteArrayAsync(server + offer.Value.url);
                string got = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
                if (got != offer.Value.sha) throw new InvalidDataException("downloaded mod does not match the server's checksum");
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
                RevivalMod.Log.Msg($"[update] mod {remote} downloaded; the game closes and starts again to use it");
                // the sign-in window counts down and closes the game; the script then starts it again
                GameSignIn.Updated($"Updated to {remote}. The game closes and starts again to use it.", 3f);
                return true;
            }
            catch (Exception e)
            {
                RevivalMod.Log.Warning("[update] could not update the mod: " + e.Message);
                GameSignIn.UpdateFailed();
                return false;
            }
        }
    }
}
#endif
