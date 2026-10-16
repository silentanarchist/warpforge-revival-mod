using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Il2Cpp;
using Il2CppCodeStage.AntiCheat.Detectors;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace WarpforgeRevival
{
    /// <summary>
    /// The game ships with Anti-Cheat Toolkit but never switches its detectors on. During online matches
    /// (not replays, not against the AI) the mod switches on two of them, report-only:
    ///  - ObscuredCheatingDetector: the game keeps mana, health, attack and the turn flags as "obscured"
    ///    values with a decoy copy; a memory editor changing the decoy raises it;
    ///  - SpeedHackDetector: the game's clock running faster than the system's.
    /// An alarm is sent to the server once per kind per match (Revival/CheatAlarm), which files it as an
    /// automatic report for the admins. Nothing is blocked and the player is not told. The detectors are
    /// stopped when the match ends.
    /// </summary>
    internal static class AntiCheat
    {
        private static bool running;
        private static float startedAt;
        private static readonly HashSet<string> sent = new HashSet<string>();

        internal static void Tick()
        {
            try
            {
                var battle = BattleManager.Instance;
                bool live = (object)battle != null && battle.Pointer != IntPtr.Zero && battle.m_CachedPtr != IntPtr.Zero && Online(battle);
                if (live && !running) Start();
                else if (!live && running) Stop();
            }
            catch (Exception e) { RevivalMod.Log.Warning("[anticheat] " + e.Message); if (running) Stop(); }
        }

        private static bool Online(BattleManager battle)
        {
            try { return !battle.IsReplayMatch() && battle.IsNetworkedGame(); } catch { return false; }
        }

        private static void Start()
        {
            running = true;
            startedAt = Time.realtimeSinceStartup;
            sent.Clear();
            try
            {
                ObscuredCheatingDetector.StartDetection(DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(new Action(() => Alarm("tamper"))));
                // interval 1 s, 3 slips allowed, 30 s of normal time before a slip is forgotten (the toolkit's defaults)
                SpeedHackDetector.StartDetection(DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(new Action(() => Alarm("speed"))), 1f, 3, 30);
                RevivalMod.Log.Msg("[anticheat] tampering and speed detectors on for this match (report only)");
            }
            catch (Exception e) { RevivalMod.Log.Warning("[anticheat] could not start the detectors: " + e.Message); }
        }

        private static void Stop()
        {
            running = false;
            try { ObscuredCheatingDetector.StopDetection(); } catch { }
            try { SpeedHackDetector.StopDetection(); } catch { }
            RevivalMod.Log.Msg("[anticheat] detectors off (match over)");
        }

        private static void Alarm(string kind)
        {
            if (!running || !sent.Add(kind)) return;
            int seconds = (int)(Time.realtimeSinceStartup - startedAt);
            RevivalMod.Log.Warning($"[anticheat] {(kind == "tamper" ? "tampering" : "speed")} detector raised an alarm {seconds} s into the match");
            string ticket = PlayFabTransport.SessionTicket;
            if (string.IsNullOrEmpty(ticket)) return;
            string url = RevivalMod.Config.ServerUrl + "/playfab/Revival/CheatAlarm";
            string detail = $"{seconds} s into the match, mod {RevivalMod.Version}";
            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    using var http = Net.Client(TimeSpan.FromSeconds(20));
                    using var msg = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, url)
                    {
                        Content = new System.Net.Http.StringContent(JsonSerializer.Serialize(new Dictionary<string, string> { ["kind"] = kind, ["detail"] = detail }), Encoding.UTF8, "application/json")
                    };
                    msg.Headers.TryAddWithoutValidation("X-Authorization", ticket);
                    using var reply = await http.SendAsync(msg);
                }
                catch (Exception e) { RevivalMod.Log.Warning("[anticheat] alarm not sent: " + e.Message); }
            });
        }
    }
}
