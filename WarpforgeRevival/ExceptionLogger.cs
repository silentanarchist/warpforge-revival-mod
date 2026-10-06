using System;
using HarmonyLib;

namespace WarpforgeRevival
{
    /// <summary>
    /// The game switches Unity logging off and runs most of its login as async code, so an
    /// exception there just leaves the loading screen hanging. These hooks surface them.
    /// </summary>
    internal static class ExceptionLogger
    {
        private static string Text(Il2CppSystem.Object o)
        {
            try { return o?.ToString() ?? "null"; } catch { return "<unprintable>"; }
        }

        // Every faulted Task (async Task / async Task<T> methods) ends up here.
        [HarmonyPatch(typeof(Il2CppSystem.Threading.Tasks.Task), nameof(Il2CppSystem.Threading.Tasks.Task.TrySetException))]
        private static class TaskFault
        {
            private static void Prefix(Il2CppSystem.Object exceptionObject)
                => RevivalMod.Log.Error("[async exception] " + Text(exceptionObject));
        }

        [HarmonyPatch(typeof(UnityEngine.Debug), nameof(UnityEngine.Debug.LogException), new[] { typeof(Il2CppSystem.Exception) })]
        private static class LogException1
        {
            private static void Prefix(Il2CppSystem.Exception exception)
                => RevivalMod.Log.Error("[unity exception] " + Text(exception));
        }

        [HarmonyPatch(typeof(UnityEngine.Debug), nameof(UnityEngine.Debug.LogException), new[] { typeof(Il2CppSystem.Exception), typeof(UnityEngine.Object) })]
        private static class LogException2
        {
            private static void Prefix(Il2CppSystem.Exception exception)
                => RevivalMod.Log.Error("[unity exception] " + Text(exception));
        }

        // Menu components are initialised by SceneInitializer, which swallows exceptions into a
        // "Failed to initialize service ..." popup. Log which component failed, with the full stack.
        [HarmonyPatch(typeof(Il2Cpp.SceneInitializer), nameof(Il2Cpp.SceneInitializer.OnInitializableError))]
        private static class InitError
        {
            private static void Prefix(Il2CppSystem.Object initializable, Il2CppSystem.Exception e)
            {
                string who = "?";
                try { who = initializable?.GetIl2CppType()?.FullName ?? "?"; } catch { }
                try
                {
                    var comp = initializable?.TryCast<UnityEngine.Component>();
                    if (comp != null) who += " on GameObject '" + comp.gameObject.name + "'";
                }
                catch { }
                RevivalMod.Log.Error($"[init error] {who}: {Text(e)}");
            }
        }

        [HarmonyPatch(typeof(UnityEngine.Debug), nameof(UnityEngine.Debug.LogError), new[] { typeof(Il2CppSystem.Object) })]
        private static class LogError1
        {
            private static void Prefix(Il2CppSystem.Object message)
                => RevivalMod.Log.Error("[unity error] " + Text(message));
        }
    }
}
