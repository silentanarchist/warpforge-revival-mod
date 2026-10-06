using System;
using HarmonyLib;
using Il2Cpp;
using Il2CppEverguild.LiveOps;
using Il2CppEverguild.UI.Notifications;

namespace WarpforgeRevival
{
    /// <summary>
    /// The server does not run seasonal campaigns (reward tracks) yet. The menu's notification
    /// badges assume one always exists; report "no campaign badges" instead of crashing.
    /// (Patched method verified to have unique native code.)
    /// </summary>
    internal static class LiveOpsGuards
    {
        [HarmonyPatch(typeof(CampaignHandler), nameof(CampaignHandler.CheckNotification))]
        private static class CampaignBadges
        {
            private static bool logged;

            private static bool Prefix(CampaignHandler __instance,
                ref Il2CppSystem.Collections.Generic.IEnumerable<CampaignBadge> __result)
            {
                try
                {
                    if (__instance.GetCampaign(__instance.SelectedArmy) != null) return true;
                    if (__instance.GetCampaign((CardArmy)10) != null) return true;
                }
                catch (Exception e)
                {
                    RevivalMod.Log.Warning("[liveops] campaign lookup failed: " + e.Message);
                }
                if (!logged) { logged = true; RevivalMod.Log.Msg("[liveops] no campaign running; skipping campaign badges"); }
                __result = new Il2CppSystem.Collections.Generic.List<CampaignBadge>()
                    .Cast<Il2CppSystem.Collections.Generic.IEnumerable<CampaignBadge>>();
                return false;
            }
        }

        // Screens ask for leaderboards the server has not defined (legend points, old seasons...) and
        // crash when the definition is missing. Hand out a definition on demand instead.
        [HarmonyPatch(typeof(LeaderboardConfig), nameof(LeaderboardConfig.GetLeaderboardDefinition))]
        private static class AnyLeaderboard
        {
            private static readonly System.Collections.Generic.Dictionary<string, LeaderboardDefinition> made =
                new System.Collections.Generic.Dictionary<string, LeaderboardDefinition>();

            private static void Postfix(string key, ref LeaderboardDefinition __result)
            {
                try
                {
                    if ((object)__result != null || string.IsNullOrEmpty(key)) return;
                    if (!made.TryGetValue(key, out var def))
                    {
                        def = new PlayfabLeaderboardDefinition().Cast<LeaderboardDefinition>();
                        def.leaderboardKey = key;
                        def.scoringMode = LeaderboardScoringMode.SetValue;
                        def.entryCountToFetch = 50;
                        made[key] = def;
                        RevivalMod.Log.Msg($"[liveops] leaderboard '{key}' is not defined on the server; using an empty one");
                    }
                    __result = def;
                }
                catch (Exception e) { RevivalMod.Log.Warning("[liveops] leaderboard fallback failed: " + e.Message); }
            }
        }
    }
}
