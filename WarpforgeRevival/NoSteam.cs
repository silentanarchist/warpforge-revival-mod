#if !ANDROID_PORT   // Windows only; left out of the Android build
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Il2Cpp;

namespace WarpforgeRevival
{
    /// <summary>
    /// Lets the game start and sign in when Steam is not available (a copy of the game files that
    /// was not installed through Steam, or Steam simply not running).
    ///
    /// Without Steam the game stops in three places before it ever contacts the server:
    ///  - on a first run it asks Steam whether this is a Steam Deck, which throws and aborts start-up;
    ///  - it waits, forever, for Steam to report that it has started;
    ///  - it asks Steam for a login ticket.
    /// When Steam did not start, every Steam call the game makes is answered with "nothing" instead,
    /// the wait is ended, and the player signs in with an id kept on this computer.
    /// With Steam running nothing here changes anything.
    /// </summary>
    internal static class NoSteam
    {
        /// <summary>True once Steam itself reported a successful start.</summary>
        private static bool steamUp;
        private static bool announced;
        private static string localId;

        private static string LocalId()
        {
            if (localId != null) return localId;
            string dir = Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "WarpforgeRevival");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "player-id.txt");
            try
            {
                if (File.Exists(file))
                {
                    string saved = File.ReadAllText(file).Trim();
                    if (saved.Length >= 16) return localId = saved;
                }
            }
            catch { }
            localId = Guid.NewGuid().ToString("N");
            try { File.WriteAllText(file, localId); }
            catch (Exception e) { RevivalMod.Log.Warning("[steam] could not save the local player id: " + e.Message); }
            return localId;
        }

        /// <summary>
        /// A game login file downloaded from the Revival site (profile page) signs the game in as the
        /// game account linked to that site account, with or without Steam. Returns its contents, or
        /// null when there is no such file. While the file is present it always wins.
        /// </summary>
        private static string GameLoginFile()
        {
            try
            {
                string file = Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "WarpforgeRevival", "game-login.txt");
                if (!File.Exists(file)) return null;
                string text = File.ReadAllText(file).Trim();
                if (text.StartsWith("wfr-login-") && text.Length > 40 && text.Length < 200) return text;
                RevivalMod.Log.Warning("[steam] game-login.txt is not a game login file from the site - ignored");
            }
            catch (Exception e) { RevivalMod.Log.Warning("[steam] could not read game-login.txt: " + e.Message); }
            return null;
        }

        private static void Announce()
        {
            if (announced) return;
            announced = true;
            RevivalMod.Log.Msg("[steam] Steam is not available - running without it");
        }

        private static Type SteamType(string name)
        {
            return AccessTools.TypeByName("Il2CppSteamworks." + name) ?? AccessTools.TypeByName("Steamworks." + name);
        }

        /// <summary>Applied by hand so a missing method only costs a log line, never the whole mod.</summary>
        internal static void Apply(HarmonyLib.Harmony harmony)
        {
            int done = 0;
            var skipWithoutSteam = new HarmonyMethod(typeof(NoSteam).GetMethod(nameof(SkipWithoutSteam), BindingFlags.Static | BindingFlags.NonPublic));
            var wanted = new Dictionary<string, string[]>
            {
                { "SteamUtils", new[] { "IsSteamRunningOnSteamDeck", "IsOverlayEnabled", "ShowFloatingGamepadTextInput" } },
                { "SteamUser", new[] { "GetSteamID", "EndAuthSession", "GetAuthSessionTicket" } },
                { "SteamUserStats", new[] { "GetUserStat", "StoreStats", "SetAchievement", "SetStat", "RequestUserStats", "GetAchievement" } },
                { "SteamApps", new[] { "GetDLCCount", "BIsDlcInstalled", "BGetDLCDataByIndex" } },
                { "SteamClient", new[] { "SetWarningMessageHook" } },
                { "SteamAPI", new[] { "RunCallbacks", "Shutdown" } },
            };
            foreach (var kv in wanted)
            {
                Type t = SteamType(kv.Key);
                if (t == null) { RevivalMod.Log.Warning("[steam] type not found: " + kv.Key); continue; }
                foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    if (Array.IndexOf(kv.Value, m.Name) < 0) continue;
                    try { harmony.Patch(m, prefix: skipWithoutSteam); done++; }
                    catch (Exception e) { RevivalMod.Log.Warning($"[steam] could not cover {kv.Key}.{m.Name}: {e.Message}"); }
                }
            }

            Patch(harmony, SteamType("SteamAPI"), "Init", ref done, postfix: nameof(InitDone));
            Patch(harmony, SteamType("NativeMethods"), "SteamAPI_RestartAppIfNecessary", ref done, prefix: nameof(NeverRelaunch));
            RevivalMod.Log.Msg($"[steam] {done} Steam calls covered for running without Steam");
        }

        private static void Patch(HarmonyLib.Harmony harmony, Type t, string name, ref int done, string prefix = null, string postfix = null)
        {
            const BindingFlags mine = BindingFlags.Static | BindingFlags.NonPublic;
            MethodInfo m = t == null ? null : AccessTools.Method(t, name);
            if (m == null) { RevivalMod.Log.Warning("[steam] method not found: " + name); return; }
            try
            {
                harmony.Patch(m,
                    prefix: prefix == null ? null : new HarmonyMethod(typeof(NoSteam).GetMethod(prefix, mine)),
                    postfix: postfix == null ? null : new HarmonyMethod(typeof(NoSteam).GetMethod(postfix, mine)));
                done++;
            }
            catch (Exception e) { RevivalMod.Log.Warning($"[steam] could not cover {name}: {e.Message}"); }
        }

        /// <summary>Skips a Steam call when Steam is not running; the caller gets false / zero / nothing.</summary>
        private static bool SkipWithoutSteam() => steamUp;

        private static void InitDone(bool __result)
        {
            if (__result) steamUp = true;
            else Announce();
        }

        /// <summary>The game would close itself and ask Steam to start it again; there is nothing to gain from that now.</summary>
        private static bool NeverRelaunch(ref bool __result)
        {
            __result = false;
            return false;
        }

        /// <summary>Start-up waits for this to become true. Without Steam it never would.</summary>
        [HarmonyPatch(typeof(SteamManager), nameof(SteamManager.Initialized), MethodType.Getter)]
        private static class Ready
        {
            private static void Postfix(ref bool __result)
            {
                if (__result) { steamUp = true; return; }
                if (!steamUp) __result = true;
            }
        }

        [HarmonyPatch(typeof(SteamManager), nameof(SteamManager.RequestUserStats))]
        private static class Stats
        {
            private static bool Prefix() => steamUp;
        }

        /// <summary>Steam purchases: without Steam, report "no store" exactly as the game itself would.</summary>
        [HarmonyPatch(typeof(InAppPurchaseSteam), nameof(InAppPurchaseSteam.Init))]
        private static class Store
        {
            private static bool Prefix()
            {
                if (steamUp) return true;
                try
                {
                    MethodInfo m = AccessTools.Method(typeof(InAppPurchaseProxy), "SetBillingNotSupported");
                    if (m != null && m.IsStatic) m.Invoke(null, null);
                }
                catch (Exception e) { RevivalMod.Log.Warning("[steam] store switch-off failed: " + e.Message); }
                return false;
            }
        }

        [HarmonyPatch(typeof(PlayerDataManager), nameof(PlayerDataManager.GetSteamAuthTicket))]
        private static class Ticket
        {
            private static bool Prefix(ref string __result)
            {
                string file = GameLoginFile();
                if (file != null)
                {
                    __result = file;
                    RevivalMod.Log.Msg("[steam] signing in with the game login file (UserData/WarpforgeRevival/game-login.txt)");
                    return false;
                }
                if (steamUp) return true;
                __result = "nosteam-" + LocalId();
                RevivalMod.Log.Msg("[steam] signing in with this computer's saved player id");
                return false;
            }
        }
    }
}
#endif
