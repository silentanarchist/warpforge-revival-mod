using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using HarmonyLib;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;

namespace WarpforgeRevival
{
    /// <summary>
    /// The custom-card package (claude/custom-bundle-plan.md; built by the kit's build_custom.py from approved creator
    /// cards): custom-tester.json next to custom-cards_&lt;platform&gt;.bundle (the RawCardScript objects) and
    /// custom-art_&lt;platform&gt;.bundle (a texture and sprite per card), in UserData/WarpforgeRevival/content/custom/.
    ///
    /// The game loads its cards through Addressables (every asset labelled "cards") and a card's picture by the
    /// GUID in its cardImageReference, so the package is made known to Addressables rather than read by hand: one
    /// resource locator, added before the game loads its card library, with a location per bundle
    /// (AssetBundleProvider) and, depending on it, one per card (keyed by the card's GUID and the label "cards") and
    /// one per picture (keyed by its GUID, as a Sprite and as a Texture2D - the game's catalog lists pictures twice
    /// the same way). Addressables merges every locator's answers for a key, so the game's own LoadAllCards then
    /// takes the custom cards in with the official ones and their pictures load through the normal path.
    /// Texts come from the package too (CardText.SetPackageTexts; per language, English fallback).
    ///
    /// The package comes from the server (0.14.54): at start-up a game with a saved sign-in asks
    /// /api/v1/content/custom/custom-tester.json (the server answers only a tester's or admin's sign-in) and fetches
    /// the files for this platform whose checksum differs from what is in the folder; the manifest is written last, so
    /// a half-finished fetch leaves the previous package usable. The card library load waits for a fetch still running
    /// (at most 20 s). A game without the package (not a tester, server unreachable) plays as before.
    /// </summary>
    internal static class CustomBundle
    {
        private static bool registered;
        private static string Folder => Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "WarpforgeRevival", "content", "custom");
        private static string Platform =>
#if ANDROID_PORT
            "android";
#else
            "pc";
#endif

        /// <summary>The package's version (0 when none is loaded): for the match ticket and replays later.</summary>
        internal static int Version { get; private set; }
        internal static int CardCount { get; private set; }

        private static System.Threading.Tasks.Task fetching;
        private static string server;

        /// <summary>Mod start-up: fetch the package in the background when the game has a saved sign-in (the server decides
        /// whether it is a tester's).</summary>
        internal static void Start(string serverUrl)
        {
            server = serverUrl;
            string login = null;
            try { login = GameSignIn.Token(); } catch { }
            if (string.IsNullOrEmpty(login)) { RevivalMod.Log.Msg("[custom] no saved sign-in yet: the custom-card package is not asked for this start"); return; }
            fetching = System.Threading.Tasks.Task.Run(() => Fetch(login));
        }

