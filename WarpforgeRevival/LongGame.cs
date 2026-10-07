using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Il2Cpp;
using Il2CppEverguild.LiveOps;

namespace WarpforgeRevival
{
    /// <summary>
    /// A third constructed format ("Long Game") next to Classic and Skirmish, with its own decks.
    /// The game files a deck under a play mode and only knows Classic and Skirmish decks, so Long Game
    /// decks are stored under a play mode the game never uses (Battle4Warpforge). That keeps them a
    /// separate set for good: nothing has to be converted later. Where the game would otherwise not
    /// show them, they are presented as Classic decks:
    ///  - on the Long Game page (and the deck editor opened from it) Long Game decks are listed and
    ///    Classic decks are not, and a deck created there becomes a Long Game deck;
    ///  - on any other mode page Long Game decks are not listed;
    ///  - in the Collection they appear next to the Classic decks so they can be edited or deleted.
    /// Long Game decks always follow the Long Game event's rules (deck size, copies per rarity), and
    /// Long Game matches use the warlord health multiplier. Settings come from the server's
    /// revival-settings.json ("longGame").
    /// </summary>
    internal static class LongGame
    {
        private enum Page { None, Long, Other }

        private static Page page = Page.None;
        private static bool editingLong;                 // deck editor was opened from the Long Game page
        private static IPlayEvent longEvent;
        private const PlayModes LongMode = PlayModes.Battle4Warpforge;
        private static bool inside;                      // guards against re-entry from our own calls

        public static void Start(string dataDir) { }

        private static string EventId => ServerSettings.LongGameEvent;

        private static IPlayEvent Event()
        {
            if (longEvent != null) return longEvent;
            string id = EventId;
            if (string.IsNullOrEmpty(id)) return null;
            try
            {
                var found = LiveOpsManager.GetEvent(id);
                if ((object)found != null) longEvent = found.TryCast<IPlayEvent>();
            }
            catch { }
            return longEvent;
        }

        private static bool IsLongEvent(IPlayEvent e)
        {
            try
            {
                if ((object)e == null || string.IsNullOrEmpty(EventId)) return false;
                var data = e.GetBaseData();
                return (object)data != null && data.eventId == EventId;
            }
            catch { return false; }
        }

        // A deck's play mode is an optional value stored inside the deck (a flag at 0x70, the mode at
        // 0x74). The generated wrapper for that field reads it wrongly, so it is read and written directly.
        private static bool TryStoredMode(CardDeck deck, out PlayModes mode)
        {
            mode = (PlayModes)System.Runtime.InteropServices.Marshal.ReadInt32(deck.Pointer + 0x74);
            return System.Runtime.InteropServices.Marshal.ReadByte(deck.Pointer + 0x70) != 0;
        }

        private static void StoreMode(CardDeck deck, PlayModes mode)
        {
            System.Runtime.InteropServices.Marshal.WriteInt32(deck.Pointer + 0x74, (int)mode);
            System.Runtime.InteropServices.Marshal.WriteByte(deck.Pointer + 0x70, 1);
        }

        /// <summary>Whether this is a Long Game deck (it is stored under the Long Game play mode).</summary>
        internal static bool Applies(CardDeck deck)
        {
            if ((object)deck == null || deck.Pointer == IntPtr.Zero || string.IsNullOrEmpty(EventId)) return false;
            if (TryStoredMode(deck, out var stored)) return stored == LongMode || (editingLong && stored == PlayModes.Classic);
            return editingLong;          // the editor's working copy of a deck opened from a Long Game list
        }

        private static bool StoredAsLong(CardDeck deck)
        {
            return (object)deck != null && deck.Pointer != IntPtr.Zero && TryStoredMode(deck, out var stored) && stored == LongMode;
        }

        /// <summary>How a deck's play mode is presented to the rest of the game (see the class summary).</summary>
        [HarmonyPatch(typeof(CardDeck), nameof(CardDeck.GameMode), MethodType.Getter)]
        private static class ShownMode
        {
            private static void Postfix(CardDeck __instance, ref PlayModes __result)
            {
                try
                {
                    if (saving > 0 || string.IsNullOrEmpty(EventId)) return;      // what gets saved is the real mode
                    bool onLongPage = page == Page.Long || editingLong;
                    if (__result == LongMode) { if (onLongPage) __result = PlayModes.Classic; }
                    else if (__result == PlayModes.Classic && onLongPage) __result = LongMode;
                }
                catch { }
            }
        }

