using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace LocalCoop
{
    /// <summary>`-lcaltf4`: ten seconds after the main menu shows, the game's window gets WM_CLOSE (what Alt+F4 sends), to see that closing it this way ends the game cleanly with the plugin loaded.</summary>
    internal static class AltF4Probe
    {
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        private static float _at = -1f; private static bool _sent, _posted;

        internal static void Tick()
        {
            if (_sent || !Lc.Has("-lcaltf4")) return;
            float now = Time.realtimeSinceStartup;
            if (MenuEntry.EntryButton == null) return;
            if (_at < 0f) { _at = now + 10f; try { Application.quitting += new Action(OnQuit); } catch { } return; }
            if (now < _at) return;
            if (Lc.Has("-lcaltf4nosend")) { _sent = true; return; }      // twin: nothing is sent, the game does not end, no RESULT line
            _sent = true;
            var h = GetForegroundWindow();
            Lc.Line("altf4: WM_CLOSE sent to the game window " + h);
            PostMessage(h, 0x0010, IntPtr.Zero, IntPtr.Zero); _posted = true;
        }

        internal static void OnQuit() { if (Lc.Has("-lcaltf4")) Lc.Line("altf4: application quitting"); Lc.Line("RESULT " + Lc.Test + " " + (_posted ? "PASS" : "FAIL") + " Alt+F4 equivalent: the window got WM_CLOSE and the game ended by itself"); }
    }
}
