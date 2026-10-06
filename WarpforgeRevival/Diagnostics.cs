using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace WarpforgeRevival
{
    /// <summary>
    /// Development aid: mirrors the game's own debug log (normally switched off in release builds)
    /// into the MelonLoader console/log, and logs entry into key login/loading methods, including
    /// each step of their async state machines. Patches are applied by name so a missing method
    /// only produces a warning.
    /// </summary>
    internal static class Diagnostics
    {
        // Individual methods to trace.
        private static readonly (string type, string method)[] Traced =
        {
            ("LoginManager", "Initialize"),
            ("LoginManager", "SetProgress"),
            ("LoginManager", "CheckInternetConnection"),
            ("LoginManager", "TryLoginToPlayfab"),
            ("LoginManager", "ProcessLoginResult"),
            ("LoginManager", "ProceedSuccessfulLogin"),
            ("LoginManager", "TryCompleteLogin"),
            ("LoginManager", "RetryLogin"),
            ("LoginManager", "ThrowCustomError"),
            ("PlayerDataManager", "LoginToPlayFab"),
            ("PlayerDataManager", "FinishLogin"),
            ("PlayerDataManager", "AsyncInitialization"),
            ("PlayerDataManager", "UnpackUserInfo"),
            ("PlayerDataManager", "UnpackConnectionSettings"),
            ("PlayerDataManager", "UnpackAddressables"),
            ("PlayerDataManager", "UnpackInventory"),
            ("PlayerDataManager", "UnpackUserProfile"),
            ("PlayerDataManager", "InitializeLiveOpsAndConfig"),
            ("PlayerDataManager", "PreloadLeaderboards"),
            ("PlayerDataManager", "ProcessNewLogin"),
            ("CloudscriptHandler", "ExecuteCustomCloudscript"),
            ("CloudscriptHandler", "LoginWithSteamAccount"),
            ("NetworkCustomManager", "StartConnection"),
            ("NetworkCustomManager", "ConnectToPhoton"),
            ("GameBootController", "StartGame"),
            ("AddressablesManager", "Init"),
            ("AddressablesManager", "CheckForCatalogUpdate"),
            ("AddressablesManager", "CheckForContentUpdate"),
            ("AddressablesManager", "DownloadContentUpdate"),
            ("AddressablesManager", "SetupCCDManager"),
            ("AddressablesManager", "SetupCaches"),
            ("AddressablesManager", "UpdateCatalogs"),
            ("AddressablesManager", "CheckCacheSize"),
            ("AddressablesManager", "WaitForDownloadConsent"),
            ("AddressablesManager", "LoadLibraries"),
            ("LiveOpsManager", "Unpack"),
            ("LiveOpsManager", "BuildHandlers"),
            ("LiveOpsManager", "InitializeHandlers"),
            ("EntityObjectsManager", "Unpack"),
            ("Everguild.LiveOps.Config.ConfigManager", "Unpack"),
            ("SharedEventDataManager", "Unpack"),
            ("SegmentationController", "Unpack"),
        };
        // NOTE: every method listed here was checked against GameAssembly.dll to have its own
        // native code. The compiler merges identical small methods (empty bodies, trivial
        // getters) into one shared function; hooking one of those hooks thousands of others.

        // Types whose async/coroutine state machines (MoveNext) are traced. MoveNext bodies are
        // always unique, so this is safe; we deliberately do NOT trace every method of a type.
        private static readonly string[] TracedTypes =
        {
            "AddressablesManager",
            "LiveOpsManager",
            "EntityObjectsManager",
            "Everguild.LiveOps.Config.ConfigManager",
            "SharedEventDataManager",
            "SegmentationController",
            "LoginManager",
        };

        // State machines of these PlayerDataManager methods (the type itself is too busy to trace whole).
        private static readonly string[] PlayerDataStateMachines =
        {
            "UnpackUserInfo", "UnpackAddressables", "InitializeLiveOpsAndConfig", "PreloadLeaderboards",
            "AsyncInitialization", "ProcessData",
        };

        private const int MaxLinesPerMethod = 40;
        private static readonly ConcurrentDictionary<MethodBase, int> Counts = new ConcurrentDictionary<MethodBase, int>();

        public static void Install(HarmonyLib.Harmony harmony)
        {
            var trace = new HarmonyMethod(typeof(Diagnostics).GetMethod(nameof(TracePrefix), BindingFlags.Static | BindingFlags.NonPublic));
            int ok = 0;

            void PatchAll(Type type, Func<MethodInfo, bool> filter)
            {
                foreach (var m in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).Where(filter))
                {
                    if (m.IsAbstract || m.ContainsGenericParameters) continue;
                    try { harmony.Patch(m, prefix: trace); ok++; }
                    catch (Exception e) { RevivalMod.Log.Warning($"[diag] could not trace {type.Name}.{m.Name}: {e.Message}"); }
                }
            }


            foreach (var (typeName, methodName) in Traced)
            {
                var type = GameTypes.Find(typeName);
                if (type == null) { RevivalMod.Log.Warning($"[diag] type not found: {typeName}"); continue; }
                PatchAll(type, m => m.Name == methodName);
            }

            // (No MoveNext tracing: async state machines are structs here and hooking them stalls
            //  the async method - see IsStruct.)

            // Forward the game's own debug output.
            var dbg = GameTypes.Find("CustomDebug");
            if (dbg != null)
            {
                foreach (var name in new[] { "Log", "LogWarning", "LogError" })
                {
                    var m = dbg.GetMethod(name, BindingFlags.Static | BindingFlags.Public);
                    var p = typeof(Diagnostics).GetMethod("Game" + name, BindingFlags.Static | BindingFlags.NonPublic);
                    if (m != null) { harmony.Patch(m, prefix: new HarmonyMethod(p)); ok++; }
                }
            }

            // Unity's own Debug.Log / LogWarning (Addressables and some game code log through these directly).
            try
            {
                harmony.Patch(AccessTools.Method(typeof(UnityEngine.Debug), nameof(UnityEngine.Debug.Log), new[] { typeof(Il2CppSystem.Object) }),
                    prefix: new HarmonyMethod(typeof(Diagnostics).GetMethod(nameof(UnityLog), BindingFlags.Static | BindingFlags.NonPublic)));
                harmony.Patch(AccessTools.Method(typeof(UnityEngine.Debug), nameof(UnityEngine.Debug.LogWarning), new[] { typeof(Il2CppSystem.Object) }),
                    prefix: new HarmonyMethod(typeof(Diagnostics).GetMethod(nameof(UnityLogWarning), BindingFlags.Static | BindingFlags.NonPublic)));
                ok += 2;
            }
            catch (Exception e) { RevivalMod.Log.Warning("[diag] could not hook Unity Debug.Log: " + e.Message); }

            RevivalMod.Log.Msg($"[diag] tracing {ok} game methods");
        }

        // Async state machines are structs in release builds; hooking their MoveNext through the
        // interop layer runs it on a copy, so the async method never advances. Only coroutine
        // (class) state machines are safe to trace.
        private static bool IsStruct(Type t)
        {
            for (var b = t.BaseType; b != null; b = b.BaseType)
                if (b.FullName == "Il2CppSystem.ValueType" || b == typeof(ValueType)) return true;
            return false;
        }

        private static void TracePrefix(MethodBase __originalMethod, object[] __args)
        {
            try
            {
                int n = Counts.AddOrUpdate(__originalMethod, 1, (_, c) => c + 1);
                if (n > MaxLinesPerMethod) return;
                var t = __originalMethod.DeclaringType;
                var owner = t?.DeclaringType != null ? t.DeclaringType.Name + "." + t.Name : t?.Name;
                var args = __args == null ? "" : string.Join(", ", __args.Select(Describe));
                RevivalMod.Log.Msg($"[trace] {owner}.{__originalMethod.Name}({args})" + (n == MaxLinesPerMethod ? "  (further calls not logged)" : ""));
            }
            catch { /* never break the game from a log line */ }
        }

        private static string Describe(object o)
        {
            if (o == null) return "null";
            if (o is string s) return "\"" + (s.Length > 80 ? s.Substring(0, 80) + "..." : s) + "\"";
            if (o is Delegate || o.GetType().Name.Contains("Action") || o.GetType().Name.Contains("Func")) return o.GetType().Name;
            var str = o.ToString();
            return str.Length > 80 ? str.Substring(0, 80) + "..." : str;
        }

        private static void GameLog(string logString) => RevivalMod.Log.Msg("[game] " + logString);
        private static void GameLogWarning(string logString) => RevivalMod.Log.Warning("[game] " + logString);
        private static void GameLogError(string logString) => RevivalMod.Log.Error("[game] " + logString);

        private static void UnityLog(Il2CppSystem.Object message)
        {
            try { RevivalMod.Log.Msg("[unity] " + message?.ToString()); } catch { }
        }

        private static void UnityLogWarning(Il2CppSystem.Object message)
        {
            try { RevivalMod.Log.Warning("[unity] " + message?.ToString()); } catch { }
        }
    }

    /// <summary>Resolves game types regardless of the Il2Cpp namespace prefix MelonLoader uses.</summary>
    internal static class GameTypes
    {
        public static Type Find(string name)
        {
            // Look only in the game's assembly; scanning every assembly trips over Unity types
            // that can't be loaded through reflection (noisy warnings in the log).
            var asm = typeof(Il2Cpp.LoginManager).Assembly;
            return asm.GetType("Il2Cpp." + name) ?? asm.GetType("Il2Cpp" + name) ?? asm.GetType(name)
                ?? asm.GetType("Il2Cpp." + name.Replace('/', '+')) ?? asm.GetType(name.Replace('/', '+'));
        }
    }
}