        /// <summary>A deck opened in the editor from the Long Game page is, or becomes, a Long Game deck.</summary>
        [HarmonyPatch(typeof(DeckEditingPanel), nameof(DeckEditingPanel.Initialize))]
        private static class EditorOpened
        {
            private static void Prefix(CardDeck deck)
            {
                try
                {
                    if (editingLong) MakeLong(deck);
                }
                catch (Exception e) { RevivalMod.Log.Warning("[long] could not mark the deck as Long Game: " + e.Message); }
            }
        }

        // ------------------------------------------------------------------ which page is open

        private static void SetPage(IPlayEvent e, string seenFrom)
        {
            bool isLong = IsLongEvent(e);
            if (isLong) longEvent = e;
            string id = "?";
            try { var d = (object)e == null ? null : e.GetBaseData(); if ((object)d != null) id = d.eventId; } catch { }
            page = isLong ? Page.Long : Page.Other;
            editingLong = false;
            RevivalMod.Log.Msg($"[long] {seenFrom}: page for event '{id}' -> {(isLong ? "Long Game decks only" : "Long Game decks hidden")}");
        }

        // The deck picker on a mode page rebuilds its list here (when the page opens and whenever the
        // faction changes); the page's event tells us which mode's decks to present.
        [HarmonyPatch(typeof(RankedDeckSelector), nameof(RankedDeckSelector.ChangeArmy))]
        private static class PageOpened
        {
            private static void Prefix(RankedDeckSelector __instance)
            {
                try { SetPage(__instance.currentEvent, "deck picker"); }
                catch (Exception ex) { RevivalMod.Log.Warning("[long] deck picker: " + ex); }
            }
        }

        // A Play-screen tile opens "the ranked window", and the game then looks up the one Classic-type
        // event it expects to exist, so the Long Game tile would open the Classic page. Remember which
        // tile was clicked and hand the window the Long Game event instead.
        private static float longTileClickedAt = -100f;

        [HarmonyPatch(typeof(LiveopMenuContainer), nameof(LiveopMenuContainer.OnClick))]
        private static class TileClicked
        {
            private static void Prefix(LiveopMenuContainer __instance)
            {
                try
                {
                    var content = __instance.Content;
                    var e = (object)content == null ? null : content.TryCast<IPlayEvent>();
                    bool isLong = IsLongEvent(e);
                    if (isLong) longEvent = e;
                    longTileClickedAt = isLong ? UnityEngine.Time.realtimeSinceStartup : -100f;
                    string id = "?";
                    try { var d = (object)e == null ? null : e.GetBaseData(); if ((object)d != null) id = d.eventId; } catch { }
                    RevivalMod.Log.Msg($"[long] tile clicked: '{id}'");
                }
                catch (Exception ex) { RevivalMod.Log.Warning("[long] tile: " + ex); }
            }
        }

        [HarmonyPatch(typeof(RankedEventWindowV2), nameof(RankedEventWindowV2.TryOpen))]
        private static class WindowV2
        {
            private static void Prefix(ref Il2CppSystem.Object data)
            {
                try
                {
                    bool fromLongTile = UnityEngine.Time.realtimeSinceStartup - longTileClickedAt < 3f;
                    longTileClickedAt = -100f;
                    if (fromLongTile && (object)longEvent != null)
                    {
                        data = new Il2CppSystem.Object(longEvent.Pointer);
                        RevivalMod.Log.Msg("[long] opening the mode window for Long Game");
                    }
                    var e = (object)data == null ? null : data.TryCast<IPlayEvent>();
                    if ((object)e != null) SetPage(e, "mode window");
                }
                catch (Exception ex) { RevivalMod.Log.Warning("[long] mode window: " + ex); }
            }
        }

