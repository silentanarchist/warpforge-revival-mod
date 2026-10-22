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
    /// 0.14.53: loaded from the folder when it holds a package (the owner's test); fetching it from the server for
    /// testers comes next.
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

        /// <summary>Before the game loads its card library: the package, if there is one, is made known to Addressables.</summary>
        [HarmonyPatch(typeof(PlayerDataManager), nameof(PlayerDataManager.LoadAllCards))]
        private static class BeforeCards
        {
            private static void Prefix() { try { Register(); } catch (Exception e) { RevivalMod.Log.Warning("[custom] package: " + e); } }
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

            // the locations as ResourceLocationData (keys, where, provider, type, dependency keys): the map wires the
            // dependencies up by key, like a built catalog does
            var data = new Il2CppSystem.Collections.Generic.List<ResourceLocationData>();
            data.Add(BundleData("custom-cards", cardsFile));
            data.Add(BundleData("custom-art", artFile));
            var texts = new Dictionary<string, Dictionary<string, (string name, string text)>>();
            int count = 0;
            foreach (var card in root.GetProperty("cards").EnumerateArray())
            {
                string id = card.GetProperty("id").GetString();
                string guid = card.GetProperty("guid").GetString();
                string art = card.GetProperty("art").GetString();
                if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(guid) || string.IsNullOrEmpty(art)) continue;
                data.Add(new ResourceLocationData(Keys(guid, "cards"), guid, Il2CppType.Of<BundledAssetProvider>(), Il2CppType.Of<RawCardScript>(), Keys("custom-cards")));
                data.Add(new ResourceLocationData(Keys(art), art, Il2CppType.Of<BundledAssetProvider>(), Il2CppType.Of<Sprite>(), Keys("custom-art")));
                data.Add(new ResourceLocationData(Keys(art), art, Il2CppType.Of<BundledAssetProvider>(), Il2CppType.Of<Texture2D>(), Keys("custom-art")));
                count++;
            }
            if (count == 0) { RevivalMod.Log.Warning("[custom] the package lists no cards"); return; }
            var map = new ResourceLocationMap("custom-tester-" + version, data.Cast<Il2CppSystem.Collections.Generic.IList<ResourceLocationData>>());
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
            RevivalMod.Log.Msg($"[custom] custom-card package v{version}: {count} card(s) made known to Addressables (bundles {Path.GetFileName(cardsFile)}, {Path.GetFileName(artFile)}); texts for {texts.Count}");
        }

        private static Il2CppStringArray Keys(params string[] keys)
        {
            var arr = new Il2CppStringArray(keys.Length);
            for (int i = 0; i < keys.Length; i++) arr[i] = keys[i];
            return arr;
        }

        /// <summary>A bundle file on disk as an Addressables location (loaded with AssetBundle.LoadFromFile; no CRC or hash check: the
        /// server's checksum is checked when the file is fetched).</summary>
        private static ResourceLocationData BundleData(string name, string path)
        {
            var loc = new ResourceLocationData(Keys(name), path, Il2CppType.Of<AssetBundleProvider>(), Il2CppType.Of<IAssetBundleResource>(), new Il2CppStringArray(0));
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
