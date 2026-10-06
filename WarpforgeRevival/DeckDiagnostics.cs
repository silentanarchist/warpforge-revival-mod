using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Il2Cpp;

namespace WarpforgeRevival
{
    /// <summary>Temporary diagnostics for the deck editor (which cards it is given and why).</summary>
    internal static class DeckDiagnostics
    {
        private static string Describe(RawCardScript c)
        {
            if ((object)c == null || c.Pointer == IntPtr.Zero) return "null";
            return $"alive={(c.m_CachedPtr != IntPtr.Zero)} {c.uniqueId} '{c.cardName}' type={c.cardType} spell={(int)c.spellType} kw={(int)c.keyword} army={c.cardArmy} " +
                   $"cost={c.manaCost} show={c.showInCollectionOptions} inv={(int)c.inventoryOptions} rarity={c.cardRarity}";
        }

        private static void Histogram(string label, Il2CppSystem.Collections.Generic.List<RawCardScript> list)
        {
            if (list == null) { RevivalMod.Log.Msg($"[deckdiag] {label}: null"); return; }
            var byType = new Dictionary<string, int>();
            var bySpell = new Dictionary<int, int>();
            var byArmy = new Dictionary<string, int>();
            int n = list.Count;
            for (int i = 0; i < n; i++)
            {
                var c = list[i];
                if ((object)c == null) continue;
                string t = c.cardType.ToString(); byType[t] = byType.GetValueOrDefault(t) + 1;
                int s = (int)c.spellType; bySpell[s] = bySpell.GetValueOrDefault(s) + 1;
                string a = c.cardArmy.ToString(); byArmy[a] = byArmy.GetValueOrDefault(a) + 1;
            }
            RevivalMod.Log.Msg($"[deckdiag] {label}: {n} cards; types {string.Join(",", byType.Select(k => k.Key + "=" + k.Value))}; " +
                               $"spellTypes {string.Join(",", bySpell.OrderBy(k => k.Key).Select(k => k.Key + "=" + k.Value))}; " +
                               $"armies {string.Join(",", byArmy.Select(k => k.Key + "=" + k.Value))}");
            for (int i = 0; i < Math.Min(n, 4); i++) RevivalMod.Log.Msg($"[deckdiag]   {Describe(list[i])}");
        }

        [HarmonyPatch(typeof(DeckEditingWindow), nameof(DeckEditingWindow.TryOpen))]
        private static class Open
        {
            private static void Postfix(DeckEditingWindow __instance)
            {
                try
                {
                    var d = __instance.EditingDeck;
                    if (d == null) RevivalMod.Log.Msg("[deckdiag] editor opened with no deck");
                    else
                        RevivalMod.Log.Msg($"[deckdiag] editor opened: deck '{d.deckName}' id={d.deckId} hero={Describe(d.deckHero)} " +
                                           $"army={d.deckArmy} mode={d.GameMode} customMode={d.HasCustomGameMode} max={d.MaxDeckSize} " +
                                           $"cards={d.cardLibrary?.Count} defensive={Describe(d.defensiveCard)}");
                    RevivalMod.Log.Msg($"[deckdiag] showOnlyOwned={__instance.showOnlyOwnedCards}");
                    Histogram("editor card list", __instance.cardCollection);
                }
                catch (Exception e) { RevivalMod.Log.Warning("[deckdiag] " + e); }
            }
        }

        [HarmonyPatch(typeof(DeckEditingWindow), nameof(DeckEditingWindow.GetCardCollection))]
        private static class Collection
        {
            private static void Postfix(Il2CppSystem.Collections.Generic.List<RawCardScript> __result)
            {
                try { Histogram("GetCardCollection result", __result); }
                catch (Exception e) { RevivalMod.Log.Warning("[deckdiag] " + e); }
            }
        }

        private static int heroLogs, visualLogs;