        // The deck list screen: the player first picks a mode, then sees that mode's decks.
        [HarmonyPatch(typeof(SelectDecksTab), nameof(SelectDecksTab.ToggleSelectDeck))]
        private static class DeckListMode
        {
            private static void Prefix(IOwnDeckPlayEvent deckPlayEvent)
            {
                try { filterLines = 12; SetPage((object)deckPlayEvent == null ? null : deckPlayEvent.TryCast<IPlayEvent>(), "deck list"); }
                catch (Exception ex) { RevivalMod.Log.Warning("[long] deck list: " + ex); }
            }
        }

        [HarmonyPatch(typeof(SelectDecksTab), nameof(SelectDecksTab.ToggleSelectGameModeDeck))]
        private static class DeckListModeChooser
        {
            private static void Prefix() { page = Page.None; editingLong = false; }
        }

        private static void MakeLong(CardDeck deck)
        {
            if ((object)deck == null || deck.Pointer == IntPtr.Zero || StoredAsLong(deck)) return;
            if (TryStoredMode(deck, out var stored) && stored != PlayModes.Classic) return;   // never a Skirmish deck
            StoreMode(deck, LongMode);
            RevivalMod.Log.Msg($"[long] '{deck.deckName}' is now a Long Game deck");
        }

        // Whatever opens the deck editor while a Long Game list is showing is editing a Long Game deck.
        [HarmonyPatch(typeof(DeckEditingWindow), nameof(DeckEditingWindow.TryOpen))]
        private static class EditorOpening
        {
            private static void Prefix(Il2CppSystem.Object data)
            {
                editorOpen = true; browseActive = false;
                try
                {
                    if (page != Page.Long) return;
                    editingLong = true;
                    var deck = (object)data == null ? null : data.TryCast<CardDeck>();
                    RevivalMod.Log.Msg($"[long] deck editor opening from a Long Game list (given: {((object)data == null ? "nothing" : data.GetIl2CppType().Name)})");
                    MakeLong(deck);
                }
                catch (Exception ex) { RevivalMod.Log.Warning("[long] editor: " + ex); }
            }
        }

        // The editor works on its own copy of the deck; make sure that copy, and what gets saved, is a
        // Long Game deck too.
        [HarmonyPatch(typeof(DeckEditingPanel), nameof(DeckEditingPanel.Initialize))]
        private static class EditorCopy
        {
            private static void Postfix(DeckEditingPanel __instance)
            {
                try { if (editingLong) MakeLong(__instance.editingDeck); }
                catch (Exception ex) { RevivalMod.Log.Warning("[long] editor copy: " + ex.Message); }
            }
        }

        [HarmonyPatch(typeof(DeckEditingWindow), nameof(DeckEditingWindow.TrySaveDeck))]
        private static class Saving
        {
            private static void Prefix(DeckEditingWindow __instance)
            {
                try
                {
                    if (!editingLong) return;
                    var panel = __instance.editingPanel;
                    if ((object)panel != null) MakeLong(panel.editingDeck);
                }
                catch (Exception ex) { RevivalMod.Log.Warning("[long] saving: " + ex.Message); }
            }
        }

        // While a deck is being written out (to the server or to text), nothing is disguised: the saved
        // deck must carry its real play mode or it would come back as a Classic deck.
        private static int saving;

        [HarmonyPatch(typeof(PlayerDataManager), nameof(PlayerDataManager.UploadUnsyncedDeck))]
        private static class Uploading
        {
            private static void Prefix(CardDeck deckData)
            {
                try { if (editingLong || page == Page.Long) MakeLong(deckData); } catch { }
                saving++;
            }
            private static void Finalizer() { if (saving > 0) saving--; }
        }

        [HarmonyPatch(typeof(PlayerDataManager), nameof(PlayerDataManager.ExtractDeckInfo))]
        private static class Extracting
        {
            private static void Prefix() { saving++; }
            private static void Finalizer() { if (saving > 0) saving--; }
        }

        [HarmonyPatch(typeof(CardDeck), nameof(CardDeck.Serialize))]
        private static class Serializing
        {
            private static void Prefix() { saving++; }
            private static void Finalizer() { if (saving > 0) saving--; }
        }

        // A copy of a Long Game deck is a Long Game deck.
        [HarmonyPatch(typeof(CardDeck), nameof(CardDeck.CopyDeck))]
        private static class Copying
        {
            private static void Postfix(CardDeck __instance, CardDeck deckToCopy)
            {
                try { if ((object)deckToCopy != null && StoredAsLong(deckToCopy)) MakeLong(__instance); } catch { }
            }
        }

