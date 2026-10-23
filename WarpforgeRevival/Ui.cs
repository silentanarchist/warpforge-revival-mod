using System;
using Il2Cpp;
using Il2CppTMPro;
using UnityEngine;

namespace WarpforgeRevival
{
    /// <summary>
    /// Small helpers the mod's pages share (code review M-26/M-27, 0.13.75): before, each file kept its own copy
    /// (seven "Alive" checks, two tab finders, fourteen "switch the translation off, then set the text" blocks,
    /// eleven "where is this on screen" corner calculations).
    /// </summary>
    internal static class Ui
    {
        /// <summary>A game object (or component) that still exists on both sides (ours and the game's).</summary>
        internal static bool Alive(UnityEngine.Object o)
        {
            try { return (object)o != null && o.Pointer != IntPtr.Zero && o.m_CachedPtr != IntPtr.Zero; } catch { return false; }
        }

        /// <summary>The window's tab of this kind, or null.</summary>
        internal static WindowTabBase TabOf<T>(GameWindowWithTabs w) where T : WindowTabBase
        {
            var tabs = (object)w == null ? null : w.tabs;
            if (tabs == null) return null;
            for (int i = 0; i < tabs.Count; i++)
            {
                var t = tabs[i];
                if ((object)t != null && (object)t.TryCast<T>() != null) return t;
            }
            return null;
        }

        /// <summary>Sets a label's text for good: its translation component is switched off first, or it would put
        /// the game's own wording back.</summary>
        internal static void SetText(TMP_Text label, string text)
        {
            if ((object)label == null) return;
            var loc = label.GetComponent<Il2CppI2.Loc.Localize>();
            if ((object)loc != null) loc.enabled = false;
            label.text = text;
        }

        /// <summary>Where a rectangle is on screen (world space): its top, bottom, left and right edges.</summary>
        /// <summary>An input box's grey hint text (placeholder), made easier to read: still grey, but lighter and less
        /// see-through than the game's (a tester found it too dark, 2026-10-23). Done once per text.</summary>
        internal static void BrighterHint(TMP_Text hint)
        {
            try
            {
                if ((object)hint == null) return;
                var c = hint.color;
                float grey = Math.Max(0.62f, (c.r + c.g + c.b) / 3f);
                hint.color = new Color(grey, grey, grey, Math.Max(c.a, 0.85f));
            }
            catch { }
        }

        internal static (float top, float bottom, float left, float right) WorldBox(RectTransform rt)
        {
            var c = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(4);
            rt.GetWorldCorners(c);
            return (Math.Max(c[0].y, c[1].y), Math.Min(c[0].y, c[1].y), Math.Min(c[0].x, c[2].x), Math.Max(c[0].x, c[2].x));
        }

        // ---- the mod's own drawn windows (sign-in, bug report, player report): one kit since 0.13.75 (M-27)

        /// <summary>A 1 x 1 texture of one colour, kept across scenes (backgrounds of the mod's windows).</summary>
        internal static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            t.hideFlags = HideFlags.HideAndDontSave;
            return t;
        }

        /// <summary>The mod's windows are laid out on a 1280 x 720 page and scaled to the screen by hand (the phone's
        /// game build lacks GUI.matrix): this is the scale.</summary>
        internal static float GuiScale() => Mathf.Max(0.5f, Mathf.Min(Screen.width / 1280f, Screen.height / 720f));

        /// <summary>Opens a web address in the player's browser. Kept apart (never inlined) so that a game build missing
        /// Application.OpenURL fails inside the caller's try, not in the code drawing the window.</summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        internal static void OpenUrl(string url) => Application.OpenURL(url);
    }
}
