using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using Il2CppEverguild.LiveOps;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace WarpforgeRevival
{
    /// <summary>
    /// Event titles are localization keys. The revival server can also send plain text ("text:Classic"),
    /// and a key with no translation falls back to its last segment instead of showing nothing.
    /// </summary>
    internal static class EventLabels
    {
        private static readonly HashSet<string> logged = new HashSet<string>();

        private static void Fix(LiveOpsEventData data, EventLabelReferenceType type, ref string __result)
        {
            try
            {
                if ((object)data == null) return;
                string key = LiveOpsAssetUtility.GetLabelKey(data, type);
                if (string.IsNullOrEmpty(key)) return;
                string before = __result;
                if (key.StartsWith("text:", StringComparison.Ordinal)) __result = key.Substring(5);
                else if (string.IsNullOrEmpty(__result)) __result = key.Substring(key.LastIndexOf('/') + 1);
                if (logged.Add(data.eventId + "/" + type))
                    RevivalMod.Log.Msg($"[labels] {data.eventId} {type}: key '{key}' game gave '{before}' -> '{__result}'");
            }
            catch (Exception e) { RevivalMod.Log.Warning("[labels] " + e.Message); }
        }

        [HarmonyPatch(typeof(LiveOpsAssetUtility), nameof(LiveOpsAssetUtility.GetLabel),
            new[] { typeof(LiveOpsEventData), typeof(EventLabelReferenceType), typeof(Il2CppReferenceArray<Il2CppSystem.Object>) })]
        private static class ByData
        {
            private static void Postfix(LiveOpsEventData liveops, EventLabelReferenceType type, ref string __result) => Fix(liveops, type, ref __result);
        }

        [HarmonyPatch(typeof(LiveOpsAssetUtility), nameof(LiveOpsAssetUtility.GetLabel),
            new[] { typeof(ILiveOps), typeof(EventLabelReferenceType), typeof(Il2CppReferenceArray<Il2CppSystem.Object>) })]
        private static class ByEvent
        {
            private static void Postfix(ILiveOps liveops, EventLabelReferenceType type, ref string __result)
            {
                try { if (liveops != null) Fix(liveops.GetBaseData(), type, ref __result); }
                catch (Exception e) { RevivalMod.Log.Warning("[labels] " + e.Message); }
            }
        }
    }
}
