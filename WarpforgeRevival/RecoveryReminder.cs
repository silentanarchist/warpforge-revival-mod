using System;
using Il2Cpp;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace WarpforgeRevival
{
    /// <summary>
    /// A reminder to make a recovery code (owner, 2026-10-16): once the game is on the main menu and the
    /// server has said the linked site account has no recovery code, the game's own two-button popup (the
    /// kind its "please enable notifications" question used) asks the player to make one.
    ///  - "Set it up" opens Settings > Account, where the password box and "Create recovery code" are (RecoveryCode).
    ///  - "Not now" asks again 3 days later. Otherwise it asks once each time the game starts, until a code exists.
    /// Never during a match, never over another popup, and only for a game linked to a site account.
    /// </summary>
    internal static class RecoveryReminder
    {
        private const string SnoozeKey = "WFR.RecoveryReminderAfter";
        private const double SnoozeDays = 3;
        private const float SettleSeconds = 4f, OpenTimeout = 6f;
        private const string Text =
            "Your account has no recovery code yet.\n\n" +
            "If you ever forget your password, a recovery code is the only way back into your account (decks, progress and friends). " +
            "It takes a moment: Settings > Account, enter your password and press Create recovery code. The code is saved to a file for you.";

        private static bool asked;
        private static float readySince = -1f, openSince = -1f;
        private static bool opener;

        internal static void Tick()
        {
            try
            {
                if (openSince >= 0f) { FinishOpen(); return; }
                if (asked) return;
                if (!AccountPage.StatusKnown || AccountPage.LinkedAccount.Length == 0 || AccountPage.HasRecovery != false) { readySince = -1f; return; }
                if (!AtMainMenu()) { readySince = -1f; return; }
                if (readySince < 0f) { readySince = Time.realtimeSinceStartup; return; }
                if (Time.realtimeSinceStartup - readySince < SettleSeconds) return;     // let the menu and the game's own popups settle
                asked = true;
                if (Snoozed(out var until)) { RevivalMod.Log.Msg($"[recovery reminder] no recovery code; not asking until {until:yyyy-MM-dd HH:mm} (Not now)"); return; }
                Show();
            }
            catch (Exception e) { asked = true; openSince = -1f; RevivalMod.Log.Warning("[recovery reminder] " + e.Message); }
        }

        private static bool AtMainMenu()
        {
            var wm = WindowsManager.Instance;
            if ((object)wm == null) return false;
            var menu = wm.BaseMenu;
            if ((object)menu == null || (object)menu.TryCast<MainMenuWindow>() == null || !menu.isActiveAndEnabled) return false;
            var popup = wm.PopupWindow;
            if ((object)popup != null && popup.isActiveAndEnabled) return false;                     // another popup is up
            if ((object)UnityEngine.Object.FindObjectOfType<BattleManager>() != null) return false;  // a match (or a replay)
            return true;
        }

        private static bool Snoozed(out DateTime until)
        {
            until = DateTime.MinValue;
            try
            {
                string s = PlayerPrefs.GetString(SnoozeKey, "");
                if (long.TryParse(s, out long ticks)) until = new DateTime(ticks, DateTimeKind.Utc).ToLocalTime();
            }
            catch { }
            return until > DateTime.Now;
        }

        private static void Show()
        {
            var wm = WindowsManager.Instance;
            Action setUp = () =>
            {
                RevivalMod.Log.Msg("[recovery reminder] Set it up: opening Settings > Account");
                PlayFabTransport.OnMainThread(OpenAccount);
            };
            Action later = () =>
            {
                try
                {
                    PlayerPrefs.SetString(SnoozeKey, DateTime.UtcNow.AddDays(SnoozeDays).Ticks.ToString());
                    PlayerPrefs.Save();
                }
                catch { }
                RevivalMod.Log.Msg($"[recovery reminder] Not now: asking again in {SnoozeDays:0} days");
            };
            wm.ShowPopUp(Text, false, true, "Set it up", "Not now",
                         DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(setUp),
                         DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(later));
            RevivalMod.Log.Msg("[recovery reminder] the linked account has no recovery code: reminder shown");
        }

        // ---------------------------------------------------------------- Settings > Account

        private static SettingsMenu Settings()
        {
            foreach (var m in Resources.FindObjectsOfTypeAll<SettingsMenu>())
                if ((object)m != null && m.gameObject.scene.IsValid()) return m;                    // one in the scene, not the window file
            return null;
        }

        private static void OpenAccount()
        {
            try
            {
                var wm = WindowsManager.Instance;
                var menu = Settings();
                opener = false;
                if ((object)menu != null)
                {
                    if (!menu.IsOpen()) wm.OpenWindow(menu, false, null);
                }
                else
                {
                    // never opened yet this session: press the menu's own settings button (the one whose window is the settings)
                    foreach (var b in UnityEngine.Object.FindObjectsOfType<OpenWindowButton>())
                    {
                        var scene = b.windowToOpenScene;
                        string path = Path(b.transform);
                        bool settings = (object)scene != null ? (object)scene.TryCast<SettingsMenu>() != null
                                                              : path.IndexOf("setting", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                                path.IndexOf("option", StringComparison.OrdinalIgnoreCase) >= 0;
                        if (!settings) continue;
                        RevivalMod.Log.Msg("[recovery reminder] pressing the settings button " + path);
                        b.OpenWindow();
                        opener = true;
                        break;
                    }
                    if (!opener)
                    {
                        string all = "";
                        foreach (var b in UnityEngine.Object.FindObjectsOfType<OpenWindowButton>()) all += " | " + Path(b.transform);
                        RevivalMod.Log.Warning("[recovery reminder] settings button not found; window buttons:" + all);
                    }
                }
                openSince = Time.realtimeSinceStartup;
            }
            catch (Exception e) { RevivalMod.Log.Warning("[recovery reminder] could not open the settings: " + e); Fallback(); }
        }

        private static void FinishOpen()
        {
            var menu = Settings();
            if ((object)menu != null && menu.IsOpen())
            {
                var tab = menu.GetComponentInChildren<AccountTab>(true);
                if ((object)tab != null)
                {
                    openSince = -1f;
                    menu.ChangeTab(tab);
                    RevivalMod.Log.Msg("[recovery reminder] Settings > Account opened");
                    return;
                }
            }
            if (Time.realtimeSinceStartup - openSince < OpenTimeout) return;
            openSince = -1f;
            RevivalMod.Log.Warning($"[recovery reminder] Settings > Account did not open (settings window {((object)menu == null ? "not found" : menu.IsOpen() ? "open, no Account tab" : "closed")})");
            Fallback();
        }

        private static void Fallback()
        {
            try { WindowsManager.Instance?.ShowPopUp("Open Settings > Account to make your recovery code.", false, true, "OK", (Il2CppSystem.Action)null); } catch { }
        }

        private static string Path(Transform t)
        {
            string p = "";
            for (int i = 0; (object)t != null && i < 4; i++, t = t.parent) p = t.name + (p.Length > 0 ? "/" + p : "");
            return p;
        }
    }
}
