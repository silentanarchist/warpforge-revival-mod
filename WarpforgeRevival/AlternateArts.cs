using System;
using HarmonyLib;
using Il2Cpp;
using Il2CppEverguild.Addressables;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace WarpforgeRevival
{
    /// <summary>
    /// Card styles (alternate art). The artwork, full-size warlord images and attack animations ship
    /// with the game; only the small definitions tying them to a card came from the dead servers.
    /// They are recreated here and handed to the game's own loader once the card library is ready.
    /// (Logan Grimnar is left out until the Space Wolves cards themselves are rebuilt.)
    /// With the game's original card file on the server none of this is needed: each card then lists
    /// its own styles, and the recreated copy is skipped.
    /// </summary>
    internal static class AlternateArts
    {
        // original card, card art, full warlord art, attack animation
        private static readonly string[][] Defs =
        {
            new[] { "UM76",  "696d6a27c10376b409baebbf1fe74955", "43a6e964c22f2654297895d457183593", "e61ea75c1dc92904e8696b9bd1f1bc09" }, // Lieutenant Titus
            new[] { "BL34",  "de82afc4058e04ed9adece95f6b097ea", "619d248b65ec641689938d26c2edec6c", "a9858e623a3664c47907758ca71bfd87" }, // Abaddon
            new[] { "SAU35", "1a132a0ce8d85264c8d0377bdb8b1021", "7f9c0d6efc3423b4d979fefce138527a", "58e7b66f4ac096c49922fe6b9b9f950e" }, // Imotekh
            new[] { "DA1",   "50de37c710fa6ee4cb84609e462180f6", "972680fa061ac6f4595e51f6b224b728", "9b837db22ae151043a9a8303c744cedf" }, // Azrael
            new[] { "GOF28", "a4c3162f8f32d45ba9ccd7362aa8564b", "8bf1dda9f253048f9a9fdb9a0a8879a0", "ca09df3c701c5fe438d58e42d18affd2" }, // Ghazghkull
            new[] { "AM2",   "7f53de0b9072d864d8f43757bdbfd48c", "4087175541336aa4da6d0de688709f6b", "5332c4cec974c324cbf48a753397c5d4" }, // Ursula Creed
        };

        internal static string IdFor(string cardId) => cardId + "#HB";

        private static IntPtr collection;      // the CollectionManager the cards were last added to
        private static IntPtr doneFor;
        private static float lastAdd;
        private static bool injecting;

        /// <summary>Called from the AddCard patch: notes when the card library is being (re)filled.</summary>
        internal static void CardAdded(CollectionManager manager)
        {
            if (injecting) return;
            collection = manager.Pointer;
            lastAdd = Time.realtimeSinceStartup;
        }

        /// <summary>Called every frame; adds the styles right after the card library finishes loading.</summary>
        public static void Tick()
        {
            if (collection == IntPtr.Zero || collection == doneFor) return;
            if (Time.realtimeSinceStartup - lastAdd < 0.05f) return;
            var pd = PlayerDataManager.singletonManager;
            if ((object)pd == null) return;
            var cards = pd.allCardCollection;
            if (cards == null || cards.Count < 500) return;
            doneFor = collection;
            Inject(cards);
        }

        private static void Inject(Il2CppSystem.Collections.Generic.List<RawCardScript> cards)
        {
            injecting = true;
            int made = 0, native = 0;
            try
            {
                var library = new AddressableAlternateArtCardLibrary();
                foreach (var d in Defs)
                {
                    try
                    {
                        RawCardScript original = null;
                        for (int i = 0; i < cards.Count; i++)
                        {
                            var c = cards[i];
                            if ((object)c != null && c.uniqueId == d[0]) { original = c; break; }
                        }
                        if ((object)original == null) { RevivalMod.Log.Warning($"[styles] card {d[0]} not found; style skipped"); continue; }

                        // A server with the game's original card file already has this style (the card
                        // itself lists it), so there is nothing to recreate.
                        var own = original.alternateArts;
                        if (own != null && own.Count > 0) { native++; continue; }

                        var art = ScriptableObject.CreateInstance<AlternateArtCard>();
                        art.name = original.name + " (Hammer and Bolter)";
                        art.hideFlags = HideFlags.DontUnloadUnusedAsset;
                        art.uniqueId = IdFor(d[0]);
                        art.style = AlternateArtSyle.HammerAndBolter;
                        art.originalCard = original;
                        art.cardRarity = (int)original.cardRarity;
                        art.cardArmy = original.cardArmy;
                        art.cardImage = new AssetReferenceTyped<Sprite>(d[1]);
                        art.fullWarlordImage = new AssetReferenceTyped<Sprite>(d[2]);
                        art.attackAnim = new AssetReferenceTyped<CardAnim>(d[3]);

                        AsyncOperationHandle handle = Addressables.ResourceManager.CreateCompletedOperation<AlternateArtCard>(art, "");
                        library.AssetCompletedLoading(handle, art.uniqueId);
                        made++;
                    }
                    catch (Exception e) { RevivalMod.Log.Warning($"[styles] could not create the style for {d[0]}: {e.Message}"); }
                }
            }
            catch (Exception e) { RevivalMod.Log.Warning("[styles] " + e); }
            finally { injecting = false; }
            RevivalMod.Log.Msg(native > 0
                ? $"[styles] {native} card styles come with the original card file; {made} recreated"
                : $"[styles] {made} of {Defs.Length} card styles created");
        }

        // The style's banner logo was a server download. Without it the banner is a white box, so
        // show the style's name as text instead (and no arrows: there is only one style).
        [HarmonyPatch(typeof(AlternateArtCardCollectionTab), nameof(AlternateArtCardCollectionTab.ConfigureStyle))]
        private static class Banner
        {
            private const string LabelName = "Revival Style Name";

            private static void Postfix(AlternateArtCardCollectionTab __instance)
            {
                try
                {
                    var img = __instance.styleImage;
                    if ((object)img == null || (object)img.sprite != null) return;
                    img.enabled = false;
                    if ((object)__instance.leftStyleButton != null) __instance.leftStyleButton.gameObject.SetActive(false);
                    if ((object)__instance.rightStyleButton != null) __instance.rightStyleButton.gameObject.SetActive(false);

                    var holder = img.transform;
                    if ((object)holder.Find(LabelName) != null) return;
                    var sample = __instance.GetComponentInParent<GameWindow>()?.GetComponentInChildren<Il2CppTMPro.TMP_Text>(true);
                    if ((object)sample == null) return;
                    var copy = UnityEngine.Object.Instantiate(sample.gameObject, holder);
                    copy.name = LabelName;
                    copy.SetActive(true);
                    var loc = copy.GetComponent<Il2CppI2.Loc.Localize>();
                    if ((object)loc != null) loc.enabled = false;
                    var rt = copy.GetComponent<RectTransform>();
                    if ((object)rt != null)
                    {
                        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
                        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
                        rt.localScale = Vector3.one;
                    }
                    var text = copy.GetComponent<Il2CppTMPro.TMP_Text>();
                    text.enableAutoSizing = true;
                    text.fontSizeMin = 18; text.fontSizeMax = 64;
                    text.alignment = Il2CppTMPro.TextAlignmentOptions.Center;
                    text.color = new Color(0.95f, 0.85f, 0.55f);
                    text.text = "HAMMER AND BOLTER";
                }
                catch (Exception e) { RevivalMod.Log.Warning("[styles] banner: " + e.Message); }
            }
        }
    }
}
