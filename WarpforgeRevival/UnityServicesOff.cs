using System;
using HarmonyLib;
using Il2Cpp;

namespace WarpforgeRevival
{
    /// <summary>
    /// The game used three of Unity's online services, all tied to the publisher's Unity project:
    /// a sign-in (rejected by Unity since the shutdown, an error on every start), usage analytics
    /// (windows opened, matches and deck ids, sent to the publisher), and "remote config", which
    /// only carries two switches allowing troop and warlord voice lines.
    ///
    /// None of that is needed to play, so the game no longer contacts Unity: the set-up step is
    /// reported as done without running, analytics is switched off with the game's own switch, and
    /// the two voice-line switches are set on here instead of being fetched.
    /// </summary>
    internal static class UnityServicesOff
    {
        private static bool noted, voiceNoted;

        /// <summary>The game's own "no Unity analytics" switch; every analytics call checks it first.</summary>
        private static void AnalyticsOff()
        {
            try { GameAnalytics.disableDDNAAnalytics = true; }
            catch (Exception e) { RevivalMod.Log.Warning("[unity services] analytics switch: " + e.Message); }
        }

        private static void VoiceLinesOn()
        {
            try
            {
                var data = UnityRemoteConfigManager.GetRemoteConfigData();     // the game's built-in defaults when nothing was fetched
                if ((object)data == null) { if (!voiceNoted) { voiceNoted = true; RevivalMod.Log.Warning("[unity services] no voice-line settings to switch on"); } return; }
                bool troops = data.EnableTroopVOs, warlords = data.EnableWarlordVOs;
                data.EnableTroopVOs = true;
                data.EnableWarlordVOs = true;
                if (!voiceNoted)
                {
                    voiceNoted = true;
                    RevivalMod.Log.Msg($"[unity services] voice lines switched on by the mod (the game's own defaults were troops {(troops ? "on" : "off")}, warlords {(warlords ? "on" : "off")})");
                }
            }
            catch (Exception e) { if (!voiceNoted) { voiceNoted = true; RevivalMod.Log.Warning("[unity services] voice lines: " + e.Message); } }
        }

        // Start-up waits for this step; hand back "already finished" instead of running it.
        [HarmonyPatch(typeof(UnityServicesManager), nameof(UnityServicesManager.InitializeUnityServices))]
        private static class SetUp
        {
            private static bool Prefix(ref Il2CppSystem.Threading.Tasks.Task __result)
            {
                try
                {
                    var done = Il2CppSystem.Threading.Tasks.Task.CompletedTask;
                    if ((object)done == null) return true;          // cannot stand in for it: let the game do its own thing
                    AnalyticsOff();
                    VoiceLinesOn();
                    __result = done;
                    if (!noted) { noted = true; RevivalMod.Log.Msg("[unity services] not contacting Unity: sign-in, analytics and remote settings skipped"); }
                    return false;
                }
                catch (Exception e)
                {
                    RevivalMod.Log.Warning("[unity services] could not skip the Unity set-up (" + e.Message + "); the game runs it as before");
                    return true;
                }
            }
        }

        [HarmonyPatch(typeof(UnityServicesManager), nameof(UnityServicesManager.SignInWithPlayfabId))]
        private static class SignIn
        {
            private static bool Prefix(ref Il2CppSystem.Threading.Tasks.Task __result)
            {
                try
                {
                    var done = Il2CppSystem.Threading.Tasks.Task.CompletedTask;
                    if ((object)done == null) return true;
                    __result = done;
                    return false;
                }
                catch { return true; }
            }
        }

        // Would ask Unity for the two voice-line switches.
        [HarmonyPatch(typeof(UnityRemoteConfigManager), nameof(UnityRemoteConfigManager.Initialize))]
        private static class RemoteSettings
        {
            private static bool Prefix() { VoiceLinesOn(); return false; }
        }

        // Would start Unity's analytics; with the set-up skipped it has nothing to start.
        [HarmonyPatch(typeof(GameAnalytics), nameof(GameAnalytics.SetupUnityAnalytics))]
        private static class Analytics
        {
            private static bool Prefix() { AnalyticsOff(); VoiceLinesOn(); return false; }
        }
    }
}
