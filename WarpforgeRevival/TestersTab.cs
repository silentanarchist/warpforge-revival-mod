using System;
using Il2Cpp;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace WarpforgeRevival
{
    /// <summary>
    /// A "Testers" button in the bottom bar of the main menu, right after Social, shown only to
    /// players whose linked site account has the in-game tester role (People tab on the site;
    /// admins count as testers). The button is a copy of the Social button with Social's own
    /// behaviour taken off it. What it opens is still to be built: for now it says so.
    /// </summary>
    internal static class TestersTab
    {
        private const string Name = "RevivalTestersToggle";

        private static GameObject button;
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
                if (Alive(button))
                {
                    if (button.activeSelf != want) button.SetActive(want);
                    return;
                }
                if (!want) return;
                var nav = UnityEngine.Object.FindObjectOfType<NavigationPanelController>();
                if ((object)nav == null) return;
                var social = nav.GetToggle(NavigationPanelToggleType.Social);
                if ((object)social == null) return;
                button = Build(social.gameObject);
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

        private static void Open()
        {
            RevivalMod.Log.Msg("[testers] Testers tab opened");
            var notice = UIMessageController.Instance;
            if ((object)notice != null) notice.ShowMessage("Testers: nothing here yet.", false);
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
