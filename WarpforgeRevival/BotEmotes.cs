using System;
using HarmonyLib;
using Il2Cpp;
using Il2CppEverguild.LiveOps.Config;

namespace WarpforgeRevival
{
    /// <summary>
    /// The AI opponent's emotes.
    ///
    /// The game decides whether the AI emotes after one of its actions, or answers the player's emote,
    /// by rolling against the chances in the server's AIBotsConfig. But when the server's
    /// FeatureConfig turns the newer matchmaking off - which the revival does, its matchmaking is
    /// the older one - the game skips that roll and the AI emotes after every single action. So the
    /// roll is made here first: the game only gets to emote when it passes.
    /// </summary>
    internal static class BotEmotes
    {
        private static bool noted;

        [HarmonyPatch(typeof(VoiceLinesController), nameof(VoiceLinesController.AIResponse))]
        private static class Roll
        {
            private static bool Prefix(bool isResponse)
            {
                try
                {
                    var config = ConfigManager.GetConfig<AIBotsConfig>();
                    if ((object)config == null) return true;
                    return isResponse ? config.AIShouldReplyToEmote() : config.AIShouldEmoteAfterAction();
                }
                catch (Exception e)
                {
                    if (!noted) { noted = true; RevivalMod.Log.Warning("[bot] emote check: " + e.Message); }
                    return true;
                }
            }
        }
    }
}