        // The game finds a deck's rules by asking for "the active event of the deck's play mode".
        // Long Game's own play mode has no event as far as the game knows, so answer for it; and while a
        // Long Game page or editor is open, the mode the game thinks it is in is really Long Game.
        [HarmonyPatch(typeof(GameModes), nameof(GameModes.GetActiveEvent), new[] { typeof(PlayModes) })]
        private static class ActiveEvent
        {
            private static void Postfix(PlayModes playMode, ref IPlayEvent __result)
            {
                try
                {
                    if (string.IsNullOrEmpty(EventId)) return;
                    if (playMode == LongMode || (playMode == PlayModes.Classic && (page == Page.Long || editingLong)))
                    {
                        var e = Event();
                        if ((object)e != null) __result = e;
                    }
                }
                catch { }
            }
        }

        // The deck list decides what to show by comparing play modes, and Long Game and Classic share
        // one, so say explicitly which decks belong in which list.
        private static int filterLines;

        [HarmonyPatch(typeof(DeckCollectionTab), nameof(DeckCollectionTab._GetCollection_b__7_0))]
        private static class DeckListFilter
        {
            private static void Postfix(DeckCollectionTab __instance, CardDeck deck, ref bool __result)
            {
                try
                {
                    if (string.IsNullOrEmpty(EventId) || (object)deck == null) return;
                    var current = __instance.CurrentEvent;
                    bool longList = IsLongEvent((object)current == null ? null : current.TryCast<IPlayEvent>());
                    bool longDeck = StoredAsLong(deck);
                    bool game = __result;
                    if (longDeck) __result = longList;
                    else if (longList) __result = false;
                    if (filterLines > 0)
                    {
                        filterLines--;
                        bool has = TryStoredMode(deck, out var stored);
                        RevivalMod.Log.Msg($"[long] list check '{deck.deckName}': stored mode {(has ? stored.ToString() : "none")}, {deck.CardCount(false)} cards, list is {(longList ? "Long Game" : "other")}, game said {game} -> {__result}");
                    }
                }
                catch (Exception ex) { RevivalMod.Log.Warning("[long] deck list filter: " + ex.Message); }
            }
        }

        [HarmonyPatch(typeof(DeckSelectionPopup), nameof(DeckSelectionPopup.TryOpen))]
        private static class Popup
        {
            private static void Prefix(Il2CppSystem.Object data)
            {
                try
                {
                    var ctx = (object)data == null ? null : data.TryCast<DeckSelectionContext>();
                    RevivalMod.Log.Msg($"[long] deck list popup opening: page={page}, asks for mode {((object)ctx == null ? "?" : ctx.GameMode.ToString())}, own list={((object)ctx != null && ctx.DeckList != null ? ctx.DeckList.Count.ToString() : "none")}");
                }
                catch (Exception ex) { RevivalMod.Log.Warning("[long] popup: " + ex); }
            }
        }

        [HarmonyPatch(typeof(RankedDeckSelector), nameof(RankedDeckSelector.OnDisable))]
        private static class PageClosed
        {
            private static void Postfix() { page = Page.None; }
        }

        [HarmonyPatch(typeof(RankedDeckSelector), nameof(RankedDeckSelector.OnCreateDeckButtonClick))]
        private static class CreateFromPage
        {
            private static void Prefix() { if (page == Page.Long) editingLong = true; }
        }

        [HarmonyPatch(typeof(RankedDeckSelector), nameof(RankedDeckSelector.OnViewDeckButtonClick))]
        private static class ViewFromPage
        {
            private static void Prefix() { if (page == Page.Long) editingLong = true; }
        }

        [HarmonyPatch(typeof(DeckEditingWindow), nameof(DeckEditingWindow.Close))]
        private static class EditorClosed
        {
            private static void Postfix() { editingLong = false; editorOpen = false; }
        }

        // ------------------------------------------------------------------ deck rules

