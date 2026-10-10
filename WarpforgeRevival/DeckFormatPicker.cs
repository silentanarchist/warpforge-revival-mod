using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using Il2CppEverguild.LiveOps;
using UnityEngine;
using UnityEngine.UI;

namespace WarpforgeRevival
{
    /// <summary>
    /// Collection > Decks first asks which format to build for, one tall tile per format side by
    /// side. Custom Test (the long-game mode) is left out there: testers build its decks in the
    /// Testers window (TestersCollection). Classic comes first, then Skirmish, both full size.
    /// </summary>
    internal static class DeckFormatPicker
    {
        private static int notes;

        private static string IdOf(SelectDeckGameModeMenuContainer tile, out IPlayEvent e)
        {
            e = null;
            try
            {
                var content = tile.Content;
                e = (object)content == null ? null : content.TryCast<IPlayEvent>();
                var d = (object)e == null ? null : e.GetBaseData();
                return (object)d == null ? "" : d.eventId ?? "";
            }
            catch { return ""; }
        }

        private static string Describe(Transform t)
        {
            var parts = new List<string>();
            foreach (var c in t.GetComponents<Component>())
                if ((object)c != null) parts.Add(c.GetIl2CppType().Name);
            var rt = t.TryCast<RectTransform>();
            string size = (object)rt == null ? "" : $" {rt.rect.width:0}x{rt.rect.height:0}";
            return $"'{t.name}'{size} [{string.Join(",", parts)}]";
        }

        [HarmonyPatch(typeof(SelectGameModeDeckTab), nameof(SelectGameModeDeckTab.Initialize))]
        private static class Built
        {
            private static void Postfix(SelectGameModeDeckTab __instance)
            {
                try { Arrange(__instance); }
                catch (Exception ex) { if (notes < 4) { notes++; RevivalMod.Log.Warning("[decks] format picker: " + ex); } }
            }
        }

        private static void Arrange(SelectGameModeDeckTab tab)
        {
            var tiles = tab.currentSelectDeckContainers;
            var anchor = tab.contentAnchor;
            if (tiles == null || (object)anchor == null) return;

            SelectDeckGameModeMenuContainer classic = null, skirmish = null, longGame = null;
            var seen = new List<string>();
            for (int i = 0; i < tiles.Count; i++)
            {
                var tile = tiles[i];
                if ((object)tile == null) continue;
                string id = IdOf(tile, out var e);
                seen.Add(id);
                if (LongGame.IsLongEvent(e)) longGame = tile;
                else if (id.IndexOf("Skirmish", StringComparison.OrdinalIgnoreCase) >= 0) skirmish = tile;
                else if (id.IndexOf("Classic", StringComparison.OrdinalIgnoreCase) >= 0) classic = tile;
            }

            bool report = notes < 2;
            if (report)
            {
                notes++;
                RevivalMod.Log.Msg($"[decks] format picker: formats {string.Join(", ", seen)}; holder {Describe(anchor)}");
                for (int i = 0; i < tiles.Count; i++)
                    if ((object)tiles[i] != null) RevivalMod.Log.Msg($"[decks]   tile {Describe(tiles[i].transform)}");
            }
            // Custom Test (the long-game mode) is for testers, who build its decks in the Testers window;
            // the normal picker leaves it out, so Classic and Skirmish are two full-size tiles side by side.
            if (TestersCollection.IsUnder(tab.transform)) return;
            if ((object)longGame != null) longGame.gameObject.SetActive(false);
            try
            {
                if ((object)classic != null) classic.transform.SetSiblingIndex(0);
                if ((object)skirmish != null) skirmish.transform.SetSiblingIndex(1);
            }
            catch { }
            LayoutRebuilder.MarkLayoutForRebuild(anchor.TryCast<RectTransform>());
            if (report) RevivalMod.Log.Msg($"[decks] format picker: Classic and Skirmish full size{((object)longGame != null ? "; Custom Test left out (Testers window has it)" : "")}");
        }
    }
}
