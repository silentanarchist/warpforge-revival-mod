using HarmonyLib;
using Il2Cpp;

namespace WarpforgeRevival
{
    /// <summary>
    /// The Classic and Skirmish pages on the Play screen are kept for their deck selector.
    /// Their season rewards bar has nothing to show on a revival server and fails while the page
    /// opens, so it is left out.
    /// </summary>
    internal static class ModePages
    {
        [HarmonyPatch(typeof(SkirmishEventWindow), nameof(SkirmishEventWindow.RefreshScore))]
        private static class NoSkirmishRewardsBar
        {
            private static bool Prefix() => false;
        }
    }
}
