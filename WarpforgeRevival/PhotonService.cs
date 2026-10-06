using System;
using HarmonyLib;
using Il2Cpp;

namespace WarpforgeRevival
{
    /// <summary>
    /// Which Photon service carries matches. Out of the box the game connects to its publisher's
    /// Photon app. A revival server can name its own in revival-settings.json:
    ///   "photon": {"appId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"}   the owner's own Photon Cloud app
    ///   "photon": {"server": "example.com:5055"}                        a self-hosted Photon server
    ///   "photon": {"service": "revival"}                               the revival server's own match service
    /// Every player of a server gets the same answer, so they still meet each other.
    ///
    /// The revival server's own service is not Photon: it is part of the revival server and speaks
    /// the same language over TCP, on the server's usual port. The game's Photon client is left as
    /// it is; only the address it connects to is changed - for every connection it makes, because
    /// it reconnects each time it enters or leaves a room.
    /// </summary>
    internal static class PhotonService
    {
        private static string announced;

        /// <summary>host:port of the revival server's match service.</summary>
        private static bool RevivalAddress(out string host, out int port)
        {
            host = null;
            port = ServerSettings.PhotonRevivalPort > 0 ? ServerSettings.PhotonRevivalPort : RevivalConfig.DefaultPort;
            try
            {
                var uri = new Uri(RevivalMod.Config.ServerUrl);
                host = uri.Host;
                // An http address already names the game port; an https one goes through a web proxy that cannot carry this.
                if (ServerSettings.PhotonRevivalPort <= 0 && uri.Scheme == "http" && uri.Port > 0) port = uri.Port;
            }
            catch (UriFormatException) { }
            return !string.IsNullOrEmpty(host);
        }

        /// <summary>
        /// Every connection of the game's match client goes to the revival server while its own
        /// service is switched on, whatever address the client was about to use.
        /// </summary>
        [HarmonyPatch(typeof(NetworkingPeer), nameof(NetworkingPeer.Connect), new[] { typeof(string), typeof(ServerConnection) })]
        private static class Redirect
        {
            private static void Prefix(ref string serverAddress)
            {
                try
                {
                    if (!ServerSettings.PhotonRevival || !RevivalAddress(out string host, out int port)) return;
                    serverAddress = host + ":" + port;
                }
                catch (Exception e) { RevivalMod.Log.Warning("[photon] " + e.Message); }
            }
        }

        private static void Say(string what)
        {
            if (what == announced) return;
            announced = what;
            RevivalMod.Log.Msg("[photon] matches use " + what);
        }

        [HarmonyPatch(typeof(NetworkCustomManager), nameof(NetworkCustomManager.ConnectToPhoton))]
        private static class Connect
        {
            private static bool Prefix(NetworkCustomManager __instance)
            {
                try
                {
                    // The settings are asked for when the game starts; give a slow answer a moment
                    // rather than quietly connecting to the wrong service.
                    for (int i = 0; i < 30 && !ServerSettings.Loaded; i++) System.Threading.Thread.Sleep(100);
                    if (!ServerSettings.Loaded) RevivalMod.Log.Warning("[photon] the server's settings have not arrived; using the game's own match service this time");

                    string server = ServerSettings.PhotonServer, appId = ServerSettings.PhotonAppId;
                    if (ServerSettings.PhotonRevival && RevivalAddress(out string rhost, out int rport))
                    {
                        var data = __instance.playerData;
                        string version = (object)data != null ? data.gameVersionWithEnviromentAndBundles : PhotonNetwork.gameVersion;
                        // Anything in the game that reconnects from its saved settings lands here too.
                        var s = PhotonNetwork.PhotonServerSettings;
                        if ((object)s != null)
                        {
                            s.HostType = Il2Cpp.ServerSettings.HostingOption.SelfHosted;
                            s.ServerAddress = rhost;
                            s.ServerPort = rport;
                            s.Protocol = Il2CppExitGames.Client.Photon.ConnectionProtocol.Tcp;
                            s.AppID = "WarpforgeRevival";
                        }
                        Say($"the revival server's own match service at {rhost}:{rport}");
                        PhotonNetwork.SwitchToProtocol(Il2CppExitGames.Client.Photon.ConnectionProtocol.Tcp);
                        PhotonNetwork.ConnectToMaster(rhost, rport, "WarpforgeRevival", version);
                        return false;
                    }
                    if (!string.IsNullOrEmpty(server))
                    {
                        string host = server;
                        int port = 5055;
                        int colon = server.LastIndexOf(':');
                        if (colon > 0 && int.TryParse(server.Substring(colon + 1), out int p)) { host = server.Substring(0, colon); port = p; }
                        var data = __instance.playerData;
                        string version = (object)data != null ? data.gameVersionWithEnviromentAndBundles : PhotonNetwork.gameVersion;
                        Say($"the self-hosted Photon server at {host}:{port}");
                        PhotonNetwork.ConnectToMaster(host, port, string.IsNullOrEmpty(appId) ? "WarpforgeRevival" : appId, version);
                        return false;
                    }
                    if (!string.IsNullOrEmpty(appId))
                    {
                        var settings = PhotonNetwork.PhotonServerSettings;
                        if ((object)settings != null)
                        {
                            settings.AppID = appId;
                            Say("the server owner's Photon app (" + appId.Substring(0, 8) + "...)");
                        }
                        return true;
                    }
                    Say("the game's original Photon app");
                }
                catch (Exception e) { RevivalMod.Log.Warning("[photon] " + e.Message); }
                return true;
            }
        }
    }
}
