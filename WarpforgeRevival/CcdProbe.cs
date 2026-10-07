#if !ANDROID_PORT   // Windows only; left out of the Android build
using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace WarpforgeRevival
{
    /// <summary>
    /// One-off check: is the game's original cloud content (Unity CCD) still being served?
    /// The real bucket ids were delivered by the old backend and are lost, so this only tries the
    /// ids found inside the game itself. Anything that answers is saved to UserData/WarpforgeRevival/ccd.
    /// </summary>
    internal static class CcdProbe
    {
        private const string Project = "d03469d2-142f-4466-b3c4-e42115b76bac";
        private static readonly string[] Buckets = { "432d18c9-9348-4b90-bfbf-9f2a10e1f15b", "981af8af-a3a3-419a-9f01-a518e3a17c1c" };
        private static readonly string[] Environments = { "production", "live", "preprod" };
        private static readonly string[] Badges = { "latest", "1.35.0", "1.35", "live", "production" };
        private static readonly string[] Files = { "catalog_main.hash", "allcards_assets_all.bundle" };

        public static void Start(string dataDir)
        {
            string marker = Path.Combine(dataDir, "ccd", "probe-done.txt");
            if (File.Exists(marker)) return;
            Task.Run(async () =>
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(marker));
                    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
                    var report = new System.Text.StringBuilder();
                    foreach (var bucket in Buckets)
                    foreach (var env in Environments)
                    foreach (var badge in Badges)
                    {
                        string url = $"https://{Project}.client-api.unity3dusercontent.com/client_api/v1/environments/{env}/buckets/{bucket}/release_by_badge/{badge}/entry_by_path/content/?path=/{Files[0]}";
                        int code;
                        try
                        {
                            using var resp = await http.GetAsync(url);
                            code = (int)resp.StatusCode;
                            if (resp.IsSuccessStatusCode)
                            {
                                RevivalMod.Log.Msg($"[ccd] FOUND content: env={env} bucket={bucket} badge={badge}");
                                foreach (var f in new[] { "catalog_main.hash", "catalog_main.json", "catalog_main.bin", "allcards_assets_all.bundle", "localization_assets_all.bundle", "alternateartstyles_assets_all.bundle" })
                                {
                                    try
                                    {
                                        var data = await http.GetByteArrayAsync(url.Replace(Files[0], f));
                                        File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(marker), $"{env}_{badge}_{f}"), data);
                                        RevivalMod.Log.Msg($"[ccd] saved {f} ({data.Length} bytes)");
                                    }
                                    catch (Exception e) { RevivalMod.Log.Msg($"[ccd] {f}: {e.Message}"); }
                                }
                            }
                        }
                        catch (Exception e) { code = -1; report.AppendLine($"{env} {bucket} {badge}: {e.Message}"); }
                        report.AppendLine($"{env} {bucket} {badge}: HTTP {code}");
                    }
                    File.WriteAllText(marker, report.ToString());
                    RevivalMod.Log.Msg("[ccd] probe finished: " + report.ToString().Replace("\r", "").Replace("\n", " | "));
                }
                catch (Exception e) { RevivalMod.Log.Warning("[ccd] probe failed: " + e.Message); }
            });
        }
    }
}
#endif
