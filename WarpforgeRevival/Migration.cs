using System;
using System.Collections.Generic;
using System.IO;
using MelonLoader;
#if !ANDROID_PORT
using System.Diagnostics;
#endif

namespace WarpforgeRevival
{
    /// <summary>
    /// The rename to WF Revival (0.15.0; claude/rename-wfrevival-plan.md). The mod's file is now
    /// Mods\WFRevival\WFRevival.dll on PC and Mods\WFRevival.Android.dll on Android. Every 0.14.x updater (and the
    /// owner's install bats) wrote the downloaded build under the OLD name, so this build often starts from
    /// Mods\WarpforgeRevival\WarpforgeRevival.dll (or Mods\WarpforgeRevival.dll, or Mods\WarpforgeRevival.Android.dll).
    /// It then moves itself:
    ///  - PC: copies itself to the new place now (a loaded file can be copied, not deleted) and leaves a small script
    ///    that waits for the game to close (normally or not) and deletes the old copy and the old folder. Nothing is
    ///    restarted; the next start loads only the new copy.
    ///  - Android: copies itself and deletes the old file at once (a file in use can be deleted there); the next start
    ///    loads the new copy.
    /// Two copies loaded at once (a failed clean-up): the copy in the old place stays switched off when a copy is
    /// already in the new place, and a copy in the new place stays switched off for this run when an OLDER mod
    /// (0.14.x) was loaded from the old place too, so the mod never runs twice; either way the old copy is removed.
    /// Owner, 2026-10-23: this stays in every build for a long time, so a player who updates late still lands on the
    /// new name (claude/rename-wfrevival-decisions.md).
    /// </summary>
    internal static class Migration
    {
#if ANDROID_PORT
        internal const string FileName = "WFRevival.Android.dll";
        private const string OldFileName = "WarpforgeRevival.Android.dll";
#else
        internal const string FileName = "WFRevival.dll";
        internal const string Folder = "WFRevival";
        private const string OldFileName = "WarpforgeRevival.dll";
        private const string OldFolder = "WarpforgeRevival";
#endif

        private static string Mods => MelonLoader.Utils.MelonEnvironment.ModsDirectory;

        /// <summary>Where this build belongs.</summary>
        internal static string NewPath =>
#if ANDROID_PORT
            Path.Combine(Mods, FileName);
#else
            Path.Combine(Mods, Folder, FileName);
#endif

        /// <summary>Every place an older build (or an older updater) put the mod.</summary>
        internal static IEnumerable<string> OldPaths()
        {
#if ANDROID_PORT
            yield return Path.Combine(Mods, OldFileName);
#else
            yield return Path.Combine(Mods, OldFolder, OldFileName);
            yield return Path.Combine(Mods, OldFileName);
#endif
        }

        /// <summary>Every place a copy of the mod may be (new and old), for the bug report's "updated since" check.</summary>
        internal static IEnumerable<string> AllPaths()
        {
            yield return NewPath;
            foreach (var p in OldPaths()) yield return p;
        }

