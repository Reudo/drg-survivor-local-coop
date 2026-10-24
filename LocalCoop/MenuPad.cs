using System;
using System.Collections.Generic;
using HarmonyLib;
using DRS.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

namespace LocalCoop
{
    /// <summary>
    /// Controller navigation of the co-op menu. With two gamepads each player has a role: controller 2 moves a blue frame over player
    /// 2's class and subclass and nothing else; controller 1 (every other gamepad) moves a white frame over everything except
    /// player 2's panel and also owns BACK and START RUN. With a single gamepad that one pad reaches everything (otherwise player 2 could
    /// not be set up without a mouse). D-pad / left stick move the frame to the nearest open choice in that direction, A presses.
    /// While the menu is open the game's own UI navigation is switched off (EventSystem.sendNavigationEvents) and the main menu's back
    /// handler is held back, so nothing underneath reacts. Twin `-lcpadsplitoff`: every pad reaches everything.
    /// </summary>
    internal static class MenuPad
    {
        internal static string Focus = "";          // controller 1's focus (the only one when a single pad is connected)
        internal static string Focus2 = "";         // controller 2's focus
        internal static bool PadUsed;
        internal static int Presses;
        private static RectTransform _frame, _frame2;
        private static bool _used2;
        private static readonly float[] _nextMoveAt = new float[2];
        private static bool _navWas = true;
        private static bool _patched;

        internal static int BackHits;

        private static GameObject _menuButtons;

        /// <summary>True while two or more gamepads are connected: then the menu is split between the players.</summary>
        internal static bool Split
        {
            get { try { return !Lc.Has("-lcpadsplitoff") && Gamepad.all != null && (Gamepad.all.Count >= 2 || (VirtualPad.Remote && Gamepad.all.Count >= 1)); } catch { return false; } }
        }

        /// <summary>The main menu stays alive underneath, but its button column is switched off while the co-op menu is open: a selected
        /// button of it would otherwise take the confirm key (UIButton polls its own input while selected).</summary>
        private static void LockMainMenu(bool locked)
        {
            try
            {
                if (locked)
                {
                    var menu = Find.One<UIMenuSplashForm>();
                    var play = menu != null ? menu.playButton : null;
                    if (play == null) return;
                    _menuButtons = play.transform.parent.gameObject;
                    _menuButtons.SetActive(false);
                }
                else if (_menuButtons != null) { _menuButtons.SetActive(true); _menuButtons = null; }
            }
            catch (Exception e) { Lc.LineOnce("ui: lock main menu failed: " + e.GetType().Name + ": " + e.Message); }
        }

        private static bool _navLocked;
        private static float _navCheckAt;
        internal static int NavFixes;

        /// <summary>
        /// Hand test: the game's own menus (shop, level-up, end screen, main menu after a run) did not react to the controller, only to the mouse.
        /// The game's navigation is switched off while the co-op menu is open; whenever the co-op menu is not open it must be on. Checked twice a
        /// second, with a log line if it had to be switched back on. Twin `-lcnonavwatch`.
        /// </summary>
        internal static void NavWatch()
        {
            if (Lc.Has("-lcnonavwatch")) return;
            float now = Time.realtimeSinceStartup;
            if (now < _navCheckAt) return;
            _navCheckAt = now + 0.5f;
            try
            {
                if (CoopMenu.Visible) return;
                var es = EventSystem.current;
                var splash = Find.One<UIMenuSplashForm>();
                if (Lc.Has("-lcmenuprobe") && !_probed && splash != null && splash.playButton != null && splash.playButton.gameObject.activeInHierarchy) { _probed = true; ProbeNav(splash); }
                bool menuUp = (splash != null && splash.playButton != null && splash.playButton.gameObject.activeInHierarchy) || (!Lc.Has("-lcnotoprowfix") && MenuEntry.EntryObject != null && MenuEntry.EntryObject.activeInHierarchy && splash != null && splash.gameObject.activeInHierarchy);
                if (menuUp) RestoreSelection("main menu without selection");
                if (es != null && !es.sendNavigationEvents) { es.sendNavigationEvents = true; _navLocked = false; NavFixes++; Lc.Line("ui: the game's navigation events were off outside the co-op menu, switched back on (" + NavFixes + ")"); }
            }
            catch { }
        }