        [HarmonyPatch(typeof(CardDeck), nameof(CardDeck.CustomGameModeEvent), MethodType.Getter)]
        private static class RulesForDeck
        {
            private static void Postfix(CardDeck __instance, ref IPlayEvent __result)
            {
                if (inside) return;
                inside = true;
                try
                {
                    if (!Applies(__instance)) return;
                    var e = Event();
                    if ((object)e != null) __result = e;
                }
                catch (Exception ex) { RevivalMod.Log.Warning("[long] deck rules: " + ex.Message); }
                finally { inside = false; }
            }
        }

        private static bool copiesNoted;

        [HarmonyPatch(typeof(CardDeck), nameof(CardDeck.CanAddCard))]
        private static class CopiesByRarity
        {
            private static void Postfix(CardDeck __instance, RawCardScript card, ref string error, ref bool __result)
            {
                try
                {
                    if (!__result || (object)card == null || card.cardType != CardTypeOptions.Minion && card.cardType != CardTypeOptions.Tactic) return;
                    if (!Applies(__instance)) return;
                    int limit = ServerSettings.LongGameCopies((int)card.cardRarity);
                    if (limit <= 0) return;
                    int have = __instance.CardCopiesInDeck(card);
                    if (have < limit) return;
                    __result = false;
                    error = $"Long Game allows {limit} of a {card.cardRarity} card.";
                    if (!copiesNoted) { copiesNoted = true; RevivalMod.Log.Msg($"[long] copy limit applied: '{card.cardName}' ({card.cardRarity}) is at {have} of {limit}"); }
                }
                catch (Exception e) { RevivalMod.Log.Warning("[long] copies: " + e.Message); }
            }
        }

        /// <summary>
        /// The deck editor writes "in deck / allowed" under each card using the game's two-tier limit
        /// (Legendary, everything else). Show the Long Game limit for the card's rarity instead.
        /// </summary>
        [HarmonyPatch(typeof(CollectionCard), nameof(CollectionCard.UpdateCardVisuals))]
        private static class CopiesShown
        {
            private static void Prefix(CollectionCard __instance, ref string counterText)
            {
                try
                {
                    if (!editingLong || string.IsNullOrEmpty(counterText)) return;
                    int slash = counterText.IndexOf('/');
                    if (slash <= 0 || !int.TryParse(counterText.Substring(slash + 1), out int shown)) return;
                    var card = __instance.Item;
                    if ((object)card == null) return;
                    int limit = ServerSettings.LongGameCopies((int)card.cardRarity);
                    if (limit > 0 && limit < shown) counterText = counterText.Substring(0, slash + 1) + limit;
                }
                catch { }
            }
        }

        // ------------------------------------------------------------------ starting hand

        // The starting hand sizes live in the battle's own settings, which are shared by every match,
        // so set them for a Long Game match and put the game's values back for any other.
        private static int gameHand = -1, gameSecondExtra = -1;

        private static bool InLongMatch()
        {
            try
            {
                var modes = LiveOpsManager.GetHandler<GameModes>();
                return (object)modes != null && IsLongEvent(modes.CurrentPlayingEvent);
            }
            catch { return false; }
        }

        private static void ApplyHand(PlayerHand hand) => ApplyHand(hand.manager);

        private static void ApplyHand(BattleManager battle)
        {
            if ((object)battle == null) return;
            var vars = battle.scenarioVariables;
            if ((object)vars == null) return;
            if (gameHand < 0) { gameHand = vars.startingHand; gameSecondExtra = vars.secondExtraCards; }
            bool isLong = InLongMatch();
            int first = isLong && ServerSettings.LongGameHand > 0 ? ServerSettings.LongGameHand : gameHand;
            int extra = isLong && ServerSettings.LongGameSecondExtra >= 0 ? ServerSettings.LongGameSecondExtra : gameSecondExtra;
            if (vars.startingHand != first || vars.secondExtraCards != extra)
            {
                vars.startingHand = first;
                vars.secondExtraCards = extra;
                RevivalMod.Log.Msg($"[long] starting hand: {first} cards, +{extra} for the second player ({(isLong ? "Long Game" : "game default")})");
            }
        }

        [HarmonyPatch(typeof(PlayerHand), nameof(PlayerHand.GetBaseCardsInHandCount))]
        private static class HandSize
        {
            private static void Prefix(PlayerHand __instance)
            {
                try { ApplyHand(__instance); } catch (Exception e) { RevivalMod.Log.Warning("[long] starting hand: " + e.Message); }
            }
        }

