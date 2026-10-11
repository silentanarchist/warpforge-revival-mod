using System;
using System.Threading;
using HarmonyLib;
using Il2Cpp;
using Il2CppInterop.Runtime;

namespace WarpforgeRevival
{
    /// <summary>
    /// Phones only. Unpacking the player's inventory at log-in is the longest freeze of start-up: on a
    /// Pixel 6 Pro it took 4.6 s with the game's memory being cleaned up 72 times at about 23 ms each,
    /// so roughly 1.7 s of it was clean-ups, all on the thread that also plays the opening video
    /// (a system trace showed the video stalling for exactly that long, 2026-10-10). Each clean-up also
    /// stops every thread with signals, the same mechanism behind the start-up crashes on that phone.
    ///
    /// So the game's garbage collector is paused while the inventory is unpacked and switched back on
    /// straight after. Two limits keep memory in check: a watcher thread switches it back on early if the
    /// game's memory grows by more than a set amount (a tenth of the phone's memory, between 256 and
    /// 768 MB), and in any case after 20 seconds.
    /// </summary>
    internal static class LoadingGc
    {
#if ANDROID_PORT
        private static int paused;           // 1 while this class has the collector paused
        private static long startHeap, limitBytes;
        private static System.Diagnostics.Stopwatch clock;

        [HarmonyPatch(typeof(PlayerDataManager), nameof(PlayerDataManager.UnpackInventory))]
        private static class Unpack
        {
            private static void Prefix()
            {
                try
                {
                    if (IL2CPP.il2cpp_gc_is_disabled() || Interlocked.Exchange(ref paused, 1) == 1) return;
                    int ramMb = 4096;
                    try { ramMb = UnityEngine.SystemInfo.systemMemorySize; } catch { }
                    limitBytes = Math.Clamp(ramMb / 10, 256, 768) * 1024L * 1024L;
                    startHeap = IL2CPP.il2cpp_gc_get_heap_size();
                    clock = System.Diagnostics.Stopwatch.StartNew();
                    IL2CPP.il2cpp_gc_disable();
                    RevivalMod.Log.Msg($"[loading] game memory clean-up paused while the inventory is unpacked (allowed to grow {limitBytes >> 20} MB)");
                    var watcher = new Thread(Watch) { IsBackground = true, Name = "Revival GC watch" };
                    watcher.Start();
                }
                catch (Exception e) { Volatile.Write(ref paused, 0); RevivalMod.Log.Warning("[loading] could not pause the clean-up: " + e.Message); }
            }

            private static void Postfix() => Resume("the inventory is unpacked");
        }

        private static void Watch()
        {
            try
            {
                while (Volatile.Read(ref paused) == 1)
                {
                    Thread.Sleep(50);
                    if (Volatile.Read(ref paused) != 1) return;
                    if (IL2CPP.il2cpp_gc_get_heap_size() - startHeap > limitBytes) { Resume("memory grew to the limit"); return; }
                    if (clock.ElapsedMilliseconds > 20000) { Resume("20 seconds passed"); return; }
                }
            }
            catch (Exception e) { Resume("the watcher stopped (" + e.Message + ")"); }
        }

        private static void Resume(string why)
        {
            if (Interlocked.Exchange(ref paused, 0) != 1) return;
            try { IL2CPP.il2cpp_gc_enable(); } catch { }
            try
            {
                long grew = (IL2CPP.il2cpp_gc_get_heap_size() - startHeap) >> 20;
                RevivalMod.Log.Msg($"[loading] game memory clean-up on again after {clock?.ElapsedMilliseconds ?? 0} ms: {why}; memory grew {grew} MB");
            }
            catch { }
        }
#endif
    }
}