        private static bool _probed;
        private static void ProbeNav(UIMenuSplashForm splash)
        {
            try
            {
                foreach (var s in splash.GetComponentsInChildren<Selectable>(false))
                {
                    var n = s.navigation;
                    string P(Selectable x) { return x != null ? x.name : "-"; }
                    Lc.Line("menuprobe: " + s.name + " (" + s.GetType().Name + ") interactable=" + s.interactable + " nav=" + n.mode + " up=" + P(n.selectOnUp) + " down=" + P(n.selectOnDown) + " left=" + P(n.selectOnLeft) + " right=" + P(n.selectOnRight) + " pos=" + s.transform.position);
                }
                var es = EventSystem.current; Lc.Line("menuprobe: selected=" + (es != null && es.currentSelectedGameObject != null ? es.currentSelectedGameObject.name : "none"));
            }
            catch (Exception e) { Lc.Line("menuprobe: failed " + e.Message); }
        }

        private static GameObject _prevSel;
        internal static int SelFixes;

        /// <summary>
        /// Hand test: after "LOCAL CO-OP" and BACK the controller reached no button of the main menu any more (only the mouse did). The main menu's
        /// button column is switched off while the co-op menu is open and the selection was cleared, so nothing was selected afterwards. Now the
        /// selection from before is restored (or the PLAY button), right when the co-op menu closes and whenever the main menu shows without a
        /// selection. Twin `-lcnoselrestore`.
        /// </summary>
        internal static void RestoreSelection(string why)
        {
            if (Lc.Has("-lcnoselrestore")) return;
            try
            {
                var es = EventSystem.current; if (es == null) return;
                var cur = es.currentSelectedGameObject;
                if (cur != null && cur.activeInHierarchy) return;
                GameObject t = _prevSel != null && _prevSel.activeInHierarchy ? _prevSel : null;
                if (t == null)
                {
                    var menu = Find.One<UIMenuSplashForm>();
                    if (menu != null && menu.playButton != null && menu.playButton.gameObject.activeInHierarchy) t = menu.playButton.gameObject;
                    // hand test: PLAY is hidden in co-op, but the game keeps selecting it: nothing can be moved from a selected inactive button (twin -lcnotoprowfix)
                    if (t == null && !Lc.Has("-lcnotoprowfix") && MenuEntry.EntryObject != null && MenuEntry.EntryObject.activeInHierarchy) t = MenuEntry.EntryObject;
                }
                if (t == null) return;
                es.SetSelectedGameObject(t); SelFixes++;
                Lc.Line("ui: main menu selection restored (" + why + "): " + t.name);
            }
            catch (Exception e) { Lc.LineOnce("ui: restore selection failed: " + e.GetType().Name + ": " + e.Message); }
        }

        internal static void OnShow()
        {
            if (Lc.Has("-lcuinolock")) return;      // twin: the main menu underneath stays live
            PatchBack();
            try { var es0 = EventSystem.current; if (es0 != null && es0.currentSelectedGameObject != null) _prevSel = es0.currentSelectedGameObject; } catch { }
            LockMainMenu(true);
            try
            {
                var es = EventSystem.current;
                if (es != null)
                {
                    if (!_navLocked) { _navWas = es.sendNavigationEvents; _navLocked = true; }      // a second OnShow must not remember the switched-off state
                    es.sendNavigationEvents = false; es.SetSelectedGameObject(null);
                }
            }
            catch { }
            Focus = ""; Focus2 = "";
            PadUsed = false; _used2 = false;
        }