        [HarmonyPatch(typeof(PlayerHand), nameof(PlayerHand.GetSecondExtraCardsCount))]
        private static class HandExtra
        {
            private static void Prefix(PlayerHand __instance)
            {
                try { ApplyHand(__instance); } catch (Exception e) { RevivalMod.Log.Warning("[long] starting hand: " + e.Message); }
            }
        }

        // The two getters above are folded into the dealing code by the compiler, so the values
        // must be in place before the opening hands are dealt.
        [HarmonyPatch(typeof(PlayerHand), nameof(PlayerHand.AddCardsToMulligan))]
        private static class HandDealt
        {
            private static void Prefix(PlayerHand __instance)
            {
                try { ApplyHand(__instance); } catch (Exception e) { RevivalMod.Log.Warning("[long] starting hand: " + e.Message); }
            }
        }

        /// <summary>Redraws the number on the warlord (it was drawn before the health was raised).</summary>
        private static void ShowHealth(CardScript hero)
        {
            try { if ((object)hero != null) hero.UpdateHealthText(false); }
            catch (Exception e) { RevivalMod.Log.Warning("[long] warlord health display: " + e.Message); }
        }

        [HarmonyPatch(typeof(BattleManager), nameof(BattleManager.SetupMulliganPhase))]
        private static class HandPhase
        {
            private static void Prefix(BattleManager __instance)
            {
                try { if (InLongMatch()) { ShowHealth(__instance.GetHero(true)); ShowHealth(__instance.GetHero(false)); } } catch { }
                try { ApplyHand(__instance); } catch (Exception e) { RevivalMod.Log.Warning("[long] starting hand: " + e.Message); }
            }
        }

        // ------------------------------------------------------------------ warlord health in the deck builder
        // The deck builder shows a warlord with the health it has under the rules of the mode being
        // built for (the game does that itself for Classic and Skirmish). Long Game's health is a
        // multiplier applied by this mod, so the builder is told the Long Game figure here.
        private static int builderNotes, browseNotes;
        private static bool editorOpen;                  // a deck is being built or edited (any mode)

        private static bool browseActive;                // the plain collection (no deck open) was the last card grid drawn

        // (Hooking the function every card display goes through, BasicCardUI.SetRawCardData, broke
        // card drawing in 0.11.2, so each screen is adjusted at its own, narrower, entry point.)

        /// <summary>The health a warlord card should show right now, or -1 to leave what the game wrote.</summary>
        private static int ShownHealth(RawCardScript item, int change, bool grid)
        {
            if ((object)item == null || item.cardType != CardTypeOptions.Hero) return -1;
            if (page == Page.Long || editingLong)
            {
                double factor = ServerSettings.LongGameHealth;
                if (factor <= 0 || Math.Abs(factor - 1.0) < 0.001) return -1;
                int before = item.maxHealth + change;
                int after = (int)Math.Round(before * factor, MidpointRounding.AwayFromZero);
                if (builderNotes < 3) { builderNotes++; RevivalMod.Log.Msg($"[long] deck builder shows Long Game warlord health ('{item.cardName}' {before} -> {after}, {(grid ? "card grid" : "enlarged card")})"); }
                return after;
            }
            // Browsing the collection (no deck being built): show warlords as Classic has them.
            int classic = ServerSettings.PracticeClassicLife;
            if (editorOpen || change != 0 || classic == 0) return -1;
            if (grid) browseActive = true;
            else if (!browseActive) return -1;
            if (browseNotes < 3) { browseNotes++; RevivalMod.Log.Msg($"[cards] collection shows Classic warlord health ('{item.cardName}' {item.maxHealth} -> {item.maxHealth + classic}, {(grid ? "card grid" : "enlarged card")})"); }
            return item.maxHealth + classic;
        }

        private static void ApplyGrid(CollectionCard card)
        {
            try
            {
                if ((object)card == null) return;
                var ui = card.cardUI;
                if ((object)ui == null) return;
                var data = card.currentGameplayVariablesData;
                int health = ShownHealth(card.Item, (object)data != null ? data.warlordLifeChange : 0, true);
                if (health >= 0) ui.SetAltHealth(health);
            }
            catch (Exception e) { if (builderNotes < 6) { builderNotes++; RevivalMod.Log.Warning("[long] deck builder health: " + e.Message); } }
        }

