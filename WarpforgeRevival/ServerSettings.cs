using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace WarpforgeRevival
{
    /// <summary>
    /// Rules the server owner decides for everyone, read from the server's
    /// content/files/revival-settings.json (for example {"turnSeconds": 120}).
    /// Fetched when the game starts and again, in the background, before battles, so every player
    /// on a server uses the same values without editing anything locally.
    /// </summary>
    internal static class ServerSettings
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        private static string url;
        private static volatile int turnSeconds;          // 0 = the server did not say
        private static DateTime lastFetch = DateTime.MinValue;
        private static volatile bool fetching;

        private static volatile bool offenseCards;      // off until the server says otherwise

        // "longGame": {"eventId": "RevivalLongGame", "warlordHealth": 1.6, "classicDeckSize": 30,
        //              "copies": {"Common": 4, "Rare": 3, "Epic": 2, "Legendary": 1}}
        private static volatile string longGameEvent;
        private static double longGameHealth = 1.0;
        private static volatile int classicDeckSize = 30;
        private static volatile int longGameHand;            // cards the first player starts with (0 = game default)
        private static volatile int longGameSecondExtra = -1; // extra cards for the second player (-1 = game default)
        public static int LongGameHand => longGameHand;
        public static int LongGameSecondExtra => longGameSecondExtra;
        private static readonly int[] longGameCopies = new int[6];     // by CardRarity value (1 Common .. 4 Legendary)

        /// <summary>Event id of the Long Game mode, or null when the server has none.</summary>
        public static string LongGameEvent => longGameEvent;
        public static double LongGameHealth => longGameHealth;
        public static int ClassicDeckSize => classicDeckSize;
        public static int LongGameCopies(int rarity) => rarity >= 0 && rarity < longGameCopies.Length ? longGameCopies[rarity] : 0;

        private static volatile string photonAppId, photonServer;
        private static volatile bool loaded;

        /// <summary>True once the server has answered (with settings or without) at least once.</summary>
        public static bool Loaded => loaded;
        /// <summary>The server owner's own Photon Cloud app ("photon": {"appId": "..."}), or null to leave the game's as it is.</summary>
        public static string PhotonAppId => photonAppId;
        /// <summary>A self-hosted Photon server ("photon": {"server": "host:5055"}), or null.</summary>
        public static string PhotonServer => photonServer;

        private static volatile bool keepPhotonChat;
        /// <summary>True when the server runs matches on a Photon service of its own.</summary>
        public static bool OwnPhoton => photonAppId != null || photonServer != null || PhotonRevival;

        private static volatile bool photonRevival;
        private static volatile int photonRevivalPort;
        /// <summary>"photon": {"service": "revival"}: matches run on the revival server's own match service.</summary>
        public static bool PhotonRevival => photonRevival;
        /// <summary>Port of that service ("photon": {"port": ...}); 0 = the server's usual port.</summary>
        public static int PhotonRevivalPort => photonRevivalPort;
        /// <summary>"photon": {"chat": "original"}: still connect to the publisher's Photon Chat.</summary>
        public static bool KeepPhotonChat => keepPhotonChat;

        private static volatile string creatorUrl;

        /// <summary>
        /// Address of the card creator site: the server's "creatorUrl" setting when it has one
        /// (for example an https address), otherwise the creator page on the game server itself.
        /// </summary>
        public static string CreatorUrl => !string.IsNullOrEmpty(creatorUrl) ? creatorUrl : RevivalMod.Config.ServerUrl + "/creator";

        /// <summary>Whether the Offence card pick step runs (the server's "offenseCards" setting).</summary>
        public static bool OffenseCards => offenseCards;

        /// <summary>Turn length chosen by the server, or null when it has not set one.</summary>
        public static int? TurnSeconds => turnSeconds > 0 ? turnSeconds : (int?)null;

        public static void Start(string serverUrl)
        {
            url = serverUrl + "/api/v1/content/files/revival-settings.json";
            Refresh();
        }

        /// <summary>Re-reads the settings in the background if the last read is more than a minute old.</summary>
        public static void Refresh()
        {
            if (url == null || fetching || (DateTime.UtcNow - lastFetch).TotalSeconds < 60) return;
            fetching = true;
            lastFetch = DateTime.UtcNow;
            Task.Run(async () =>
            {
                try
                {
                    using var reply = await Http.GetAsync(url);
                    if (!reply.IsSuccessStatusCode)
                    {
                        photonAppId = photonServer = null;
                        photonRevival = false;
                        loaded = true;
                        if (turnSeconds != 0) RevivalMod.Log.Msg("[settings] the server no longer provides settings; using local values");
                        turnSeconds = 0;
                        offenseCards = false;
                        BattlefieldCards.Set(false);
                        return;
                    }
                    using var doc = JsonDocument.Parse(await reply.Content.ReadAsStringAsync());
                    int seconds = 0;
                    if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                        doc.RootElement.TryGetProperty("turnSeconds", out var t) && t.ValueKind == JsonValueKind.Number)
                        seconds = t.GetInt32();
                    string creator = null;
                    if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("creatorUrl", out var cu) &&
                        cu.ValueKind == JsonValueKind.String)
                    {
                        string v = (cu.GetString() ?? "").Trim();
                        if (v.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || v.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) creator = v;
                    }
                    creatorUrl = creator;
                    string appId = null, server = null;
                    bool keepChat = false, revival = false;
                    int revivalPort = 0;
                    if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("photon", out var ph3) && ph3.ValueKind == JsonValueKind.Object)
                    {
                        revival = ph3.TryGetProperty("service", out var sv) && sv.ValueKind == JsonValueKind.String &&
                                  string.Equals(sv.GetString(), "revival", StringComparison.OrdinalIgnoreCase);
                        if (ph3.TryGetProperty("port", out var pp) && pp.ValueKind == JsonValueKind.Number) revivalPort = pp.GetInt32();
                    }
                    if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("photon", out var ph) && ph.ValueKind == JsonValueKind.Object)
                    {
                        if (ph.TryGetProperty("appId", out var pa) && pa.ValueKind == JsonValueKind.String && Guid.TryParse(pa.GetString(), out var g)) appId = g.ToString();
                        if (ph.TryGetProperty("server", out var ps) && ps.ValueKind == JsonValueKind.String && ps.GetString().Trim().Length > 0) server = ps.GetString().Trim();
                    }
                    if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("photon", out var ph2) && ph2.ValueKind == JsonValueKind.Object &&
                        ph2.TryGetProperty("chat", out var pc) && pc.ValueKind == JsonValueKind.String)
                        keepChat = string.Equals(pc.GetString(), "original", StringComparison.OrdinalIgnoreCase);
                    keepPhotonChat = keepChat;
                    if (appId != photonAppId || server != photonServer || revival != photonRevival)
                        RevivalMod.Log.Msg("[settings] server settings: match service " + (revival ? "the revival server's own" : server != null ? "self-hosted at " + server : appId != null ? "the server owner's Photon app" : "the game's original"));
                    photonAppId = appId;
                    photonServer = server;
                    photonRevivalPort = revivalPort;
                    photonRevival = revival;
                    loaded = true;
                    bool offence = false;
                    if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("offenseCards", out var o) &&
                        (o.ValueKind == JsonValueKind.True || o.ValueKind == JsonValueKind.False))
                        offence = o.GetBoolean();
                    if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("longGame", out var lg) && lg.ValueKind == JsonValueKind.Object)
                    {
                        string id = lg.TryGetProperty("eventId", out var le) ? le.GetString() : null;
                        if (lg.TryGetProperty("warlordHealth", out var lh) && lh.ValueKind == JsonValueKind.Number) longGameHealth = lh.GetDouble();
                        longGameHand = lg.TryGetProperty("startingHand", out var sh) && sh.ValueKind == JsonValueKind.Number ? sh.GetInt32() : 0;
                        longGameSecondExtra = lg.TryGetProperty("secondPlayerExtraCards", out var se) && se.ValueKind == JsonValueKind.Number ? se.GetInt32() : -1;
                        if (lg.TryGetProperty("classicDeckSize", out var lc) && lc.ValueKind == JsonValueKind.Number) classicDeckSize = lc.GetInt32();
                        if (lg.TryGetProperty("copies", out var cp) && cp.ValueKind == JsonValueKind.Object)
                        {
                            string[] names = { "", "Common", "Rare", "Epic", "Legendary" };
                            for (int i = 1; i < names.Length; i++)
                                longGameCopies[i] = cp.TryGetProperty(names[i], out var n) && n.ValueKind == JsonValueKind.Number ? n.GetInt32() : 0;
                        }
                        if (id != longGameEvent) RevivalMod.Log.Msg($"[settings] server settings: Long Game mode '{id}', warlord health x{longGameHealth}");
                        longGameEvent = id;
                    }
                    else longGameEvent = null;
                    if (offence != offenseCards) RevivalMod.Log.Msg($"[settings] server settings: Offence cards {(offence ? "on" : "off")}");
                    offenseCards = offence;
                    BattlefieldCards.Set(offence);
                    if (seconds != turnSeconds) RevivalMod.Log.Msg($"[settings] server settings: turn timer {(seconds > 0 ? seconds + "s" : "not set")}");
                    turnSeconds = seconds;
                }
                catch (Exception e) { RevivalMod.Log.Warning("[settings] could not read server settings: " + e.Message); }
                finally { fetching = false; }
            });
        }
    }
}