        internal static void OnHide()
        {
            LockMainMenu(false);
            try { var es = EventSystem.current; if (es != null) es.sendNavigationEvents = _navLocked ? _navWas : true; _navLocked = false; } catch { }
            if (_frame != null) _frame.gameObject.SetActive(false);
            if (_frame2 != null) _frame2.gameObject.SetActive(false);
            Focus = ""; Focus2 = "";
            RestoreSelection("co-op menu closed");
        }

        /// <summary>The main menu's back action (B / Esc) would open the quit dialog: while the co-op menu is open it closes the co-op menu instead.</summary>
        private static void PatchBack()
        {
            if (_patched) return;
            _patched = true;
            try
            {
                var h = new Harmony("localcoop.menupad");
                var orig = AccessTools.Method(typeof(UIMenuSplashForm), "OnBack");
                var pre = new HarmonyMethod(typeof(MenuPad).GetMethod("BackPrefix", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic));
                h.Patch(orig, pre);
                Lc.Line("ui: main menu back handler patched (closes the co-op menu while it is open)");
            }
            catch (Exception e) { Lc.Line("ui: back patch failed: " + e.GetType().Name + ": " + e.Message); }
        }

        private static bool BackPrefix()
        {
            BackHits++;
            if (!CoopMenu.Visible) return true;
            // The game's back action cannot tell which controller pressed B. With two pads it is swallowed (controller 1's B and the Esc key
            // close the menu in Tick), so controller 2's B can never close the menu for both.
            if (!Split) CoopMenu.BackStep();
            return false;
        }

        // test `-lcmenunavtest`: the d-pad walks up to the top row (Discord / Steam / Wiki) and has to come back down
        private static int _nt, _ntOpen; private static float _ntAt = -1f;
        private static readonly GamepadButton[] NtSteps = { GamepadButton.DpadUp, GamepadButton.DpadUp, GamepadButton.DpadRight, GamepadButton.DpadRight, GamepadButton.DpadDown };
        private static void NavTest()
        {
            try
            {
                float now = Time.realtimeSinceStartup;
                var splash = Find.One<UIMenuSplashForm>();
                if (splash == null || splash.playButton == null || (_ntOpen != 1 && (!(splash.playButton.gameObject.activeInHierarchy || (MenuEntry.EntryObject != null && MenuEntry.EntryObject.activeInHierarchy)) || CoopMenu.Visible))) return;
                if (_ntAt < 0f) { _ntAt = now + 4f; return; }
                if (now < _ntAt || _nt > NtSteps.Length) return;
                if (Lc.Has("-lcmenunavtest2") && _ntOpen < 2)
                {
                    // the same walk after the co-op menu was opened and closed once (its lock switches the main menu's buttons off and on)
                    if (_ntOpen == 0) { CoopMenu.Show(); _ntOpen = 1; _ntAt = now + 1.5f; return; }
                    CoopMenu.Hide(); _ntOpen = 2; _ntAt = now + 1.5f; return;
                }
                var es = EventSystem.current;
                string sel = es != null && es.currentSelectedGameObject != null ? es.currentSelectedGameObject.name : "none";
                if (_nt > 0) Lc.Line("navtest: after " + NtSteps[_nt - 1] + " selected=" + sel);
                if (_nt == NtSteps.Length)
                {
                    bool selOk = es != null && es.currentSelectedGameObject != null && es.currentSelectedGameObject.activeInHierarchy && !sel.StartsWith("Button_Cut_Small_");
                    Lc.Line("navtest: CHECK " + (selOk ? "OK  " : "FAIL") + " the pad is not trapped in the top row | selected=" + sel);
                    _nt++; return;
                }
                if (_nt == 4 && Lc.Has("-lcmenunavtest3"))
                {
                    // simulate the trap: the top row's navigation is gone, the game itself can no longer lead away from it
                    if (es != null) es.sendNavigationEvents = false;
                    Lc.Line("navtest: the game's navigation events switched off, selected=" + sel);
                }
                VirtualPad.Press(NtSteps[_nt]); _nt++; _ntAt = now + 0.8f;
            }
            catch (Exception e) { Lc.LineOnce("navtest threw " + e.Message); _nt = 99; }
        }

