using System;
using HarmonyLib;
using Il2Cpp;
using UnityEngine;

namespace WarpforgeRevival
{
    /// <summary>
    /// Player profile window on a revival server: the parts that depend on things the server does
    /// not run (warlord mastery, forge, campaigns, alliances, avatar borders from events) are hidden,
    /// and title plates get their text even though the wording template is missing.
    /// </summary>
    internal static class ProfilePage
    {
        private static void Hide(Component c)
        {
            if ((object)c != null && c.Pointer != IntPtr.Zero) c.gameObject.SetActive(false);
        }

        // Warlord mastery / forge / campaign boxes: nothing behind them, and they crash without events.
        [HarmonyPatch(typeof(ProfileEventSection), nameof(ProfileEventSection.Initialize))]
        private static class NoEventBoxes
        {
            private static bool Prefix(ProfileEventSection __instance)
            {
                try { __instance.gameObject.SetActive(false); }
                catch (Exception e) { RevivalMod.Log.Warning("[profile] " + e.Message); }
                return false;
            }
        }

        [HarmonyPatch(typeof(ProfileTab), nameof(ProfileTab.Initialize))]
        private static class ProfileButtons
        {
            private static void Postfix(ProfileTab __instance)
            {
                try
                {
                    Hide(__instance.inviteToAllianceButton);
                    ProfileStats.Show(__instance, ShowFriendCode(__instance));
                }
                catch (Exception e) { RevivalMod.Log.Warning("[profile] " + e.Message); }
            }
        }

        private static readonly System.Text.RegularExpressions.Regex PlayerId = new System.Text.RegularExpressions.Regex("[0-9A-Fa-f]{16}");

        // "Player id: 79C92EDA2B14C6FC" becomes "Friend code: 79C9-2EDA" (works on other players' profiles too).
        // Returns the id of the player whose profile this is (null when it cannot be read).
        private static string ShowFriendCode(ProfileTab tab)
        {
            var button = tab.playerIdDisplay;
            if ((object)button == null) return null;
            var label = button.GetComponentInChildren<Il2CppTMPro.TMP_Text>(true);
            if ((object)label == null) return null;
            var m = PlayerId.Match(label.text ?? "");
            if (!m.Success) return null;
            var loc = label.GetComponent<Il2CppI2.Loc.Localize>();
            if ((object)loc != null) loc.enabled = false;
            label.text = "Friend code: " + AccountPage.FriendCode(m.Value);
            return m.Value.ToUpperInvariant();
        }

        // Clicking it copies the id (and then crashes looking for a "copied" notice that is not in
        // this screen). Copy the friend code instead and show the notice only when it is available.
        [HarmonyPatch(typeof(ProfileTab.__c__DisplayClass11_0), nameof(ProfileTab.__c__DisplayClass11_0._Initialize_b__0))]
        private static class CopyFriendCode
        {
            private static bool Prefix(ProfileTab.__c__DisplayClass11_0 __instance)
            {
                try
                {
                    var who = __instance.context;
                    string id = (object)who != null ? who.PlayfabId : null;
                    if (string.IsNullOrEmpty(id)) id = PlayerDataManager.singletonManager?.playFabId;
                    string code = AccountPage.FriendCode(id);
                    if (code.Length == 0) return false;
                    GUIUtility.systemCopyBuffer = code;
                    RevivalMod.Log.Msg($"[profile] friend code {code} copied to the clipboard");
                    var notice = UIMessageController.Instance;
                    if ((object)notice != null) notice.ShowMessage("Friend code copied", false);
                }
                catch (Exception e) { RevivalMod.Log.Warning("[profile] could not copy the friend code: " + e.Message); }
                return false;
            }
        }

