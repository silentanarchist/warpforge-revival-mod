using System;
using HarmonyLib;
using Il2Cpp;
using Il2CppEverguild.Addressables;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace WarpforgeRevival
{
    /// <summary>
    /// The Testers window's own deck editor (0.14.64). The Testers window is a copy of the Collection window
    /// (TestersCollection); until now its decks still opened the game's one deck editor, shared with the normal
    /// Collection. Owner 2026-10-22: the tester deck flow must be separate down to the editor, so custom cards
    /// never reach the normal deck builder.
    ///
    /// How the game opens the editor (PC code, DeckInfoPopup.EditDeck and SelectDecksTab.CreateDeck): both end in
    /// WindowsManager.OpenWindow&lt;DeckEditingWindow&gt;(deck), which looks the editor's window file up in the window
    /// list and calls WindowsManager.OpenWindow(ComponentReference, data, closeAll, options). While one of those two
    /// runs for the Testers window, that call is answered with a copy of the editor made from the same window file
    /// with our own loading handle (as the Testers window is made), opened with the same data. Everything the
    /// editor then does is the game's own code on the copy: closing it shows the window underneath (the Testers
    /// window) and returns its deck list to the deck's mode.
    /// Custom cards are offered only in this copy (CustomCards.EditorList). The copy is removed with the Testers window.
    /// </summary>
    internal static class TestersEditor
    {
        private const string CopyName = "RevivalTestersEditor";
        private static DeckEditingWindow copy;
        private static GameObject sourcePrefab;
        private static string sourceGuid;
        private static int wanted;          // > 0 while a Testers-window action is opening the deck editor

        internal static bool IsCopy(GameWindow w) => Ui.Alive(copy) && (object)w != null && w.Pointer == copy.Pointer;

        /// <summary>Remove the copy (with the Testers window, once that is closed and removed).</summary>
        internal static void Drop()
        {
            try
            {
                if (!Ui.Alive(copy)) { copy = null; return; }
                if (copy.IsOpen()) return;
                UnityEngine.Object.Destroy(copy.gameObject);
                copy = null;
                RevivalMod.Log.Msg("[testers] Testers deck editor removed");
            }
            catch (Exception e) { RevivalMod.Log.Warning("[testers] editor: " + e.Message); }
        }

        private static DeckEditingWindow Copy(ComponentReference<GameWindow> reference)
        {
            string guid = (object)reference == null ? null : reference.AssetGUID;
            if (string.IsNullOrEmpty(guid)) { RevivalMod.Log.Warning("[testers] the deck editor is not a window file; the game's editor opens"); return null; }
            if (Ui.Alive(copy) && guid == sourceGuid) return copy;
            if (!Ui.Alive(sourcePrefab) || guid != sourceGuid)
            {
                // our own handle on the file: the game's reference (and the editor it keeps loaded) is left alone
                var loaded = new AssetReference(guid).LoadAssetAsync<GameObject>().WaitForCompletion();
                if (!Ui.Alive(loaded) || (object)loaded.GetComponent<DeckEditingWindow>() == null)
                {
                    RevivalMod.Log.Warning("[testers] deck editor window file not loaded; the game's editor opens");
                    return null;
                }
                sourcePrefab = loaded;
                sourceGuid = guid;
            }
            if (Ui.Alive(copy)) UnityEngine.Object.Destroy(copy.gameObject);
            var placement = sourcePrefab.GetComponent<GameWindow>().WindowsPlacement;
            var anchor = WindowsManager.Instance.GetWindowAnchor(placement);
            var go = UnityEngine.Object.Instantiate(sourcePrefab, anchor, false);
            go.name = CopyName;
            copy = go.GetComponent<DeckEditingWindow>();
            RevivalMod.Log.Msg($"[testers] Testers deck editor made from the editor's window file {guid}");
            return copy;
        }

        // the two ways the Testers window opens the editor: a deck's Edit button (its info popup) and Create Deck
        [HarmonyPatch(typeof(DeckInfoPopup), nameof(DeckInfoPopup.EditDeck))]
        private static class EditFromInfo
        {
            private static void Prefix() { if (TestersCollection.InUse()) wanted++; }
            private static void Finalizer() { wanted = 0; }
        }

        [HarmonyPatch(typeof(SelectDecksTab), nameof(SelectDecksTab.CreateDeck))]
        private static class CreateFromList
        {
            private static void Prefix(SelectDecksTab __instance) { try { if (TestersCollection.IsCopy(__instance.Window)) wanted++; } catch { } }
            private static void Finalizer() { wanted = 0; }
        }

        [HarmonyPatch(typeof(WindowsManager), nameof(WindowsManager.OpenWindow),
            new[] { typeof(ComponentReference<GameWindow>), typeof(Il2CppSystem.Object), typeof(bool), typeof(GameWindowOptions) })]
        private static class OpenCopy
        {
            private static bool Prefix(WindowsManager __instance, ComponentReference<GameWindow> gameWindowToOpen, Il2CppSystem.Object data,
                                       bool closeAll, GameWindowOptions options, ref GameWindow __result)
            {
                if (wanted <= 0) return true;
                wanted = 0;
                try
                {
                    var window = Copy(gameWindowToOpen);
                    if ((object)window == null) return true;
                    __instance.OpenWindow(window, data, closeAll, options);
                    __result = window;
                    RevivalMod.Log.Msg("[testers] deck opened in the Testers deck editor");
                    return false;
                }
                catch (Exception e) { RevivalMod.Log.Warning("[testers] Testers deck editor: " + e); return true; }
            }
        }
    }
}