        internal static int TopRowFixes;

        /// <summary>Hand test: once the controller stood on Discord / Steam / Wiki (the small buttons at the top of the main menu) it could not get away any more.
        /// Whatever the game does, Down / left stick down / B take the selection back to the PLAY button from there. Twin `-lcnotoprowfix`.</summary>
        private static void TopRowEscape()
        {
            if (Lc.Has("-lcnotoprowfix") || CoopMenu.Visible) return;
            var es = EventSystem.current; if (es == null) return;
            var cur = es.currentSelectedGameObject;
            if (cur == null || !cur.name.StartsWith("Button_Cut_Small_")) return;
            bool down = false;
            foreach (var g in Gamepad.all)
                if (g != null && (g.dpad.down.wasPressedThisFrame || g.leftStick.ReadValue().y < -0.6f)) down = true;
            if (Keyboard.current != null && (Keyboard.current.downArrowKey.wasPressedThisFrame || Keyboard.current.sKey.wasPressedThisFrame)) down = true;
            if (!down) return;
            var splash = Find.One<UIMenuSplashForm>();
            // the game's PLAY button is hidden in co-op (MenuEntry): the top row's own "down" points at it, so the way out is the co-op entry then
            GameObject target = splash != null && splash.playButton != null && splash.playButton.gameObject.activeInHierarchy ? splash.playButton.gameObject : MenuEntry.EntryObject;
            if (target == null || !target.activeInHierarchy) return;
            es.SetSelectedGameObject(target); TopRowFixes++;
            Lc.Line("ui: the pad left the top row (" + cur.name + ") for " + target.name + " (" + TopRowFixes + ")");
        }

        internal static int SubmitClears;
        internal static void Tick()
        {
            try { TopRowEscape(); } catch (Exception e) { Lc.LineOnce("ui: top row escape threw " + e.GetType().Name + ": " + e.Message); }
            if (Lc.Has("-lcmenunavtest")) NavTest();
            if (!CoopMenu.Visible) return;
            try
            {
                // hand test: A pressed CHANGE (or another button) although the pad's own code pressed something else: the game's UI also takes A as "submit" for the button the
                // event system has selected (the last one clicked or focused). While the co-op menu is open nothing of it stays selected. Twin `-lcnosubmitfix`.
                if (!Lc.Has("-lcnosubmitfix")) { var es = UnityEngine.EventSystems.EventSystem.current; var sel = es != null ? es.currentSelectedGameObject : null; var rt = CoopMenu.RootTransform; if (sel != null && rt != null && sel.transform.IsChildOf(rt)) { es.SetSelectedGameObject(null); SubmitClears++; } }
                var pads = Gamepad.all;
                bool split = Split;
                if (split && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) { CoopMenu.Hide(); return; }
                var p2 = split ? VirtualPad.P2Device() : null;
                for (int i = 0; pads != null && i < pads.Count; i++)
                {
                    var g = pads[i];
                    bool isP2 = split && p2 != null && g.deviceId == p2.deviceId;
                    Read(g, isP2 ? 1 : 0, split);
                }
                if (!Lc.Has("-lcnopreselect") && pads != null && pads.Count > 0) { PreSelect(0, split); if (split) PreSelect(1, split); }
                { string fk = Lc.Str("-lcfocuskey="); Button fb; if (fk.Length > 0 && CoopMenu.Buttons.TryGetValue(fk, out fb) && fb != null && fb.gameObject.activeInHierarchy) { Focus = fk; PadUsed = true; } }      // test only: show the focus frame on one chosen button
                Place();
                if (!Lc.Has("-lcnosectorinfo")) CoopMenu.SectorInfoTick(Focus);
            }
            catch (Exception e) { Lc.LineOnce("ui: pad tick failed: " + e.GetType().Name + ": " + e.Message + " @ " + (e.StackTrace ?? "").Split((char)10)[0].Trim()); }
        }

