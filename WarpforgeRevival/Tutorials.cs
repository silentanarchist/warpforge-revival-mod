using HarmonyLib;

namespace WarpforgeRevival
{
    /// <summary>
    /// The tutorial battles need the original cloud-hosted card data, and a fresh revival
    /// account would otherwise be sent into the tutorial on every launch. With SkipTutorial
    /// on (default), the game treats the tutorials as completed and goes to the main menu.
    /// (Each method below was checked to have its own native code, so patching is safe.)
    /// </summary>
    internal static class Tutorials
    {
        [HarmonyPatch(typeof(Il2Cpp.PlayerDataManager), nameof(Il2Cpp.PlayerDataManager.FirstTwoTutorialCompleted))]
        private static class FirstTwo
        {
            private static void Postfix(ref bool __result) { if (RevivalMod.Config.SkipTutorial) __result = true; }
        }

        [HarmonyPatch(typeof(Il2Cpp.PlayerDataManager), nameof(Il2Cpp.PlayerDataManager.AllTutorialsCompleted))]
        private static class All
        {
            // The original throws when the server runs no tutorial event, so skip it entirely.
            private static bool Prefix(ref bool __result)
            {
                if (!RevivalMod.Config.SkipTutorial) return true;
                __result = true;
                return false;
            }
        }

        // The original throws on a fresh account (no tutorial save data yet), so skip it entirely.
        [HarmonyPatch(typeof(Il2Cpp.PlayerDataManager), nameof(Il2Cpp.PlayerDataManager.IsTutorialIndexCompleted))]
        private static class Index
        {
            private static bool Prefix(ref bool __result)
            {
                if (!RevivalMod.Config.SkipTutorial) return true;
                __result = true;
                return false;
            }
        }

        // "First-time player" steps in the main menu (pick a starter deck, onboarding pointers)
        // are driven by player tags. Treat them as already done.
        [HarmonyPatch(typeof(Il2Cpp.SegmentationController), nameof(Il2Cpp.SegmentationController.CheckTag))]
        private static class Tags
        {
            private static void Postfix(Il2Cpp.PlayerTag tag, ref bool __result)
            {
                if (!RevivalMod.Config.SkipTutorial || __result) return;
                if (tag == Il2Cpp.PlayerTag.CollectedFirstDeck || tag == Il2Cpp.PlayerTag.FinishedFTUETutorial
                    || tag == Il2Cpp.PlayerTag.FinishedOnboarding)
                    __result = true;
            }
        }

        // The card detail page draws a first-time help overlay ("Melee Attack", "Energy Cost" ...)
        // and its own check for whether to show it fails on a revival account. Keep it hidden.
        [HarmonyPatch(typeof(Il2Cpp.CardDisplayWindow), nameof(Il2Cpp.CardDisplayWindow.SetupTutorialObj))]
        private static class CardHelpOverlay
        {
            private static bool Prefix(Il2Cpp.CardDisplayWindow __instance)
            {
                if (!RevivalMod.Config.SkipTutorial) return true;
                try
                {
                    if (__instance.tutorialObj != null) __instance.tutorialObj.SetActive(false);
                    if (__instance.tutorialObjUnit != null) __instance.tutorialObjUnit.SetActive(false);
                    if (__instance.tutorialObjCost != null) __instance.tutorialObjCost.SetActive(false);
                }
                catch (System.Exception e) { RevivalMod.Log.Warning("[tutorial] could not hide card help overlay: " + e.Message); }
                return false;
            }
        }
    }
}