        [HarmonyPatch(typeof(CardDeck), nameof(CardDeck.CanAddHero))]
        private static class AddHero
        {
            private static void Postfix(RawCardScript card, ref string error, bool __result)
            {
                if (heroLogs++ > 60) return;
                try
                {
                    int byId = -1; string id = "?";
                    try
                    {
                        id = card.GetID();
                        byId = PlayerDataManager.singletonManager.inventoryManager.GetOwnedCount(id);
                    }
                    catch (Exception e) { id += " (" + e.Message + ")"; }
                    RevivalMod.Log.Msg($"[deckdiag] CanAddHero {Describe(card)} -> {__result} error='{error}' GetID={id} ownedById={byId} itemClass={card.ItemClass}");
                }
                catch (Exception e) { RevivalMod.Log.Warning("[deckdiag] " + e.Message); }
            }
        }

        [HarmonyPatch(typeof(CollectionCard), nameof(CollectionCard.UpdateCardVisuals))]
        private static class Visuals
        {
            private static void Prefix(bool isValid, bool isOwned, string counterText)
            {
                if (visualLogs++ > 40) return;
                RevivalMod.Log.Msg($"[deckdiag] card cell: valid={isValid} owned={isOwned} counter='{counterText}'");
            }
        }

        private static int addLogs;

        [HarmonyPatch(typeof(CardDeck), nameof(CardDeck.CanAddCard))]
        private static class AddCard
        {
            private static void Postfix(RawCardScript card, ref string error, bool __result)
            {
                try
                {
                    if ((object)card == null || card.cardType != CardTypeOptions.Hero || addLogs++ > 30) return;
                    int byId = -1; string id = "?";
                    try { id = card.GetID(); byId = PlayerDataManager.singletonManager.inventoryManager.GetOwnedCount(id); }
                    catch (Exception e) { id += " (" + e.Message + ")"; }
                    RevivalMod.Log.Msg($"[deckdiag] CanAddCard(hero) {Describe(card)} -> {__result} error='{error}' GetID={id} ownedById={byId} itemClass={card.ItemClass}");
                }
                catch (Exception e) { RevivalMod.Log.Warning("[deckdiag] " + e.Message); }
            }
        }

        private static int presetLogs;

        [HarmonyPatch(typeof(DeckSelectionTabController), nameof(DeckSelectionTabController.ShowPrebuiltDecks))]
        private static class PresetDecks
        {
            private static void Postfix(DeckSelectionTabController __instance, bool state)
            {
                if (presetLogs++ > 6) return;
                try
                {
                    var bots = __instance.botDecks;
                    RevivalMod.Log.Msg($"[deckdiag] preset tab (state={state}): botDecks={(bots == null ? "null" : bots.Count.ToString())}");
                    var coll = PlayerDataManager.singletonManager.PrebuiltDeckCollection;
                    if (coll == null) { RevivalMod.Log.Msg("[deckdiag] prebuilt deck collection is null"); return; }
                    RevivalMod.Log.Msg($"[deckdiag] prebuilt decks: all={coll.decks?.Count} bot={coll.botDecks?.Count} practice={coll.practiceDecks?.Count}");
                    var pr = coll.practiceDecks;
                    for (int i = 0; pr != null && i < Math.Min(pr.Count, 6); i++)
                    {
                        var d = pr[i];
                        string hidden;
                        try { hidden = d.HasHiddenCards().ToString(); } catch (Exception e) { hidden = "threw " + e.Message.Split('\n')[0]; }
                        string hero;
                        try { hero = Describe(d.DeckHero); } catch (Exception e) { hero = "threw " + e.Message.Split('\n')[0]; }
                        RevivalMod.Log.Msg($"[deckdiag]   practice deck {d.deckId} '{d.deckName}' alive={(d.m_CachedPtr != IntPtr.Zero)} mode={d.gameMode} hidden={hidden} heroRef={d.deckHero?.targetId} hero={hero}");
                    }
                    for (int i = 0; bots != null && i < Math.Min(bots.Count, 4); i++)
                    {
                        var b = bots[i];
                        RevivalMod.Log.Msg($"[deckdiag]   bot deck '{b.deckName}' mode={b.GameMode} army={b.deckArmy} event={(b.CustomGameModeEvent == null ? "none" : "set")}");
                    }
                    var sel = __instance.deckSelectionPopup?.context;
                    RevivalMod.Log.Msg($"[deckdiag] popup context: {(sel == null ? "null" : "set")}");
                }
                catch (Exception e) { RevivalMod.Log.Warning("[deckdiag] preset tab: " + e); }
            }
        }
    }
}
