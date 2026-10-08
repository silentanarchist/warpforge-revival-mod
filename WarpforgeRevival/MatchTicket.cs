using System;
using System.Threading.Tasks;
using HarmonyLib;
using Il2Cpp;

namespace WarpforgeRevival
{
    /// <summary>
    /// Proves to the match service who is connecting.
    ///
    /// The game's match client signs in to the match service on its own, encrypted, with nothing the
    /// revival server can check. So the mod asks the game server (over https, with its session) for a
    /// match ticket, and sends it to the match service as one extra message just before the match
    /// client signs in. A server that requires accounts refuses match connections without one, and
    /// gives a seat in a room back only to the same signed-in player.
    /// The ticket is good for half an hour and for every connection in that time (the match client
    /// reconnects each time it enters or leaves a room); a fresh one is fetched before it runs out.
    /// </summary>
    internal static class MatchTicket
    {
        private const byte OpTicket = 160;                 // the same number the server listens for
        private static readonly System.Net.Http.HttpClient Http = Net.Client(TimeSpan.FromSeconds(10));
        private static volatile string ticket;
        private static DateTime validUntil = DateTime.MinValue;
        private static Task fetching;
        private static readonly object Gate = new object();
        private static bool reported;

        /// <summary>Starts fetching a ticket if the one held is missing or running out (any thread).</summary>
        internal static Task Refresh()
        {
            lock (Gate)
            {
                if (ticket != null && DateTime.UtcNow < validUntil.AddMinutes(-10)) return Task.CompletedTask;
                if (fetching != null && !fetching.IsCompleted) return fetching;
                string session = PlayFabTransport.SessionTicket;
                if (string.IsNullOrEmpty(session)) return Task.CompletedTask;
                fetching = Task.Run(() => Fetch(session));
                return fetching;
            }
        }

        private static async Task Fetch(string session)
        {
            try
            {
                using var msg = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, RevivalMod.Config.ServerUrl + "/playfab/Revival/MatchTicket")
                {
                    Content = new System.Net.Http.StringContent("{}", System.Text.Encoding.UTF8, "application/json")
                };
                msg.Headers.TryAddWithoutValidation("X-Authorization", session);
                using var reply = await Http.SendAsync(msg);
                using var doc = System.Text.Json.JsonDocument.Parse(await reply.Content.ReadAsStringAsync());
                if (reply.IsSuccessStatusCode && doc.RootElement.TryGetProperty("data", out var data)
                    && data.TryGetProperty("ticket", out var t))
                {
                    int seconds = data.TryGetProperty("seconds", out var s) && s.TryGetInt32(out int n) ? n : 1800;
                    ticket = t.GetString();
                    validUntil = DateTime.UtcNow.AddSeconds(seconds);
                    if (!reported) { reported = true; RevivalMod.Log.Msg("[match] match ticket received"); }
                }
                else RevivalMod.Log.Warning("[match] the server gave no match ticket (" + (int)reply.StatusCode + ")");
            }
            catch (Exception e) { RevivalMod.Log.Warning("[match] could not get a match ticket: " + e.Message); }
        }

        // Fetch one as soon as the game starts connecting to the match service...
        [HarmonyPatch(typeof(NetworkCustomManager), nameof(NetworkCustomManager.StartConnection))]
        private static class Early
        {
            private static void Prefix() { try { Refresh(); MatchTunnel.Probe(); } catch { } }
        }

        // ...and hand it over just before the match client signs in (on every connection).
        [HarmonyPatch(typeof(NetworkingPeer), "CallAuthenticate")]
        private static class Hand
        {
            private static void Prefix(NetworkingPeer __instance)
            {
                try
                {
                    var pending = Refresh();
                    if (ticket == null && !pending.IsCompleted) pending.Wait(TimeSpan.FromSeconds(5));
                    string t = ticket;
                    if (string.IsNullOrEmpty(t)) { RevivalMod.Log.Warning("[match] no match ticket to send"); return; }
                    var parameters = new Il2CppSystem.Collections.Generic.Dictionary<byte, Il2CppSystem.Object>();
                    parameters.Add(1, new Il2CppSystem.Object(Il2CppInterop.Runtime.IL2CPP.ManagedStringToIl2Cpp(t)));
                    __instance.SendOperation(OpTicket, parameters, Il2CppExitGames.Client.Photon.SendOptions.SendReliable);
                }
                catch (Exception e) { RevivalMod.Log.Warning("[match] could not send the match ticket: " + e.Message); }
            }
        }
    }
}
