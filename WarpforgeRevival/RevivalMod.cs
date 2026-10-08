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
        public const string Version = "0.11.31-a";
#else
        public const string Version = "0.11.31-w";
#endif

        /// <summary>
        /// Raised whenever a release changes something both players of a match must agree on (how
        /// the mod alters rules, cards or the match itself). Players are only matched with players
        /// on the same number, so an older mod never meets a newer one in a match. It is the same
        /// for Windows and Android, which is what keeps the two playing each other.
        /// </summary>
        public const int RulesVersion = 1;

        /// <summary>The rules a match is played under: the mod's rules number plus the server's own ("rulesVersion" in its settings).</summary>
        internal static string RulesTag
        {
            get { string server = ServerSettings.Rules; return "r" + RulesVersion + (string.IsNullOrEmpty(server) ? "" : "-" + server); }
        }

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

        /// <summary>SHA-256 of this mod's own file as it was when the game started ("" if it could not be read).</summary>
        internal static string FileHash = "";

        /// <summary>
        /// Names of every other mod and plugin the loader has running, comma separated ("" when this
        /// mod is alone). A server can refuse to sign a game in when others are loaded, so that
        /// everyone on it plays by the same rules.
        /// </summary>
        internal static string OtherMods()
        {
            var names = new System.Collections.Generic.List<string>();
            try
            {
                foreach (var m in MelonBase.RegisteredMelons)
                {
                    if (m == null || m is RevivalMod) continue;
                    string name = null;
                    try { name = m.Info?.Name; } catch { }
                    if (string.IsNullOrWhiteSpace(name)) { try { name = m.MelonAssembly?.Assembly?.GetName().Name; } catch { } }
                    names.Add(string.IsNullOrWhiteSpace(name) ? "unnamed" : name.Replace(',', ' ').Trim());
                }
            }
            catch (Exception e) { Log?.Warning("[mods] could not list the loaded mods: " + e.Message); names.Add("unknown"); }
            return string.Join(", ", names);
        }

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;
            Config = RevivalConfig.Load();
            try
            {
                string file = MelonAssembly.Location ?? "";
                if (file.Length > 0 && System.IO.File.Exists(file))
                    using (var sha = System.Security.Cryptography.SHA256.Create())
                        FileHash = Convert.ToHexString(sha.ComputeHash(System.IO.File.ReadAllBytes(file))).ToLowerInvariant();
            }
            catch (Exception e) { Log.Warning("[mods] could not read this mod's own file: " + e.Message); }
#if !ANDROID_PORT   // Windows only
            try { Updater.LoadedFrom = MelonAssembly.Location ?? ""; } catch { }
#endif
            Log.Msg($"Warpforge Revival {Version} - server: {Config.ServerUrl}" + (Config.Secure ? " (encrypted)" : " (not encrypted)"));

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

        /// <summary>Closes the game for real (on a phone the game's own exit only puts it in the background).</summary>
        internal static void QuitGame()
        {
#if ANDROID_PORT
            AndroidUpdater.CloseApp();
#else
            UnityEngine.Application.Quit();
#endif
        }

        /// <summary>
        /// The server refused to sign in because this mod is out of date: fetch and install its build
        /// now, without asking, and close the game so the next start uses it. False when that could
        /// not be done (no update offered, updates switched off, download failed).
        /// </summary>
        internal static System.Threading.Tasks.Task<bool> UpdateForServer()
        {
#if ANDROID_PORT
            return AndroidUpdater.UpdateNow();
#else
            return Updater.UpdateNow();
#endif
        }

        public override void OnGUI()
        {
            GameSignIn.Draw();
        }

#if ANDROID_PORT
        private static bool graphicsNoted;
#endif
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
            if (!graphicsNoted)
            {
                // Which drawing system the engine really started (the graphics plugin only asks for one).
                graphicsNoted = true;
                try { Log.Msg("[graphics] the game draws with " + UnityEngine.SystemInfo.graphicsDeviceType + " (" + UnityEngine.SystemInfo.graphicsDeviceVersion + ", " + UnityEngine.SystemInfo.graphicsDeviceName + ")"); }
                catch (Exception e) { Log.Msg("[graphics] could not read the drawing system: " + e.Message); }
            }
            AndroidUpdater.Tick();
#else
            Updater.Pump();
#endif
            SupportPage.Tick();
            AccountPage.Tick();
            GameSignIn.Tick();
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
