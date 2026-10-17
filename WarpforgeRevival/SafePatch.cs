using System;
using System.Reflection;
using HarmonyLib;

namespace WarpforgeRevival
{
    /// <summary>
    /// Applies the mod's [HarmonyPatch] hooks one class at a time, each inside its own try (0.13.31).
    /// Harmony's own PatchAll stops at the first hook that cannot be applied, and every hook after it is
    /// then left out too: in 0.13.29 one such hook left the mod's sign-in redirection unapplied and every
    /// game stopped before signing in. Here a hook that fails is logged and skipped, and the rest are applied
    /// as usual. Used on PC and Android (MelonLoader's automatic PatchAll is switched off for both).
    ///
    /// Android (0.13.53): the loader's hook copies the first 16 bytes of the game's code for the method; when those
    /// cross a 4 KB memory page it crashes the app at start-up (0.13.52's hook on WindowTabBase.TryOpenTab, which
    /// starts 8 bytes before a page end, crashed the Galaxy Tab S7 and the Pixel 8 Pro; none of the 220 hooks that
    /// worked crosses a page). Such a hook is now skipped with an error naming it, instead of being installed.
    /// </summary>
    internal static class SafePatch
    {
        internal static void All(HarmonyLib.Harmony harmony, Assembly assembly)
        {
            int applied = 0, failed = 0;
            foreach (var type in AccessTools.GetTypesFromAssembly(assembly))
            {
                try
                {
#if ANDROID_PORT
                    string unsafeTarget = CrossesPage(type);
                    if (unsafeTarget != null)
                    {
                        failed++;
                        RevivalMod.Log.Error($"[startup] the hook {type.FullName} is left out: {unsafeTarget} starts too close to the end of a memory page for the Android loader to hook it");
                        continue;
                    }
#endif
                    var done = harmony.CreateClassProcessor(type).Patch();
                    if (done != null && done.Count > 0) applied++;
                }
                catch (Exception e)
                {
                    failed++;
                    var inner = e;
                    while (inner.InnerException != null) inner = inner.InnerException;
                    RevivalMod.Log.Error($"[startup] the hook {type.FullName} could not be applied and is left out: {inner.GetType().Name}: {inner.Message}");
                }
            }
            RevivalMod.Log.Msg($"[startup] {applied} hook class(es) applied" + (failed > 0 ? $", {failed} left out (see the errors above)" : ""));
        }

#if ANDROID_PORT
        /// <summary>The hooked game method, when its code's first 16 bytes cross a 4 KB page; null when safe or unknown.</summary>
        private static unsafe string CrossesPage(Type type)
        {
            try
            {
                var parts = HarmonyMethodExtensions.GetFromType(type);
                if (parts == null || parts.Count == 0) return null;
                var info = HarmonyMethod.Merge(parts);
                if (info.declaringType == null || string.IsNullOrEmpty(info.methodName)) return null;
                var original = AccessTools.Method(info.declaringType, info.methodName, info.argumentTypes);
                if (original == null) return null;
                var field = Il2CppInterop.Common.Il2CppInteropUtils.GetIl2CppMethodInfoPointerFieldForGeneratedMethod(original);
                if (field == null) return null;
                var methodInfo = (IntPtr)field.GetValue(null);
                if (methodInfo == IntPtr.Zero) return null;
                long code = (*(IntPtr*)methodInfo).ToInt64();      // Il2CppMethodInfo starts with the code pointer
                if (code == 0) return null;
                return (code & 0xFFF) > 0x1000 - 16 ? $"{info.declaringType.Name}.{info.methodName} (code at page offset 0x{code & 0xFFF:X})" : null;
            }
            catch { return null; }
        }
#endif
    }
}
