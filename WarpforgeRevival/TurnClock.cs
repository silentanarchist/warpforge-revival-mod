using System;
using System.Reflection;
using HarmonyLib;
using Il2Cpp;

namespace WarpforgeRevival
{
    /// <summary>
    /// The turn timer length is part of the game's built-in battle rules (60 seconds). Set it each
    /// time a battle's clock is set up, from the server's settings (so both players agree); the
    /// local TurnSeconds setting is only used when the server does not provide one.
    /// </summary>
    internal static class TurnClock
    {
        [HarmonyPatch(typeof(ClockManager), nameof(ClockManager.Initialize))]
        private static class Setup
        {
            private static void Prefix(ClockManager __instance) => Apply(__instance);
            private static void Postfix(ClockManager __instance) => Apply(__instance);
        }

        [HarmonyPatch(typeof(ClockManager), nameof(ClockManager.StartTimer))]
        private static class EachTurn
        {
            private static void Prefix(ClockManager __instance) => Apply(__instance);
        }

        private static bool noted;

        private static void Apply(ClockManager clock)
        {
            try
            {
                ServerSettings.Refresh();
                int seconds = ServerSettings.TurnSeconds ?? RevivalMod.Config.TurnSeconds;
                if (seconds < 20 || seconds > 3600) return;
                PropertyInfo p = AccessTools.Property(typeof(ClockManager), "manager");
                if (p == null) return;
                bool isStatic = p.GetGetMethod(true) != null && p.GetGetMethod(true).IsStatic;
                var battle = p.GetValue(isStatic ? null : clock) as BattleManager;
                if ((object)battle == null || battle.Pointer == IntPtr.Zero) return;
                var rules = battle.scenarioVariables;
                if ((object)rules == null) return;
                float before = rules.clockTimeLimit;
                if (Math.Abs(before - seconds) < 0.5f) return;
                // Keep the shorter clock (used after a player lets a turn run out) in the same proportion.
                float ratio = before > 1f ? rules.clockTimeLimitReduced / before : 0.5f;
                rules.clockTimeLimit = seconds;
                rules.clockTimeLimitReduced = Math.Max(10f, seconds * ratio);
                if (!noted)
                {
                    noted = true;
                    RevivalMod.Log.Msg($"[clock] turn timer {before:0}s -> {seconds}s (short clock {rules.clockTimeLimitReduced:0}s)");
                }
            }
            catch (Exception e) { RevivalMod.Log.Warning("[clock] " + e.Message); }
        }
    }
}