        /// <summary>Hand test: the focus appeared only after the first stick press. When a page opens the choice that is already selected is focused right away. Twin `-lcnopreselect`.</summary>
        private static void PreSelect(int role, bool split)
        {
            if (!string.IsNullOrEmpty(FocusOf(role)) || CoopMenu.Mode == "final") return;
            var all = Candidates(role, split); string d = DefaultFocus(role);
            if (d.Length > 0 && all.Contains(d)) { SetFocus(role, d); Used(role); Preselects++; }
        }
        internal static int Preselects;

        private static string FocusOf(int role) { return role == 1 ? Focus2 : Focus; }
        private static void SetFocus(int role, string k) { if (role == 1) Focus2 = k; else Focus = k; }

        /// <summary>Where the focus starts on a page: the choice that is already selected, or (character pages) what the player most likely does next.</summary>
        private static string DefaultFocus(int role)
        {
            string mode = CoopMenu.Mode;
            if (mode == "pick")
            {
                int side = role == 1 ? 1 : 0; string tag = side == 0 ? "P1" : "P2";
                if (CoopMenu.Confirmed[side]) return CoopMenu.PadHidden(tag + "edit") ? "" : tag + "edit";
                if (CoopMenu.Stage[side] == "s")
                {
                    for (int i = 0; i < 3; i++) if (CoopMenu.Marked(tag + "s" + i)) return tag + "s" + i;      // the selected subclass
                    return tag + "s0";
                }
                return tag + "c" + (side == 0 ? CoopMenu.P1.ClassIndex : CoopMenu.P2.ClassIndex);
            }
            if (mode == "biome") return "bio" + CoopMenu.BiomeIndex;
            if (mode == "danger") return "ddng" + CoopMenu.Danger;
            if (mode == "final") return "";          // nothing is focused: A must not press CHANGE by accident, the menu button does the going on
            return mode == "lobby" ? "type0" : mode == "sector" ? "sec0" : role == 1 ? "P2class" + CoopMenu.P2.ClassIndex : "P1class" + CoopMenu.P1.ClassIndex;
        }

        /// <summary>What the menu button does on the current page (the go-on buttons are not focused by the pad any more): the key of the button to press, or "".</summary>
        private static string StartKey(int role, bool split)
        {
            string mode = CoopMenu.Mode;
            if (mode == "biome") return "bnext";
            if (mode == "danger") return "confirm";
            if (mode == "final") return role == 1 ? "P2ready" : "start";
            if (mode != "pick") return "";
            // with two pads each presses its own side; a single pad takes the first side that still has something to do
            int side = split ? (role == 1 ? 1 : 0) : (CoopMenu.Confirmed[0] ? 1 : 0);
            if (CoopMenu.Confirmed[side]) return "";
            return (side == 0 ? "P1" : "P2") + (CoopMenu.Stage[side] == "s" ? "ok" : "next");
        }

        /// <summary>Where B goes: on the subclass page of a player back to the class page of that player, else one page back (controller 1 only).</summary>
        private static void BackPressed(int role, bool split)
        {
            if (CoopMenu.Mode == "pick")
            {
                int side = split ? (role == 1 ? 1 : 0) : (CoopMenu.Confirmed[0] ? 1 : 0);
                string tag = side == 0 ? "P1" : "P2";
                if (CoopMenu.Confirmed[side]) { Press(tag + "edit", role, split); return; }
                if (CoopMenu.Stage[side] == "s") { Press(tag + "back", role, split); return; }
            }
            if (role == 0 || !split) CoopMenu.BackStep();
        }

