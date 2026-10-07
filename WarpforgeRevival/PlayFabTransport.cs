using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using Il2CppPlayFab.Internal;

namespace WarpforgeRevival
{
    /// <summary>
    /// Sends every PlayFab API call to the revival server instead of PlayFab.
    ///
    /// The SDK's UnityWebRequest transport would refuse plain http:// (Unity's
    /// "insecure connection" rule), so we replace it: the request is sent with .NET's
    /// HttpClient on a worker thread, and the JSON reply is handed back to the SDK's own
    /// OnResponse on the main thread - from there the game parses it exactly as if it came
    /// from PlayFab.
    /// </summary>
    internal static class PlayFabTransport
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        private static readonly ConcurrentQueue<Action> MainThread = new ConcurrentQueue<Action>();
        // Keep in-flight containers referenced from managed code while the request runs.
        private static readonly HashSet<CallRequestContainer> InFlight = new HashSet<CallRequestContainer>();

        public static string RewriteUrl(string fullUrl)
        {
            // https://<title>.playfabapi.com/Client/LoginWithSteam?sdk=...  ->  <server>/playfab/Client/LoginWithSteam?sdk=...
            var uri = new Uri(fullUrl);
            return RevivalMod.Config.ServerUrl + "/playfab" + uri.PathAndQuery;
        }

        /// <summary>Called from OnUpdate: delivers finished requests on Unity's main thread.</summary>
        /// <summary>The signed-in player's session ticket, as the game last sent it (null before sign-in).</summary>
        internal static volatile string SessionTicket;

        /// <summary>Runs an action on the game's main thread on the next frame.</summary>
        internal static void OnMainThread(Action a) => MainThread.Enqueue(a);

        public static void Pump()
        {
            int n = 0;
            while (n++ < 32 && MainThread.TryDequeue(out var a))
            {
                try { a(); }
                catch (Exception e) { RevivalMod.Log.Error("[playfab] callback failed: " + e); }
            }
        }

        [HarmonyPatch(typeof(PlayFabUnityHttp), nameof(PlayFabUnityHttp.MakeApiCall))]
        private static class MakeApiCallPatch
        {
            private static bool Prefix(PlayFabUnityHttp __instance, Il2CppSystem.Object reqContainerObj)
            {
                var container = reqContainerObj.Cast<CallRequestContainer>();
                string url;
                byte[] payload;
                var headers = new List<KeyValuePair<string, string>>();
                try
                {
                    url = RewriteUrl(container.FullUrl);
                    payload = container.Payload != null ? (byte[])container.Payload : Array.Empty<byte>();
                    if (container.RequestHeaders != null)
                        foreach (var kv in container.RequestHeaders)
                            headers.Add(new KeyValuePair<string, string>(kv.Key, kv.Value));
                    foreach (var h in headers)
                        if (h.Key == "X-Authorization" && !string.IsNullOrEmpty(h.Value) && h.Value != SessionTicket)
                        {
                            SessionTicket = h.Value;
#if !ANDROID_TEST || ANDROID_PORT
                            AccountPage.SignedIn();
#endif
                        }
                }
                catch (Exception e)
                {
                    RevivalMod.Log.Error("[playfab] could not read request: " + e);
                    return true; // fall back to the game's own transport
                }

                var endpoint = container.ApiEndpoint;
                if (RevivalMod.Config.DiagnosticLogging)
                    RevivalMod.Log.Msg($"[playfab] -> {endpoint}");

                lock (InFlight) InFlight.Add(container);
                var http = __instance;
                Task.Run(async () =>
                {
                    string body = null, error = null;
#if ANDROID_TEST
                    // test build: one more try when the connection itself fails, and the full reason in the log
                    for (int attempt = 1; attempt <= 2 && body == null; attempt++)
                    {
                        try
                        {
                            using var msg = new HttpRequestMessage(HttpMethod.Post, url) { Content = new ByteArrayContent(payload) };
                            msg.Content.Headers.TryAddWithoutValidation("Content-Type", "application/json");
                            foreach (var h in headers)
                                if (!h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                                    msg.Headers.TryAddWithoutValidation(h.Key, h.Value);
                            using var resp = await Http.SendAsync(msg);
                            body = await resp.Content.ReadAsStringAsync();
                            error = null;
                        }
                        catch (Exception e)
                        {
                            string why = e.GetType().Name + ": " + e.Message;
                            for (var inner = e.InnerException; inner != null; inner = inner.InnerException)
                                why += " <- " + inner.GetType().Name + ": " + inner.Message;
                            error = $"Revival server unreachable ({why})";
                            RevivalMod.Log.Warning($"[playfab] {endpoint} attempt {attempt} failed: {why}");
                        }
                    }
#else
                    try
                    {
                        using var msg = new HttpRequestMessage(HttpMethod.Post, url)
                        {
                            Content = new ByteArrayContent(payload)
                        };
                        msg.Content.Headers.TryAddWithoutValidation("Content-Type", "application/json");
                        foreach (var h in headers)
                            if (!h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                                msg.Headers.TryAddWithoutValidation(h.Key, h.Value);
                        using var resp = await Http.SendAsync(msg);
                        body = await resp.Content.ReadAsStringAsync();
                    }
                    catch (Exception e)
                    {
                        error = $"Revival server unreachable ({e.GetType().Name}: {e.Message})";
                    }
#endif

                    MainThread.Enqueue(() =>
                    {
                        lock (InFlight) InFlight.Remove(container);
                        if (body != null)
                        {
                            if (RevivalMod.Config.DiagnosticLogging)
                                RevivalMod.Log.Msg($"[playfab] <- {endpoint} ({body.Length} bytes)");
#if ANDROID_TEST
                            // test build: show what the server answered when it is short (errors are short)
                            if (body.Length < 600) RevivalMod.Log.Msg("[playfab]    " + body);
#endif
                            http.OnResponse(body, container);
                        }
                        else
                        {
                            RevivalMod.Log.Error($"[playfab] {endpoint}: {error}");
                            http.OnError(error, container);
                        }
                    });
                });
                return false; // skip the original UnityWebRequest path
            }
        }

        // Fire-and-forget telemetry calls (screen time, device info) would still go to the real
        // PlayFab; drop them silently.
        [HarmonyPatch(typeof(PlayFabUnityHttp), nameof(PlayFabUnityHttp.SimplePostCall))]
        private static class DropSimplePost { private static bool Prefix() => false; }

        [HarmonyPatch(typeof(PlayFabUnityHttp), nameof(PlayFabUnityHttp.SimpleGetCall))]
        private static class DropSimpleGet { private static bool Prefix() => false; }

        [HarmonyPatch(typeof(PlayFabUnityHttp), nameof(PlayFabUnityHttp.SimplePutCall))]
        private static class DropSimplePut { private static bool Prefix() => false; }
    }
}