        // The pencil opens the game's rename window; it normally charges for a rename and uses a
        // server function for it. Here it is free and goes through the same check as Settings > Account.
        [HarmonyPatch(typeof(ChangeNameWindow), nameof(ChangeNameWindow.Open))]
        private static class FreeRename
        {
            private static void Postfix(ChangeNameWindow __instance)
            {
                try
                {
                    __instance.canChangeNameBecauseOfCost = true;
                    __instance.ToggleChangeFree(true);
                    var b = __instance.changeNameButton;
                    if ((object)b != null) b.interactable = true;
                    var input = __instance.nameInput;
                    if ((object)input != null)
                    {
                        input.characterLimit = AccountPage.MaxName;
                        var ph = input.placeholder?.TryCast<Il2CppTMPro.TMP_Text>();
                        if ((object)ph != null) ph.text = $"{AccountPage.MinName}-{AccountPage.MaxName} characters";
                    }
                }
                catch (Exception e) { RevivalMod.Log.Warning("[profile] " + e.Message); }
            }
        }

        [HarmonyPatch(typeof(ChangeNameWindow), nameof(ChangeNameWindow.ChangeNameButtonClick))]
        private static class Rename
        {
            private static bool Prefix(ChangeNameWindow __instance)
            {
                var w = __instance;
                try
                {
                    AccountPage.ChangeName(w.nameInput.text, (saved, message) =>
                    {
                        try
                        {
                            if (saved) { w.Close(); return; }
                            // No message line in this window: show the reason in the box itself.
                            w.nameInput.text = "";
                            var ph = w.nameInput.placeholder?.TryCast<Il2CppTMPro.TMP_Text>();
                            if ((object)ph != null) ph.text = message;
                        }
                        catch (Exception e) { RevivalMod.Log.Warning("[profile] " + e.Message); }
                    });
                }
                catch (Exception e) { RevivalMod.Log.Warning("[profile] " + e); }
                return false;
            }
        }

        // The border button belongs to expansion-pass events.
        [HarmonyPatch(typeof(AvatarTab), nameof(AvatarTab.ToggleSelectButton))]
        private static class NoBorderButtonEver
        {
            private static void Postfix(AvatarTab __instance)
            {
                try { Hide(__instance.toggleAvatarBorderIcon); } catch { }
            }
        }

        [HarmonyPatch(typeof(AvatarTab), nameof(AvatarTab.RefreshDisplays))]
        private static class NoBorderButtonOnRefresh
        {
            private static void Postfix(AvatarTab __instance)
            {
                try { Hide(__instance.toggleAvatarBorderIcon); } catch { }
                Scrolling.Improve(__instance.contentHolder);
            }
        }

        [HarmonyPatch(typeof(TitleTab), nameof(TitleTab.Initialize))]
        private static class TitleScrolling
        {
            private static void Postfix(TitleTab __instance) => Scrolling.Improve(__instance.contentHolder);
        }

        // The border button belongs to expansion-pass events.
        [HarmonyPatch(typeof(AvatarTab), nameof(AvatarTab.CheckEnableAvatarBorder))]
        private static class NoBorderButton
        {
            private static bool Prefix(AvatarTab __instance)
            {
                try { Hide(__instance.toggleAvatarBorderIcon); }
                catch (Exception e) { RevivalMod.Log.Warning("[profile] " + e.Message); }
                return false;
            }
        }

        private static string Pretty(string assetName)
        {
            // "Title_SOR_warlord_Morvenn_2" -> "SOR warlord Morvenn 2"
            if (string.IsNullOrEmpty(assetName)) return "";
            string s = assetName.StartsWith("Title_", StringComparison.Ordinal) ? assetName.Substring(6) : assetName;
            return s.Replace('_', ' ');
        }

        [HarmonyPatch(typeof(TitleDrawer), nameof(TitleDrawer.Draw))]
        private static class TitleText
        {
            private static int noted;

            private static void Postfix(TitleDrawer __instance, CosmeticItemTitle item)
            {
                try
                {
                    var label = __instance.title;
                    if ((object)label == null || (object)item == null || !string.IsNullOrEmpty(label.text)) return;
                    string name = item.LocalizedName;
                    if (string.IsNullOrEmpty(name)) name = Pretty(item.name);
                    label.text = name;
                    if (noted++ == 0) RevivalMod.Log.Msg("[profile] title plates had no text; filled in from the title names");
                }
                catch (Exception e) { RevivalMod.Log.Warning("[profile] " + e.Message); }
            }
        }
    }
}
