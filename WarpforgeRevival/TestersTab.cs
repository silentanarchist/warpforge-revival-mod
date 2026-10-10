using System;
using Il2Cpp;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace WarpforgeRevival
{
    /// <summary>
    /// A "Testers" button in the bottom bar of the main menu, right after Social, shown only to
    /// players whose linked site account has the in-game tester role (People tab on the site;
    /// admins count as testers). The button is a copy of the Social button with Social's own
    /// behaviour taken off it. It opens the Testers page (TestersPage).
    /// </summary>
    internal static class TestersTab
    {
        private const string Name = "RevivalTestersToggle";

        private static GameObject button;
        // The button's picture: the game's own expansion-pass medallion (a servo-tool), loaded from the
        // game's files by its asset id (40k_rewards_bt_missions_expansion pass, liveopsicons bundle).
        private const string IconAsset = "89b680029c9624e6e905db2fc0a47a9d";
        private static AsyncOperationHandle<Sprite> iconLoad;
        private static bool iconAsked, iconDone;
        private static float nextCheck;
        private static bool shownLogged, hierarchyLogged;

        private static bool Alive(GameObject g) => (object)g != null && g.Pointer != IntPtr.Zero && g.m_CachedPtr != IntPtr.Zero;

        /// <summary>Called every frame from the mod's update loop.</summary>
        internal static void Tick()
        {
            float now = Time.realtimeSinceStartup;
            if (now < nextCheck) return;
            nextCheck = now + 1f;
            try
            {
                bool want = AccountPage.IsTester;
                if (!want && TestersPage.IsOpen) TestersPage.Close();
                if (!want) TestersCollection.Close();
                if (Alive(button))
                {
                    if (button.activeSelf != want) button.SetActive(want);
                    if (!iconDone) Icon();
                    return;
                }
                if (!want) return;
                var nav = UnityEngine.Object.FindObjectOfType<NavigationPanelController>();
                if ((object)nav == null) return;
                var social = nav.GetToggle(NavigationPanelToggleType.Social);
                if ((object)social == null) return;
                button = Build(social.gameObject);
                iconDone = false;                        // a new copy of the button needs the picture again
            }
            catch (Exception e)
            {
                nextCheck = now + 30f;
                RevivalMod.Log.Warning("[testers] " + e.Message);
            }
        }

        private static GameObject Build(GameObject social)
        {
            var parent = social.transform.parent;
            // Copied under a switched-off holder, so the copy's own start-up code (which would hook
            // it up to open the Social window) never runs before that code is taken off.
            var holder = new GameObject("RevivalTestersHolder");
            holder.SetActive(false);
            var copy = UnityEngine.Object.Instantiate(social, holder.transform);
            copy.name = Name;
            if (!hierarchyLogged) { hierarchyLogged = true; LogHierarchy(copy.transform, 0); }

            foreach (var c in copy.GetComponentsInChildren<OpenWindowButton>(true)) UnityEngine.Object.DestroyImmediate(c);
            foreach (var c in copy.GetComponentsInChildren<NavigationPanelToggle>(true)) UnityEngine.Object.DestroyImmediate(c);
            foreach (var c in copy.GetComponentsInChildren<Il2CppI2.Loc.Localize>(true)) c.enabled = false;
            foreach (var t in copy.GetComponentsInChildren<Il2CppTMPro.TMP_Text>(true)) t.text = "Testers";

            var toggle = copy.GetComponent<UnityEngine.UI.Toggle>();
            if ((object)toggle != null)
            {
                toggle.group = null;                     // not one of the bar's own pages
                toggle.SetIsOnWithoutNotify(false);
                toggle.onValueChanged = new UnityEngine.UI.Toggle.ToggleEvent();
                toggle.onValueChanged.AddListener(DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction<bool>>(
                    new Action<bool>(on => { if (on) { toggle.SetIsOnWithoutNotify(false); Open(); } })));
            }

            copy.transform.SetParent(parent, false);
            copy.transform.SetSiblingIndex(social.transform.GetSiblingIndex() + 1);
            UnityEngine.Object.Destroy(holder);
            if (!shownLogged) { shownLogged = true; RevivalMod.Log.Msg("[testers] Testers button added to the menu bar (in-game tester)"); }
            return copy;
        }

        private static void Icon()
        {
            try
            {
                if (!iconAsked)
                {
                    iconAsked = true;
                    iconLoad = new AssetReference(IconAsset).LoadAssetAsync<Sprite>();
                    return;
                }
                if (!iconLoad.IsDone) return;
                iconDone = true;
                var sprite = iconLoad.Result;
                if ((object)sprite == null) { RevivalMod.Log.Warning("[testers] icon not found in the game's files; the Social picture stays"); return; }
                var img = button.transform.Find("Image");
                var image = (object)img == null ? null : img.GetComponent<UnityEngine.UI.Image>();
                if ((object)image == null) { RevivalMod.Log.Warning("[testers] no picture slot on the button"); return; }
                image.sprite = sprite;
                RevivalMod.Log.Msg("[testers] button picture set");
            }
            catch (Exception e) { iconDone = true; RevivalMod.Log.Warning("[testers] icon: " + e.Message); }
        }

        private static void Open()
        {
            RevivalMod.Log.Msg("[testers] Testers tab opened");
            Il2CppTMPro.TMP_FontAsset gameFont = null;
            try { var label = button.GetComponentInChildren<Il2CppTMPro.TMP_Text>(true); if ((object)label != null) gameFont = label.font; }
            catch { }
            if ((object)gameFont != null) TestersPage.font = gameFont;
            if (!TestersCollection.Open()) TestersPage.Open(gameFont);   // the Testers window; the plain page if it cannot be made
        }

        // What the copied button is made of, once, so the page behind it can be built to match.
        private static void LogHierarchy(Transform t, int depth)
        {
            if (depth > 4) return;
            var names = new System.Text.StringBuilder();
            foreach (var c in t.GetComponents<Component>())
                if ((object)c != null) names.Append(c.GetIl2CppType().Name).Append(' ');
            RevivalMod.Log.Msg($"[testers] {new string(' ', depth * 2)}{t.name}: {names}");
            for (int i = 0; i < t.childCount; i++) LogHierarchy(t.GetChild(i), depth + 1);
        }
    }
}
