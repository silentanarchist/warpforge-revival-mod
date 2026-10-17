using System;
using HarmonyLib;
using Il2Cpp;

namespace WarpforgeRevival
{
    /// <summary>
    /// "While in play" abilities on enchantments (fix outline from the card-creator chat, 2026-10-16, project doc
    /// claude/fix-while-in-play-enchantments.md). The game re-applies "while in play" effects whenever the board
    /// changes (BattleManagerSupport.BroadcastWhileInPlay), but only on cards where CardScript.HasWhileInPlayAbility()
    /// is true, and that looks at the card's own abilities only. An enchantment's while-in-play ability is therefore
    /// never re-run once it is attached. Seen on the Space Wolves Offence card Everstorm (SW64): its hidden
    /// enchantment on the warlord gives every unit -1 Ranged, but only the two warlords (the only units when it was
    /// attached) ever got it. The game has the right check, EntityScript.HasEnchantmentWhileInPlay(), and never
    /// calls it.
    ///
    /// The postfix also says yes when an attached enchantment has a while-in-play ability, keeping the game's first
    /// rule: a jammed unit has no effects (DefinedTrait.jam), so it stays "no". Once the refresh reaches the card,
    /// RawCardScript.TriggerAbilities already runs the enchantments' abilities. Everstorm is the only official card
    /// affected; custom enchantments can now use "while in play" too. Both players need this (the server only hands
    /// out the latest mod).
    /// </summary>
    [HarmonyPatch(typeof(CardScript), nameof(CardScript.HasWhileInPlayAbility))]
    internal static class WhileInPlayEnchantments
    {
        private static int logged;

        private static void Postfix(CardScript __instance, ref bool __result)
        {
            if (__result) return;
            try
            {
                if ((object)__instance == null || __instance.m_CachedPtr == IntPtr.Zero) return;
                if (__instance.HasCurrentTrait(DefinedTrait.jam)) return;          // the game's rule: jammed units have no effects
                if (!__instance.HasEnchantmentWhileInPlay()) return;
                __result = true;
                if (logged < 5)
                {
                    logged++;
                    string name = "?";
                    try { name = __instance.rawCardData?.uniqueId ?? "?"; } catch { }
                    RevivalMod.Log.Msg($"[whileinplay] {name} (#{__instance.uniqueInGameID}) has an enchantment with a while-in-play ability: its effects are refreshed");
                }
            }
            catch (Exception e) { RevivalMod.Log.Warning("[whileinplay] " + e.Message); }
        }
    }
}
