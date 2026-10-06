using System;
using HarmonyLib;
using Il2Cpp;

namespace WarpforgeRevival
{
    /// <summary>
    /// Offensive cards (given to the attacker by the battlefield) are marked "can't add to deck" in
    /// the card data, but the deck check only refuses defensive cards, relying on players not owning
    /// the offensive ones. The revival server grants every card, so enforce the mark here.
    /// </summary>
    internal static class DeckRules
    {
        [HarmonyPatch(typeof(CardDeck), nameof(CardDeck.CanAddCard))]
        private static class NoBattlefieldCards
        {
            private static bool noted;

            private static void Postfix(RawCardScript card, ref string error, ref bool __result)
            {
                try
                {
                    if (!__result || (object)card == null) return;
                    // Defensive cards are chosen in the deck editor (they fill the deck's defensive slot),
                    // so they must stay addable even though some carry the "can't add to deck" mark.
                    if (card.spellType == SpellType.DefensiveCard) return;
                    if (card.inventoryOptions != CardInventoryOptions.CantAddToDeck && card.spellType != SpellType.OffensiveCard) return;
                    __result = false;
                    error = "Offensive cards cannot be added to a deck.";
                    if (!noted) { noted = true; RevivalMod.Log.Msg($"[decks] refused '{card.cardName}' ({card.spellType}, {card.inventoryOptions}): not a deck card"); }
                }
                catch (Exception e) { RevivalMod.Log.Warning("[decks] " + e.Message); }
            }
        }

        // Offensive and defensive cards unlock with a faction's Forge level. The revival server has
        // no Forge, so treat every faction as fully levelled: all of them are available.
        [HarmonyPatch(typeof(EnviromentalEffectCardsSO), nameof(EnviromentalEffectCardsSO.GetEnvCardList))]
        private static class ForgeMaxed
        {
            private static bool noted;

            private static void Prefix(CardArmy useArmy, bool shouldUseOffensive, ref int currentForgeLevelObtained)
            {
                if (!noted)
                {
                    noted = true;
                    RevivalMod.Log.Msg($"[forge] battlefield cards requested for {useArmy} (offensive={shouldUseOffensive}) at forge level {currentForgeLevelObtained}; treating as max");
                }
                currentForgeLevelObtained = 100000;
            }

            private static void Postfix(CardArmy useArmy, bool shouldUseOffensive, Il2CppSystem.Collections.Generic.List<RawCardScript> __result)
            {
                try { RevivalMod.Log.Msg($"[forge] {(shouldUseOffensive ? "offensive" : "defensive")} cards available for {useArmy}: {(__result == null ? 0 : __result.Count)}"); }
                catch { }
            }
        }
    }
}
