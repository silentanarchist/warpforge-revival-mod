using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using UnityEngine;

namespace WarpforgeRevival
{
    /// <summary>
    /// Mulligan, then the Offence / Defence pick.
    ///
    /// After Continue the game leaves the mulligan cards (the ones kept and their replacements) where
    /// the mulligan showed them, and only moves them into the hand in the mulligan's last step
    /// (PlayerHand.CompleteMulliganPhase). In the shipped game that step came straight away. With the
    /// Offence card step switched back on (BattlefieldCards) the pick window opens in between, so the
    /// mulligan cards stayed on screen around and under it, and were hard to click past.
    ///
    /// Now, when the pick window opens during the mulligan, those cards are hidden. They are shown again
    /// as the game moves them into the hand (they appear there; the mulligan screen does not come back),
    /// or, as a safety net, when the mulligan is cancelled, when the mulligan is over by any other way,
    /// or after two minutes - so a hand can never stay invisible.
    /// </summary>
    internal static class MulliganPick
    {
        private static readonly List<GameObject> hidden = new List<GameObject>();
        private static float hiddenAt;
        private const float SafetySeconds = 120f;

        [HarmonyPatch(typeof(ChooseCardMenu), nameof(ChooseCardMenu.Setup))]
        private static class OnPickOpened
        {
            private static void Postfix()
            {
                try
                {
                    var bm = BattleManager.Instance;
                    if ((object)bm == null || !bm.IsDuringMulligan()) return;
                    int n = 0;
                    foreach (var hand in new[] { bm.playerHand, bm.enemyHand })
                    {
                        if ((object)hand == null) continue;
                        var cards = hand.currentMulligan;
                        if ((object)cards == null) continue;
                        for (int i = 0; i < cards.Count; i++)
                        {
                            var card = cards[i];
                            if ((object)card == null || card.m_CachedPtr == IntPtr.Zero) continue;
                            var go = card.gameObject;
                            if ((object)go == null || !go.activeSelf) continue;
                            go.SetActive(false);
                            hidden.Add(go);
                            n++;
                        }
                    }
                    if (n > 0)
                    {
                        hiddenAt = Time.realtimeSinceStartup;
                        RevivalMod.Log.Msg($"[mulligan] pick window open: {n} mulligan card(s) hidden until they go into the hand");
                    }
                }
                catch (Exception e) { RevivalMod.Log.Warning("[mulligan] could not hide the mulligan cards: " + e.Message); Restore("after an error"); }
            }
        }

        // The cards go into the hand here: show them first, so the game moves visible cards (and can run
        // what it needs on them).
        [HarmonyPatch(typeof(PlayerHand), nameof(PlayerHand.CompleteMulliganPhase))]
        private static class OnIntoHand { private static void Prefix() => Restore("as they go into the hand"); }

        [HarmonyPatch(typeof(BattleManager), nameof(BattleManager.CancelMulliganAfterBattleEnd))]
        private static class OnCancelled { private static void Prefix() => Restore("the mulligan was cancelled"); }

        /// <summary>Every frame (RevivalMod): the safety net.</summary>
        internal static void Tick()
        {
            if (hidden.Count == 0) return;
            try
            {
                var bm = BattleManager.Instance;
                if ((object)bm == null || bm.m_CachedPtr == IntPtr.Zero) { Restore("the battle ended"); return; }
                if (!bm.IsDuringMulligan()) { Restore("the mulligan is over"); return; }
                if (Time.realtimeSinceStartup - hiddenAt > SafetySeconds) Restore("after two minutes");
            }
            catch (Exception e) { RevivalMod.Log.Warning("[mulligan] " + e.Message); Restore("after an error"); }
        }

        private static void Restore(string why)
        {
            if (hidden.Count == 0) return;
            int n = 0;
            foreach (var go in hidden)
            {
                try
                {
                    if ((object)go == null || go.m_CachedPtr == IntPtr.Zero) continue;
                    if (!go.activeSelf) { go.SetActive(true); n++; }
                }
                catch { }
            }
            hidden.Clear();
            RevivalMod.Log.Msg($"[mulligan] {n} mulligan card(s) shown again ({why})");
        }
    }
}
