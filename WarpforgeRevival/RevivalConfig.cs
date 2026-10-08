using MelonLoader;

namespace WarpforgeRevival
{
    /// <summary>
    /// Settings live in UserData/WarpforgeRevival.cfg (created on first launch).
    /// Players only need to set ServerUrl to the community server.
    /// </summary>
    internal class RevivalConfig
    {
        private MelonPreferences_Entry<string> serverUrl;
        private MelonPreferences_Entry<bool> diagnostics;
        private MelonPreferences_Entry<int> timeoutSeconds;
        private MelonPreferences_Entry<bool> skipTutorial;
        private MelonPreferences_Entry<bool> hideShopAndRewards;
        private MelonPreferences_Entry<string> autoUpdate;
        private MelonPreferences_Entry<int> turnSeconds;
        private MelonPreferences_Entry<bool> runInBackground;
        private MelonPreferences_Entry<string> secureServer;
        private MelonPreferences_Category category;
        private string resolved;

        /// <summary>Keep the game running while its window is not in front.</summary>
        public bool RunInBackground => runInBackground.Value;


        public const int DefaultPort = 8780;

        /// <summary>The community server, used when the settings file is new or the address is left blank.</summary>
        public const string DefaultServer = "warpforge.silentanarchist.com";

        /// <summary>
        /// The server address as the mod uses it, decided once per run (see <see cref="Resolve"/>).
        /// </summary>
        public string ServerUrl => resolved ??= Resolve(serverUrl.Value);

