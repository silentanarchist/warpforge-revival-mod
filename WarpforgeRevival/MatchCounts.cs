using System;
using Il2Cpp;
using UnityEngine;

namespace WarpforgeRevival
{
    /// <summary>
    /// Tab in a match shows the cards left in both decks and both hand sizes while it is held (0.14.49, owner's
    /// request). The game shows the same four counters while a card is pressed and held (CardScript.OnTouchDown ->
    /// BattleManager.ShowDeckSize, which turns on both DeckManagers' "Cards left" and both PlayerHands' "Cards in
    /// hand"; the release calls StopShowingDeckSize). Tab calls the same two functions, so nothing new is drawn.
    /// PC only: a phone has no Tab key. Tab was unused by the game (its keys: Escape, Space/Enter, X, chat keys).
    /// </summary>
    internal static class MatchCounts
    {
        private static bool held;

        internal static void Tick()
        {
#if !ANDROID_PORT
            try
            {
                bool down = EverguildInput.GetKey(KeyCode.Tab, false);   // the game's own key reader: not while typing in a text box
                if (down == held) return;
                if (!GameState.InBattle) { held = false; return; }
                var battle = BattleManager.Instance;
                if ((object)battle == null) { held = false; return; }
                held = down;
                if (down) battle.ShowDeckSize(true);
                else battle.StopShowingDeckSize();
            }
            catch (Exception e) { held = false; RevivalMod.Log.Warning("[match] counts on Tab: " + e.Message); }
#endif
        }
    }
}
