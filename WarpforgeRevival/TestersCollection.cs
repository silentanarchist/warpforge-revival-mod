using System;
using HarmonyLib;
using Il2Cpp;
using Il2CppEverguild.LiveOps;
using UnityEngine;

namespace WarpforgeRevival
{
    /// <summary>
    /// Testers page > Collection and Decks: a second, separate copy of the game's Collection screen.
    /// The copy is made from the same window the Collection button in the menu bar opens, so it looks
    /// and works the same, but it is its own object: what happens in it does not touch the normal one.
    ///  - Collection: the copy opens on its card list. As a check that it really is separate, it leaves
    ///    out every Space Wolves card (the normal Collection still shows them).
    ///  - Decks: the copy opens straight on the deck list of one tester mode (for now Custom Test),
    ///    skipping the "which format" picker; Back closes it. Any mode can be passed in, so more tester
    ///    modes can get their own deck list later.
    /// The normal Collection > Decks keeps every format, Custom Test included.
    /// </summary>
    internal static class TestersCollection
    {
        internal enum Mode { Cards, Decks }

        private const string CopyName = "RevivalTestersCollection";
        private static CollectionScreen copy;
        private static Mode mode;
        private static string decksFor = "";
        private static int hidden;

        private static bool Alive(UnityEngine.Object o) => (object)o != null && o.Pointer != IntPtr.Zero && o.m_CachedPtr != IntPtr.Zero;

        /// <summary>True when this window is the testers' copy (not the normal Collection).</summary>
        internal static bool IsCopy(GameWindow w) => Alive(copy) && (object)w != null && w.Pointer == copy.Pointer;

        /// <summary>Open the copy on its card list (Space Wolves left out).</summary>
        internal static void OpenCards()
        {
            mode = Mode.Cards;
            decksFor = "";
            Open(null);
        }

        /// <summary>Open the copy on the deck list of one game mode (an event on this server).</summary>
        internal static void OpenDecks(LiveOpsEvent ev, string id)
        {
            mode = Mode.Decks;
            decksFor = id ?? "";
            CollectionScreen.OpenDeckCollectionContext context = null;
            try
            {
                var playEvent = (object)ev == null ? null : ev.TryCast<IPlayEvent>();
                if ((object)playEvent == null) { RevivalMod.Log.Warning($"[testers] {id} is not a mode with decks"); return; }
                context = new CollectionScreen.OpenDeckCollectionContext { eventReference = playEvent };
            }
            catch (Exception e) { RevivalMod.Log.Warning($"[testers] decks for {id}: {e.Message}"); return; }
            Open(context);
        }

        private static void Open(CollectionScreen.OpenDeckCollectionContext context)
        {
            try
            {
                var window = Copy();
                if ((object)window == null) return;
                var wm = WindowsManager.Instance;
                if ((object)wm == null) { RevivalMod.Log.Warning("[testers] window manager not ready"); return; }
                hidden = 0;
                if (window.IsOpen()) wm.CloseWindow(window);    // opened again: start over on the asked-for tab
                wm.OpenWindow(window, context, true, null);
                RevivalMod.Log.Msg(mode == Mode.Decks ? $"[testers] collection copy opened on the decks of {decksFor}"
                                                      : "[testers] collection copy opened on its cards");
            }
            catch (Exception e) { RevivalMod.Log.Warning("[testers] could not open the collection copy: " + e); }
        }