        /// <summary>True when everything the mod sends to the server this run is encrypted (https).</summary>
        public bool Secure => ServerUrl.StartsWith("https://", System.StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Turns what the player wrote into the address to use.
        ///  - "http://..." or "https://..." is taken as written (plus the usual port for plain http).
        ///  - A bare name ("example.com") is tried over https on the game port first, and plain http
        ///    is used only if the server does not answer that way. Once a server HAS answered over
        ///    https its name is remembered, and from then on the mod never drops back to plain http
        ///    for it: otherwise anyone able to get in the way of the connection could block https
        ///    and read everything that followed.
        ///  - A number address or "localhost" is plain http: a certificate cannot name those.
        /// </summary>
        private string Resolve(string value)
        {
            string text = (value ?? "").Trim().TrimEnd('/');
            if (text.Length == 0) text = DefaultServer;
            if (text.Contains("://")) return Normalize(text);

            string plain = Normalize(text);                              // http://host:port[/path]
            string host;
            try { host = new System.Uri(plain).Host; } catch (System.UriFormatException) { return plain; }
            if (host == "localhost" || System.Net.IPAddress.TryParse(host.Trim('[', ']'), out _)) return plain;

            string secure = "https://" + plain.Substring("http://".Length);
            string remembered = secureServer != null ? (secureServer.Value ?? "") : "";
            string why = Probe(secure);
            if (why == null)
            {
                if (remembered != host)
                {
                    try { secureServer.Value = host; category.SaveToFile(false); } catch { }
                }
                return secure;
            }
            if (remembered == host)
            {
                RevivalMod.Log.Warning("[secure] " + host + " did not answer over https (" + why + "). It has before, so the mod will not use an unencrypted connection to it. If the server has really stopped offering https, clear SecureServer in UserData/WarpforgeRevival.cfg");
                return secure;
            }
            RevivalMod.Log.Warning("[secure] " + host + " did not answer over https (" + why + "); using an unencrypted connection");
            return plain;
        }

        /// <summary>Null when the server answers over https at this address, otherwise the reason it did not.</summary>
        private static string Probe(string secureUrl)
        {
            try
            {
                var task = System.Threading.Tasks.Task.Run(async () =>
                {
                    using var http = Net.Client(System.TimeSpan.FromSeconds(5));
                    using var reply = await http.GetAsync(secureUrl + "/api/v1/status", System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
                    return (int)reply.StatusCode;
                });
                if (!task.Wait(System.TimeSpan.FromSeconds(6))) return "no answer in time";
                return null;        // any answer at all means the encrypted connection itself worked
            }
            catch (System.Exception e)
            {
                System.Exception inner = e;
                while (inner.InnerException != null) inner = inner.InnerException;
                string message = (inner.Message ?? "").Replace('\n', ' ').Replace('\r', ' ');
                if (message.Length > 160) message = message.Substring(0, 160);
                return inner.GetType().Name + ": " + message;
            }
        }

        internal static string Normalize(string value)
        {
            string url = (value ?? "").Trim().TrimEnd('/');
            if (url.Length == 0) url = DefaultServer;
            if (!url.Contains("://")) url = "http://" + url;
            try
            {
                var uri = new System.Uri(url);
                // Uri reports the scheme's default port when none was written; look at the text itself.
                string authority = url.Substring(url.IndexOf("://") + 3);
                int slash = authority.IndexOf('/');
                string host = slash < 0 ? authority : authority.Substring(0, slash);
                bool hasPort = host.LastIndexOf(':') > host.LastIndexOf(']');
                if (uri.Scheme == "http" && !hasPort)
                    url = "http://" + host + ":" + DefaultPort + (slash < 0 ? "" : authority.Substring(slash));
            }
            catch (System.UriFormatException) { }
            return url.TrimEnd('/');
        }
        public bool DiagnosticLogging => diagnostics.Value;
        public int TimeoutSeconds => timeoutSeconds.Value;
        public bool SkipTutorial => skipTutorial.Value;
        public bool HideShopAndRewards => hideShopAndRewards.Value;
        public string AutoUpdate => autoUpdate.Value;
        public int TurnSeconds => turnSeconds.Value;

        public static RevivalConfig Load()
        {
            var cat = MelonPreferences.CreateCategory("WarpforgeRevival", "Warpforge Revival");
            // A full path: on a phone the game's working folder is not the loader's folder.
            cat.SetFilePath(System.IO.Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "WarpforgeRevival.cfg"), autoload: true);
            var c = new RevivalConfig
            {
                category = cat,
                serverUrl = cat.CreateEntry("ServerUrl", DefaultServer,
                    description: "Address of the Warpforge Revival server, for example example.com (port 8780 is assumed, and an encrypted connection is used when the server offers one). Write http://... to force an unencrypted connection or https://... to insist on an encrypted one. Use 127.0.0.1 for a server on this computer"),
                diagnostics = cat.CreateEntry("DiagnosticLogging", true,
                    description: "Log the game's login/loading steps (useful while the mod is in development)"),
                timeoutSeconds = cat.CreateEntry("TimeoutSeconds", 15,
                    description: "How long to wait for the server before using the cached card pack"),
                skipTutorial = cat.CreateEntry("SkipTutorial", true,
                    description: "Treat the tutorial battles as completed and go straight to the main menu"),
                hideShopAndRewards = cat.CreateEntry("HideShopAndRewards", true,
                    description: "Remove the Shop and Rewards buttons from the main menu (nothing is behind them on a revival server)"),
                runInBackground = cat.CreateEntry("RunInBackground", true,
                    description: "Keep the game running when you click on another window. When it is off the game freezes in the background: a match you are waiting for is lost and your opponent sees you fall behind"),
                autoUpdate = cat.CreateEntry("AutoUpdate", "Ask",
                    description: "Mod updates from the server: Ask (confirm first), Auto (update without asking), Off (never check). Only use Ask/Auto with a server you trust"),
                secureServer = cat.CreateEntry("SecureServer", "",
                    description: "Filled in by the mod: the server that has answered over an encrypted (https) connection. While it matches ServerUrl the mod refuses to fall back to an unencrypted one. Clear it only if that server has really stopped offering https"),
                turnSeconds = cat.CreateEntry("TurnSeconds", 120,
                    description: "Only used when the server does not set a turn length (content/files/revival-settings.json on the server). Length of a turn in seconds; the game's own value is 60"),
            };
            cat.SaveToFile(false);
            return c;
        }
    }
}