        private static async System.Threading.Tasks.Task Fetch(string login)
        {
            try
            {
                using var http = Net.Client(TimeSpan.FromSeconds(60));
                http.DefaultRequestHeaders.TryAddWithoutValidation("X-Revival-Login", login);
                using var reply = await http.GetAsync(server + "/api/v1/content/custom/custom-tester.json");
                if (reply.StatusCode == System.Net.HttpStatusCode.Forbidden) { RevivalMod.Log.Msg("[custom] the server's custom-card package is for testers; none fetched"); return; }
                if (reply.StatusCode == System.Net.HttpStatusCode.NotFound) { RevivalMod.Log.Msg("[custom] the server has no custom-card package"); return; }
                if (!reply.IsSuccessStatusCode) { RevivalMod.Log.Warning($"[custom] package manifest: HTTP {(int)reply.StatusCode}"); return; }
                string manifest = await reply.Content.ReadAsStringAsync();
                string folder = Folder;
                Directory.CreateDirectory(folder);
                var wanted = new List<(string name, string sha)>();
                using (var doc = JsonDocument.Parse(manifest))
                {
                    if (doc.RootElement.TryGetProperty("files", out var files) && files.TryGetProperty(Platform, out var mine))
                        foreach (var f in mine.EnumerateObject())
                            wanted.Add((f.Name, f.Value.TryGetProperty("sha256", out var sh) ? sh.GetString() ?? "" : ""));
                }
                if (wanted.Count == 0) { RevivalMod.Log.Warning("[custom] the package manifest lists no files for " + Platform); return; }
                int fetched = 0;
                foreach (var (name, sha) in wanted)
                {
                    if (name.IndexOfAny(new[] { '/', '\\' }) >= 0 || !name.StartsWith("custom-")) continue;
                    string path = Path.Combine(folder, name);
                    if (File.Exists(path) && Sha256(path) == sha) continue;
                    var data = await http.GetByteArrayAsync(server + "/api/v1/content/custom/" + name);
                    string got = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data)).ToLowerInvariant();
                    if (got != sha) { RevivalMod.Log.Warning($"[custom] {name} does not match the server's checksum; package not updated"); return; }
                    File.WriteAllBytes(path + ".new", data);
                    File.Move(path + ".new", path, true);
                    fetched++;
                }
                string manifestPath = Path.Combine(folder, "custom-tester.json");
                if (fetched > 0 || !File.Exists(manifestPath) || File.ReadAllText(manifestPath) != manifest)
                    File.WriteAllText(manifestPath, manifest);
                RevivalMod.Log.Msg($"[custom] custom-card package: {(fetched > 0 ? fetched + " file(s) fetched" : "up to date")}");
            }
            catch (Exception e) { RevivalMod.Log.Warning("[custom] package fetch: " + e.Message); }
        }

        private static string Sha256(string path)
        {
            using var f = File.OpenRead(path);
            using var sha = System.Security.Cryptography.SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(f)).ToLowerInvariant();
        }

        /// <summary>As the game starts Addressables (AddressablesManager.Init, the same moment AddressablesRedirect installs its
        /// rewrite; PlayerDataManager.LoadAllCards and BaseAddressableLibrary.LoadLibrary are compiled into their callers, so
        /// neither can be hooked): the package, if there is one, is made known to Addressables. A locator added here stays in
        /// the list when the game's own catalog is added by the initialisation.</summary>
        [HarmonyPatch(typeof(AddressablesManager), nameof(AddressablesManager.Init))]
        private static class BeforeCards
        {
            private static void Postfix()
            {
                if (registered) return;
                try
                {
                    var f = fetching;
                    if (f != null && !f.IsCompleted)
                    {
                        RevivalMod.Log.Msg("[custom] waiting for the custom-card package fetch before the cards load");
                        f.Wait(TimeSpan.FromSeconds(20));
                    }
                    Register();
                }
                catch (Exception e) { RevivalMod.Log.Warning("[custom] package: " + e); }
            }
        }

        private static void Register()
        {
            if (registered) return;
            registered = true;
            string folder = Folder;
            string manifest = Path.Combine(folder, "custom-tester.json");
            if (!File.Exists(manifest)) { RevivalMod.Log.Msg("[custom] no custom-card package in " + folder); return; }
            using var doc = JsonDocument.Parse(File.ReadAllText(manifest));
            var root = doc.RootElement;
            int version = root.TryGetProperty("version", out var v) && v.TryGetInt32(out int vi) ? vi : 0;
            string cardsFile = Path.Combine(folder, "custom-cards_" + Platform + ".bundle");   // the package files for this platform
            string artFile = Path.Combine(folder, "custom-art_" + Platform + ".bundle");
            foreach (var f in new[] { cardsFile, artFile })
                if (!File.Exists(f)) { RevivalMod.Log.Warning("[custom] package file missing: " + f); return; }

            // the locations, built directly (ResourceLocationBase with the bundle's request options as Data; a map built
            // from ResourceLocationData lost the options in this game's Addressables - 0.14.57: "Invalid path in
            // AssetBundleProvider" although the file was there - and without them the bundle is never opened)
            var cardsBundle = BundleLocation("custom-cards", cardsFile);
            var artBundle = BundleLocation("custom-art", artFile);
            var map = new ResourceLocationMap("custom-tester-" + version, 16);
            var locations = map.Locations;
            var texts = new Dictionary<string, Dictionary<string, (string name, string text)>>();
            int count = 0;
            Add(locations, "custom-cards", cardsBundle);
            Add(locations, "custom-art", artBundle);
            foreach (var card in root.GetProperty("cards").EnumerateArray())
            {
                string id = card.GetProperty("id").GetString();
                string guid = card.GetProperty("guid").GetString();
                string art = card.GetProperty("art").GetString();
                if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(guid) || string.IsNullOrEmpty(art)) continue;
                var cardLoc = new ResourceLocationBase(guid, guid, Il2CppType.Of<BundledAssetProvider>().FullName, Il2CppType.Of<RawCardScript>(), Deps(cardsBundle));
                Add(locations, guid, cardLoc);
                Add(locations, "cards", cardLoc);
                Add(locations, art, new ResourceLocationBase(art, art, Il2CppType.Of<BundledAssetProvider>().FullName, Il2CppType.Of<Sprite>(), Deps(artBundle)));
                Add(locations, art, new ResourceLocationBase(art, art, Il2CppType.Of<BundledAssetProvider>().FullName, Il2CppType.Of<Texture2D>(), Deps(artBundle)));
                count++;
            }
            if (count == 0) { RevivalMod.Log.Warning("[custom] the package lists no cards"); return; }
            bool hasOptions = false;
            try { hasOptions = (object)cardsBundle.Cast<IResourceLocation>().Data?.TryCast<AssetBundleRequestOptions>() != null; } catch { }
            Addressables.AddResourceLocator(map.Cast<IResourceLocator>(), null, null);

            if (root.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.Object)
                foreach (var entry in text.EnumerateObject())
                {
                    var langs = new Dictionary<string, (string, string)>();
                    foreach (var lang in entry.Value.EnumerateObject())
                    {
                        string name = lang.Value.TryGetProperty("name", out var n) ? n.GetString() : null;
                        string body = lang.Value.TryGetProperty("text", out var t) ? t.GetString() : null;
                        if (!string.IsNullOrEmpty(name)) langs[lang.Name] = (name, body ?? "");
                    }
                    if (langs.Count > 0) texts[entry.Name] = langs;
                }
            CardText.SetPackageTexts(texts);
            Version = version; CardCount = count;
            int locators = 0;
            try { var it = Addressables.ResourceLocators.GetEnumerator().Cast<Il2CppSystem.Collections.IEnumerator>(); while (it.MoveNext()) locators++; } catch { }
            RevivalMod.Log.Msg($"[custom] custom-card package v{version}: {count} card(s) made known to Addressables (bundles {Path.GetFileName(cardsFile)}, {Path.GetFileName(artFile)}; {locators} locator(s) now; bundle options {(hasOptions ? "set" : "MISSING")}); texts for {texts.Count}");
        }

        private static Il2CppReferenceArray<IResourceLocation> Deps(ResourceLocationBase bundle)
        {
            var deps = new Il2CppReferenceArray<IResourceLocation>(1);
            deps[0] = bundle.Cast<IResourceLocation>();
            return deps;
        }

        private static void Add(Il2CppSystem.Collections.Generic.Dictionary<Il2CppSystem.Object, Il2CppSystem.Collections.Generic.IList<IResourceLocation>> map, string key, ResourceLocationBase loc)
        {
            var k = (Il2CppSystem.Object)(Il2CppSystem.String)key;
            Il2CppSystem.Collections.Generic.List<IResourceLocation> list;
            if (map.TryGetValue(k, out var have) && (object)have != null) list = have.TryCast<Il2CppSystem.Collections.Generic.List<IResourceLocation>>();
            else list = null;
            if (list == null)
            {
                list = new Il2CppSystem.Collections.Generic.List<IResourceLocation>();
                map[k] = list.Cast<Il2CppSystem.Collections.Generic.IList<IResourceLocation>>();
            }
            list.Add(loc.Cast<IResourceLocation>());
        }

        /// <summary>A bundle file on disk as an Addressables location (loaded with AssetBundle.LoadFromFile; no CRC or hash check: the
        /// server's checksum is checked when the file is fetched).</summary>
        private static ResourceLocationBase BundleLocation(string name, string path)
        {
            var loc = new ResourceLocationBase(name, path, Il2CppType.Of<AssetBundleProvider>().FullName, Il2CppType.Of<IAssetBundleResource>(), new Il2CppReferenceArray<IResourceLocation>(0));
            var options = new AssetBundleRequestOptions
            {
                BundleName = name,
                BundleSize = new FileInfo(path).Length,
                Crc = 0,
                Hash = "",
                UseCrcForCachedBundle = false,
                Timeout = 0,
                RetryCount = 0,
            };
            loc.Data = options;
            return loc;
        }
    }
}
