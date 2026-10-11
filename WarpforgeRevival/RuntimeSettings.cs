using System;
using System.IO;

namespace WarpforgeRevival
{
    /// <summary>
    /// Phones only. The memory settings of the loader's .NET, read by the patched loader from
    /// UserData/runtime-gc.txt before the runtime starts (so a change here takes effect at the next
    /// start, with no new patch of the game).
    ///
    /// MONO_THREADS_SUSPEND=coop (0.12.56): the runtime stops its threads for garbage collection at safe
    /// points instead of with signals. On Android it used the signals SIGPWR and SIGXCPU for that, the
    /// same two the game's own collector uses; the loader's patched runtime guesses which collector each
    /// signal was meant for, and a wrong guess hits a thread in the middle of an allocation. Both crash
    /// reports from the Pixel 6 Pro (2026-10-10, 21:26 and 21:35) are inside the runtime's allocator, in
    /// unrelated places. With "coop" the runtime sends no such signals, so there is nothing to guess.
    ///
    /// Also: the 64 MB first area. Tried in 0.12.51-0.12.52 and dropped: major=marksweep with
    /// clear-at-gc (the crashes went on: 2 of about 3 starts, at new places).
    ///
    /// Why: on a Pixel 6 Pro the runtime crashes now and then while handing out memory during start-up
    /// (2026-10-10): it zeroes a fresh block whose end lies past its own memory, a SIGSEGV inside memset
    /// called from mono_gc_alloc_obj, while the mod applies its hooks. Pausing the game's collector did
    /// not stop it, so the runtime's own memory handling is what is tried here:
    ///  - major=marksweep: no background collector thread ("SGen worker") touching memory alongside;
    ///  - clear-at-gc: new memory is cleared during clean-ups instead of each time a block is handed
    ///    out, which takes the crashing step out of hand-outs.
    /// The 64 MB first area stays (fewer clean-ups during start-up).
    /// </summary>
    internal static class RuntimeSettings
    {
        private const string Wanted =
            "# Written by Warpforge Revival; read by the patched loader before .NET starts.\n" +
            "MONO_GC_PARAMS=nursery-size=64m\n" +
            "MONO_THREADS_SUSPEND=coop\n";

        internal static void Apply()
        {
#if ANDROID_PORT
            try
            {
                string now = $"MONO_GC_PARAMS={Environment.GetEnvironmentVariable("MONO_GC_PARAMS") ?? "(none)"}, MONO_GC_DEBUG={Environment.GetEnvironmentVariable("MONO_GC_DEBUG") ?? "(none)"}, MONO_THREADS_SUSPEND={Environment.GetEnvironmentVariable("MONO_THREADS_SUSPEND") ?? "(none)"}";
                string file = Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "runtime-gc.txt");
                string had = File.Exists(file) ? File.ReadAllText(file) : null;
                if (had != Wanted)
                {
                    File.WriteAllText(file, Wanted);
                    RevivalMod.Log.Msg($"[startup] runtime memory settings now {now}; new ones written for the next start");
                }
                else RevivalMod.Log.Msg($"[startup] runtime memory settings: {now}");
            }
            catch (Exception e) { RevivalMod.Log.Warning("[startup] runtime memory settings: " + e.Message); }
#endif
        }
    }
}
