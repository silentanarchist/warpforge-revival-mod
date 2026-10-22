using System;
using HarmonyLib;
using Il2Cpp;

namespace WarpforgeRevival
{
    /// <summary>
    /// Card frames by rarity (0.14.45, owner's decision 2026-10-21). The game draws each card in one of four frames
    /// (CardTier Tier1..Tier4, per army and card kind; CardFramesSO.GetClanFrame) and the matching foil look
    /// (CardMaterialHelper), chosen by the card's Forge level: CardServices.GetCardTier looks the card up in the
    /// player's tier collection, which on the revival server never grows (no Forge), so every card showed the plain
    /// first frame. Now the tier is the card's rarity: Common 1, Rare 2, Epic 3, Legendary 4, Special 4 (the top).
    /// Every place that shows a card asks this one function (hand, board, collection, deck lists, card popups,
    /// the opponent's cards), so the frame follows the card everywhere.
    /// Undo (if the Forge ever comes back): remove this patch.
    /// </summary>
    internal static class CardFrames
    {
        internal static CardTier ByRarity(CardRarity rarity) => rarity switch
        {
            CardRarity.Rare => CardTier.Tier2,
            CardRarity.Epic => CardTier.Tier3,
            CardRarity.Legendary => CardTier.Tier4,
            CardRarity.Special => CardTier.Tier4,
            _ => CardTier.Tier1,
        };

        [HarmonyPatch(typeof(CardServices), nameof(CardServices.GetCardTier))]
        private static class TierIsRarity
        {
            private static bool noted;

            private static void Postfix(RawCardScript card, ref CardTier __result)
            {
                try
                {
                    if ((object)card == null) return;
                    var original = card.GetOriginalCard();
                    var rarity = ((object)original != null ? original : card).cardRarity;
                    __result = ByRarity((CardRarity)(int)rarity);
                    if (!noted) { noted = true; RevivalMod.Log.Msg("[frames] card frames follow rarity (Common 1, Rare 2, Epic 3, Legendary and Special 4)"); }
                }
                catch (Exception e) { RevivalMod.Log.Warning("[frames] " + e.Message); }
            }
        }
    }
}
