using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace LocalCoop
{
    /// <summary>
    /// `-lcui=chooser` (with `-lcpoll`, two virtual gamepads): a level-up is answered by the host first and by player 2 afterwards. While it
    /// is somebody's turn the other pad must not reach the game's input actions. The test watches a button action bound to every gamepad's
    /// south button (the kind of action the UI uses): during the host's turn only pad 1 may fire it, during player 2's turn only pad 2, and
    /// afterwards both again. It also reads the banner state. Twin `-lcnochooser`: the other pad fires too (checks go red).
    /// </summary>
    internal static class ChooserTest
    {
        private static int _step, _ok, _fail;
        private static float _at = -1f, _t0, _feed;
        private static InputAction _act;
        private static bool _hostChecked, _p2Checked;
        private static int _sub, _cb0, _bb0, _bph, _bph2; private static bool _aDone, _aDone2;
        private static bool _p1Fired, _p2Fired, _moved;
        private static float _back = -1f; private static int _try1, _try2;
        private static int _nav, _nav2; private static float _navAt, _navAt2; private static string _sel0 = "", _sel2 = ""; private static bool _moved2;

        private static string PairInfo()
        {
            try
            {
                var pi = InputHandler.PlayerInput;
                var sb = new System.Text.StringBuilder();
                sb.Append("scheme=").Append(pi.currentControlScheme).Append(" paired=");
                foreach (var d in pi.devices) sb.Append(d.displayName).Append('#').Append(d.deviceId).Append(' ');
                sb.Append("inputType=").Append(InputHandler.InputType);
                return sb.ToString();
            }
            catch (Exception e) { return "pairinfo err " + e.GetType().Name; }
        }

        /// <summary>The selected card moves with d-pad right from the given pad: true when the probe is finished (checks written).</summary>
        private static bool NavProbe(bool padTwo, float now, ref int n, ref float at, ref string sel, ref bool moved, string label)
        {
            if (n >= 6) return true;
            if (now < at) return false;
            if (n == 0) { sel = SelName(); Lc.Line("chooser-test: [" + label + "] selection before moving: " + sel + " | " + PairInfo()); }
            // the real player steers the cursor with the stick: hold it right for a moment, let go, read the selection
            if (n % 2 == 0) { if (padTwo) VirtualPad.SetStick(new Vector2(1f, 0f)); else VirtualPad.SetStick1(new Vector2(1f, 0f)); at = now + 0.4f; }
            else
            {
                if (padTwo) VirtualPad.SetStick(Vector2.zero); else VirtualPad.SetStick1(Vector2.zero);
                string s2 = SelName(); Lc.Line("chooser-test: [" + label + "] selection after the stick right #" + (n / 2 + 1) + ": " + s2); if (s2 != sel) moved = true; sel = s2; at = now + 0.5f;
            }
            n++;
            if (n == 6) { Check("[" + label + "] the choosing pad can move the selection", moved, "selection now " + sel + " | " + PairInfo()); }
            return false;
        }

        /// <summary>The game's own UI submit action (what A does on a card): it only sees the devices the game's PlayerInput is paired with.</summary>
        private static InputAction GameSubmit()
        {
            try
            {
                var pi = InputHandler.PlayerInput;
                string json = pi.actions.ToJson();
                Lc.Line("chooser-test: game actions json " + (json.Length > 1500 ? json.Substring(0, 1500) : json));
                foreach (var n in new[] { "UI/Submit", "Submit", "UI/Confirm", "Confirm", "UI/Select", "Select" })
                {
                    var a = pi.actions.FindAction(n, false);
                    if (a != null) { Lc.Line("chooser-test: using the game's action " + n + " enabled=" + a.enabled); return a; }
                }
            }
            catch (Exception e) { Lc.Line("chooser-test: GameSubmit threw " + e.GetType().Name + ": " + e.Message); }
            return null;
        }

        private static string SelName()
        {
            try { var es = UnityEngine.EventSystems.EventSystem.current; var o = es != null ? es.currentSelectedGameObject : null; return o != null ? o.name : "<none>"; } catch (Exception e) { return "err " + e.GetType().Name; }
        }

        private static void Check(string name, bool ok, string detail)
        {
            if (ok) _ok++; else _fail++;
            Lc.Line("chooser-test: CHECK " + (ok ? "ok  " : "FAIL") + " " + name + " - " + detail);
        }

        /// <summary>Press one pad for a moment, report whether the action saw it (a few frames later).</summary>
        private static bool Probe(bool padTwo, float now, ref int sub, ref bool fired)
        {
            if (sub == 0) { fired = false; if (padTwo) VirtualPad.Press(GamepadButton.South); else VirtualPad.Press1(GamepadButton.South); sub = 1; _t0 = now; return false; }
            if (_act.IsPressed()) fired = true;
            if (now < _t0 + 0.12f) return false;
            sub = 0;
            return true;
        }

        internal static void Tick()
        {
            float now = Time.realtimeSinceStartup;
            if (_step == 0 && (MenuEntry.EntryButton == null || !CoopMenu.Built)) return;
            if (now < _at) return;
            _at = now + 0.03f;
            try
            {
                switch (_step)
                {
                    case 0: MenuEntry.EntryButton.OnClick.Invoke(); _step++; _at = now + 0.6f; break;
                    case 1: UiTest.Click("P1class3"); UiTest.Click("P2class1"); UiTest.Click("sector0"); UiTest.Click("danger1"); UiTest.Click("biome3"); _step++; _at = now + 0.6f; break;
                    case 2:
                        UiTest.Click("start"); _step++; _t0 = now; _at = now + 3f;
                        AutoStart.NoServe = true;
                        VirtualPad.Pad1Touch();
                        break;
                    case 3:
                        if (Lc.InRunAt < 0f) { if (now > _t0 + 70f) UiTest.FinishWith(false, "the run did not start"); return; }
                        if (Phase1.Second == null || !Phase1.Second.isAlive || now < Lc.InRunAt + 14f) return;
                        _act = GameSubmit();
                        if (_act == null) { UiTest.FinishWith(false, "the game's submit action was not found"); return; }
                        VirtualPad.Press(GamepadButton.West);          // player 2 presses something in the game: the game pairs ITS pad with the UI actions
                        _step++; _t0 = now; _feed = 0f; break;
                    case 4:
                        // feed experience until the level-up screen of the host's turn is up
                        if (Chooser.Who == 0)
                        {
                            if (now > _t0 + 60f) { UiTest.FinishWith(false, "no level-up screen"); return; }
                            if (Time.timeScale > 0f && now >= _feed) { Phase1.Second.GainXp(6f, false); _feed = now + 0.05f; }
                            return;
                        }
                        _step++; _t0 = now; _sub = 0; _at = now + 0.5f; break;
                    case 5:      // the host's turn: first, can the choosing pad move the selection? (hand test: it could not)
                        if (!NavProbe(false, now, ref _nav, ref _navAt, ref _sel0, ref _moved, "host's turn, pad 1")) return;
                        if (!_hostChecked)
                        {
                            Check("the host's turn is announced (banner for player 1)", Chooser.Who == 1, "who=" + Chooser.Who + " kind=" + Chooser.Kind);
                            if (!_aDone)
                            {
                                if (_sub == 0) _cb0 = Chooser.CrossBlocked;
                                if (!Probe(true, now, ref _sub, ref _p2Fired)) return;
                            Check("host's turn: pad 2 does not reach the actions", !_p2Fired, "fired=" + _p2Fired + " locked=" + Chooser.Locked);
                            Check("host's turn: the selected card's own polling is held back for pad 2's A", Chooser.CrossBlocked > _cb0, "blocked " + _cb0 + " -> " + Chooser.CrossBlocked + " selected=" + SelName());
                                _aDone = true;
                            }
                            if (Lc.Has("-lcnobtest")) _bph = 2;
                            if (_bph == 0) { _bb0 = Chooser.BackBlocked; VirtualPad.Press(GamepadButton.East); _bph = 1; _t0 = now; return; }
                            if (_bph == 1 && now < _t0 + 0.4f) return;
                            if (_bph == 1) Check("host's turn: pad 2's B (back / Details) is held back", Chooser.BackBlocked > _bb0, "blocked " + _bb0 + " -> " + Chooser.BackBlocked);
                            _hostChecked = true; return;
                        }
                        if (!Probe(false, now, ref _sub, ref _p1Fired)) return;
                        Check("host's turn: pad 1 reaches the actions", _p1Fired, "fired=" + _p1Fired);
                        Shots.Take("chooser-host");
                        AutoStart.NoServe = false;           // the automatic player answers the host's cards
                        _step++; _t0 = now; break;
                    case 6:      // wait for player 2's turn
                        if (Chooser.Who != 2)
                        {
                            if (now > _t0 + 40f) { Check("player 2's turn comes", false, "who=" + Chooser.Who + " " + LevelRounds.State); UiTest.FinishWith(false, "no round for player 2"); }
                            return;
                        }
                        AutoStart.NoServe = true;
                        _step++; _sub = 0; _t0 = now; _at = now + 0.5f; _p1Fired = _p2Fired = false; break;
                    case 7:
                        if (!NavProbe(true, now, ref _nav2, ref _navAt2, ref _sel2, ref _moved2, "player 2's turn, pad 2")) return;
                        if (!_p2Checked)
                        {
                            Check("player 2's turn is announced (banner for player 2)", Chooser.Who == 2, "who=" + Chooser.Who + " kind=" + Chooser.Kind);
                            if (!_aDone2)
                            {
                                if (_sub == 0) _cb0 = Chooser.CrossBlocked;
                                if (!Probe(false, now, ref _sub, ref _p1Fired)) return;
                            Check("player 2's turn: pad 1 does not reach the actions", !_p1Fired, "fired=" + _p1Fired + " locked=" + Chooser.Locked);
                            Check("player 2's turn: the selected card's own polling is held back for pad 1's A", Chooser.CrossBlocked > _cb0, "blocked " + _cb0 + " -> " + Chooser.CrossBlocked + " selected=" + SelName());
                                _aDone2 = true;
                            }
                            if (Lc.Has("-lcnobtest")) _bph2 = 2;
                            if (_bph2 == 0) { _bb0 = Chooser.BackBlocked; VirtualPad.Press1(GamepadButton.East); _bph2 = 1; _t0 = now; return; }
                            if (_bph2 == 1 && now < _t0 + 0.4f) return;
                            if (_bph2 == 1) Check("player 2's turn: pad 1's B (back / Details) is held back", Chooser.BackBlocked > _bb0, "blocked " + _bb0 + " -> " + Chooser.BackBlocked);
                            _p2Checked = true; return;
                        }
                        if (!Probe(true, now, ref _sub, ref _p2Fired)) return;
                        Check("player 2's turn: pad 2 reaches the actions", _p2Fired, "fired=" + _p2Fired);
                        Shots.Take("chooser-p2");
                        AutoStart.NoServe = false;
                        _step++; _t0 = now; break;
                    case 8:      // everything given back
                        if (Chooser.Who != 0 || Chooser.Locked != 0) { if (now > _t0 + 40f) { Check("both pads are given back", false, "who=" + Chooser.Who + " locked=" + Chooser.Locked); UiTest.FinishWith(false, "pads not given back"); } return; }
                        if (_back < 0f) { _back = now; return; }
                        if (now < _back + 1.5f) return;
                        { var sb = new System.Text.StringBuilder(); foreach (var g in Gamepad.all) sb.Append(g.deviceId).Append(g.enabled ? "(on) " : "(OFF) "); Lc.Line("chooser-test: 1.5 s after the turns: pads " + sb); }
                        _sub = 0; _p1Fired = _p2Fired = false; _step++; break;
                    case 9:
                        if (Chooser.Who != 0) { _step = 8; _back = -1f; _t0 = now; _sub = 0; return; }          // another round started meanwhile (the run keeps earning XP): wait for it to end
                        if (!Probe(false, now, ref _sub, ref _p1Fired)) return;
                        if (!_p1Fired && _try1++ < 2) return;      // the first press on a pad the game is not paired with only makes it switch (as in the unmodified game)
                        Lc.Line("chooser-test: pad 1 probe after the turns: fired=" + _p1Fired);
                        _step++; break;
                    case 10:
                        if (Chooser.Who != 0) { _step = 8; _back = -1f; _t0 = now; _sub = 0; return; }
                        if (!Probe(true, now, ref _sub, ref _p2Fired)) return;
                        if (!_p2Fired && _try2++ < 2) return;
                        Check("after the turns both pads reach the actions again", _p1Fired && _p2Fired, "pad1=" + _p1Fired + " pad2=" + _p2Fired);
                        UiTest.FinishWith(_fail == 0, "chooser ok=" + _ok + " fail=" + _fail);
                        break;
                }
            }
            catch (Exception e) { Lc.Line("chooser-test: tick threw " + e.GetType().Name + ": " + e.Message); UiTest.FinishWith(false, "threw " + e.GetType().Name + ": " + e.Message); }
        }
    }
}
