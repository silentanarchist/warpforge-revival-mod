using System;
using HarmonyLib;
using Il2Cpp;

namespace WarpforgeRevival
{
    /// <summary>Temporary diagnostics for battles: why the computer opponent stalls on its turn.</summary>
    internal static class BattleDiagnostics
    {
        private static BattleManager battle;
        private static float nextCheck;
        private static int stallLogs, turnLogs;
        private static string lastState;

        private static string Count(Il2CppSystem.Collections.Generic.List<BattleAction> q) => q == null ? "null" : q.Count.ToString();

        /// <summary>Called from the mod's update loop.</summary>
        internal static void Tick()
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now < nextCheck) return;
            nextCheck = now + 2f;
            try
            {
                if ((object)battle == null || battle.Pointer == IntPtr.Zero || battle.m_CachedPtr == IntPtr.Zero) return;
                if (battle.IsPlayerTurn()) { lastState = null; return; }
                string queues = "?";
                try { queues = battle.DebugQueueCompressed(battle.actionQueue); } catch { }
                string state = $"allowEnemyActions={battle.allowEnemyActionsFlag} inQueue={battle.ActionsInQueue()} " +
                               $"action={Count(battle.actionQueue)} instant={Count(battle.instantQueue)} delayed={Count(battle.delayedInstantQueue)} " +
                               $"interrupt={Count(battle.interruptQueue)} resolving={(bool)battle.resolvingActionFlag} stage={battle._battleStage} queue=[{queues}]";
                if (state == lastState && stallLogs > 3) return;
                if (stallLogs++ > 40) return;
                lastState = state;
                RevivalMod.Log.Msg("[battlediag] enemy turn: " + state);
            }
            catch (Exception e)
            {
                if (stallLogs++ < 5) RevivalMod.Log.Warning("[battlediag] " + e.Message);
            }
        }

        [HarmonyPatch(typeof(BattleManager), nameof(BattleManager.PlayAi))]
        private static class PlayAi
        {
            private static void Prefix(BattleManager __instance)
            {
                battle = __instance; stallLogs = 0; lastState = null;
                RevivalMod.Log.Msg("[battlediag] AI turn started");
            }
        }

        [HarmonyPatch(typeof(AI), nameof(AI.PlayTurn))]
        private static class PlayTurn
        {
            private static void Prefix(BattleManager manager, AIModes mode)
            {
                if (turnLogs++ > 60) return;
                int n = -1;
                try { n = manager.GetAvailableActions(false, true)?.Count ?? -1; } catch (Exception e) { RevivalMod.Log.Warning("[battlediag] GetAvailableActions threw: " + e.Message.Split('\n')[0]); }
                RevivalMod.Log.Msg($"[battlediag] AI.PlayTurn mode={mode} availableActions={n}");
            }
            private static void Postfix(bool __result)
            {
                if (turnLogs <= 61) RevivalMod.Log.Msg($"[battlediag] AI.PlayTurn -> played={__result}");
            }
        }
    }
}
