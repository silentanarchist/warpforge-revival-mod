using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2Cpp;

namespace WarpforgeRevival
{
    /// <summary>
    /// Currency definitions (gold, crystals, energy, ...) were assets in the lost cloud bundle,
    /// so CurrencyConfig has nothing to return and the menu's resource bar crashes.
    /// Until the rebuilt bundle provides them, create stand-in Currency objects on demand.
    /// (Patched methods verified to have unique native code.)
    /// </summary>
    internal static class Currencies
    {
        private static readonly Dictionary<GameCurrency, Currency> Fallbacks = new Dictionary<GameCurrency, Currency>();

        internal static Currency Get(GameCurrency type)
        {
            if (Fallbacks.TryGetValue(type, out var c) && c != null) return c;
            c = UnityEngine.ScriptableObject.CreateInstance(Il2CppType.Of<Currency>()).Cast<Currency>();
            c.name = "Revival_" + type;
            c.code = type.ToString();
            c.type = type;
            c.hideFlags = UnityEngine.HideFlags.DontUnloadUnusedAsset;
            Fallbacks[type] = c;
            RevivalMod.Log.Msg($"[currency] created stand-in currency '{type}'");
            return c;
        }

        [HarmonyPatch(typeof(CurrencyConfig), nameof(CurrencyConfig.GetCurrencyByType))]
        private static class ByType
        {
            private static void Postfix(GameCurrency currencyType, ref Currency __result)
            {
                if (__result == null) __result = Get(currencyType);
            }
        }

        // The game's own lookups go through this helper, which has GetCurrencyByType compiled into it.
        [HarmonyPatch(typeof(CurrencyExtensions), nameof(CurrencyExtensions.ToCurrency))]
        private static class ToCurrency
        {
            private static void Postfix(GameCurrency currencyType, ref ICurrency __result)
            {
                if (__result == null) __result = Get(currencyType).Cast<ICurrency>();
            }
        }

        // Card assets that fail to load arrive here as null; skip them quietly instead of
        // throwing once per card (the rebuilt card bundle will supply real ones).
        [HarmonyPatch(typeof(CollectionManager), nameof(CollectionManager.AddCard))]
        private static class AddCard
        {
            private static int skipped, duplicates;
            private static readonly System.Collections.Generic.HashSet<IntPtr> Seen = new System.Collections.Generic.HashSet<IntPtr>();
            private static IntPtr owner;
            private static bool Prefix(CollectionManager __instance, RawCardScript card)
            {
                if (__instance.Pointer != owner) { owner = __instance.Pointer; Seen.Clear(); }   // collection rebuilt
                if ((object)card == null || card.Pointer == IntPtr.Zero)
                {
                    if (skipped++ == 0) RevivalMod.Log.Warning("[cards] card assets missing from the content bundle; skipping them");
                    return false;
                }
                // Cards the revival bundle does not have yet are answered with one shared stand-in
                // object (so the game's "load every card" request succeeds); only register it once.
                if (!Seen.Add(card.Pointer))
                {
                    if (duplicates++ == 0) RevivalMod.Log.Msg("[cards] some cards are not rebuilt yet; using a stand-in for them");
                    return false;
                }
                AlternateArts.CardAdded(__instance);
                return true;
            }
        }

        // Stand-ins have no icon references; skip the Addressables lookup instead of erroring.
        [HarmonyPatch(typeof(Currency), nameof(Currency.GetIcon))]
        private static class Icon
        {
            private static bool Prefix(Currency __instance, ref UnityEngine.Sprite __result)
            {
                try
                {
                    var small = __instance.smallIcon;
                    if (small != null && !string.IsNullOrEmpty(small.AssetGUID)) return true;
                }
                catch { }
                __result = null;
                return false;
            }
        }
    }
}