        private static bool Same(string a, string b)
        {
            try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        private static bool IsOld(string path)
        {
            foreach (var p in OldPaths()) if (Same(p, path)) return true;
            return false;
        }

        /// <summary>
        /// Run first thing at start-up. False when this copy must stay switched off for this run (another copy runs).
        /// Never throws: a failed move leaves the mod running from where it is, and the next start tries again.
        /// </summary>
        internal static bool Run(string me, string dataDir)
        {
            try
            {
                if (string.IsNullOrEmpty(me) || !File.Exists(me)) return true;
                if (IsOld(me))
                {
                    if (File.Exists(NewPath) && !Same(me, NewPath))
                    {
                        RevivalMod.Log.Msg($"[rename] a copy of the mod is already at {NewPath}; this copy at {me} stays switched off and is removed");
                        Remove(new List<string> { me }, dataDir);
                        return false;
                    }
                    MoveTo(me, dataDir);
                    return true;
                }
                // running from the new place (or somewhere else): old copies are left-overs
                var olds = new List<string>();
                foreach (var p in OldPaths()) if (File.Exists(p) && !Same(p, me)) olds.Add(Path.GetFullPath(p));
                if (olds.Count == 0) return true;
                bool olderRunning = OlderLoadedFromOldPlace();
                RevivalMod.Log.Msg($"[rename] {olds.Count} old cop{(olds.Count == 1 ? "y" : "ies")} of the mod found ({string.Join(", ", olds)}); removed " +
                                   (Platform == "pc" ? "once the game closes" : "now") +
                                   (olderRunning ? "; an older mod was loaded from there too, so this copy stays switched off for this run" : ""));
                Remove(olds, dataDir);
                return !olderRunning;
            }
            catch (Exception e)
            {
                RevivalMod.Log.Warning("[rename] " + e.GetType().Name + ": " + e.Message);
                return true;
            }
        }

        private static string Platform =>
#if ANDROID_PORT
            "android";
#else
            "pc";
#endif

        /// <summary>Whether MelonLoader also loaded a mod older than 0.15.0 from one of the old places (it would run
        /// alongside this one).</summary>
        private static bool OlderLoadedFromOldPlace()
        {
            try
            {
                foreach (var melon in MelonMod.RegisteredMelons)
                {
                    string where = melon?.MelonAssembly?.Location;
                    if (string.IsNullOrEmpty(where) || !IsOld(where)) continue;
                    var v = RevivalMod.Number(melon.Info?.Version ?? "");
                    var mine = RevivalMod.Number(RevivalMod.Version);
                    if (v == null || mine == null || v < mine) return true;
                }
            }
            catch { }
            return false;
        }

        private static void MoveTo(string me, string dataDir)
        {
            string target = NewPath;
            string folder = Path.GetDirectoryName(target);
            Directory.CreateDirectory(folder);
#if !ANDROID_PORT
            // MelonLoader only loads mods from a folder inside Mods when that folder holds a manifest.json
            string manifest = Path.Combine(folder, "manifest.json");
            if (!File.Exists(manifest))
                File.WriteAllText(manifest,
                    "{\n  \"name\": \"WFRevival\",\n  \"description\": \"WF Revival mod. MelonLoader needs this file to load mods from this folder - do not delete it.\"\n}\n");
#endif
            string fresh = target + ".new";
            File.Copy(me, fresh, true);
            File.Move(fresh, target, true);
            RevivalMod.Log.Msg($"[rename] the mod copied itself from {me} to {target}");
            var olds = new List<string>();
            foreach (var p in OldPaths()) if (File.Exists(p)) olds.Add(Path.GetFullPath(p));
            Remove(olds, dataDir);
        }

        /// <summary>Deletes old copies: on Android at once; on PC once this game process has ended (the files are in use),
        /// then the old folder when nothing else is in it.</summary>
        private static void Remove(List<string> files, string dataDir)
        {
            if (files.Count == 0) return;
#if ANDROID_PORT
            foreach (var f in files)
                try { File.Delete(f); RevivalMod.Log.Msg("[rename] removed " + f); }
                catch (Exception e) { RevivalMod.Log.Warning($"[rename] could not remove {f}: {e.Message} (tried again at the next start)"); }
#else
            try
            {
                Directory.CreateDirectory(dataDir);
                int pid = Environment.ProcessId;
                string oldFolder = Path.Combine(Mods, OldFolder);
                string script = Path.Combine(dataDir, "move-mod.cmd");
                string log = Path.Combine(dataDir, "update.log");
                var text = new System.Text.StringBuilder();
                text.Append("@echo off\r\n:wait\r\n");
                text.Append($"tasklist /FI \"PID eq {pid}\" 2>nul | find \" {pid} \" >nul\r\n");
                text.Append("if not errorlevel 1 (\r\n  ping -n 2 127.0.0.1 >nul\r\n  goto wait\r\n)\r\n");
                // only while the new copy is in place: never leave the game without the mod
                foreach (var f in files)
                    text.Append($"if exist \"{NewPath}\" del /q \"{f}\" >> \"{log}\" 2>&1\r\n");
                text.Append($"if exist \"{NewPath}\" if exist \"{oldFolder}\\manifest.json\" del /q \"{oldFolder}\\manifest.json\" >> \"{log}\" 2>&1\r\n");
                text.Append($"if exist \"{NewPath}\" rmdir \"{oldFolder}\" >nul 2>&1\r\n");
                text.Append($"echo %date% %time% old copies of the mod removed (WF Revival rename) >> \"{log}\"\r\n");
                File.WriteAllText(script, text.ToString());
                Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"\"{script}\"\"")
                {
                    UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = dataDir
                });
                RevivalMod.Log.Msg($"[rename] old cop{(files.Count == 1 ? "y" : "ies")} ({string.Join(", ", files)}) removed when the game closes");
            }
            catch (Exception e) { RevivalMod.Log.Warning("[rename] could not set up the clean-up: " + e.Message + " (tried again at the next start)"); }
#endif
        }
    }
}
