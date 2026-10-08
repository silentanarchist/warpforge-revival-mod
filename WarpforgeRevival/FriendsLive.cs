using System;
using HarmonyLib;
using Il2Cpp;
using Il2CppInterop.Runtime;

namespace WarpforgeRevival
{
    /// <summary>
    /// Keeps the friend list current without restarting: when Social > Friends is opened, and every
    /// 15 seconds while it stays open, the list is fetched from the server again (someone who added
    /// you shows up without a relog).
    /// </summary>
    internal static class FriendsLive
    {
        private static FriendsTab open;
        private static float next;
        private static bool busy;
        private static string shown = "";

        private static float busySince;

        private static string Key(Il2CppSystem.Collections.Generic.List<Friend> list)
        {
            if (list == null) return "";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < list.Count; i++) { var f = list[i]; if (f != null) sb.Append(f.id).Append('=').Append(f.name).Append(';'); }
            return sb.ToString();
        }

        private static void Refresh()
        {
            // A request that never answered must not block refreshing for good.
            if (busy && UnityEngine.Time.realtimeSinceStartup - busySince < 30f) return;
            var pd = PlayerDataManager.singletonManager;
            if ((object)pd == null) return;
            busy = true;
            busySince = UnityEngine.Time.realtimeSinceStartup;
            Action<Il2CppSystem.Collections.Generic.List<Friend>> done = list =>
            {
                busy = false;
                try
                {
                    string key = Key(list);
                    if (key == shown) return;
                    shown = key;
                    RevivalMod.Log.Msg($"[friends] list updated: {(list == null ? 0 : list.Count)} friend(s)");
                    var tab = open;
                    if ((object)tab != null && tab.Pointer != IntPtr.Zero && tab.m_CachedPtr != IntPtr.Zero) tab.FillInFriendsContainer();
                }
                catch (Exception e) { RevivalMod.Log.Warning("[friends] " + e.Message); }
            };
            Action<string> failed = error => { busy = false; };
            try
            {
                // An empty id adds nobody; the server answers with the current list.
                pd.AddFriendToList("", DelegateSupport.ConvertDelegate<Il2CppSystem.Action<Il2CppSystem.Collections.Generic.List<Friend>>>(done),
                    DelegateSupport.ConvertDelegate<Il2CppSystem.Action<string>>(failed));
            }
            catch (Exception e) { busy = false; RevivalMod.Log.Warning("[friends] " + e.Message); }
        }

        /// <summary>Called every frame from the mod's update loop.</summary>
        public static void Tick()
        {
            if ((object)open == null) return;
            if (UnityEngine.Time.realtimeSinceStartup < next) return;
            next = UnityEngine.Time.realtimeSinceStartup + 15f;
            if (open.Pointer == IntPtr.Zero || open.m_CachedPtr == IntPtr.Zero) { open = null; return; }
#if ANDROID_PORT
            // On the phone "close the page" is too short to hook safely (see NoShortHooks), so a
            // page that is no longer on screen is noticed here instead.
            try { if (!open.gameObject.activeInHierarchy) { open = null; return; } } catch { open = null; return; }
#endif
            if (!UnityEngine.Application.isFocused) return;     // no polling while the game is in the background
            Refresh();
        }

        [HarmonyPatch(typeof(FriendsTab), nameof(FriendsTab.OnOpen))]
        private static class Opened
        {
            private static void Postfix(FriendsTab __instance)
            {
                open = __instance;
                SocialPage.DressFriendsTab(__instance);
                try { shown = Key(PlayerDataManager.singletonManager?.friendsData?.friendList); } catch { shown = ""; }
                next = UnityEngine.Time.realtimeSinceStartup + 15f;
                Refresh();
            }
        }

#if !ANDROID_PORT
        [HarmonyPatch(typeof(FriendsTab), nameof(FriendsTab.OnClose))]
        private static class Closed
        {
            private static void Postfix() => open = null;
        }
#endif
    }
}
