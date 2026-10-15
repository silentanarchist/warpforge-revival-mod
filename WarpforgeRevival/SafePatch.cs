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
    }
}
