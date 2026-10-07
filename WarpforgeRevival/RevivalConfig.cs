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

        /// <summary>Keep the game running while its window is not in front.</summary>
        public bool RunInBackground => runInBackground.Value;


        public const int DefaultPort = 8780;

        /// <summary>The community server, used when the settings file is new or the address is left blank.</summary>
        public const string DefaultServer = "warpforge.silentanarchist.com";

        /// <summary>
        /// The server address as the mod uses it. Players may write just a name ("example.com"):
        /// a missing "http://" is added, and a plain http address without a port gets the server's
        /// usual port. An https address without a port is left alone (443).
        /// </summary>
        public string ServerUrl => Normalize(serverUrl.Value);

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
                serverUrl = cat.CreateEntry("ServerUrl", DefaultServer,
                    description: "Address of the Warpforge Revival server, for example example.com (http:// and port 8780 are assumed when left out). Use 127.0.0.1 for a server on this computer"),
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
                turnSeconds = cat.CreateEntry("TurnSeconds", 120,
                    description: "Only used when the server does not set a turn length (content/files/revival-settings.json on the server). Length of a turn in seconds; the game's own value is 60"),
            };
            cat.SaveToFile(false);
            return c;
        }
    }
}
