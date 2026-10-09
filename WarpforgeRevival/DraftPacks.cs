using System;
using System.Text.Json;
using HarmonyLib;
using Il2Cpp;
using Il2CppEverguild.LiveOps;

namespace WarpforgeRevival
{
    /// <summary>
    /// Draft packs offered by the server.
    ///
    /// The game picks the three packs to choose from itself (a shuffle of the ready-made packs of
    /// the warlord's army). On a revival server the server makes that choice: each draft answer
    /// carries "revivalDraftOffer" (pack ids, in the run's save or next to it), and the game is
    /// handed exactly those packs. The server accepts only a pack from its offer and uses its own
    /// list of the pack's cards. Without an offer from the server the game picks as it always did.
    /// </summary>
    internal static class DraftPacks
    {
        private const string Key = "revivalDraftOffer";
        private static volatile string[] offer;
        private static bool noted;

        /// <summary>Every server answer passes through here (main thread, before the game reads it).</summary>
        internal static void Watch(string body)
        {
            if (body == null || body.IndexOf(Key, StringComparison.Ordinal) < 0) return;
            try
            {
                using var doc = JsonDocument.Parse(body);
                var found = Find(doc.RootElement, 0);
                if (found == null) return;
                offer = found;
                RevivalMod.Log.Msg("[draft] packs offered by the server: " + (found.Length == 0 ? "none" : string.Join(", ", found)));
            }
            catch (Exception e) { RevivalMod.Log.Warning("[draft] could not read the pack offer: " + e.Message); }
        }

        private static string[] Find(JsonElement e, int depth)
        {
            if (depth > 12) return null;
            if (e.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in e.EnumerateObject())
                {
                    if (p.Name == Key && p.Value.ValueKind == JsonValueKind.Array)
                    {
                        var list = new System.Collections.Generic.List<string>();
                        foreach (var v in p.Value.EnumerateArray())
                            if (v.ValueKind == JsonValueKind.String) list.Add(v.GetString());
                        return list.ToArray();
                    }
                }
                foreach (var p in e.EnumerateObject())
                {
                    var inner = Find(p.Value, depth + 1);
                    if (inner != null) return inner;
                }
            }
            else if (e.ValueKind == JsonValueKind.Array)
            {
                foreach (var v in e.EnumerateArray())
                {
                    var inner = Find(v, depth + 1);
                    if (inner != null) return inner;
                }
            }
            return null;
        }

        /// <summary>Hooks the game's pack offer. Applied on its own (not with the other patches):
        /// the method belongs to a generic class, and a failure here must not stop anything else.</summary>
        internal static void Apply(HarmonyLib.Harmony harmony)
        {
            try
            {
                var target = AccessTools.Method(typeof(DraftEventBase<DraftEventSaveGame, DraftEventData>), nameof(DraftEventBase<DraftEventSaveGame, DraftEventData>.GetRandomPack));
                harmony.Patch(target, prefix: new HarmonyMethod(AccessTools.Method(typeof(Offer), nameof(Offer.Prefix))));
                RevivalMod.Log.Msg("[draft] pack offers come from the server");
            }
            catch (Exception e) { RevivalMod.Log.Warning("[draft] could not hook the pack offer (the game will pick packs, the server may refuse them): " + e.Message); }
        }

        private static class Offer
        {
            internal static bool Prefix(ref Il2CppSystem.Collections.Generic.List<PrebuiltPack> __result)
            {
                var ids = offer;
                if (ids == null || ids.Length == 0)
                {
                    if (!noted) { noted = true; RevivalMod.Log.Msg("[draft] no pack offer from the server; the game picks the packs"); }
                    return true;
                }
                try
                {
                    var list = new Il2CppSystem.Collections.Generic.List<PrebuiltPack>();
                    foreach (var id in ids)
                    {
                        var pack = AssetLocator.GetAsset<PrebuiltPack>(id);
                        if ((object)pack != null) list.Add(pack);
                        else RevivalMod.Log.Warning("[draft] the game has no pack " + id);
                    }
                    if (list.Count == 0) return true;
                    __result = list;
                    return false;
                }
                catch (Exception e)
                {
                    RevivalMod.Log.Warning("[draft] could not show the server's packs: " + e.Message);
                    return true;
                }
            }
        }
    }
}
