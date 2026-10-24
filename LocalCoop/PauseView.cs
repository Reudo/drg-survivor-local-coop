using System;
using DRS.UI;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace LocalCoop
{
    /// <summary>
    /// The pause menu (UICorePauseForm) shows one dwarf, whatever `player` it was injected with (the host): weapons, artifacts and stats are built from
    /// that field when the form is shown. Now it shows the dwarf whose controller opened it (player 2's Start button -> player 2), and a switch inside the
    /// open menu (Y on a controller, Tab on the keyboard) changes to the other dwarf. A line on the form says whose view it is. Twin `-lcnopauseview`.
    /// The field is set back to the host when the menu closes.
    /// </summary>
    internal static class PauseView
    {
        internal static bool ViewP2;
        internal static int Swaps;
        private static TextMeshProUGUI _banner;

        private static bool P2Pressed()
        {
            try { var d = VirtualPad.P2Device() as Gamepad; return d != null && (d.startButton.wasPressedThisFrame || d.selectButton.wasPressedThisFrame); }
            catch { return false; }
        }

        internal static int Opener = 1;                 // 1 = the host's controller opened the menu, 2 = player 2's: only that one drives the menu (see Chooser)
        private static bool _p2Request;
        private static bool _swapped;
        private static float _runsAt = -1f;
        private static ClassData _hostClass; private static ClassArtifactData _hostMod;

        private static void Point(UICorePauseForm f, bool p2)
        {
            var who = p2 ? Phase1.Second : Phase1.First();
            if (who == null) return;
            Phase1.SetRef(f, typeof(UICorePauseForm), "player", who.Pointer);
            if (Lc.Has("-lcnopausestats")) return;
            ClassView(p2);
            // hand test: the statistics and the class always showed the host. The stat lines were bound to the host's stats once, when the form started,
            // and the class comes from the run settings (the host's choice).
            try
            {
                var l = f.uiStats;
                for (int i = 0; l != null && i < l.Count; i++) { var st = l[i]; if (st != null && st.statSetting != null) st.Setup(st.statSetting, who); }
            }
            catch (Exception e) { Lc.LineOnce("pauseview: stats threw " + e.GetType().Name + ": " + e.Message); }
        }

        /// <summary>The class shown in the pause menu is the run settings' current class and class mod: player 2's while its view is on, the host's otherwise.</summary>
        private static void ClassView(bool p2)
        {
            try
            {
                var rsm = CoopStart.Rsm(); if (rsm == null) return;
                if (p2)
                {
                    if (!_swapped) { _hostClass = rsm.selectedClass; _hostMod = rsm.selectedClassMod; _swapped = true; }
                    var c = Phase1.Second;
                    if (c != null && c.CurrentClass != null) rsm.selectedClass = c.CurrentClass;
                    if (CoopMenu.P2.Sub != null) rsm.selectedClassMod = CoopMenu.P2.Sub;
                }
                else if (_swapped) { rsm.selectedClass = _hostClass; rsm.selectedClassMod = _hostMod; _swapped = false; }
            }
            catch (Exception e) { Lc.LineOnce("pauseview: class view threw " + e.GetType().Name + ": " + e.Message); }
        }

        internal static int GoalFixes, BackPauseBlocked;

        /// <summary>Hand test: the mission goals of the biome were missing in player 2's pause menu. The game fills them when its biome button gets selected; that button was
        /// still selected from the last opening, so no new selection event came. Whenever the goals are not there after Show, the game's own handler is called. Twin `-lcnopausegoals`.</summary>
        private static void Goals(UICorePauseForm f)
        {
            if (Lc.Has("-lcnopausegoals")) return;
            try
            {
                var g = f.biomeGoalGroup;
                if (g == null || g.activeSelf) return;
                var m = AccessTools.Method(typeof(UICorePauseForm), "OnBiomeSelect");
                if (m != null) { m.Invoke(f, null); GoalFixes++; }
            }
            catch (Exception e) { Lc.LineOnce("pauseview: goals threw " + e.GetType().Name + ": " + e.Message); }
        }

        internal static bool GoalsShown(UICorePauseForm f)
        {
            try
            {
                if (f.biomeGoalGroup == null || !f.biomeGoalGroup.activeSelf) return false;
                var l = f.biomeGoalMilestoneProgress; int n = 0;
                for (int i = 0; l != null && i < l.Length; i++) if (l[i] != null && l[i].gameObject.activeInHierarchy) n++;
                return n > 0;
            }
            catch { return false; }
        }

        private static TextMeshProUGUI _title;
        internal static string TitleInfo = "";

        /// <summary>The big "GAME PAUSED" text of the form: the largest text whose words contain PAUS (any language).</summary>
        private static TextMeshProUGUI FindTitle(UICorePauseForm f)
        {
            if (_title != null && _title.transform.IsChildOf(f.transform)) return _title;
            TextMeshProUGUI best = null;
            foreach (var t in f.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if (t == null || t == _banner) continue;
                string s = t.text ?? ""; if (s.ToUpperInvariant().IndexOf("PAUS", StringComparison.Ordinal) < 0) continue;
                if (best == null || t.fontSize > best.fontSize) best = t;
            }
            _title = best;
            return best;
        }

        /// <summary>Hand test: the screen shows "PLAYER n" above the game's own title, and the title ("GAME PAUSED") in that player's colour.</summary>
        private static void Banner(UICorePauseForm f)
        {
            try
            {
                if (_banner == null)
                {
                    var go = new GameObject("LcPauseView"); var rt = go.AddComponent<RectTransform>(); rt.SetParent(f.transform, false);
                    rt.sizeDelta = new Vector2(900f, 54f);
                    _banner = go.AddComponent<TextMeshProUGUI>();
                    var font = CoopMenu.FontAsset != null ? CoopMenu.FontAsset : TMP_Settings.defaultFontAsset; if (font != null) _banner.font = font;
                    _banner.alignment = TextAlignmentOptions.Midline; _banner.raycastTarget = false; _banner.fontSize = 34f; _banner.enableWordWrapping = false;
                    _banner.outlineWidth = 0.1f; _banner.outlineColor = new Color32(0, 0, 0, 255);
                }
                var col = ViewP2 ? Chooser.Blue : Chooser.Red;
                var title = FindTitle(f);
                var brt = _banner.rectTransform;
                if (title != null)
                {
                    var trt = title.rectTransform;
                    brt.SetParent(trt.parent, false);
                    brt.anchorMin = trt.anchorMin; brt.anchorMax = trt.anchorMax; brt.pivot = new Vector2(0.5f, 0f);
                    brt.sizeDelta = new Vector2(Mathf.Max(600f, trt.rect.width), 54f);
                    brt.anchoredPosition = trt.anchoredPosition + new Vector2(0f, trt.rect.height * (1f - trt.pivot.y) + 2f - 8f);
                    title.color = col;
                    TitleInfo = "title '" + title.text + "' size " + title.fontSize;
                }
                else
                {
                    brt.SetParent(f.transform, false);
                    brt.anchorMin = new Vector2(0.5f, 1f); brt.anchorMax = new Vector2(0.5f, 1f); brt.pivot = new Vector2(0.5f, 1f); brt.anchoredPosition = new Vector2(0f, -18f);
                    TitleInfo = "no title found";
                }
                _banner.color = col;
                _banner.text = Loc.T(ViewP2 ? "PLAYER 2" : "PLAYER 1");
                _banner.gameObject.SetActive(true);
                Lc.LineOnce("pauseview: banner for player " + (ViewP2 ? 2 : 1) + ", " + TitleInfo);
            }
            catch (Exception e) { Lc.LineOnce("pauseview: banner threw " + e.GetType().Name + ": " + e.Message); }
        }

        [HarmonyPatch(typeof(UICorePauseForm), "Show")]
        internal static class ShowPatch
        {
            private static bool Prefix(UICorePauseForm __instance)
            {
                try
                {
                    if (Lc.Has("-lcnopauseview") || Phase1.Second == null) return true;
                    // hand test: B (the game's back action) opened player 1's pause menu on every pad, outside any level-up or shop. The pause menu opens with Start only; B still
                    // closes it and works inside the level-up / shop screens (Who != 0: their Details window). Twin `-lcbackpause`.
                    if (Chooser.InBack && Chooser.Who == 0 && !Lc.Has("-lcbackpause") && Lc.InRunAt >= 0f) { BackPauseBlocked++; return false; }
                    // hand test: the menu showed player 2 every time once it had been opened for player 2 (the flag was only ever set). It is decided afresh at every opening.
                    bool prevView = ViewP2;
                    ViewP2 = _p2Request || P2Pressed() || LevelRounds.P2Round || (prevView && Lc.Has("-lcstickyp2"));
                    _p2Request = false;
                    Opener = ViewP2 ? 2 : 1;
                    Point(__instance, ViewP2);
                }
                catch (Exception e) { Lc.LineOnce("pauseview: show prefix threw " + e.GetType().Name + ": " + e.Message); }
                return true;
            }

            private static void Postfix(UICorePauseForm __instance)
            {
                if (Lc.Has("-lcnopauseview") || Phase1.Second == null) return;
                Banner(__instance);
                Goals(__instance);
            }
        }

        // the back button may close the form through SetVisibility(false) without Hide: the view is given back to the host there too (twin -lcnopausevis)
        [HarmonyPatch(typeof(UICorePauseForm), "SetVisibility")]
        internal static class VisibilityPatch
        {
            private static void Postfix(UICorePauseForm __instance, bool visible)
            {
                try
                {
                    if (visible || Lc.Has("-lcnopausevis") || Lc.Has("-lcnopauseview") || Phase1.Second == null) return;
                    ViewP2 = false; Point(__instance, false);
                    if (_banner != null) _banner.gameObject.SetActive(false);
                }
                catch { }
            }
        }

        [HarmonyPatch(typeof(UICorePauseForm), "Hide")]
        internal static class HidePatch
        {
            private static void Postfix(UICorePauseForm __instance)
            {
                try
                {
                    if (Lc.Has("-lcnopauseview") || Phase1.Second == null) return;
                    ViewP2 = false; Point(__instance, false);
                    if (_banner != null) _banner.gameObject.SetActive(false);
                }
                catch { }
            }
        }

        // No switching inside the open menu any more (hand test: switching with Y two or three times left the menu unclosable, and then the game ran on
        // behind the open menu): each pause button opens the menu of its own dwarf, and Show() runs once per opening.
        /// <summary>True while the pause menu shows (the Chooser pairs the game's UI with the opener's controller then).</summary>
        internal static bool IsOpen { get { try { return !Lc.Has("-lcnopauseview") && Phase1.Second != null && Open(_form); } catch { return false; } } }

        private static UICorePauseForm _form;

        private static UICorePauseForm FormObj()
        {
            foreach (var f in Resources.FindObjectsOfTypeAll<UICorePauseForm>()) if (f != null && f.gameObject.scene.IsValid()) return f;
            return null;
        }

        internal static int P2Opens;

        /// <summary>The game's pause action only listens to the host's controller: player 2's Start button opens the pause menu (for player 2) here.</summary>
        private static void OpenForP2()
        {
            var d = VirtualPad.P2Device() as Gamepad;
            if (d != null && d.startButton.wasPressedThisFrame && (Lc.InRunAt < 0f || Time.timeScale < 0.5f)) Lc.Line("pauseview: player 2's Start ignored, inRunAt=" + Lc.InRunAt + " timeScale=" + Time.timeScale);
            if (Lc.Has("-lcnop2pause") || Lc.InRunAt < 0f || Time.timeScale < 0.5f) return;
            if (d == null || !d.startButton.wasPressedThisFrame) return;
            _p2Request = true; P2Opens++;
            var ui = Find.One<UIManager>();
            if (ui != null) ui.TryShowCorePauseForm();
            _p2Request = false;
        }

        private static bool Open(UICorePauseForm f) { try { return f != null && f.gameObject.activeInHierarchy && _banner != null && _banner.gameObject.activeSelf; } catch { return false; } }

        internal static void Tick()
        {
            try
            {
                Test();
                if (Lc.Has("-lcnopauseview") || Phase1.Second == null) return;
                if (_form == null) _form = FormObj();
                { var sd = VirtualPad.P2Device() as Gamepad; if (sd != null && sd.startButton.wasPressedThisFrame) Lc.Line("pauseview: player 2's Start seen, menu open=" + Open(_form) + " form active=" + (_form != null && _form.gameObject.activeInHierarchy) + " banner=" + (_banner != null && _banner.gameObject.activeSelf)); }
                if (!Open(_form)) { _runsAt = -1f; OpenForP2(); return; }
                // diagnosis for the hand test: the menu is open but the game clock runs
                if (Time.timeScale > 0.5f)
                {
                    float now = Time.realtimeSinceStartup;
                    if (_runsAt < 0f) _runsAt = now;
                    else if (now - _runsAt > 1.5f) { _runsAt = now + 30f; var gc = Find.One<GameController>(); Lc.Line("pauseview: WARNING the menu is open but the game runs (timeScale " + Time.timeScale + ", pauseCount " + (gc != null ? gc.pauseCount : -1) + ")"); }
                }
                else _runsAt = -1f;
            }
            catch (Exception e) { Lc.LineOnce("pauseview: tick threw " + e.GetType().Name + ": " + e.Message); }
        }

        // ---- test (`-lcpausetest`, twin `-lcnopauseview`) ---------------------------------------------------------------------
        private static int _stage, _ok, _bad;
        private static bool _pressed, _stk, _sh1, _sh2, _b0; private static int _bp0; private static int _waits;
        private static float _at = -1f;
        internal static bool TestDone { get { return _stage >= 6; } }
        private static void Check(string name, bool ok, string info) { if (ok) _ok++; else _bad++; Lc.Line("pauseview: CHECK " + (ok ? "OK   " : "FAIL ") + name + " | " + info); }

        private static long _statPtr1, _classData1; private static int _pc1;
        private static long StatPtr(UICorePauseForm f) { try { var l = f.uiStats; return l != null && l.Count > 0 && l[0] != null && l[0].stat != null ? l[0].stat.Pointer.ToInt64() : 0L; } catch { return 0L; } }
        private static long ClassDataPtr(UICorePauseForm f) { try { return f.classArtifactButton != null && f.classArtifactButton.data != null ? f.classArtifactButton.data.Pointer.ToInt64() : 0L; } catch { return 0L; } }

        private static bool TitleIs(Color c) { try { return _title != null && Mathf.Abs(_title.color.r - c.r) < 0.02f && Mathf.Abs(_title.color.g - c.g) < 0.02f && Mathf.Abs(_title.color.b - c.b) < 0.02f; } catch { return false; } }

        private static string Weapons(UICorePauseForm f)
        {
            var sb = new System.Text.StringBuilder();
            try { for (int i = 0; f.weapons != null && i < f.weapons.Count; i++) { var w = f.weapons[i]; if (w != null && w.weaponHandler != null && w.weaponHandler.data != null) sb.Append(w.weaponHandler.data.name).Append(','); } } catch { }
            return sb.ToString();
        }

        private static void Test()
        {
            if (!Lc.Has("-lcpausetest") || Phase1.Second == null || _stage >= 6 || Lc.InRunAt < 0f) return;
            float now = Time.realtimeSinceStartup;
            try
            {
                if (_at < 0f) { _at = now + 8f; return; }
                if (now < _at) return;
                var f = FormObj(); var host = Phase1.First(); var clone = Phase1.Second;
                if (f == null || host == null) { Check("the pause form exists", false, "form=" + (f != null)); _stage = 6; return; }
                string hw = host.weapons.Count > 0 ? host.weapons[0].data.name : "?", cw = clone.weapons.Count > 0 ? clone.weapons[0].data.name : "?";
                switch (_stage)
                {
                    case 0:
                        if (VirtualPad.P2Device() != null)
                        {
                            if (!_b0) { _b0 = true; _bp0 = BackPauseBlocked; VirtualPad.Press1(GamepadButton.East); _at = now + 1.0f; break; }
                            Check("B alone (outside level-up and shop) does not open the pause menu", !Open(f) && !f.gameObject.activeInHierarchy, "open=" + Open(f) + " blocked " + _bp0 + " -> " + BackPauseBlocked);
                        }
                        ViewP2 = false; f.Show(); _stage = 1; _at = now + 1.2f; break;
                    case 1:
                        if (!_sh1) { Shots.Take("pause-p1"); _sh1 = true; _at = now + 0.8f; break; }
                        Check("the pause menu opened for player 1 shows player 1's weapon", Weapons(f).Contains(hw), "weapons=" + Weapons(f) + " host=" + hw + " clone=" + cw);
                        _statPtr1 = StatPtr(f); _classData1 = ClassDataPtr(f);
                        { var gc0 = Find.One<GameController>(); _pc1 = gc0 != null ? gc0.pauseCount : -1; }
                        if (VirtualPad.P2Device() != null) Check("while the menu is open only its opener's controller drives the game's UI (the other pad is kept away)", Chooser.Who == Opener && Chooser.Locked > 0, "chooser who=" + Chooser.Who + " opener=" + Opener + " locked=" + Chooser.Locked + " kind=" + Chooser.Kind);
                        Check("the menu of player 1 has 'PLAYER 1' above the title and the title in player 1's colour", TitleIs(Chooser.Red) && _banner != null && _banner.text == Loc.T("PLAYER 1"), "banner=" + (_banner != null ? _banner.text : "-") + " " + TitleInfo + " titleColor=" + (_title != null ? _title.color.ToString() : "-"));
                        Check("the chooser's corner banner ('PLAYER n - PAUSE') is not shown for the pause menu", !Chooser.PanelShown, "panelShown=" + Chooser.PanelShown);
                        Check("the mission goals of the biome are in the menu of player 1", GoalsShown(f), "goalsShown=" + GoalsShown(f) + " fixes=" + GoalFixes);
                        f.Hide(); _p2Request = true; f.Show(); _stage = 2; _at = now + 1.2f; break;       // player 2's own pause button, in a second opening
                    case 2:
                        if (!_sh2) { Shots.Take("pause-p2"); _sh2 = true; _at = now + 0.8f; break; }
                        Check("the menu opened for player 2 shows player 2's weapon", Weapons(f).Contains(cw) && !Weapons(f).Contains(hw), "weapons=" + Weapons(f) + " host=" + hw + " clone=" + cw + " banner=" + (_banner != null ? _banner.text : "-"));
                        Check("the menu of player 2 has 'PLAYER 2' above the title and the title in player 2's colour", TitleIs(Chooser.Blue) && _banner != null && _banner.text == Loc.T("PLAYER 2"), "banner=" + (_banner != null ? _banner.text : "-") + " " + TitleInfo + " titleColor=" + (_title != null ? _title.color.ToString() : "-"));
                        { long sp = StatPtr(f); Check("the statistics are player 2's, not the host's", sp != 0 && sp != _statPtr1, "stat object " + _statPtr1 + " -> " + sp); }
                        Check("the mission goals of the biome are in the menu of player 2", GoalsShown(f), "goalsShown=" + GoalsShown(f) + " fixes=" + GoalFixes);
                        if (CoopMenu.P2.Sub != null && CoopMenu.P2.Sub.Pointer.ToInt64() != _classData1 && _classData1 != 0)
                            Check("the class shown is player 2's", ClassDataPtr(f) == CoopMenu.P2.Sub.Pointer.ToInt64(), "class mod " + _classData1 + " -> " + ClassDataPtr(f) + " wanted " + CoopMenu.P2.Sub.Pointer.ToInt64());
                        { var gc1 = Find.One<GameController>(); Check("closing and opening again adds no pause level (one B closes the menu)", gc1 != null && _pc1 >= 0 && gc1.pauseCount == _pc1, "pauseCount " + _pc1 + " -> " + (gc1 != null ? gc1.pauseCount : -1)); }
                        f.SetVisibility(false, true); _stage = 3; _at = now + 1f; break;          // closed the way the back button does it
                    case 3:
                        if (!_stk)
                        {
                            { var p0 = f.player; Check("closing the menu (back button path) gives the form back to the host", p0 != null && p0.Pointer == host.Pointer && !ViewP2, "viewP2=" + ViewP2); }
                            f.Show(); _stk = true; _at = now + 1.2f; break;
                        }
                        Check("the next opening by player 1 shows player 1 (not stuck on player 2)", Weapons(f).Contains(hw) && !Weapons(f).Contains(cw) && Opener == 1, "weapons=" + Weapons(f) + " opener=" + Opener + " host=" + hw + " clone=" + cw);
                        f.Hide();
                        LevelRounds.TestForceP2 = true; ViewP2 = false; f.Show(); _stage = 4; _at = now + 1.2f; break;
                    case 4:
                        Check("in player 2's level-up round the Details window shows player 2's weapons", Weapons(f).Contains(cw) && !Weapons(f).Contains(hw), "weapons=" + Weapons(f) + " clone=" + cw);
                        f.Hide(); LevelRounds.TestForceP2 = false;
                        if (VirtualPad.P2Device() != null) { _stage = 5; _at = now + 1.5f; break; }
                        Lc.Line("pauseview: SUMMARY ok=" + _ok + " fail=" + _bad + " (no second pad: the Start button check was skipped)");
                        _stage = 6; break;
                    case 5:
                        if (!_pressed)
                        {
                            // the menu had time to close; the direct Show() above stopped the game clock, so it is put back before the button is pressed
                            // the chooser still pairs the game's UI with player 2's pad until it has seen the menu close; then the game's own pause action
                            // would answer player 2's Start as well and this check would not prove the plugin's own opening
                            if (Chooser.Who != 0 && _waits++ < 20) { _at = now + 0.3f; break; }
                            Lc.Line("pauseview: before player 2's Start: menu open=" + Open(f) + " timeScale=" + Time.timeScale + " chooser who=" + Chooser.Who + " pauseCount=" + (Find.One<GameController>() != null ? Find.One<GameController>().pauseCount : -1) + " pad devices=" + (InputHandler.PlayerInput != null ? InputHandler.PlayerInput.devices.Count : -1));
                            _pressed = true; Time.timeScale = 1f; try { var gcz = Find.One<GameController>(); if (gcz != null) gcz.pauseCount = 0; } catch { }      // test hygiene: the direct Show/SetVisibility calls above left the game's pause level up
                            VirtualPad.Press(GamepadButton.Start); _at = now + 1.5f; break;
                        }
                        Check("player 2's Start button opens the pause menu for player 2", (P2Opens > 0 || (_pressed && Open(f))) && Weapons(f).Contains(cw) && !Weapons(f).Contains(hw), "opens=" + P2Opens + " weapons=" + Weapons(f));
                        Check("a menu opened by player 2 belongs to player 2's controller", Opener == 2 && (VirtualPad.P2Device() == null || Chooser.Who == 2), "opener=" + Opener + " chooser who=" + Chooser.Who + " locked=" + Chooser.Locked);
                        f.Hide();
                        Lc.Line("pauseview: SUMMARY ok=" + _ok + " fail=" + _bad + (Lc.Has("-lcnopauseview") || Lc.Has("-lcnop2pause") ? " (twin: expected FAIL)" : ""));
                        _stage = 6; break;
                }
            }
            catch (Exception e) { Lc.LineOnce("pauseview: test threw " + e.GetType().Name + ": " + e.Message); _stage = 6; }
        }
    }
}
