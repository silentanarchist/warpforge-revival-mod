using System;
using System.Text.Json;
using HarmonyLib;
using Il2Cpp;
using Il2CppEverguild.Utils;
using Il2CppPlayFab.ClientModels;

namespace WarpforgeRevival
{
    /// <summary>
    /// Match replays.
    ///
    /// The game sends each match's recording to the server when the match ends, and asks for it
    /// back when Replay is pressed. The server answers {"recordingData": recording}, but the
    /// name the game itself looks the recording up under is not readable from its files, so the
    /// mod reads the server's answer here and hands the game the recording. When the answer has
    /// no recording the game's own handling runs (and shows "Error loading match replay").
    /// </summary>
    internal static class Replays
    {
        [HarmonyPatch(typeof(PlayerDataManager), nameof(PlayerDataManager.ProcessMatchRecording))]
        private static class Load
        {
            private static bool Prefix(ExecuteCloudScriptResult result, ref BattleRecordData __result)
            {
                try
                {
                    if (result == null || result.FunctionResult == null) return true;
                    string json = JsonWrapper.SerializeObject(result.FunctionResult);
                    if (string.IsNullOrEmpty(json)) return true;
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                        !doc.RootElement.TryGetProperty("recordingData", out var rec) || rec.ValueKind != JsonValueKind.Object)
                        return true;
                    var record = JsonWrapper.DeserializeObject<BattleRecordData>(rec.GetRawText());
                    if (record == null) return true;
                    __result = record;
                    RevivalMod.Log.Msg("[replay] recording loaded");
                    return false;
                }
                catch (Exception e)
                {
                    RevivalMod.Log.Warning("[replay] could not read the recording: " + e.Message);
                    return true;
                }
            }
        }
    }
}
