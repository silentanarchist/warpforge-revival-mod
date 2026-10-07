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
        #if ANDROID_PORT
        public const string Version = "0.11.9-a";
#else
        public const string Version = "0.11.9-w";
#endif

        internal static MelonLogger.Instance Log;
        internal static RevivalConfig Config;
        internal static CardPackClient Cards;
        private static bool backgroundNoted;

        /// <summary>The number part of a version: 0.11.0-w -> 0.11.0 (-w is Windows, -a is Android).</summary>
        internal static System.Version Number(string text)
        {
            text = text ?? "";
            int dash = text.IndexOf('-');
            return System.Version.TryParse(dash < 0 ? text : text.Substring(0, dash), out var v) ? v : null;
        }

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;
            Config = RevivalConfig.Load();
#if !ANDROID_PORT   // Windows only
            try { Updater.LoadedFrom = MelonAssembly.Location ?? ""; } catch { }
#endif
            Log.Msg($"Warpforge Revival {Version} - server: {Config.ServerUrl}");

            string dataDir = Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "WarpforgeRevival");
            Directory.CreateDirectory(dataDir);

            // First of all: make sure nothing can delete the original game files in the cache.
#if !ANDROID_PORT   // the phone version is given its content files by the mod, so its cache holds nothing to protect
            try { CacheGuard.Apply(HarmonyInstance, dataDir); }
            catch (Exception e) { Log.Warning("[cache] " + e.Message); }
#endif

            Cards = new CardPackClient(Config.ServerUrl, dataDir);
            Cards.StartSync();
#if !ANDROID_PORT   // Windows only
            NoSteam.Apply(HarmonyInstance);
#endif
            ServerSettings.Start(Config.ServerUrl);
            CardText.Start(Config.ServerUrl);
            GlobalChat.Start(Config.ServerUrl);
            LongGame.Start(dataDir);
#if ANDROID_PORT
            try { AndroidUpdater.LoadedFrom = MelonAssembly.Location ?? ""; } catch { }
            AndroidUpdater.Start(Config.ServerUrl);
#else
            Updater.Start(Config.ServerUrl, dataDir);
#endif
#if !ANDROID_PORT   // Windows only
            CcdProbe.Start(dataDir); // background; falls back to the cached pack when the server is unreachable
#endif

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
#if !ANDROID_PORT   // a phone has no "window in the background", and its game build lacks this setting
            if (Config.RunInBackground && !UnityEngine.Application.runInBackground)
            {
                UnityEngine.Application.runInBackground = true;
                if (!backgroundNoted) { backgroundNoted = true; Log.Msg("[game] the game now keeps running in the background"); }
            }
#endif
            PlayFabTransport.Pump();
#if ANDROID_PORT
            AndroidUpdater.Tick();
#else
            Updater.Pump();
#endif
            SupportPage.Tick();
            AccountPage.Tick();
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
#if !ANDROID_PORT   // on a phone this would send every game log line to the system log and slow loading down
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
#endif
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
