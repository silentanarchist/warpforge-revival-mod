using System;
using Il2Cpp;
using Il2CppInterop.Runtime;

namespace WarpforgeRevival
{
    /// <summary>
    /// Shared answers to "which scene", "is a match on" and "where is the menu bar" (code review M-11, 0.13.73).
    /// Several of the mod's per-frame checks asked Unity for these themselves, some every frame: the scene name
    /// from SceneManager (four places, two every frame), the match by searching the whole scene for its
    /// BattleManager, the menu bar by searching for its NavigationPanelController. Here the scene name is kept
    /// from Unity's own "active scene changed" event, the match from the game's BattleManager.Instance, and the
    /// menu bar is found once and kept while it lives (searched for again at most once a second).
    /// </summary>
    internal static class GameState
    {
        private static string scene;
        private static bool sceneHooked;

        /// <summary>The active scene's name ("?" when Unity cannot say).</summary>
        internal static string Scene
        {
            get
            {
                if (!sceneHooked) HookScene();
                return scene ?? "?";
            }
        }

        private static void HookScene()
        {
            sceneHooked = true;
            try { scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name; } catch { scene = "?"; }
            try
            {
                UnityEngine.SceneManagement.SceneManager.add_activeSceneChanged(
                    DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction<UnityEngine.SceneManagement.Scene, UnityEngine.SceneManagement.Scene>>(
                        new Action<UnityEngine.SceneManagement.Scene, UnityEngine.SceneManagement.Scene>((_, now) =>
                        {
                            try { scene = now.name; } catch { scene = "?"; }
                        })));
            }
            catch (Exception e)
            {
                // without the event, fall back to asking each time (the old way)
                sceneHooked = false;
                scene = null;
                RevivalMod.Log.Warning("[state] scene changes cannot be followed: " + e.Message);
                try { scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name; } catch { }
            }
        }

        /// <summary>A match (or a replay) is on: the game's BattleManager exists and is alive.</summary>
        internal static bool InBattle
        {
            get
            {
                try
                {
                    var b = BattleManager.Instance;
                    return (object)b != null && b.Pointer != IntPtr.Zero && b.m_CachedPtr != IntPtr.Zero;
                }
                catch { return false; }
            }
        }

        private static NavigationPanelController nav;
        private static float navLookedAt = -10f;

        /// <summary>The main menu bar, or null (not in this scene).</summary>
        internal static NavigationPanelController Nav
        {
            get
            {
                if ((object)nav != null && nav.Pointer != IntPtr.Zero && nav.m_CachedPtr != IntPtr.Zero) return nav;
                nav = null;
                float now = UnityEngine.Time.realtimeSinceStartup;
                if (now - navLookedAt < 1f) return null;
                navLookedAt = now;
                try { nav = UnityEngine.Object.FindObjectOfType<NavigationPanelController>(); } catch { nav = null; }
                return nav;
            }
        }

        /// <summary>The main menu bar now (searched for at once when not kept): for one-off look-ups.</summary>
        internal static NavigationPanelController FindNav()
        {
            if ((object)nav != null && nav.Pointer != IntPtr.Zero && nav.m_CachedPtr != IntPtr.Zero) return nav;
            try { nav = UnityEngine.Object.FindObjectOfType<NavigationPanelController>(); } catch { nav = null; }
            navLookedAt = UnityEngine.Time.realtimeSinceStartup;
            return nav;
        }

        /// <summary>The bar the game has just built or set up (its own Awake / OnInitializationComplete): kept.</summary>
        internal static void SawNav(NavigationPanelController bar)
        {
            if ((object)bar != null && bar.Pointer != IntPtr.Zero) nav = bar;
        }
    }
}
