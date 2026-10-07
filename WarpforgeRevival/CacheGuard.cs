#if !ANDROID_PORT   // Windows only; left out of the Android build
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using Il2Cpp;

namespace WarpforgeRevival
{
    /// <summary>
    /// The files the game downloaded from its original servers (card data, artwork, texts) cannot be
    /// downloaded again: those servers are gone. They live in the game's cache under
    /// AppData\LocalLow, and the game is free with that cache - it clears it when an update fails,
    /// when a file looks out of date, when it runs short of space, or when a file has not been used
    /// for a while. On a revival server every one of those is a mistake, because the "newer" version
    /// it expects to fetch no longer exists.
    ///
    /// Two protections:
    ///  1. nothing in the cache is deleted while the mod is loaded - every way the game and the
    ///     engine have of clearing cached files is switched off, and size / age limits are lifted;
    ///  2. the two irreplaceable sets of files (all cards, all texts) and the downloaded file index
    ///     are copied once to UserData\WarpforgeRevival\original-cache, so that even a later run
    ///     without the mod cannot lose them.
    /// </summary>
    internal static class CacheGuard
    {
        private static readonly HashSet<string> noted = new HashSet<string>();

        private static void Note(string what)
        {
            lock (noted) { if (!noted.Add(what)) return; }
            try { RevivalMod.Log.Msg("[cache] kept the original game files: " + what); } catch { }
        }

        // ------------------------------------------------------------------ nothing gets deleted
        private static bool RefuseBool(MethodBase __originalMethod, ref bool __result)
        {
            __result = false;                                   // "could not clear", which callers already handle
            Note("the game asked to clear its cache (" + __originalMethod.Name + "); refused");
            return false;
        }

        private static bool RefuseVoid(MethodBase __originalMethod)
        {
            Note("the game asked to remove a downloaded file (" + __originalMethod.Name + "); refused");
            return false;
        }

        private static void KeepOnUpdate(ref bool cleanCache)
        {
            if (!cleanCache) return;
            cleanCache = false;
            Note("an update wanted to start from an empty cache; refused");
        }

        private static void NoSizeLimit(ref long value) => value = long.MaxValue;

        private static void LongestAge(ref int value) => value = 12960000;      // 150 days, the most the engine allows

        /// <summary>Folders holding downloaded game files: never removed, whoever asks.</summary>
        private static bool KeepFolder(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return true;
                string full = Path.GetFullPath(path).Replace('/', '\\');
                if (full.IndexOf("\\AppData\\LocalLow\\Unity\\", StringComparison.OrdinalIgnoreCase) < 0) return true;
                Note("the game tried to delete a cache folder (" + Path.GetFileName(full.TrimEnd('\\')) + "); refused");
                return false;
            }
            catch { return true; }
        }

