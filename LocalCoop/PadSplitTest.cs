using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace LocalCoop
{
    /// <summary>
    /// `-lcui=padsplit`: two virtual gamepads in the co-op menu. Controller 2 may only reach player 2's class and subclass (blue frame),
    /// controller 1 everything except player 2's panel, and controller 2's B must not close the menu. Twin `-lcpadsplitoff`:
    /// every pad reaches everything (the checks must go red).
    /// </summary>
    internal static class PadSplitTest
    {
        private static int _s, _i;
        private static float _at = -1f;
        private static int _ok, _fail;
        private static readonly List<string> P1Seen = new List<string>(), P2Seen = new List<string>();
        private static GamepadButton[] _p1Seq, _p2Seq;
        private static int _p2Class0, _p1Class0;
        private static string _snapshot;

        private static void Check(string name, bool ok, string detail)
        {
            if (ok) _ok++; else _fail++;
            Lc.Line("padsplit: CHECK " + (ok ? "ok  " : "FAIL") + " " + name + " - " + detail);
        }

        internal static void Tick()
        {
            float now = Time.realtimeSinceStartup;
            if (now < _at) return;
            if (_s == 0 && (MenuEntry.EntryButton == null || !CoopMenu.Built)) return;
            _at = now + 0.4f;
            try
            {
                // record where each controller's focus has been
                if (!string.IsNullOrEmpty(MenuPad.Focus) && (P1Seen.Count == 0 || P1Seen[P1Seen.Count - 1] != MenuPad.Focus)) P1Seen.Add(MenuPad.Focus);
                if (!string.IsNullOrEmpty(MenuPad.Focus2) && (P2Seen.Count == 0 || P2Seen[P2Seen.Count - 1] != MenuPad.Focus2)) P2Seen.Add(MenuPad.Focus2);
                switch (_s)
                {
                    case 0:
                        CoopStart.Dry = true;
                        VirtualPad.Pad1Touch();                        // the second virtual pad: controller 1; the first one (Pad) is controller 2
                        MenuEntry.EntryButton.OnClick.Invoke();
                        _s++; break;
                    case 1:
                        Check("two pads: the menu is split", MenuPad.Split || Lc.Has("-lcpadsplitoff"), "split=" + MenuPad.Split + " pads=" + Gamepad.all.Count);
                        _snapshot = CoopMenu.Summary();
                        _p1Class0 = CoopMenu.P1.ClassIndex; _p2Class0 = CoopMenu.P2.ClassIndex;
                        var R = GamepadButton.DpadRight; var Dn = GamepadButton.DpadDown; var L = GamepadButton.DpadLeft; var U = GamepadButton.DpadUp;
                        // controller 1 wanders over the whole menu, also to the right where player 2's panel is
                        _p1Seq = new[] { R, R, R, Dn, Dn, Dn, R, R, R, R, Dn, Dn, L, L, L, L, U, U, R, R, R, R, R, R, Dn, Dn, Dn, Dn, Dn, R, R };
                        _i = 0; _s++; break;
                    case 2:
                        if (_i < _p1Seq.Length) { VirtualPad.Press1(_p1Seq[_i++]); _at = now + 0.3f; return; }
                        _s++; break;
                    case 3:
                        {
                            bool any = false; string seen = "";
                            foreach (var k in P1Seen) { if (k.StartsWith("P2")) any = true; }
                            seen = string.Join(",", P1Seen.ToArray());
                            Check("controller 1 never gets into player 2's panel", !any && P1Seen.Count > 3, "controller 1 visited: " + seen);
                            // controller 2: only its own class and subclass
                            var D = GamepadButton.DpadDown; var Ri = GamepadButton.DpadRight; var Le = GamepadButton.DpadLeft; var Up = GamepadButton.DpadUp;
                            _p2Seq = new[] { D, D, D, Le, Le, Le, Up, Up, Ri, Ri, Ri, D, D, D, D, D, Le, Le };
                            _i = 0; _s++;
                        }
                        break;
                    case 4:
                        if (_i < _p2Seq.Length) { VirtualPad.Press(_p2Seq[_i++]); _at = now + 0.3f; return; }
                        _s++; break;
                    case 5:
                        {
                            bool other = false;
                            foreach (var k in P2Seen) { if (!k.StartsWith("P2class") && !k.StartsWith("P2sub")) other = true; }
                            Check("controller 2 only reaches player 2's class and subclass", !other && P2Seen.Count > 1, "controller 2 visited: " + string.Join(",", P2Seen.ToArray()));
                            // A on a P2 class picks it for P2 only
                            int want = (CoopMenu.P2.ClassIndex + 1) % 4;
                            MenuPad.Focus2 = "P2class" + want;
                            VirtualPad.Press(GamepadButton.South);
                            _s++;
                        }
                        break;
                    case 6:
                        {
                            Check("A on controller 2 picks player 2's class and leaves everything else", CoopMenu.P2.ClassIndex != _p2Class0 && CoopMenu.P1.ClassIndex == _p1Class0, CoopMenu.Summary() + " (before: " + _snapshot + ")");
                            VirtualPad.Press(GamepadButton.East);          // B of controller 2
                            _s++;
                        }
                        break;
                    case 7:
                        Check("B on controller 2 does not close the menu", CoopMenu.Visible, "visible=" + CoopMenu.Visible);
                        VirtualPad.Press(GamepadButton.Start);              // Start of controller 2
                        _s++; break;
                    case 8:
                        Check("Start on controller 2 does not start the run", !CoopStart.Started && CoopMenu.Visible, "started=" + CoopStart.Started);
                        VirtualPad.Press1(GamepadButton.East);              // B of controller 1
                        _s++; break;
                    case 9:
                        Check("B on controller 1 closes the menu", !CoopMenu.Visible, "visible=" + CoopMenu.Visible);
                        Shots.Take("padsplit-end");
                        UiTest.FinishWith(_fail == 0, "padsplit checks ok=" + _ok + " fail=" + _fail);
                        _s++; break;
                }
            }
            catch (Exception e) { Lc.Line("padsplit: tick threw " + e.GetType().Name + ": " + e.Message); UiTest.FinishWith(false, "threw " + e.GetType().Name + ": " + e.Message); }
        }
    }
}