        /// <summary>
        /// The focus must never point at a button that is gone: after CONTINUE (or CONFIRM, CHANGE) the old button is hidden and A pressed nothing until the
        /// player had moved the stick or clicked something (hand test: "CONFIRM only works after the subclass is clicked again"). Whenever the focused
        /// button is not usable any more the focus moves to the default of the page. Twin `-lcnofocusfix`.
        /// </summary>
        private static void FixFocus(int role, bool split)
        {
            if (Lc.Has("-lcnofocusfix")) return;
            string f = FocusOf(role);
            if (string.IsNullOrEmpty(f)) return;
            Button b;
            if (CoopMenu.Buttons.TryGetValue(f, out b) && b != null && b.interactable && b.gameObject.activeInHierarchy) return;
            var all = Candidates(role, split);
            if (all.Count == 0) return;
            string d = DefaultFocus(role);
            SetFocus(role, d.Length > 0 && CoopMenu.Buttons.ContainsKey(d) && all.Contains(d) ? d : (CoopMenu.Mode == "final" ? "" : all[0]));
            FocusFixes++;
        }

        internal static int FocusFixes;

        private static void Read(Gamepad p, int role, bool split)
        {
            if (p == null) return;
            FixFocus(role, split);
            Vector2 dir = Vector2.zero;
            if (p.dpad.up.wasPressedThisFrame) dir = Vector2.up;
            else if (p.dpad.down.wasPressedThisFrame) dir = Vector2.down;
            else if (p.dpad.left.wasPressedThisFrame) dir = Vector2.left;
            else if (p.dpad.right.wasPressedThisFrame) dir = Vector2.right;
            else
            {
                var s = p.leftStick.ReadValue();
                if (s.magnitude > 0.6f && Time.realtimeSinceStartup >= _nextMoveAt[role])
                    dir = Mathf.Abs(s.x) > Mathf.Abs(s.y) ? (s.x > 0 ? Vector2.right : Vector2.left) : (s.y > 0 ? Vector2.up : Vector2.down);
            }
            if (dir != Vector2.zero) { _nextMoveAt[role] = Time.realtimeSinceStartup + 0.22f; Used(role); Move(dir, role, split); }

            if (p.buttonSouth.wasPressedThisFrame) { Used(role); Presses++; SouthPressed(role, split); }
            if (p.buttonWest.wasPressedThisFrame && !Lc.Has("-lceditona")) { Used(role); Presses++; EditPressed(role, split); }
            if (p.buttonNorth.wasPressedThisFrame && CoopMenu.Mode == "final" && !Lc.Has("-lcrevivebutton")) { Used(role); Presses++; Press("revive", role, split); }      // Y: the revive switch
            if (p.buttonEast.wasPressedThisFrame) { Used(role); Presses++; BackPressed(role, split); }
            // hand test: go on with A everywhere, the menu button only starts the run (host)
            if (p.startButton.wasPressedThisFrame) { Used(role); Presses++; string sk = Lc.Has("-lcstartgoeson") ? StartKey(role, split) : (CoopMenu.Mode == "final" && role == 0 ? "start" : ""); Lc.Line("ui: menu button on pad " + p.deviceId + " role " + role + " -> " + (sk.Length > 0 ? sk : "nothing") + " (mode " + CoopMenu.Mode + ")"); Press(sk, role, split, true); }
        }

        private static readonly System.Text.RegularExpressions.Regex ChoiceKey = new System.Text.RegularExpressions.Regex(@"^(bio\d+|ddng\d+|P[12][cs]\d+)$");

        /// <summary>Biome, danger level, class and subclass are chosen by moving onto them; nothing has to be confirmed with A. Twin `-lcnoautoselect`.</summary>
        internal static bool IsChoiceKey(string k) { return !Lc.Has("-lcnoautoselect") && !string.IsNullOrEmpty(k) && ChoiceKey.IsMatch(k); }

        internal static int AutoSelects;

        internal static void FocusMoved(int role) { AutoSelect(role, Split); }

