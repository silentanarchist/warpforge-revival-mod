using System;
using System.Collections.Generic;
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

        // ---- cards hidden from the opponent (owner, 2026-10-23)
        // With frames by rarity, a card the opponent may not see showed its rarity anyway: a Genestealer Cults troop
        // played in Ambush (face hidden until it springs) and a Dark Angels secret were drawn in their real frame
        // and foil. While an opponent's card is hidden it gets the plain first frame and foil; when it is revealed it
        // gets its own back. The player's own hidden cards keep their frame (they know what they played).
        private static readonly HashSet<IntPtr> hidden = new HashSet<IntPtr>();

        private static void Show(CardScript card, CardTier tier)
        {
            card.CardTier = tier;
            var ui = card.CardUI;
            if ((object)ui == null) return;
            ui.CardTier = tier;
            var raw = card.rawCardData;
            var frame = ui.cardTierUI;
            if ((object)frame != null && (object)raw != null) frame.SetFrame(raw, tier);
            try { ui.RefreshCardVisualEffects(tier); } catch { }
        }

        private static CardTier Real(CardScript card)
        {
            var raw = card.rawCardData;
            return (object)raw == null ? CardTier.Tier1 : CardServices.GetCardTier(raw);
        }

        [HarmonyPatch(typeof(CardScript), nameof(CardScript.SetAmbush))]
        private static class Ambush
        {
            private static bool noted;

            private static void Postfix(CardScript __instance, bool activateAmbush)
            {
                try
                {
                    if ((object)__instance == null || __instance.isPlayer) return;
                    if (activateAmbush)
                    {
                        if (hidden.Count > 500) hidden.Clear();
                        hidden.Add(__instance.Pointer);
                        Show(__instance, CardTier.Tier1);
                        if (!noted) { noted = true; RevivalMod.Log.Msg("[frames] an opponent's card in Ambush shows the plain frame until it is revealed"); }
                    }
                    else if (hidden.Remove(__instance.Pointer)) Show(__instance, Real(__instance));
                }
                catch (Exception e) { RevivalMod.Log.Warning("[frames] ambush: " + e.Message); }
            }
        }

        [HarmonyPatch(typeof(CardScript), nameof(CardScript.TriggerAmbush))]
        private static class AmbushSprung
        {
            private static void Prefix(CardScript __instance)
            {
                try { if ((object)__instance != null && hidden.Remove(__instance.Pointer)) Show(__instance, Real(__instance)); }
                catch (Exception e) { RevivalMod.Log.Warning("[frames] ambush sprung: " + e.Message); }
            }
        }

        // a hidden card's frame is redrawn by the game (a refresh while it is still hidden): keep it plain
        [HarmonyPatch(typeof(CardTierUIController), nameof(CardTierUIController.SetFrame))]
        private static class HiddenFrame
        {
            private static void Prefix(CardTierUIController __instance, ref CardTier tier)
            {
                try
                {
                    if (hidden.Count == 0 && !secretSetup) return;
                    if (secretSetup) { tier = CardTier.Tier1; return; }
                    var card = __instance.GetComponentInParent<CardScript>();
                    if ((object)card != null && hidden.Contains(card.Pointer)) tier = CardTier.Tier1;
                }
                catch { }
            }
        }

        // an opponent's secret (Dark Angels) is set up face hidden: its frame and foil are drawn plain
        private static bool secretSetup;

        [HarmonyPatch(typeof(BattleCardUI), nameof(BattleCardUI.SetEnemySecret))]
        private static class Secret
        {
            private static void Prefix(CardScript cardScript)
            {
                secretSetup = true;
                try { if ((object)cardScript != null) cardScript.CardTier = CardTier.Tier1; } catch { }
            }
            private static void Finalizer() { secretSetup = false; }
        }

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
