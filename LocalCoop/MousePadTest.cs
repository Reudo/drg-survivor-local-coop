using System;
using UnityEngine;
using UnityEngine.InputSystem.LowLevel;

namespace LocalCoop
{
    /// <summary>
    /// `-lcui=flowmouse`: hand test finding: once the mouse had moved over the co-op menu, the controller no longer worked in it. The test opens the
    /// lobby, uses the pad, moves a virtual mouse over the menu (and clicks), then uses the pad again: the focus must still move and A must still press.
    /// </summary>
    internal static class MousePadTest
    {
        private static int _step, _ok, _fail;
        private static float _at = -1f;
        private static string _f0 = "";

        private static void Check(string name, bool ok, string detail)
        {
            if (ok) _ok++; else _fail++;
            Lc.Line("mousepad: CHECK " + (ok ? "ok  " : "FAIL") + " " + name + " - " + detail);
        }

        internal static void Tick()
        {
            float now = Time.realtimeSinceStartup;
            if (_step == 0 && (MenuEntry.EntryButton == null || !CoopMenu.Built)) return;
            if (now < _at) return;
            _at = now + 0.6f;
            try
            {
                switch (_step++)
                {
                    case 0: MenuEntry.EntryButton.OnClick.Invoke(); _at = now + 1.2f; break;
                    case 1: VirtualPad.Press(GamepadButton.DpadRight); break;
                    case 2: _f0 = MenuPad.Focus; Check("the pad focuses a choice in the lobby", _f0.Length > 0 && MenuPad.PadUsed, "focus=" + _f0); VirtualPad.Press(GamepadButton.DpadRight); break;
                    case 3: Check("the pad moves the focus", MenuPad.Focus != _f0 || CoopMenu.Mode != "lobby", "focus=" + MenuPad.Focus); _f0 = MenuPad.Focus; break;
                    case 4: VirtualMouse.Step(0, new Vector2(Screen.width * 0.3f, Screen.height * 0.5f)); break;
                    case 5: VirtualMouse.Step(0, new Vector2(Screen.width * 0.5f, Screen.height * 0.6f)); break;
                    case 6: VirtualMouse.Step(0, new Vector2(Screen.width * 0.55f, Screen.height * 0.4f)); break;
                    case 7: VirtualPad.Press(GamepadButton.DpadLeft); break;
                    case 8:
                        Check("after the mouse moved, the pad still moves the focus", MenuPad.Focus != _f0, "focus " + _f0 + " -> " + MenuPad.Focus + " padUsed=" + MenuPad.PadUsed + " gameInputType=" + InputHandler.InputType + " pads=" + UnityEngine.InputSystem.Gamepad.all.Count + " enabled=" + (UnityEngine.InputSystem.Gamepad.all.Count > 0 && UnityEngine.InputSystem.Gamepad.all[0].enabled));
                        _f0 = MenuPad.Focus; Shots.Take("mousepad-after-mouse");
                        VirtualPad.Press(GamepadButton.South); break;
                    case 9:
                        Check("after the mouse moved, A still presses the focused choice", CoopMenu.Mode != "lobby" || MenuPad.Presses > 0, "mode=" + CoopMenu.Mode + " presses=" + MenuPad.Presses);
                        CoopMenu.Show(); CoopMenu.Show(); CoopMenu.Hide();      // hand test: a second Show remembered "navigation off" and the game's menus stayed dead
                        { var es = UnityEngine.EventSystems.EventSystem.current; Check("after the menu was shown twice and closed, the game's own navigation is on", es != null && es.sendNavigationEvents, "sendNavigationEvents=" + (es != null && es.sendNavigationEvents)); }
                        UiTest.FinishWith(_fail == 0, "mousepad ok=" + _ok + " fail=" + _fail); break;
                }
            }
            catch (Exception e) { Lc.Line("mousepad: tick threw " + e.GetType().Name + ": " + e.Message); UiTest.FinishWith(false, "threw " + e.GetType().Name); }
        }
    }
}