        private static void AutoSelect(int role, bool split)
        {
            string k = FocusOf(role);
            if (!IsChoiceKey(k) || CoopMenu.IsSelected(k)) return;
            Press(k, role, split); AutoSelects++;
        }

        /// <summary>A: on a choice that is already the selected one it goes on (the menu button's job), CHANGE is A on a confirmed page, anything else is pressed as focused.</summary>
        private static void SouthPressed(int role, bool split)
        {
            string f = FocusOf(role) ?? "";
            string mode = CoopMenu.Mode;
            if (split && !Lc.Has("-lcnoeditpad") && Lc.Has("-lceditona"))
            {
                int side = role == 1 ? 1 : 0; string tag = side == 0 ? "P1" : "P2";
                if (mode == "pick" && CoopMenu.Confirmed[side]) { Press(tag + "edit", role, split); return; }
                if (mode == "final") { Press(tag + "fedit", role, split); return; }
            }
            // hand test: A on CHANGE went back into the class choice by accident; CHANGE is X
            if (!Lc.Has("-lceditona") && !Lc.Has("-lcnoeditpad") && f.EndsWith("edit")) return;
            // hand test: A goes on everywhere (biome, danger, class, subclass, ready); the menu button only starts the run (host)
            string go = Lc.Has("-lcstartgoeson") ? StartKey(role, split) : (mode == "final" && role != 0 ? "P2ready" : (mode == "final" ? "" : StartKey(role, split)));
            if (go.Length > 0 && (f.Length == 0 || ((IsChoiceKey(f) || CoopMenu.PadHidden(f)) && Allowed(f, role, split)))) { Press(go, role, split, true); return; }
            if (IsChoiceKey(f) && CoopMenu.IsSelected(f) && Allowed(f, role, split))
            {
                string sk = StartKey(role, split);
                if (sk.Length > 0 && sk != "start") { Press(sk, role, split, true); return; }
            }
            Press(f, role, split);
        }

        /// <summary>X: CHANGE on a confirmed page and on the final page (hand test: it was A, and A also confirms, so a double press jumped back into the class choice).</summary>
        private static void EditPressed(int role, bool split)
        {
            if (!split || Lc.Has("-lcnoeditpad")) return;
            int side = role == 1 ? 1 : 0; string tag = side == 0 ? "P1" : "P2"; string mode = CoopMenu.Mode;
            if (mode == "pick" && CoopMenu.Confirmed[side]) Press(tag + "edit", role, split);
            else if (mode == "final") Press(tag + "fedit", role, split);
        }

        private static void Used(int role) { if (role == 1) _used2 = true; else PadUsed = true; }

        /// <summary>Which choices a controller may reach: player 2's controller only player 2's class and subclass; with two pads the first
        /// controller everything but player 2's panel; a single pad everything.</summary>
        private static bool Allowed(string key, int role, bool split)
        {
            if (!split) return true;
            bool p2key = key.StartsWith("P2");
            return role == 1 ? p2key : !key.StartsWith("P2");
        }

        private static void Press(string key, int role, bool split, bool byHint = false)
        {
            Button b;
            if (string.IsNullOrEmpty(key) || !Allowed(key, role, split) || !CoopMenu.Buttons.TryGetValue(key, out b) || b == null) return;
            if (b.interactable && b.gameObject.activeInHierarchy) b.onClick.Invoke();
        }

        private static List<string> Candidates(int role, bool split)
        {
            var l = new List<string>();
            foreach (var kv in CoopMenu.Buttons)
                if (kv.Value != null && kv.Value.interactable && kv.Value.gameObject.activeInHierarchy && !CoopMenu.PadHidden(kv.Key) && Allowed(kv.Key, role, split)) l.Add(kv.Key);
            return l;
        }

