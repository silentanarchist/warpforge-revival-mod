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
#if ANDROID_TEST
                    // On a phone (ARM64) the patching layer mishandles game methods that hand back a
                    // struct (async methods do): the method then runs with scrambled arguments and
                    // crashes. Leave those alone.
                    if (m.ReturnType != typeof(void) && !m.ReturnType.IsPrimitive && !m.ReturnType.IsEnum && IsStruct(m.ReturnType))
                    {
                        RevivalMod.Log.Msg($"[diag] not tracing {type.Name}.{m.Name} (returns a struct)");
                        continue;
                    }
#endif
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
#if ANDROID_PORT
            // Left out on a phone for now: the game writes a line per inventory item through these.
            if (ok >= 0) { RevivalMod.Log.Msg($"[diag] tracing {ok} game methods"); return; }
#endif
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
#if ANDROID_PORT
                if (__originalMethod.Name == "UnpackInventory") InventoryStarted();
#endif
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

#if ANDROID_PORT
        // Measuring why unpacking the inventory takes so much longer on a phone.
        private static int gcAtStart = -1;
        private static readonly System.Diagnostics.Stopwatch InventoryClock = new System.Diagnostics.Stopwatch();
        internal static void InventoryStarted()
        {
            try
            {
                gcAtStart = Il2CppSystem.GC.CollectionCount(0);
                RevivalMod.Log.Msg("[timing] loader hook calls before the inventory: " + Il2CppInterop.Runtime.Injection.HookStats.Snapshot()
                    + $", content lookups {AddressablesRedirect.Lookups}");
                InventoryClock.Restart();
            }
            catch (Exception e) { RevivalMod.Log.Warning("[timing] " + e.Message); }
        }
        private static void GameLog(string logString)
        {
            RevivalMod.Log.Msg("[game] " + logString);
            try
            {
                if (logString != null && logString.StartsWith("Finished unpacking inventory") && gcAtStart >= 0)
                {
                    int collections = Il2CppSystem.GC.CollectionCount(0) - gcAtStart;
                    RevivalMod.Log.Msg($"[timing] inventory took {InventoryClock.ElapsedMilliseconds} ms with {collections} memory clean-ups by the game");
                    RevivalMod.Log.Msg("[timing] loader hook calls after the inventory:  " + Il2CppInterop.Runtime.Injection.HookStats.Snapshot()
                        + $", content lookups {AddressablesRedirect.Lookups}");
                    try
                    {
                        // how long one trip through a loader hook takes
                        IntPtr type = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_type(Il2CppInterop.Runtime.Il2CppClassPointerStore<Il2CppSystem.Object>.NativeClassPtr);
                        const int rounds = 20000;
                        var trip = System.Diagnostics.Stopwatch.StartNew();
                        for (int i = 0; i < rounds; i++) Il2CppInterop.Runtime.IL2CPP.il2cpp_class_from_il2cpp_type(type);
                        trip.Stop();
                        RevivalMod.Log.Msg($"[timing] one trip through a loader hook takes about {trip.Elapsed.TotalMilliseconds * 1000.0 / rounds:F2} microseconds");
                    }
                    catch (Exception e) { RevivalMod.Log.Warning("[timing] hook trip: " + e.Message); }
                    var one = System.Diagnostics.Stopwatch.StartNew();
                    Il2CppSystem.GC.Collect();
                    RevivalMod.Log.Msg($"[timing] one memory clean-up takes {one.ElapsedMilliseconds} ms; game memory in use {Il2CppSystem.GC.GetTotalMemory(false) / (1024 * 1024)} MB");
                }
            }
            catch (Exception e) { RevivalMod.Log.Warning("[timing] " + e.Message); }
        }
#else
        private static void GameLog(string logString) => RevivalMod.Log.Msg("[game] " + logString);
#endif
        private static void GameLogWarning(string logString) => RevivalMod.Log.Warning("[game] " + logString);
        private static void GameLogError(string logString) => RevivalMod.Log.Error("[game] " + logString);

        private static void UnityLog(Il2CppSystem.Object message)
        {
            try
            {
                string text = message?.ToString();
#if ANDROID_TEST
                // Written once per inventory item (thousands); on a phone writing them all out
                // makes loading take several times longer.
                if (text == "No listener registered to this event") return;
#endif
                RevivalMod.Log.Msg("[unity] " + text);
            }
            catch { }
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
