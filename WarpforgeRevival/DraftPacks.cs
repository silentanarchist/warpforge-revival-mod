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
    ///
    /// After a pick the game draws the next packs twice: when the pick animation ends, and again when
    /// the server confirms the pick. The game's own choice was the same both times (seeded by the
    /// round); the server's next offer only comes with its answer. So when the answer is slower than
    /// the animation (about a second, common on phones), the first drawing showed the packs just chosen
    /// from, with the next round's number, until the answer replaced them - and tapping one of them
    /// then was refused. Now, from a pick until the server's answer (with its next offer) arrives,
    /// the packs are not drawn; the answer draws the new ones. A refused pick also carries the offer,
    /// so it ends the wait too; a pick that never gets an answer stops waiting after 15 seconds.
    /// </summary>
    internal static class DraftPacks
    {
        private const string Key = "revivalDraftOffer";
        private static volatile string[] offer;
        private static bool noted;
        private const long WaitLimitMs = 15000;
        private static long waitingSince;       // when a pick was sent and its answer had not come yet (0 = not waiting)

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
                waitingSince = 0;
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
            try
            {
                harmony.Patch(AccessTools.Method(typeof(DraftModeSelectPacksStateDemo), nameof(DraftModeSelectPacksStateDemo.PackSelected)),
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(Wait), nameof(Wait.Picked))));
                harmony.Patch(AccessTools.Method(typeof(DraftModeSelectPacksStateDemo), nameof(DraftModeSelectPacksStateDemo.ShowPacks)),
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(Wait), nameof(Wait.Show))));
            }
            catch (Exception e) { RevivalMod.Log.Warning("[draft] could not hook the pack drawing (old packs may show for a moment after a pick): " + e.Message); }
        }

        private static class Wait
        {
            internal static void Picked() => waitingSince = Math.Max(1, Environment.TickCount64);

            internal static bool Show()
            {
                long since = waitingSince;
                if (since == 0) return true;
                if (Environment.TickCount64 - since > WaitLimitMs)
                {
                    waitingSince = 0;
                    RevivalMod.Log.Warning("[draft] no answer to the pack pick yet; drawing the packs anyway");
                    return true;
                }
                RevivalMod.Log.Msg("[draft] waiting for the server's next packs before drawing them");
                return false;
            }
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
