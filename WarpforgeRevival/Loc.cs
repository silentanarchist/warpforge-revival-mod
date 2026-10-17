using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace WarpforgeRevival
{
    /// <summary>
    /// The mod's own text in the player's language (0.14.1). Every sentence the mod shows goes through
    /// Loc.T("area.key", "English text", ("name", value)...): the English is in the code, so the mod always has
    /// something to show, and tools/localization/extract.py collects every key with its English into
    /// localization/mod/en.json, the source the translators work from (Weblate). Translations come from the server
    /// (/api/v1/content/files/localization/mod/&lt;code&gt;.json, listed with their checksums in
    /// localization/index.json), so a new or corrected translation reaches players without a mod update. They are
    /// kept in UserData/WarpforgeRevival/lang/ for the next start and for playing offline.
    ///
    /// The language is the game's own (Settings > General > Language, I2's CurrentLanguageCode: en, es, ru, fr, de,
    /// pt-BR, it, ko, zh-CN, ja, pl, cs, tr). A text that has no translation yet is shown in English.
    /// {name} in a text is replaced by the value given for it; translators keep those words as they are.
    /// Where the game already has the same text (its own translations of "Friends", mode names, rule names...), the
    /// mod uses that through Loc.Game("Its/Term", "English") instead, so it matches the game and needs no translating.
    /// </summary>
    internal static class Loc
    {
        private static readonly HttpClient Http = Net.Client(TimeSpan.FromSeconds(20));
        private static string server, cacheDir;
        private static volatile Dictionary<string, string> table;       // the current language's texts, or null
        private static volatile string tableLang = "en";
        private static volatile string wantLang = "en";
        private static volatile string loadingLang;
        private static Dictionary<string, string> index;                 // language -> checksum on the server
        private static bool indexAsked;

        /// <summary>Called once at start-up with the server's address and the mod's data folder.</summary>
        internal static void Start(string serverUrl, string dataDir)
        {
            server = serverUrl;
            cacheDir = Path.Combine(dataDir, "lang");
        }

        private static float nextLook;

        /// <summary>Called every frame (main thread): reads the game's language once a second and loads its texts
        /// when it changes. T itself never calls into the game, so it is safe on any thread.</summary>
        internal static void Tick()
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now < nextLook) return;
            nextLook = now + 1f;
            string code = "en";
            try { code = Il2CppI2.Loc.LocalizationManager.CurrentLanguageCode ?? "en"; } catch { }
            if (string.IsNullOrEmpty(code)) code = "en";
            if (code == wantLang) return;
            wantLang = code;
            RevivalMod.Log.Msg("[lang] game language: " + code);
            if (code == "en") { table = null; tableLang = "en"; }
            else Load(code);
        }

        /// <summary>The game's language code as last seen ("en" until then).</summary>
        internal static string Language() => wantLang;

        /// <summary>The text for a key in the player's language, with {name} words filled in.</summary>
        internal static string T(string key, string english, params (string name, object value)[] args)
        {
            string text = english;
            try
            {
                if (Language() != "en")
                {
                    var t = table;
                    if (t != null && tableLang == wantLang && t.TryGetValue(key, out var found) && !string.IsNullOrEmpty(found))
                        text = found;
                }
            }
            catch { }
            if (args == null || args.Length == 0) return text;
            var sb = new StringBuilder(text);
            foreach (var (name, value) in args) sb.Replace("{" + name + "}", value?.ToString() ?? "");
            return sb.ToString();
        }

        /// <summary>A text the game itself already has in every language (an I2 term such as "SocialMenu/Friends"):
        /// the game's own translation, or the English given when the game has none. Main thread only (it asks the
        /// game). These need no translating, so they are not in en.json.</summary>
        internal static string Game(string term, string english)
        {
            try
            {
                var t = Il2CppI2.Loc.LocalizationManager.GetTranslation(term, true, 0, true, false, null, null, true);
                if (!string.IsNullOrEmpty(t)) return t;
            }
            catch { }
            return english;
        }

        private static string CachePath(string code) => Path.Combine(cacheDir ?? "", "mod-" + code + ".json");

        /// <summary>Uses the kept copy at once (if any), then asks the server for a newer one, in the background.</summary>
        private static void Load(string code)
        {
            if (loadingLang == code) return;
            loadingLang = code;
            try
            {
                var kept = File.Exists(CachePath(code)) ? Parse(File.ReadAllBytes(CachePath(code))) : null;
                if (kept != null) { table = kept; tableLang = code; }
            }
            catch (Exception e) { RevivalMod.Log.Warning("[lang] kept " + code + " texts could not be read: " + e.Message); }
            if (string.IsNullOrEmpty(server)) { loadingLang = null; return; }
            Task.Run(async () =>
            {
                try
                {
                    if (!indexAsked)
                    {
                        indexAsked = true;
                        using var reply = await Http.GetAsync(server + "/api/v1/content/files/localization/index.json");
                        if (reply.IsSuccessStatusCode)
                        {
                            using var doc = JsonDocument.Parse(await reply.Content.ReadAsStringAsync());
                            var found = new Dictionary<string, string>();
                            if (doc.RootElement.TryGetProperty("mod", out var mod) && mod.ValueKind == JsonValueKind.Object)
                                foreach (var l in mod.EnumerateObject())
                                    if (l.Value.ValueKind == JsonValueKind.String) found[l.Name] = l.Value.GetString();
                            index = found;
                        }
                    }
                    var idx = index;
                    if (idx == null || !idx.TryGetValue(code, out var sha))
                    {
                        if (idx != null) RevivalMod.Log.Msg($"[lang] the server has no {code} texts for the mod yet; English is shown");
                        return;
                    }
                    if (File.Exists(CachePath(code)) && Hex(SHA256.HashData(File.ReadAllBytes(CachePath(code)))) == sha)
                    {
                        RevivalMod.Log.Msg($"[lang] {code} texts are up to date ({table?.Count ?? 0})");
                        return;
                    }
                    var data = await Http.GetByteArrayAsync(server + "/api/v1/content/files/localization/mod/" + Uri.EscapeDataString(code) + ".json");
                    if (Hex(SHA256.HashData(data)) != sha) { RevivalMod.Log.Warning($"[lang] the {code} texts do not match the server's list; not used"); return; }
                    var fresh = Parse(data);
                    if (fresh == null) return;
                    Directory.CreateDirectory(cacheDir);
                    File.WriteAllBytes(CachePath(code), data);
                    if (wantLang == code) { table = fresh; tableLang = code; }
                    RevivalMod.Log.Msg($"[lang] {fresh.Count} {code} texts loaded from the server");
                }
                catch (Exception e) { RevivalMod.Log.Warning($"[lang] {code} texts could not be loaded: " + e.Message); }
                finally { if (loadingLang == code) loadingLang = null; }
            });
        }

        private static string Hex(byte[] b) => Convert.ToHexString(b).ToLowerInvariant();

        /// <summary>A language file: a flat JSON object of key -> text (other entries are ignored).</summary>
        private static Dictionary<string, string> Parse(byte[] data)
        {
            using var doc = JsonDocument.Parse(data);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            var d = new Dictionary<string, string>();
            foreach (var p in doc.RootElement.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.String) d[p.Name] = p.Value.GetString();
            return d;
        }
    }
}
