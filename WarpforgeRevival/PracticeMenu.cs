using System;
using HarmonyLib;
using Il2Cpp;

namespace WarpforgeRevival
{
    /// <summary>Fixes for the Play-page practice menu (pick a ready-made deck and fight the AI).</summary>
    internal static class PracticeMenu
    {
        // A player with no deck for the selected mode gets an empty placeholder deck (no warlord),
        // which the menu then dereferences. Treat that as "no own deck".
        [HarmonyPatch(typeof(PracticeModePopup), nameof(PracticeModePopup.SetArmyButtons))]
        private static class EmptyOwnDeck
        {
            private static void Prefix(PracticeModePopup __instance)
            {
                try
                {
                    var w = __instance.playerDeckInfoWrapper;
                    if ((object)w == null) return;
                    var d = w.cardDeck;
                    if ((object)d != null && (object)d.deckHero == null)
                    {
                        w.cardDeck = null;
                        RevivalMod.Log.Msg("[practice] no own deck for this mode; showing ready-made decks only");
                    }
                }
                catch (Exception e) { RevivalMod.Log.Warning("[practice] " + e.Message); }
            }
        }

        // Ready-made deck names are localization keys that the recovered text data does not contain.
        [HarmonyPatch(typeof(DemoDeckInfoSO), nameof(DemoDeckInfoSO.GetName))]
        private static class DeckName
        {
            private static void Postfix(DemoDeckInfoSO __instance, PlayModes gameMode, ref string __result)
            {
                try
                {
                    if (!string.IsNullOrEmpty(__result)) return;
                    var deck = __instance.GetDeck(gameMode);
                    if ((object)deck != null && !string.IsNullOrEmpty(deck.deckName)) __result = deck.deckName;
                }
                catch (Exception e) { RevivalMod.Log.Warning("[practice] " + e.Message); }
            }
        }

        // The first button is the player's own deck; its "Your deck" caption is a missing translation too.
        [HarmonyPatch(typeof(DeckSelectorMenuItemDemo), nameof(DeckSelectorMenuItemDemo.InitializeWithPlayerDeck))]
        private static class OwnDeckCaption
        {
            private static void Postfix(DeckSelectorMenuItemDemo __instance, CardDeck playerDeck)
            {
                try
                {
                    var label = __instance.deckName;
                    if ((object)label == null || !string.IsNullOrEmpty(label.text)) return;
                    string name = (object)playerDeck != null ? playerDeck.deckName : null;
                    label.text = string.IsNullOrEmpty(name) ? "Your deck" : name;
                }
                catch (Exception e) { RevivalMod.Log.Warning("[practice] " + e.Message); }
            }
        }
    }
}
