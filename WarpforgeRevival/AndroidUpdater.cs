#if ANDROID_PORT
using System;

namespace WarpforgeRevival
{
    /// <summary>
    /// Closing the app for real on Android (after an update, and the mod's Quit). The update itself is in
    /// Updater.cs, shared with Windows since 0.13.74.
    /// </summary>
    internal static class AndroidUpdater
    {
        // ---- closing the app for real ----
        // Ending the process alone is not enough: Android keeps the app's screen in its list of recent
        // apps and starts it again from that leftover state, and that restart has been seen to break
        // the game (errors from its own code before login). So first ask Android to finish the screen
        // and remove it from the recent apps, the way a real exit does, and only then end the process.

        [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "getpid")] private static extern int getpid();
        [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "kill")] private static extern int kill(int pid, int sig);

        private static bool closeRequested;

        internal static void CloseApp()
        {
            BugReport.End();                 // closing the app ourselves (Quit, updates) is not a crash
            // Asked again: the process outlived an earlier close and the app was opened again in it.
            // That happened on an emulator: Android froze the closed app's process before the
            // background thread below could end it, the next start woke the same process, which still
            // remembered closing and closed again at once - every start, until a force stop.
            if (closeRequested) { EndProcess(); return; }
            closeRequested = true;
            try
            {
                var none = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<UnityEngine.jvalue>(0);
                IntPtr player = UnityEngine.AndroidJNI.FindClass("com/unity3d/player/UnityPlayer");
                IntPtr field = UnityEngine.AndroidJNI.GetStaticFieldID(player, "currentActivity", "Landroid/app/Activity;");
                IntPtr activity = UnityEngine.AndroidJNI.GetStaticObjectField(player, field);
                IntPtr activityClass = UnityEngine.AndroidJNI.FindClass("android/app/Activity");
                IntPtr finish = UnityEngine.AndroidJNI.GetMethodID(activityClass, "finishAndRemoveTask", "()V");
                if (activity == IntPtr.Zero || finish == IntPtr.Zero) throw new Exception("the app's screen was not found");
                UnityEngine.AndroidJNI.CallVoidMethod(activity, finish, none);
                RevivalMod.Log.Msg("[update] asked Android to close the app's screen and remove it from recent apps");
            }
            catch (Exception e)
            {
                try { UnityEngine.AndroidJNI.ExceptionClear(); } catch { }
                RevivalMod.Log.Warning("[update] could not close the app's screen the normal way (" + e.Message + "); ending the process only");
            }
            // End the process right here, a moment after the request: Android has the request by
            // then (the call above returns once it has), and waiting longer risks the process being
            // frozen as a closed app before it ends. (Ending it from the game's per-frame update
            // never ran either: once the screen is closed no frames run.) A background thread
            // stays as a second try.
            var ender = new System.Threading.Thread(() =>
            {
                System.Threading.Thread.Sleep(1500);
                EndProcess();
            });
            ender.IsBackground = true;
            ender.Start();
            System.Threading.Thread.Sleep(150);
            EndProcess();
        }

        private static int ending;

        private static void EndProcess()
        {
            // logged once; the kill itself is always tried (an earlier one may not have gone through)
            if (System.Threading.Interlocked.Exchange(ref ending, 1) == 0) RevivalMod.Log.Msg("[update] ending the process");
            try { kill(getpid(), 9); } catch { }
            try { System.Diagnostics.Process.GetCurrentProcess().Kill(); } catch { }
            Environment.Exit(0);
        }
    }
}
#endif