        [HarmonyPatch(typeof(CollectionCard), nameof(CollectionCard.Config))]
        private static class BuilderHealth
        {
            private static void Postfix(CollectionCard __instance) { ApplyGrid(__instance); }
        }

        // The grid writes each card's numbers again after setting it up, and again whenever the deck changes.
        [HarmonyPatch(typeof(CardCollectionDisplay), nameof(CardCollectionDisplay.SetCell))]
        private static class GridCell
        {
            private static void Postfix(Il2CppPolyAndCode.UI.ICell cell)
            {
                try { ApplyGrid((object)cell == null ? null : cell.TryCast<CollectionCard>()); }
                catch (Exception e) { if (builderNotes < 6) { builderNotes++; RevivalMod.Log.Warning("[long] deck builder health (cell): " + e.Message); } }
            }
        }

        [HarmonyPatch(typeof(CardCollectionDisplay), nameof(CardCollectionDisplay.UpdateCardVisuals))]
        private static class GridRefresh
        {
            private static void Postfix(CollectionCard card) { ApplyGrid(card); }
        }

        // The deck editor has its own grid, which does the same rewriting.
        [HarmonyPatch(typeof(DeckEditorCollectionDisplay), nameof(DeckEditorCollectionDisplay.SetCell))]
        private static class EditorCell
        {
            private static void Postfix(Il2CppPolyAndCode.UI.ICell cell)
            {
                try { ApplyGrid((object)cell == null ? null : cell.TryCast<CollectionCard>()); }
                catch (Exception e) { if (builderNotes < 6) { builderNotes++; RevivalMod.Log.Warning("[long] deck builder health (editor cell): " + e.Message); } }
            }
        }

        [HarmonyPatch(typeof(DeckEditorCollectionDisplay), nameof(DeckEditorCollectionDisplay.DrawCell))]
        private static class EditorRedraw
        {
            private static void Postfix(CollectionCard card) { ApplyGrid(card); }
        }

        // The enlarged card shown when one is clicked.
        [HarmonyPatch(typeof(CardDisplayWindow), nameof(CardDisplayWindow.InitializeCardForDisplay))]
        private static class EnlargedCard
        {
            private static void Postfix(CardDisplayWindow __instance, RawCardScript cardData, int index, int warlordHealthModifier)
            {
                try
                {
                    int health = ShownHealth(cardData, warlordHealthModifier, false);
                    if (health < 0) return;
                    var uis = __instance.cardUIs;
                    if (uis == null || index < 0 || index >= uis.Length) return;
                    var ui = uis[index];
                    if ((object)ui != null) ui.SetAltHealth(health);
                }
                catch (Exception e) { if (builderNotes < 6) { builderNotes++; RevivalMod.Log.Warning("[long] enlarged card health: " + e.Message); } }
            }
        }

        // ------------------------------------------------------------------ warlord health

        [HarmonyPatch(typeof(BattleManager), nameof(BattleManager.SetupFullHero))]
        private static class WarlordHealth
        {
            private static void Postfix(BattleManager __instance, bool isPlayer)
            {
                browseActive = false;
                try { ApplyHand(__instance); } catch (Exception e) { RevivalMod.Log.Warning("[long] starting hand: " + e.Message); }
                try
                {
                    double factor = ServerSettings.LongGameHealth;
                    if (factor <= 0 || Math.Abs(factor - 1.0) < 0.001) return;
                    if (!InLongMatch()) return;
                    var hero = __instance.GetHero(isPlayer);
                    if ((object)hero == null) return;
                    int before = hero.currentMaxHealth;
                    int after = (int)Math.Round(before * factor, MidpointRounding.AwayFromZero);
                    hero.currentMaxHealth = after;
                    hero.currentHealth = after;
                    ShowHealth(hero);
                    RevivalMod.Log.Msg($"[long] {(isPlayer ? "your" : "enemy")} warlord health {before} -> {after}");
                }
                catch (Exception e) { RevivalMod.Log.Warning("[long] warlord health: " + e.Message); }
            }
        }
    }
}
