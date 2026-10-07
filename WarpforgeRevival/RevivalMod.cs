using System;
using System.IO;
using MelonLoader;

[assembly: MelonInfo(typeof(WarpforgeRevival.RevivalMod), "Warpforge Revival", WarpforgeRevival.RevivalMod.Version, "Warpforge Revival community")]
[assembly: MelonGame("Everguild", "Warpforge")]

namespace WarpforgeRevival
{
    /// <summary>
    /// Entry point. Milestone 1:
    ///  - reads the server address from UserData/WarpforgeRevival.cfg (MelonPreferences)
    ///  - downloads / caches the card pack from the revival server
    ///  - logs every step of the game's login flow so we can see where it stalls offline
    /// Later milestones add the offline backend, card injection and direct connect.
    /// </summary>
    public class RevivalMod : MelonMod
    {
        public const string Version = "0.10.12";

        internal static MelonLogger.Instance Log;
        internal static RevivalConfig Config;
        internal static CardPackClient Cards;
        private static bool backgroundNoted;

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;
            Config = RevivalConfig.Load();
            try { Updater.LoadedFrom = MelonAssembly.Location ?? ""; } catch { }
            Log.Msg($"Warpforge Revival {Version} - server: {Config.ServerUrl}");

            string dataDir = Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "WarpforgeRevival");
            Directory.CreateDirectory(dataDir);

            // First of all: make sure nothing can delete the original game files in the cache.
            try { CacheGuard.Apply(HarmonyInstance, dataDir); }
            catch (Exception e) { Log.Warning("[cache] " + e.Message); }

            Cards = new CardPackClient(Config.ServerUrl, dataDir);
            Cards.StartSync();
            NoSteam.Apply(HarmonyInstance);
            ServerSettings.Start(Config.ServerUrl);
            CardText.Start(Config.ServerUrl);
            GlobalChat.Start(Config.ServerUrl);
            LongGame.Start(dataDir);
            Updater.Start(Config.ServerUrl, dataDir);
            CcdProbe.Start(dataDir); // background; falls back to the cached pack when the server is unreachable

            if (Config.DiagnosticLogging)
                Diagnostics.Install(HarmonyInstance);
            // PlayFabTransport patches are applied automatically by MelonLoader ([HarmonyPatch]).
        }

        private float nextQuitCheck;
        private bool loggerNoted;

        public override void OnUpdate()
        {
            // The game freezes when its window loses focus, and a frozen game drops out of the match
            // service within seconds: the room you were waiting in closes and nobody can find you.
            if (Config.RunInBackground && !UnityEngine.Application.runInBackground)
            {
                UnityEngine.Application.runInBackground = true;
                if (!backgroundNoted) { backgroundNoted = true; Log.Msg("[game] the game now keeps running in the background"); }
            }
            PlayFabTransport.Pump();
            Updater.Pump();
            FriendsLive.Tick();
            GlobalChat.Tick();
            AlternateArts.Tick();
            BattleDiagnostics.Tick();

            // Dev helper: creating UserData/WarpforgeRevival/quit closes the game cleanly
            // (lets the tester restart it without touching the game window).
            if (UnityEngine.Time.realtimeSinceStartup >= nextQuitCheck)
            {
                nextQuitCheck = UnityEngine.Time.realtimeSinceStartup + 1f;
                // The game turns Unity's logger off, which also hides crashes inside menu screens.
                // Keep it on so they are written to Player.log (LocalLow/Everguild/Warpforge).
                try
                {
                    var logger = UnityEngine.Debug.unityLogger;
                    if (logger != null && !logger.logEnabled)
                    {
                        logger.logEnabled = true;
                        if (!loggerNoted) { loggerNoted = true; Log.Msg("Unity logging re-enabled (errors go to Player.log)"); }
                    }
                }
                catch (Exception e) { if (!loggerNoted) { loggerNoted = true; Log.Warning("could not enable Unity logging: " + e.Message); } }
                var flag = Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "WarpforgeRevival", "quit");
                if (File.Exists(flag))
                {
                    try { File.Delete(flag); } catch { }
                    Log.Msg("Quit requested via quit flag");
                    UnityEngine.Application.Quit();
                }
            }
        }
    }
}
