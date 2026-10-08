using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WarpforgeRevival
{
    /// <summary>
    /// Keeps a local copy of the server's card pack in UserData/WarpforgeRevival/cardpack.json.
    /// The pack version is a content hash; both players need the same version to play each other.
    /// </summary>
    internal class CardPackClient
    {
        private readonly string server;
        private readonly string packPath;
        private readonly ManualResetEventSlim ready = new ManualResetEventSlim(false);

        public string PackVersion { get; private set; }
        public int CardCount { get; private set; }
        public string PackJson { get; private set; }
        public bool FromCache { get; private set; }

        public CardPackClient(string server, string dataDir)
        {
            this.server = server;
            packPath = Path.Combine(dataDir, "cardpack.json");
        }

        public void StartSync() => Task.Run(Sync);

        /// <summary>Block until the pack is available (or failed). Returns true when a pack is loaded.</summary>
        public bool WaitReady(int timeoutMs) => ready.Wait(timeoutMs) && PackJson != null;

        private async Task Sync()
        {
            try
            {
                LoadCached();
                using var http = Net.Client(TimeSpan.FromSeconds(RevivalMod.Config.TimeoutSeconds), gzip: true);
                #if ANDROID_TEST
                // a phone needs the Android build of the content files
                var manifestJson = await http.GetStringAsync(server + "/api/v1/content/manifest?platform=android");
#else
                var manifestJson = await http.GetStringAsync(server + "/api/v1/content/manifest");
#endif
                using var manifest = JsonDocument.Parse(manifestJson);
                var remoteVersion = manifest.RootElement.GetProperty("packVersion").GetString();
                var sha = manifest.RootElement.GetProperty("sha256").GetString();

                if (manifest.RootElement.TryGetProperty("bundles", out var bundles))
                    await SyncBundles(http, bundles);

                if (remoteVersion == PackVersion)
                {
                    RevivalMod.Log.Msg($"Card pack {PackVersion} is up to date ({CardCount} cards)");
                    return;
                }

                RevivalMod.Log.Msg($"Downloading card pack {remoteVersion} (have {PackVersion ?? "none"})...");
                var body = await http.GetByteArrayAsync(server + "/api/v1/content/pack");
                var got = Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant();
                if (got != sha)
                    throw new InvalidDataException($"card pack checksum mismatch ({got} != {sha})");

                var tmp = packPath + ".tmp";
                File.WriteAllBytes(tmp, body);
                File.Move(tmp, packPath, true);
                Parse(System.Text.Encoding.UTF8.GetString(body));
                FromCache = false;
                RevivalMod.Log.Msg($"Card pack {PackVersion} installed ({CardCount} cards)");
            }
            catch (Exception e)
            {
                if (PackJson != null)
                    RevivalMod.Log.Warning($"Server unreachable ({e.Message}); using cached card pack {PackVersion}");
                else
                    RevivalMod.Log.Error($"No card pack available: {e.Message}. Check ServerUrl in UserData/WarpforgeRevival.cfg");
            }
            finally
            {
                ready.Set();
            }
        }

        /// <summary>Replacement content bundles -> UserData/WarpforgeRevival/content (see AddressablesRedirect).</summary>
        private async Task SyncBundles(HttpClient http, JsonElement bundles)
        {
            Directory.CreateDirectory(ContentDir);
            foreach (var b in bundles.EnumerateArray())
            {
                var name = b.GetProperty("name").GetString();
                var want = b.GetProperty("sha256").GetString();
                if (string.IsNullOrEmpty(name) || name.Contains("/") || name.Contains("\\") || name.Contains("..")) continue;
                var path = Path.Combine(ContentDir, name);
                if (File.Exists(path) && Sha256File(path) == want) continue;
                RevivalMod.Log.Msg($"Downloading content bundle {name} ({b.GetProperty("size").GetInt64() / 1024} KB)...");
                var data = await http.GetByteArrayAsync(server + b.GetProperty("url").GetString());
                var got = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
                if (got != want) throw new InvalidDataException($"bundle {name} checksum mismatch");
                File.WriteAllBytes(path + ".tmp", data);
                File.Move(path + ".tmp", path, true);
            }
        }

        private static string Sha256File(string path)
        {
            using var s = File.OpenRead(path);
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(s)).ToLowerInvariant();
        }

        public string ContentDir => Path.Combine(Path.GetDirectoryName(packPath), "content");

        private void LoadCached()
        {
            if (!File.Exists(packPath)) return;
            try
            {
                Parse(File.ReadAllText(packPath));
                FromCache = true;
            }
            catch (Exception e)
            {
                RevivalMod.Log.Warning($"Cached card pack is unreadable, will re-download: {e.Message}");
            }
        }

        private void Parse(string json)
        {
            using var doc = JsonDocument.Parse(json);
            PackVersion = doc.RootElement.GetProperty("version").GetString();
            CardCount = doc.RootElement.GetProperty("cards").GetArrayLength();
            PackJson = json;
        }
    }
}
