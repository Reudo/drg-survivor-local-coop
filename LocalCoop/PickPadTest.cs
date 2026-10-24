using System;
using UnityEngine;
using UnityEngine.InputSystem.LowLevel;

namespace LocalCoop
{
    /// <summary>
    /// `-lcui=padpick`: hand test finding: in the character menu CONFIRM could only be pressed after the subclass had been clicked again (when the first
    /// subclass was wanted). With a controller: after CONTINUE the focus still pointed at the hidden CONTINUE button, so A pressed nothing. The test
    /// walks the pick pages with the pad only (d-pad to CONTINUE, A, then A at once) and asks whether the default subclass is confirmed.
    /// Twin `-lcnofocusfix` (the old behaviour) must fail the last check.
    /// </summary>
    internal static class PickPadTest
    {
        private static int _step, _ok, _fail, _nav, _c0;
        private static float _at = -1f;

        private static void Check(string name, bool ok, string detail)
        {
            if (ok) _ok++; else _fail++;
            Lc.Line("padpick: CHECK " + (ok ? "ok  " : "FAIL") + " " + name + " - " + detail);
        }

        internal static void Tick()
        {
            float now = Time.realtimeSinceStartup;
            if (_step == 0 && (MenuEntry.EntryButton == null || !CoopMenu.Built)) return;
            if (now < _at) return;
            _at = now + 0.5f;
            try
            {
                switch (_step)
                {
                    case 0: MenuEntry.EntryButton.OnClick.Invoke(); _at = now + 1.2f; _step++; break;
                    case 1:
                        CoopMenu.ShowMode("pick");
                        MenuPad.Focus = ""; MenuPad.PadUsed = false;
                        Check("the pick page is open on the class page", CoopMenu.Mode == "pick" && CoopMenu.Stage[0] == "c", "mode=" + CoopMenu.Mode + " stage=" + CoopMenu.Stage[0]);
                        VirtualPad.Press(GamepadButton.DpadDown); _step++; break;
                    case 2:
                        Check("the d-pad focuses a class", MenuPad.Focus.StartsWith("P1c"), "focus=" + MenuPad.Focus);
                        _c0 = CoopMenu.P1.ClassIndex; VirtualPad.Press(GamepadButton.DpadRight); _step++; break;      // moving the focus is choosing
                    case 3:
                        Check("moving onto another class chooses it, no A needed", MenuPad.Focus == "P1c" + CoopMenu.P1.ClassIndex && CoopMenu.P1.ClassIndex != _c0, "was " + _c0 + " now " + CoopMenu.P1.ClassIndex + " focus=" + MenuPad.Focus);
                        VirtualPad.Press(GamepadButton.Start, 0.6f); _step++; _at = now + 1.2f; break;       // the menu button is CONTINUE
                    case 4:
                        Check("the menu button on the class page opens the subclass page", CoopMenu.Stage[0] == "s", "stage=" + CoopMenu.Stage[0] + " focus=" + MenuPad.Focus);
                        Check("after CONTINUE the focus is on a subclass choice (not on the hidden class button)", MenuPad.Focus.StartsWith("P1s"), "focus=" + MenuPad.Focus);
                        VirtualPad.Press(GamepadButton.Start, 0.6f); _step++; _at = now + 1.2f; break;       // the menu button is CONFIRM
                    case 5:
                        Check("the menu button on the subclass page confirms the default subclass (no second click on it needed)", CoopMenu.Confirmed[0], "confirmed=" + CoopMenu.Confirmed[0] + " focus=" + MenuPad.Focus + " stage=" + CoopMenu.Stage[0]);
                        UiTest.FinishWith(_fail == 0, "padpick ok=" + _ok + " fail=" + _fail); _step++; break;
                }
            }
            catch (Exception e) { Lc.Line("padpick: tick threw " + e.GetType().Name + ": " + e.Message); UiTest.FinishWith(false, "threw " + e.GetType().Name); }
        }
    }
}