        private static void Move(Vector2 dir, int role, bool split)
        {
            var all = Candidates(role, split);
            if (all.Count == 0) return;
            string focus = FocusOf(role);
            if (focus == "" || !all.Contains(focus))
            {
                string first = DefaultFocus(role);
                SetFocus(role, CoopMenu.Buttons.ContainsKey(first) && all.Contains(first) ? first : all[0]);
                return;
            }
            Vector2 from = CoopMenu.RefCenter(CoopMenu.Buttons[focus]);
            string best = null; float bestScore = float.MaxValue;
            foreach (var k in all)
            {
                if (k == focus) continue;
                Vector2 d = CoopMenu.RefCenter(CoopMenu.Buttons[k]) - from;
                float along = Vector2.Dot(d, dir);
                if (along <= 1f) continue;
                float across = Mathf.Abs(Vector2.Dot(d, new Vector2(-dir.y, dir.x)));
                float score = along + 2.5f * across;
                if (score < bestScore) { bestScore = score; best = k; }
            }
            if (best != null) { SetFocus(role, best); AutoSelect(role, split); }
        }

        private static void Place()
        {
            ZoomCards();
            PlaceOne(ref _frame, Focus, PadUsed, Color.white);
            PlaceOne(ref _frame2, Focus2, _used2, CoopMenu.BlueP2);
        }

        /// <summary>The focused mission type card is shown bigger (both pads: each card its own).</summary>
        private static void ZoomCards()
        {
            if (Lc.Has("-lcnozoom")) return;
            for (int i = 0; i < 2; i++)
            {
                Button b; string k = "type" + i;
                if (!CoopMenu.Buttons.TryGetValue(k, out b) || b == null) continue;
                bool on = CoopMenu.Visible && ((PadUsed && Focus == k) || (_used2 && Focus2 == k));
                var t = b.transform; float want = on ? 1.05f : 1f;
                if (Mathf.Abs(t.localScale.x - want) > 0.001f) t.localScale = new Vector3(want, want, 1f);
                CoopMenu.CardBrackets(t, on && !Lc.Has("-lcnocardbrackets"), !(PadUsed && Focus == k));
                var zg = t.parent != null ? t.parent.Find("ZGlow" + i) : null; if (zg != null) { if (zg.gameObject.activeSelf) zg.gameObject.SetActive(false); var zi = zg.GetComponent<Image>(); bool p2 = _used2 && Focus2 == k && !(PadUsed && Focus == k); if (zi != null) zi.color = p2 ? new Color(CoopMenu.BlueP2.r, CoopMenu.BlueP2.g, CoopMenu.BlueP2.b, 0.34f) : new Color(0.95f, 0.22f, 0.18f, 0.34f); }      // red for player 1, blue for player 2
            }
        }

        private static void PlaceOne(ref RectTransform frame, string focus, bool used, Color color)
        {
            if (!used || string.IsNullOrEmpty(focus) || !CoopMenu.Buttons.ContainsKey(focus) || (!Lc.Has("-lcstaleframe") && !CoopMenu.Buttons[focus].gameObject.activeInHierarchy)) { if (frame != null) frame.gameObject.SetActive(false); return; }      // hand test: after CONFIRM the frame stayed as an empty white box over the hidden button it was on (twin `-lcstaleframe`)
            if (!Lc.Has("-lcnozoom") && !Lc.Has("-lcnocardbrackets") && focus.StartsWith("type")) { if (frame != null) frame.gameObject.SetActive(false); return; }      // the mission cards carry their own brackets (ZoomCards)
            if (frame == null) frame = CoopMenu.MakeFrame(color);
            if (frame == null) return;
            frame.gameObject.SetActive(true);
            try { CoopMenu.PlaceFrame(frame, CoopMenu.Buttons[focus], focus, color); } catch (Exception e) { Lc.LineOnce("ui: place frame threw for '" + focus + "' active=" + CoopMenu.Buttons[focus].gameObject.activeInHierarchy + ": " + e.Message + " " + string.Join(" ", (e.StackTrace ?? "").Split((char)10))); }
        }
    }
}