        /// <summary>The copy, made the first time it is needed from the window behind the menu bar's Collection button.</summary>
        private static CollectionScreen Copy()
        {
            if (Alive(copy)) return copy;
            copy = null;
            var nav = UnityEngine.Object.FindObjectOfType<NavigationPanelController>();
            var toggle = (object)nav == null ? null : nav.GetToggle(NavigationPanelToggleType.Collection);
            if ((object)toggle == null) { RevivalMod.Log.Warning("[testers] menu bar Collection button not found"); return null; }
            var opener = toggle.GetComponentInChildren<OpenWindowButton>(true);
            if ((object)opener == null) opener = toggle.GetComponentInParent<OpenWindowButton>();
            if ((object)opener == null) { RevivalMod.Log.Warning("[testers] Collection button has no window behind it"); return null; }

            GameObject source = null;
            string from;
            var prefabRef = opener.windowToOpenPrefab;
            if ((object)prefabRef != null && prefabRef.RuntimeKeyIsValid())
            {
                var loaded = prefabRef.LoadAssetAsync().WaitForCompletion();   // the game keeps it loaded once used
                if ((object)loaded != null) source = loaded.gameObject;
                from = "prefab " + prefabRef.AssetGUID;
            }
            else
            {
                if (Alive(opener.windowToOpenScene)) source = opener.windowToOpenScene.gameObject;
                from = "scene window";
            }
            if ((object)source == null || (object)source.GetComponent<CollectionScreen>() == null)
            {
                RevivalMod.Log.Warning($"[testers] Collection window not found ({from})");
                return null;
            }

            var placement = source.GetComponent<GameWindow>().WindowsPlacement;
            var anchor = WindowsManager.Instance.GetWindowAnchor(placement);
            var go = UnityEngine.Object.Instantiate(source, anchor, false);
            go.name = CopyName;
            copy = go.GetComponent<CollectionScreen>();
            copy.updateNavPanel = false;        // leave the menu bar alone: its Collection button opens the normal one
            RevivalMod.Log.Msg($"[testers] collection copy made from the {from}, under {(object)anchor?.name ?? "no anchor"}");
            return copy;
        }

        private static WindowTabBase FindTab(GameWindowWithTabs w, Il2CppSystem.Type type)
        {
            var tabs = w.tabs;
            if (tabs == null) return null;
            for (int i = 0; i < tabs.Count; i++)
            {
                var t = tabs[i];
                if ((object)t != null && type.IsAssignableFrom(t.GetIl2CppType())) return t;
            }
            return null;
        }

        // The copy starts on the tab its button asked for.
        [HarmonyPatch(typeof(GameWindowWithTabs), nameof(GameWindowWithTabs.GetStartingTab))]
        private static class StartingTab
        {
            private static void Postfix(GameWindowWithTabs __instance, ref WindowTabBase __result)
            {
                try
                {
                    if (!IsCopy(__instance)) return;
                    var want = mode == Mode.Decks ? Il2CppInterop.Runtime.Il2CppType.Of<SelectDecksTab>()
                                                  : Il2CppInterop.Runtime.Il2CppType.Of<CardCollectionTab>();
                    var tab = FindTab(__instance, want);
                    if ((object)tab != null) __result = tab;
                    else RevivalMod.Log.Warning($"[testers] collection copy has no {want.Name}");
                }
                catch (Exception e) { RevivalMod.Log.Warning("[testers] " + e.Message); }
            }
        }

        // The separation check: the copy's card list leaves out Space Wolves.
        [HarmonyPatch(typeof(CardCollectionTab), "GetCollection")]
        private static class NoSpaceWolves
        {
            private static void Postfix(CardCollectionTab __instance, ref Il2CppSystem.Collections.Generic.List<RawCardScript> __result)
            {
                try
                {
                    if (__result == null || !IsCopy(__instance.Window)) return;
                    var kept = new Il2CppSystem.Collections.Generic.List<RawCardScript>();
                    int left = 0;
                    for (int i = 0; i < __result.Count; i++)
                    {
                        var c = __result[i];
                        if ((object)c != null && c.cardArmy == CardArmy.SpaceWolves) { left++; continue; }
                        kept.Add(c);
                    }
                    __result = kept;
                    if (left != hidden) { hidden = left; RevivalMod.Log.Msg($"[testers] collection copy: {kept.Count} cards shown, {left} Space Wolves left out"); }
                }
                catch (Exception e) { RevivalMod.Log.Warning("[testers] " + e.Message); }
            }
        }

        // Opened on one mode's decks, Back from the deck list closes the copy instead of showing the format picker.
        [HarmonyPatch(typeof(SelectDecksTab), nameof(SelectDecksTab.BackButtonClicked))]
        private static class BackCloses
        {
            private static bool Prefix(SelectDecksTab __instance)
            {
                try
                {
                    if (mode != Mode.Decks || !IsCopy(__instance.Window)) return true;
                    WindowsManager.Instance.CloseWindow(copy);
                    return false;
                }
                catch (Exception e) { RevivalMod.Log.Warning("[testers] " + e.Message); return true; }
            }
        }
    }
}
