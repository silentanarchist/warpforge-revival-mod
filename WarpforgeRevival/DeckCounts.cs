using System;
using HarmonyLib;
using Il2Cpp;
using UnityEngine;
using Il2CppTMPro;

namespace WarpforgeRevival
{
    /// <summary>
    /// Deck editor > Deck info: the Troops and Stratagems lines show their counts again ("Troops: 7").
    /// The game still fills them in (DeckTypeCounterDrawer.Initialize, with each language's own word),
    /// but both labels also carry the translation component, which rewrites them to the bare word
    /// whenever the Deck info panel is shown - after the counts were written while it was hidden.
    /// Switching that component off on those two labels keeps the game's own text.
    /// </summary>
    internal static class DeckCounts
    {
        private static bool logged;

        [HarmonyPatch(typeof(DeckTypeCounterDrawer), nameof(DeckTypeCounterDrawer.Initialize))]
        private static class Counts
        {
            private static void Postfix(DeckTypeCounterDrawer __instance)
            {
                try
                {
                    foreach (var label in new[] { __instance.troopCount, __instance.stratagemsCount })
                    {
                        if ((object)label == null) continue;
                        var loc = label.GetComponent<Il2CppI2.Loc.Localize>();
                        if ((object)loc != null && loc.enabled) loc.enabled = false;
                    }
                    if (!logged)
                    {
                        logged = true;
                        RevivalMod.Log.Msg($"[deck] counts shown: \"{__instance.troopCount?.text}\", \"{__instance.stratagemsCount?.text}\"");
                    }
                }
                catch (Exception e) { RevivalMod.Log.Warning("[deck] counts: " + e.Message); }
            }
        }

        // Play screen deck details (and anywhere else the Energy cost chart is drawn): a line under the chart with
        // the deck's Troop and Stratagem counts (0.14.44; a tester's request). The counts the game shows in the deck
        // editor's Deck info use the same two terms. The line is a copy of a row's own count label, so it keeps the
        // chart's font and size, placed as the last child of the rows' container (a layout group puts it under the
        // chart on its own; otherwise it is set under the last row by hand).
        private const string LineName = "RevivalTypeCounts";

        [HarmonyPatch(typeof(DeckEnergyCostDrawer), nameof(DeckEnergyCostDrawer.Initialize), new[] { typeof(Il2CppSystem.Collections.Generic.List<RawCardScript>) })]
        private static class TypeLine
        {
            private static void Postfix(DeckEnergyCostDrawer __instance, Il2CppSystem.Collections.Generic.List<RawCardScript> cards)
            {
                try
                {
                    if ((object)__instance == null || cards == null) return;
                    int troops = 0, stratagems = 0;
                    for (int i = 0; i < cards.Count; i++)
                    {
                        var c = cards[i];
                        if ((object)c == null) continue;
                        if (c.cardType == CardTypeOptions.Minion) troops++;
                        else if (c.cardType == CardTypeOptions.Tactic) stratagems++;
                    }
                    var rows = __instance.rows;
                    if (rows == null || rows.Count == 0) return;
                    var last = rows[rows.Count - 1];
                    var model = (object)last == null ? null : last.cardsInDeck;
                    if ((object)model == null) return;
                    var holder = last.transform.parent;
                    if ((object)holder == null) return;

                    TMP_Text label = null;
                    var existing = holder.Find(LineName);
                    if ((object)existing != null) label = existing.GetComponent<TMP_Text>();
                    if ((object)label == null)
                    {
                        var go = UnityEngine.Object.Instantiate(model.gameObject, holder, false);
                        go.name = LineName;
                        var loc = go.GetComponent<Il2CppI2.Loc.Localize>();
                        if ((object)loc != null) UnityEngine.Object.Destroy(loc);
                        label = go.GetComponent<TMP_Text>();
                        if ((object)label == null) { UnityEngine.Object.Destroy(go); return; }
                        label.alignment = TextAlignmentOptions.Center;
                        label.enableAutoSizing = false;
                        var rt = go.GetComponent<RectTransform>();
                        var lastRt = last.GetComponent<RectTransform>();
                        if ((object)rt != null && (object)lastRt != null)
                        {
                            rt.anchorMin = lastRt.anchorMin; rt.anchorMax = lastRt.anchorMax; rt.pivot = lastRt.pivot;
                            rt.sizeDelta = new Vector2(lastRt.sizeDelta.x, lastRt.sizeDelta.y);
                            if ((object)holder.GetComponent<UnityEngine.UI.VerticalLayoutGroup>() == null)
                                rt.anchoredPosition = lastRt.anchoredPosition - new Vector2(0, lastRt.rect.height + 4f);
                        }
                        go.transform.SetAsLastSibling();
                    }
                    Ui.SetText(label, Loc.Game("MenuDeck/HUD/DeckDescription/Minions", "Troops") + ": " + troops + "    " +
                                      Loc.Game("MenuDeck/HUD/DeckDescription/Spells", "Stratagems") + ": " + stratagems);
                    label.gameObject.SetActive(true);
                }
                catch (Exception e) { RevivalMod.Log.Warning("[deck] type counts under the cost chart: " + e.Message); }
            }
        }
    }
}