        private static HarmonyMethod Mine(string name) =>
            new HarmonyMethod(typeof(CacheGuard).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic));

        private static int Cover(HarmonyLib.Harmony harmony, Type type, string method, string prefix, Type[] args = null)
        {
            int done = 0;
            try
            {
                const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
                foreach (MethodInfo m in type.GetMethods(all))
                {
                    if (m.Name != method) continue;
                    if (args != null)
                    {
                        var ps = m.GetParameters();
                        if (ps.Length != args.Length) continue;
                        bool same = true;
                        for (int i = 0; i < ps.Length; i++) same &= ps[i].ParameterType == args[i];
                        if (!same) continue;
                    }
                    try { harmony.Patch(m, prefix: Mine(prefix)); done++; }
                    catch (Exception e) { RevivalMod.Log.Warning($"[cache] could not guard {type.Name}.{method}: {e.Message}"); }
                }
                if (done == 0) RevivalMod.Log.Warning($"[cache] {type.Name}.{method} was not found, so it is not guarded");
            }
            catch (Exception e) { RevivalMod.Log.Warning($"[cache] {type.Name}.{method}: {e.Message}"); }
            return done;
        }

        internal static void Apply(HarmonyLib.Harmony harmony, string dataDir)
        {
            int n = 0;
            var caching = typeof(UnityEngine.Caching);
            n += Cover(harmony, caching, "ClearCache", nameof(RefuseBool), Type.EmptyTypes);
            n += Cover(harmony, caching, "ClearAllCachedVersions", nameof(RefuseBool));
            n += Cover(harmony, caching, "ClearCachedVersionInternal", nameof(RefuseBool));
            n += Cover(harmony, caching, "ClearCachedVersions", nameof(RefuseBool));
            var cache = typeof(UnityEngine.Cache);
            n += Cover(harmony, cache, "Cache_SetMaximumDiskSpaceAvailable", nameof(NoSizeLimit));
            n += Cover(harmony, cache, "Cache_SetExpirationDelay", nameof(LongestAge));
            n += Cover(harmony, typeof(AssetBundleManager), nameof(AssetBundleManager.ClearBundleFromCache), nameof(RefuseVoid));
            n += Cover(harmony, typeof(AddressablesManager), nameof(AddressablesManager.DownloadContentUpdate), nameof(KeepOnUpdate));
            n += Cover(harmony, typeof(Il2CppSystem.IO.Directory), "Delete", nameof(KeepFolder), new[] { typeof(string), typeof(bool) });
            RevivalMod.Log.Msg($"[cache] the game's downloaded files are protected from deletion ({n} guards)");

            // Lift the limits on the caches that already exist (the guards above only catch new settings).
            try
            {
                var paths = new Il2CppSystem.Collections.Generic.List<string>();
                UnityEngine.Caching.GetAllCachePaths(paths);
                for (int i = 0; i < paths.Count; i++)
                {
                    var c = UnityEngine.Caching.GetCacheByPath(paths[i]);
                    c.maximumAvailableStorageSpace = long.MaxValue;
                    c.expirationDelay = 12960000;
                }
            }
            catch (Exception e) { RevivalMod.Log.Msg("[cache] limits will be lifted when the game sets them (" + e.Message + ")"); }

            Task.Run(() => Backup(dataDir));
        }

        // ------------------------------------------------------------------ a second copy
        private static readonly string[] Irreplaceable = { "allcards", "localization" };

        private static void Backup(string dataDir)
        {
            try
            {
                string localLow = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "Low";
                string target = Path.Combine(dataDir, "original-cache");
                int files = 0;
                long bytes = 0;
                var found = new List<string>();

                // Downloaded files: AppData\LocalLow\Unity\<studio>_<game>\<file name>\<version>\__data
                string unity = Path.Combine(localLow, "Unity");
                if (Directory.Exists(unity))
                    foreach (string root in Directory.GetDirectories(unity))
                    {
                        string rootName = Path.GetFileName(root);
                        if (rootName.IndexOf("warpforge", StringComparison.OrdinalIgnoreCase) < 0 && rootName.IndexOf("everguild", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        foreach (string dir in Directory.GetDirectories(root))
                        {
                            string name = Path.GetFileName(dir);
                            if (!Array.Exists(Irreplaceable, k => name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                            found.Add(name);
                            Copy(dir, Path.Combine(target, rootName, name), ref files, ref bytes);
                        }
                    }

                // The downloaded index of those files (small).
                string catalog = Path.Combine(localLow, "Everguild", "Warpforge", "com.unity.addressables");
                if (Directory.Exists(catalog)) Copy(catalog, Path.Combine(target, "com.unity.addressables"), ref files, ref bytes);

                if (found.Count == 0)
                {
                    RevivalMod.Log.Msg("[cache] no original card or text files in this computer's cache (nothing to back up)");
                    return;
                }
                File.WriteAllText(Path.Combine(target, "READ ME.txt"),
                    "These are copies of the original Warpforge game files from this computer's cache,\r\n" +
                    "made by the Warpforge Revival mod so they cannot be lost. The game's own servers\r\n" +
                    "are gone, so these files cannot be downloaded again.\r\n\r\n" +
                    "If the server owner asked for the original card and text files, zip this whole\r\n" +
                    "folder and send it to them.\r\n");
                RevivalMod.Log.Msg($"[cache] ORIGINAL GAME FILES FOUND: {string.Join(", ", found)}. " +
                                   (files > 0 ? $"Backed up {files} file(s), {bytes / 1048576} MB, to {target}" : $"Already backed up in {target}"));
            }
            catch (Exception e) { RevivalMod.Log.Warning("[cache] backup: " + e.Message); }
        }

        private static void Copy(string from, string to, ref int files, ref long bytes)
        {
            Directory.CreateDirectory(to);
            foreach (string f in Directory.GetFiles(from))
            {
                string dest = Path.Combine(to, Path.GetFileName(f));
                long size = new FileInfo(f).Length;
                if (File.Exists(dest) && new FileInfo(dest).Length == size) continue;
                try
                {
                    // the game may have the file open; reading alongside it is fine
                    using (var src = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var dst = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None))
                        src.CopyTo(dst);
                    files++;
                    bytes += size;
                }
                catch (Exception e) { RevivalMod.Log.Warning($"[cache] could not copy {Path.GetFileName(f)}: {e.Message}"); }
            }
            foreach (string d in Directory.GetDirectories(from))
                Copy(d, Path.Combine(to, Path.GetFileName(d)), ref files, ref bytes);
        }
    }
}
#endif
