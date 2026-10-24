using System;
using System.Collections.Generic;
using DRS.UI;
using UnityEngine;
using UnityEngine.UI;

namespace LocalCoop
{
    /// <summary>
    /// `-lcui=menu`: scripted check of the co-op menu on the real main menu: the entry is there and opens the menu, the two
    /// players are independent, the overview shows exactly what was chosen, Back changes no run settings, and START writes the
    /// chosen values into the game's run settings (`-lcuidry` set up here so the run does not start). Every check logs CHECK ok/FAIL.
    /// Twins: `-lcuishare` (P2 follows P1: the independence check must go red), `-lcuiskipclass` (start skips the class: the read-back must go red).
    /// </summary>
    internal static class UiTest
    {
        private static int _step;
        private static float _nextAt = -1f;
        private static int _fail, _ok;
        private static string _classBefore;
        private static bool _done;

        private static void Check(string name, bool ok, string detail)
        {
            if (ok) _ok++; else _fail++;
            Lc.Line("ui: CHECK " + (ok ? "ok  " : "FAIL") + " " + name + " - " + detail);
        }

        internal static void Click(string key)
        {
            Button b;
            if (!CoopMenu.Buttons.TryGetValue(key, out b) || b == null) { Check("button " + key, false, "missing"); return; }
            b.onClick.Invoke();
        }

        internal static void Tick()
        {
            if (Lc.Str("-lcui=") == "flow" || Lc.Str("-lcui=") == "flowpad") { if (!_done) FlowTest.Tick(); return; }
            if (Lc.Str("-lcui=") == "start") { StartTick(); return; }
            if (Lc.Str("-lcui=") == "mouse") { MouseTick(); return; }
            if (Lc.Str("-lcui=") == "locks") { LocksTick(); return; }
            if (Lc.Str("-lcui=") == "pad") { PadTick(); return; }
            if (Lc.Str("-lcui=") == "levelup") { LevelupTick(); return; }
            if (Lc.Str("-lcui=") == "pages") { PagesTick(); return; }
            if (Lc.Str("-lcui=") == "pickup") { PickupTick(); return; }
            if (Lc.Str("-lcui=") == "wallet") { WalletTick(); return; }
            if (Lc.Str("-lcui=") == "hudprobe") { HudProbeTick(); return; }
            if (Lc.Str("-lcui=") == "stage" || Lc.Str("-lcui=") == "stages") { StageTick(); return; }
            if (Lc.Str("-lcui=") == "flowmouse") { if (!_done) MousePadTest.Tick(); return; }
            if (Lc.Str("-lcui=") == "padpick") { if (!_done) PickPadTest.Tick(); return; }
            if (Lc.Str("-lcui=") == "chooser") { if (!_done) ChooserTest.Tick(); return; }
            if (Lc.Str("-lcui=") == "retry") { if (!_done) RetryTest.Tick(); return; }
            if (Lc.Str("-lcui=") == "anim") { if (!_done) AnimTest.Tick(); return; }
            if (Lc.Str("-lcui=") == "target") { if (!_done) WeaponTest.TargetTick(); return; }
            if (Lc.Str("-lcui=") == "padsplit") { if (!_done) PadSplitTest.Tick(); return; }
            if (Lc.Str("-lcui=") == "dmg") { if (!_done) DamageTest.Tick(); return; }
            if (Lc.Str("-lcui=") == "input") { if (!_done) InputTest.Tick(); return; }
            if (Lc.Str("-lcui=") == "boss") { if (!_done) BossTest.Tick(); return; }
            if (Lc.Str("-lcui=") == "gate") { if (!_done) BossTest.GateTick(); return; }
            if (Lc.Str("-lcui=") == "weapons") { if (!_done) WeaponTest.Tick(); return; }
            if (_done || Lc.Str("-lcui=") != "menu") return;
            float now = Time.realtimeSinceStartup;
            if (now < _nextAt) return;
            if (_step == 0 && (MenuEntry.EntryButton == null || !CoopMenu.Built)) return;
            _nextAt = now + 0.6f;
            try { Step(); } catch (Exception e) { Check("step " + _step, false, "threw " + e.GetType().Name + ": " + e.Message); _step = 99; }
        }

        private static void Step()
        {
            var cm = typeof(CoopMenu);
            switch (_step++)
            {
                case 0:
                    CoopMenu.ShareTwin = Lc.Has("-lcuishare");
                    CoopStart.Dry = true;
                    { var rsm = CoopStart.Rsm(); _classBefore = rsm != null && rsm.CurrentClass != null ? rsm.CurrentClass.Dwarf.ToString() : "null"; Lc.Line("ui: run settings before: class=" + _classBefore); }
                    Check("entry exists", MenuEntry.EntryButton != null && MenuEntry.EntryButton.gameObject.activeInHierarchy, "button in the main menu");
                    Check("menu starts hidden", !CoopMenu.Visible, "visible=" + CoopMenu.Visible);
                    { var par = MenuEntry.EntryButton.transform.parent; var sb = new System.Text.StringBuilder(); for (int i = 0; i < par.childCount; i++) { var ch = par.GetChild(i); var r = ch.GetComponent<RectTransform>(); if (r != null && ch.name.StartsWith("Button_Cut_")) sb.Append(ch.name.Substring(11)).Append("@").Append(r.anchoredPosition.y.ToString("F0")).Append(" "); } Lc.Line("ui: menu order " + sb); }
                    Shots.Take("menu-entry");
                    _nextAt = Time.realtimeSinceStartup + 1.5f;
                    break;
                case 1:
                    MenuEntry.EntryButton.OnClick.Invoke();
                    break;
                case 2:
                    Check("entry opens the menu", CoopMenu.Visible, "visible=" + CoopMenu.Visible);
                    Check("defaults", CoopMenu.P1.ClassIndex == 0 && CoopMenu.P2.ClassIndex == 0 && CoopMenu.Sector == 0 && CoopMenu.Danger == 2 && CoopMenu.MissionType == 0, CoopMenu.Summary());
                    Shots.Take("menu-defaults");
                    _nextAt = Time.realtimeSinceStartup + 1.5f;
                    break;
                case 3:
                    Click("P1class3");                                  // P1: Scout
                    Check("P1 pick leaves P2 alone", CoopMenu.P1.ClassIndex == 3 && CoopMenu.P2.ClassIndex == 0, CoopMenu.Summary());
                    break;
                case 4:
                    Click("P2class1");                                  // P2: Driller
                    Check("P2 pick leaves P1 alone", CoopMenu.P2.ClassIndex == 1 && CoopMenu.P1.ClassIndex == 3, CoopMenu.Summary());
                    break;
                case 5:
                    {
                        var before = CoopMenu.P1.Sub;
                        Click("P2sub1");
                        var want = CoopMenu.SubsFor(CoopMenu.Classes[1]);
                        Check("P2 subclass picked, P1 subclass unchanged", CoopMenu.P2.Sub == want[1] && CoopMenu.P1.Sub == before, CoopMenu.Summary());
                        Click("P2class2");                              // class change resets the subclass to the class default
                        var def = CoopMenu.Classes[2].DefaultClassArtifact;
                        Check("class change resets subclass", CoopMenu.P2.Sub == def, "P2 sub=" + CoopMenu.SubName(CoopMenu.P2.Sub) + " default=" + CoopMenu.SubName(def));
                    }
                    break;
                case 6:
                    Click("P2class1"); Click("P2sub1");
                    Click("sector1"); Click("danger2"); Click("biome3");
                    { string s = CoopMenu.Summary(); Lc.Line("ui: chosen " + s); }
                    Check("overview rows show the choices",
                        CoopMenu.Values["ov0"].text == "Elimination" && CoopMenu.Values["ov1"].text == "2" && CoopMenu.Values["ov2"].text == "3"
                        && CoopMenu.Values["ov3"].text == CoopMenu.BiomeName(CoopMenu.Biomes[3]) && CoopMenu.Values["ov4"].text == "2"
                        && CoopMenu.Values["ov5"].text.Contains(CoopMenu.Classes[3].DisplayName) && CoopMenu.Values["ov5"].text.Contains(CoopMenu.Classes[1].DisplayName)
                        && CoopMenu.Values["ov6"].text.Contains(CoopMenu.SubName(CoopMenu.P2.Sub)),
                        CoopMenu.Values["ov0"].text + " | " + CoopMenu.Values["ov1"].text + " | " + CoopMenu.Values["ov2"].text + " | " + CoopMenu.Values["ov3"].text + " | " + CoopMenu.Values["ov5"].text + " | " + CoopMenu.Values["ov6"].text);
                    Shots.Take("menu-chosen");
                    _nextAt = Time.realtimeSinceStartup + 1.5f;
                    break;
                case 7:
                    Click("start");                                     // dry start: settings only
                    Check("start writes the run settings", ReadbackMatches(), CoopStart.Readback);
                    break;
                case 8:
                    Click("back");
                    Check("back hides the menu", !CoopMenu.Visible, "visible=" + CoopMenu.Visible);
                    _nextAt = Time.realtimeSinceStartup + 1.5f;
                    break;
                case 9:
                    // after BACK the game shows its main menu again and rewrites the button texts: the entry must still read LOCAL CO-OP (it read "SPIEL")
                    Check("after BACK the main menu entry still reads LOCAL CO-OP", MenuEntry.Label == "LOCAL CO-OP", "label='" + MenuEntry.Label + "' fixes=" + MenuEntry.LabelFixes);
                    MenuEntry.SimulateGameRewrite();
                    Check("when the game rewrites the label nothing of it shows (no flicker)", MenuEntry.VisibleLabel == Loc.T("LOCAL CO-OP"), "visible='" + MenuEntry.VisibleLabel + "'");
                    MenuEntry.EntryButton.OnClick.Invoke();
                    break;
                case 10:
                    {
                        // gates: the end-of-sector levels are choices of their own; a gate fixes the biome
                        Click("gate0");
                        Check("a gate can be chosen (it fixes the biome, the biome buttons are off)", CoopMenu.GateSel && CoopMenu.Sector == 0 && !CoopMenu.IsOpen("biome0") && CoopMenu.Values["ov1"].text == "Gate 1", CoopMenu.Summary() + " ov1=" + CoopMenu.Values["ov1"].text);
                        Click("start");
                        Check("start with a gate sets up the gate node", CoopStart.Readback.Contains("node=GATE"), CoopStart.Readback);
                        Click("sector0");
                        Check("choosing a sector again leaves the gate", !CoopMenu.GateSel && CoopMenu.IsOpen("biome0"), CoopMenu.Summary());
                        // locked choices say what they need
                        int locked = 0, shown = 0;
                        for (int i = 1; i < 5; i++) if (!CoopMenu.IsOpen("sector" + i)) { locked++; if (CoopMenu.Values["sector" + i + "cost"].text.Length > 0) shown++; }
                        int gateText = 0; for (int i = 0; i < 4; i++) if (CoopMenu.Values["gate" + i + "cost"].text.Length > 0) gateText++;
                        for (int i = 0; i < CoopMenu.Biomes.Length && i < 6; i++) if (!CoopMenu.IsOpen("biome" + i)) { locked++; if (CoopMenu.Values["biome" + i + "cost"].text.Length > 0) shown++; }
                        Check("the header shows the player level", CoopMenu.PlayerLevel() > 0 && CoopMenu.Values["plevel"].text.EndsWith(" " + CoopMenu.PlayerLevel()), "level " + CoopMenu.PlayerLevel() + ", header '" + CoopMenu.Values["plevel"].text + "'");
                        Check("gates carry no text (the game's own sector screen has none)", gateText == 0, "gates with a text=" + gateText);
                        Check("every locked sector and biome shows its requirement", locked > 0 && shown == locked, "locked=" + locked + " with a requirement text=" + shown + ", points shown: " + CoopMenu.Values["points"].text);
                        Click("back");
                    }
                    break;
                default:
                    _done = true;
                    bool pass = _fail == 0 && _ok >= 16;
                    Lc.QuitSent = true;
                    Lc.Line("RESULT " + Lc.Test + " " + (pass ? "PASS" : "FAIL") + " co-op menu checks ok=" + _ok + " fail=" + _fail);
                    Application.Quit();
                    break;
            }
        }

        // ---- real clicks through a virtual mouse ----------------------------------------------------------------
        private static int _mStep, _mPhase;
        private static float _mAt = -1f;
        private static Vector2 _mPos;
        private static Component _mTarget;

        private static Vector2 ScreenCenter(Component c)
        {
            var rt = c.GetComponent<RectTransform>();
            // The game's own canvas: the transform gives the screen position through its camera.
            if (!c.gameObject.scene.name.Equals("DontDestroyOnLoad") || c.transform.root.name != "LcCoopMenu")
            {
                var canvas = c.GetComponentInParent<Canvas>();
                var root = canvas != null ? canvas.rootCanvas : null;
                Camera cam = root != null && root.renderMode != RenderMode.ScreenSpaceOverlay ? root.worldCamera : null;
                Vector3 world = rt.TransformPoint(new Vector3(rt.rect.center.x, rt.rect.center.y, 0f));
                return RectTransformUtility.WorldToScreenPoint(cam, world);
            }
            // Our own canvas: every widget is anchored top-left in a 1920x1080 reference space, so the position is the sum of the
            // anchored positions up the chain, scaled like the CanvasScaler does (width and height, 0.5).
            float x = 0f, y = 0f;
            for (Transform t = rt; t != null && t.parent != null; t = t.parent)
            {
                var r = t.GetComponent<RectTransform>();
                if (r == null) break;
                x += r.anchoredPosition.x; y += r.anchoredPosition.y;
            }
            x += rt.rect.width * 0.5f; y -= rt.rect.height * 0.5f;
            float sc = Mathf.Exp(0.5f * Mathf.Log(Screen.width / 1920f) + 0.5f * Mathf.Log(Screen.height / 1080f));
            return new Vector2(x * sc, Screen.height + y * sc);
        }

        /// <summary>Queue a click on a component: move, press, release over three ticks. Returns true while busy.</summary>
        private static int MouseClass() { return Lc.Num("-lcmouseclass=") >= 1f ? (int)Lc.Num("-lcmouseclass=") : 3; }
        private static int _mSub = -1;
        private static int MouseSub() { if (_mSub >= 0) return _mSub; var subs = CoopMenu.SubsFor(CoopMenu.Classes[CoopMenu.P2.ClassIndex]); for (int i = 0; i < subs.Count; i++) if (subs[i] != CoopMenu.P2.Sub && CoopMenu.SubOpen(subs[i])) { _mSub = i; break; } return _mSub < 0 ? 1 : _mSub; }
        private static int _mTries; private static int _mCountBefore;
        internal static int MouseRetries;
        private static bool ClickReal(Component target)
        {
            if (_mPhase == 0) { _mTarget = target; _mPos = ScreenCenter(target); if (_mTries == 0) _mCountBefore = CoopMenu.ClickCount; }
            if (Lc.Has("-lcnomouse")) { _mPhase = 0; return false; }      // twin: nothing is clicked, the checks must go red
            VirtualMouse.Step(_mPhase, _mPos);
            if (_mPhase == 2) Diagnose();
            _mPhase++;
            if (_mPhase <= 2) return true;
            _mPhase = 0;
            // hand-written harness mouse: a click on one of the menu's own buttons sometimes does not arrive (cause unknown); it is repeated up to 4 times until the button has seen it
            bool ours = target != null && target.name.StartsWith("Btn_");
            if (ours && CoopMenu.ClickCount == _mCountBefore && _mTries < 5) { _mTries++; MouseRetries++; Lc.Line("mouse: the click did not arrive at " + target.name + ", again (" + _mTries + ")"); return true; }
            if (ours && _mTries > 0) Lc.Line("mouse: " + target.name + " got the click after " + _mTries + " retries: " + (CoopMenu.ClickCount != _mCountBefore));
            _mTries = 0;
            return false;
        }

        private static void Diagnose()
        {
            try
            {
                var es = UnityEngine.EventSystems.EventSystem.current;
                var m = UnityEngine.InputSystem.Mouse.current;
                { var tb = _mTarget != null ? _mTarget.GetComponent<Button>() : null; Lc.Line("mouse: target button interactable=" + (tb != null ? tb.interactable.ToString() : "n/a") + " active=" + (_mTarget != null && _mTarget.gameObject.activeInHierarchy)); }
                Lc.Line("mouse: eventSystem=" + (es != null) + " module=" + (es != null && es.currentInputModule != null ? es.currentInputModule.GetType().Name : "null")
                    + " Mouse.current=" + (m != null ? m.name + " pos=" + m.position.ReadValue() + " left=" + m.leftButton.isPressed : "null") + " target=" + (_mTarget != null ? _mTarget.name : "null") + " at " + _mPos
                    + " focused=" + Application.isFocused + " screen=" + Screen.width + "x" + Screen.height);
                if (es != null)
                {
                    var ped = new UnityEngine.EventSystems.PointerEventData(es);
                    ped.position = _mPos;
                    var hits = new Il2CppSystem.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
                    es.RaycastAll(ped, hits);
                    var sb = new System.Text.StringBuilder();
                    for (int i = 0; i < hits.Count && i < 4; i++) sb.Append(hits[i].gameObject.name).Append(" | ");
                    Lc.Line("mouse: raycast hits (" + hits.Count + "): " + sb);
                }
            }
            catch (Exception e) { Lc.Line("mouse: diagnose threw " + e.GetType().Name + ": " + e.Message); }
        }

        private static void MouseTick()
        {
            if (_done) return;
            float now = Time.realtimeSinceStartup;
            if (now < _mAt) return;
            if (_mStep == 0 && (MenuEntry.EntryButton == null || !CoopMenu.Built)) return;
            _mAt = now + 0.35f;
            try
            {
                Button b = null;
                switch (_mStep)
                {
                    case 0: if (ClickReal(MenuEntry.EntryButton)) return; _mAt = now + 1.0f; break;
                    case 1: Check("real click on the main-menu entry opens the menu", CoopMenu.Visible, "pos=" + _mPos + " visible=" + CoopMenu.Visible); break;
                    case 2: if (CoopMenu.Mode != "classic") { CoopMenu.ShowMode("classic"); _mAt = now + 0.8f; return; }      // the entry opens the flow pages now; this test clicks the classic one-page menu
                        CoopMenu.Buttons.TryGetValue("P1class3", out b); if (ClickReal(b)) return; break;
                    case 3: Check("real click picks P1 class", CoopMenu.P1.ClassIndex == 3 && CoopMenu.P2.ClassIndex == 0, CoopMenu.Summary()); break;
                    case 4: CoopMenu.Buttons.TryGetValue("biome2", out b); if (ClickReal(b)) return; break;
                    case 5: Check("real click picks a biome", CoopMenu.BiomeIndex == 2, CoopMenu.Summary()); break;
                    case 6: CoopMenu.Buttons.TryGetValue("P2sub" + MouseSub(), out b); if (ClickReal(b)) return; break;      // the first subclass of player 2 that is open and not the chosen one (the test profile locks the third of the Gunner)
                    case 7: Check("real click picks a P2 subclass", CoopMenu.P2.Sub == CoopMenu.SubsFor(CoopMenu.Classes[CoopMenu.P2.ClassIndex])[MouseSub()] && CoopMenu.P1.ClassIndex == 3, CoopMenu.Summary()); Shots.Take("mouse-menu"); _mAt = now + 1.5f; break;
                    case 8: CoopMenu.Buttons.TryGetValue("back", out b); if (ClickReal(b)) return; break;
                    case 9: Check("real click on BACK closes the menu", !CoopMenu.Visible, "visible=" + CoopMenu.Visible); break;
                    default:
                        _done = true;
                        Lc.QuitSent = true;
                        Lc.Line("RESULT " + Lc.Test + " " + (_fail == 0 && _ok == 5 ? "PASS" : "FAIL") + " co-op menu real clicks ok=" + _ok + " fail=" + _fail + " clicks=" + VirtualMouse.Clicks);
                        Application.Quit();
                        return;
                }
                _mStep++;
            }
            catch (Exception e) { Check("mouse step " + _mStep, false, "threw " + e.GetType().Name + ": " + e.Message); _mStep = 99; }
        }

        // ---- locks: greyed out, not clickable, answers equal the game's own; goals and mutator shown ------------------------------------
        private static int _lStep;
        private static float _lAt = -1f;

        private static string Flat(string t) { return t.Replace("\n", " / "); }

        private static void LocksTick()
        {
            if (_done) return;
            float now = Time.realtimeSinceStartup;
            if (now < _lAt) return;
            if (_lStep == 0 && (MenuEntry.EntryButton == null || !CoopMenu.Built)) return;
            _lAt = now + 0.5f;
            try { LocksStep(now); }
            catch (Exception e) { Check("locks step " + _lStep, false, "threw " + e.GetType().Name + ": " + e.Message); _lStep = 99; }
        }

        private static void LocksStep(float now)
        {
            var save = CoopStart.Resolve<DRS.SaveSystem.SaveDataManager>();
            var road = CoopStart.Resolve<MissionRoadManager>();
            switch (_lStep++)
            {
                case 0:
                    CoopMenu.IgnoreLocks = Lc.Has("-lcuinolocks");
                    MenuEntry.EntryButton.OnClick.Invoke();
                    break;
                case 1:
                    {
                        // 1. every lock state equals what the game itself says
                        int locked = 0, wrong = 0; var bad = new System.Text.StringBuilder();
                        var st = road.GetMissionRoadState(EMainMissionType.Elimination);
                        for (int i = 0; i < 5; i++)
                        {
                            bool want = !st.SectorStates[i].IsLocked;
                            if (!want) locked++;
                            if (CoopMenu.IsOpen("sector" + i) != want) { wrong++; bad.Append("sector" + (i + 1) + " "); }
                        }
                        var node = CoopMenu.NodeOf(0, CoopMenu.Sector, CoopMenu.BiomeIndex);
                        for (int i = 0; i < 5; i++)
                        {
                            bool want = save.IsHazIndexUnlocked(i) && i >= node.MinHazIndex;
                            if (!want) locked++;
                            if (CoopMenu.IsOpen("danger" + i) != want) { wrong++; bad.Append("danger" + (i + 1) + " "); }
                        }
                        for (int i = 0; i < 6; i++)
                        {
                            MissionNodeState n; st.TryGetNodeState(CoopMenu.Biomes[i].BiomeType, CoopMenu.Sector, out n);
                            if (n.IsLocked) locked++;
                            if (CoopMenu.IsOpen("biome" + i) != !n.IsLocked) { wrong++; bad.Append("biome" + (i + 1) + " "); }
                        }
                        for (int i = 0; i < 4; i++)
                        {
                            bool want = save.IsClassUnlocked(CoopMenu.Classes[i].Dwarf);
                            if (!want) locked++;
                            if (CoopMenu.IsOpen("P1class" + i) != want) { wrong++; bad.Append("class" + i + " "); }
                        }
                        foreach (var cls in new[] { 1, 2 })           // Driller, Engineer: locked subclasses in the test profile
                        {
                            CoopMenu.Buttons["P1class" + cls].onClick.Invoke();
                            var subs = CoopMenu.SubsFor(CoopMenu.Classes[cls]);
                            for (int i = 0; i < subs.Count; i++)
                            {
                                bool want = save.IsUnlocked(subs[i]);
                                if (!want) locked++;
                                if (CoopMenu.IsOpen("P1sub" + i) != want) { wrong++; bad.Append("sub" + cls + "/" + i + " "); }
                            }
                        }
                        Check("greyed-out state equals the game's own answers", wrong == 0, "wrong=" + wrong + " [" + bad + "]");
                        Check("some choices are locked in this profile", locked > 0, "locked=" + locked);
                        Shots.Take("menu-locks");
                        _lAt = now + 1.5f;
                    }
                    break;
                case 2:
                    {
                        // 2. a locked choice cannot be picked
                        int lockedSector = -1;
                        for (int i = 0; i < 5; i++) if (!CoopMenu.IsOpen("sector" + i)) { lockedSector = i; break; }
                        int before = CoopMenu.Sector;
                        if (lockedSector >= 0) CoopMenu.Buttons["sector" + lockedSector].onClick.Invoke();
                        Check("clicking a locked sector changes nothing", lockedSector >= 0 && CoopMenu.Sector == before, "locked sector=" + (lockedSector + 1) + " now=" + (CoopMenu.Sector + 1));
                        int lockedSub = -1; var subs = CoopMenu.SubsFor(CoopMenu.Classes[CoopMenu.P1.ClassIndex]);
                        for (int i = 0; i < subs.Count; i++) if (!CoopMenu.IsOpen("P1sub" + i)) { lockedSub = i; break; }
                        var sb4 = CoopMenu.P1.Sub;
                        if (lockedSub >= 0) CoopMenu.Buttons["P1sub" + lockedSub].onClick.Invoke();
                        Check("clicking a locked subclass changes nothing", lockedSub >= 0 && CoopMenu.P1.Sub == sb4, "locked sub=" + lockedSub);
                    }
                    break;
                case 3:
                    // 3. a sector sets a minimum danger: picking sector 3 (minimum danger 3) lifts danger 1
                    CoopMenu.Buttons["P1class0"].onClick.Invoke();
                    CoopMenu.Buttons["danger0"].onClick.Invoke();
                    CoopMenu.Buttons["sector2"].onClick.Invoke();
                    Check("sector raises the danger to its minimum", CoopMenu.Sector == 2 && CoopMenu.Danger >= 2, "sector=" + (CoopMenu.Sector + 1) + " danger=" + (CoopMenu.Danger + 1));
                    break;
                case 4:
                    // 4. escort: a biome that is locked there is replaced by an open one
                    CoopMenu.Buttons["sector0"].onClick.Invoke();
                    CoopMenu.Buttons["biome1"].onClick.Invoke();
                    CoopMenu.Buttons["mission1"].onClick.Invoke();
                    Check("escort moves to an open biome", CoopMenu.MissionType == 1 && CoopMenu.BiomeOpen(1, CoopMenu.Sector, CoopMenu.BiomeIndex), "biome=" + CoopMenu.BiomeName(CoopMenu.Biomes[CoopMenu.BiomeIndex]));
                    CoopMenu.Buttons["mission0"].onClick.Invoke();
                    break;
                case 5:
                    {
                        // 5. goals: completed ones struck through, open ones not; mutator of the node shown
                        CoopMenu.Buttons["sector0"].onClick.Invoke();
                        CoopMenu.Buttons["biome0"].onClick.Invoke();
                        string done = CoopMenu.Values["goals0"].text;
                        var n0 = CoopMenu.NodeOf(0, 0, 0);
                        bool anyDone = false; foreach (var g in n0.GetMissionGoalStates()) if (g.IsComplete) anyDone = true;
                        Check("completed main-mission goals are struck through", anyDone && done.Contains("<s>"), "completed=" + anyDone + " text=" + Flat(done));
                        CoopMenu.Buttons["biome4"].onClick.Invoke();
                        string open = CoopMenu.Values["goals0"].text;
                        var n4 = CoopMenu.NodeOf(0, 0, 4);
                        Check("open goals are listed plainly with the game's text", !open.Contains("<s>") && open.Contains(n4.GetMissionGoalStates()[0].Data.GetFormattedDescriptionText()), Flat(open));
                        CoopMenu.Buttons["sector1"].onClick.Invoke();
                        CoopMenu.Buttons["biome1"].onClick.Invoke();
                        var nm = CoopMenu.NodeOf(0, 1, 1);
                        string title = nm.Data.Mutators[0].GetTitle();
                        Check("mutator of the node is shown", CoopMenu.Values["goals2"].text.Contains(title), "title='" + title + "' shown='" + Flat(CoopMenu.Values["goals2"].text) + "'");
                        Check("biome goals follow the danger level", CoopMenu.Values["goals1"].text.Contains(CoopMenu.Biomes[1].HazardLevels[CoopMenu.Danger].Goals[1].GetFormattedDescriptionText()), Flat(CoopMenu.Values["goals1"].text));
                        Shots.Take("menu-goals");
                        _lAt = now + 1.5f;
                    }
                    break;
                default:
                    _done = true;
                    Lc.QuitSent = true;
                    Lc.Line("RESULT " + Lc.Test + " " + (_fail == 0 && _ok == 10 ? "PASS" : "FAIL") + " co-op menu locks and goals ok=" + _ok + " fail=" + _fail);
                    Application.Quit();
                    break;
            }
        }

        // ---- controller navigation, through the virtual gamepad ---------------------------------------------------------------
        private static int _pStep;
        private static float _pAt = -1f;
        private static Vector2 _pFrom;

        private static int LastClassIndex() { int best = 0; for (int i = 0; i < CoopMenu.Classes.Length; i++) if (CoopMenu.DisplayPos(i) > CoopMenu.DisplayPos(best)) best = i; return best; }

        private static void PadTick()
        {
            if (_done) return;
            float now = Time.realtimeSinceStartup;
            if (now < _pAt) return;
            if (_pStep == 0 && (MenuEntry.EntryButton == null || !CoopMenu.Built)) return;
            _pAt = now + 0.55f;
            try { PadStep(now); }
            catch (Exception e) { Check("pad step " + _pStep, false, "threw " + e.GetType().Name + ": " + e.Message); _pStep = 99; }
        }

        private static void PadStep(float now)
        {
            switch (_pStep++)
            {
                case 0:
                    CoopStart.Dry = true;
                    MenuEntry.EntryButton.OnClick.Invoke();
                    _pAt = now + 1.0f;
                    break;
                case 1:
                    VirtualPad.Press(UnityEngine.InputSystem.LowLevel.GamepadButton.South);          // A before any focus: nothing may react
                    _pAt = now + 1.2f;
                    break;
                case 2:
                    {
                        string form = AutoStart.ActiveFormName();
                        Check("A with the co-op menu open does not press the main menu", CoopMenu.Visible && form == "UIMenuSplashForm", "form=" + form + " visible=" + CoopMenu.Visible);
                        Check("the page opens with the selected class already focused (no press needed)", MenuPad.Focus == "P1class" + CoopMenu.P1.ClassIndex, "focus=" + MenuPad.Focus);
                        VirtualPad.Press(UnityEngine.InputSystem.LowLevel.GamepadButton.DpadDown);
                    }
                    break;
                case 3:
                    Check("d-pad press moves within the P1 classes", MenuPad.Focus.StartsWith("P1class"), "focus=" + MenuPad.Focus);
                    VirtualPad.Press(UnityEngine.InputSystem.LowLevel.GamepadButton.DpadDown);
                    break;
                case 4: if (MenuPad.Focus != "P1class" + LastClassIndex()) VirtualPad.Press(UnityEngine.InputSystem.LowLevel.GamepadButton.DpadDown); break;      // the list is in unlock order now: press down only as far as the last entry
                case 5: if (MenuPad.Focus != "P1class" + LastClassIndex()) VirtualPad.Press(UnityEngine.InputSystem.LowLevel.GamepadButton.DpadDown); break;
                case 6:
                    Check("three presses down reach the last class of the list", MenuPad.Focus == "P1class" + LastClassIndex(), "focus=" + MenuPad.Focus + " last=" + LastClassIndex());
                    VirtualPad.Press(UnityEngine.InputSystem.LowLevel.GamepadButton.South);
                    break;
                case 7:
                    Check("A picks the focused class", CoopMenu.P1.ClassIndex == LastClassIndex(), CoopMenu.Summary());
                    Shots.Take("menu-pad");
                    _pFrom = CoopMenu.RefCenter(CoopMenu.Buttons[MenuPad.Focus]);
                    _pAt = now + 1.5f;
                    break;
                case 8:
                    VirtualPad.Press(UnityEngine.InputSystem.LowLevel.GamepadButton.DpadRight);
                    break;
                case 9:
                    {
                        var to = CoopMenu.RefCenter(CoopMenu.Buttons[MenuPad.Focus]);
                        Check("d-pad right moves the focus to the right", to.x > _pFrom.x + 5f, "focus=" + MenuPad.Focus + " x " + _pFrom.x + " -> " + to.x);
                        Click("start");      // the menu button no longer starts from the classic page (since round 9, it only goes on in the flow pages): the START button itself
                    }
                    break;
                case 10:
                    Check("Start writes the run settings", CoopStart.Readback.Contains("class=" + CoopMenu.Classes[LastClassIndex()].Dwarf.ToString()), CoopStart.Readback);
                    VirtualPad.Press(UnityEngine.InputSystem.LowLevel.GamepadButton.East);
                    break;
                case 11:
                    Check("B closes the co-op menu", !CoopMenu.Visible, "visible=" + CoopMenu.Visible + " backHits=" + MenuPad.BackHits + " form=" + AutoStart.ActiveFormName());
                    break;
                default:
                    _done = true;
                    Lc.QuitSent = true;
                    Lc.Line("RESULT " + Lc.Test + " " + (_fail == 0 && _ok == 8 ? "PASS" : "FAIL") + " co-op menu pad ok=" + _ok + " fail=" + _fail);
                    Application.Quit();
                    break;
            }
        }

        // ---- a screenshot of every page of the co-op menu (`-lcui=pages`) ------------------------------------------------------------
        private static int _pgStep; private static float _pgAt = -1f; private static bool _pgHid;
        private static readonly string[] PageModes = { "lobby", "sector", "biome", "danger", "pick", "final" };

        private static void PagesTick()
        {
            if (_done) return;
            float now = Time.realtimeSinceStartup;
            if (now < _pgAt) return;
            if (_pgStep == 0 && (MenuEntry.EntryButton == null || !CoopMenu.Built)) return;
            _pgAt = now + 1.5f;
            if (Lc.Has("-lcsprprobe") && _pgStep == 0)
            {
                foreach (var s in Resources.FindObjectsOfTypeAll<Sprite>()) if (s != null && s.rect.width >= 1200f) Lc.Line("sprprobe: sprite " + s.name + " " + s.rect.width + "x" + s.rect.height);
                foreach (var t in Resources.FindObjectsOfTypeAll<Texture2D>()) if (t != null && t.width >= 1400) Lc.Line("sprprobe: texture " + t.name + " " + t.width + "x" + t.height);
                foreach (var r in Resources.FindObjectsOfTypeAll<UnityEngine.UI.RawImage>()) if (r != null && r.gameObject.scene.IsValid()) Lc.Line("sprprobe: rawimage " + r.gameObject.name + " tex=" + (r.texture != null ? r.texture.name : "-") + " active=" + r.gameObject.activeInHierarchy);
            }
            if (Lc.Has("-lcorigpages"))
            {
                // the game's own campaign screens (for comparison with the co-op pages): press its PLAY button and look
                var menu = Find.One<UIMenuSplashForm>();
                if (_pgStep == 0 && menu != null && menu.playButton != null) { menu.playButton.gameObject.SetActive(true); menu.playButton.OnClick.Invoke(); _pgStep++; _pgAt = now + 4f; return; }
                if (_pgStep >= 1) { Shots.Take("orig-" + _pgStep); _pgStep++; _pgAt = now + 4f; if (_pgStep > 5) Finish(true, "original screens taken"); }
                return;
            }
            if (_pgStep == 0) { MenuEntry.EntryButton.OnClick.Invoke(); _pgStep++; return; }
            int k = _pgStep - 1;                  // odd k: show page k/2, even k: take its picture (the picture is taken a tick after the switch)
            int page = k / 2;
            if (page >= PageModes.Length)
            {
                if (!_pgHid) { _pgHid = true; CoopMenu.Hide(); _pgAt = now + 1f; return; }       // BACK out of the co-op menu: the main menu must be reachable by the controller again
                var es = UnityEngine.EventSystems.EventSystem.current;
                var sel = es != null ? es.currentSelectedGameObject : null;
                Check("after leaving the co-op menu something of the main menu is selected (the controller reaches it)", sel != null && sel.activeInHierarchy, "selected=" + (sel != null ? sel.name : "none"));
                Finish(_fail == 0, "page screenshots taken, checks ok=" + _ok + " fail=" + _fail); return;
            }
            if (k % 2 == 0) { CoopMenu.ShowMode(PageModes[page]); if (Lc.Has("-lcfocusshots")) { string fk = PageModes[page] == "sector" ? "gat1" : PageModes[page] == "biome" ? "bio3" : PageModes[page] == "pick" ? "P1c1" : ""; if (fk.Length > 0) { MenuPad.Focus = fk; MenuPad.PadUsed = true; } } }
            else
            {
                if (PageModes[page] == "biome") Check("the biome hex shows how many missions are done", CoopMenu.ProgTotal > 0 && CoopMenu.Values["bio" + CoopMenu.BiomeIndex + "progtext"].text == CoopMenu.ProgDone + "/" + CoopMenu.ProgTotal, "done " + CoopMenu.ProgDone + " of " + CoopMenu.ProgTotal);
                Shots.Take("page-" + PageModes[page]);
                bool map = PageModes[page] == "sector" || PageModes[page] == "biome" || PageModes[page] == "danger";
                Check("page " + PageModes[page] + (map ? " has the game artwork behind it" : " has the flat background"), CoopMenu.BackdropOn == map, "backdrop=" + CoopMenu.BackdropOn);
                if (Loc.German) { CoopMenu.RefreshAll(); int eng = Loc.Untranslated(CoopMenu.RootTransform); Check("page " + PageModes[page] + ": after a refresh no text is left in English (no flicker)", eng == 0, "untranslated texts=" + eng); }
                if (PageModes[page] == "sector") Check("the sector page has its 8 links between hexes and gates", CoopMenu.LinkCount == 8, "links=" + CoopMenu.LinkCount);
                if (PageModes[page] == "sector")
                {
                    var keys = new[] { "sec0", "gat0", "sec1", "gat1", "sec2", "gat2", "sec3", "gat3", "sec4" };
                    var bad = new List<string>(); var all = new List<string>();
                    foreach (var key in keys)
                    {
                        string r = CoopMenu.SectorInfoProbe(key); all.Add(key + "=" + r);
                        var parts = r.Split('/');
                        if (parts.Length == 3 && parts[0] != "-1" && parts[0] != parts[2]) bad.Add(key);        // a locked sector shows "Locked." instead of a number
                    }
                    Lc.Line("ui: sector info shown/game/mine: " + string.Join(" ", all.ToArray()));
                    Check("the sector info numbers equal the goals of the biomes this menu offers (not the DLC ones)", bad.Count == 0, string.Join(",", bad.ToArray()));
                }
            }
            _pgStep++;
        }

        // ---- shared XP and level-up rounds ------------------------------------------------------------------------------------
        private static int _uStep;
        private static float _uAt = -1f, _uInRun = -1f, _uFeed = -1f;
        private static int _uStartLevel = -1;

        private static float _docLvAt = -1f; private static int _docLvShots;
        private static bool _burstDone;
        /// <summary>`-lcxpburst`: two host levels land in the queue in the same frame (the hand-test case "two levels at once").</summary>
        private static void BurstSecondLevel(Player host)
        {
            if (!Lc.Has("-lcxpburst") || _burstDone) return;
            var mgr = CoopStart.Resolve<LevelUpManager>();
            if (mgr == null || mgr.flowActive || Time.timeScale < 0.5f) return;
            _burstDone = true;
            for (int k = 1; k <= 2; k++) { LevelRounds.OnHostLevel(_uStartLevel + k); mgr.levelUps.Add(new PlayerLevelGainArgs(host, _uStartLevel + k)); }
            mgr.StartFlow();
            Lc.Line("ui: levelup test: two host levels queued in the same frame, queue=" + mgr.levelUps.Count + " flow=" + mgr.flowActive);
        }

        private static int LevelOf(Player p) { try { return p.currentLevel; } catch { return -1; } }

        private static void LevelupTick()
        {
            if (_done) return;
            float now = Time.realtimeSinceStartup;
            if (now < _uAt) return;
            if (_uStep == 0 && (MenuEntry.EntryButton == null || !CoopMenu.Built)) return;
            _uAt = now + 0.5f;
            try
            {
                switch (_uStep)
                {
                    case 0: MenuEntry.EntryButton.OnClick.Invoke(); _uStep++; break;
                    case 1: Click("P1class3"); Click("P2class1"); Click("sector0"); Click("danger1"); Click("biome3"); _uStep++; break;
                    case 2: Click("start"); _uStep++; _uAt = now + 3f; break;
                    default:
                        if (Lc.InRunAt < 0f) { if (Lc.EverInRun) Finish(false, "the run ended before the test did (" + LevelRounds.State + ")"); else if (now > _uAt + 60f) Finish(false, "the run did not start"); return; }
                        if (_uInRun < 0f) _uInRun = now;
                        if (now < _uInRun + 18f) return;
                        var host = Phase1.First(); var clone = Phase1.Second;
                        if (host == null || clone == null) { Finish(false, "no host or clone"); return; }
                        if (_uStartLevel < 0)
                        {
                            _uStartLevel = LevelOf(host);
                            _uFeed = now;
                            Lc.Line("ui: levelup test: host level " + _uStartLevel + ", clone level " + LevelOf(clone) + ", feeding xp");
                        }
                        if (Lc.Has("-lcdoc")) { var m0 = CoopStart.Resolve<LevelUpManager>(); if (m0 != null && m0.flowActive) { if (_docLvAt < 0f) _docLvAt = now; else if (_docLvShots < 3 && now > _docLvAt + 1.5f) { Shots.Take("doc-levelup-open"); _docLvShots++; _docLvAt = now + 1f; } } }
                        // feed the clone's xp (it must land in the host's pool) until the host has gained two levels
                        if (Lc.Has("-lcxpburst") && !_burstDone) { BurstSecondLevel(host); _uFeed = now; return; }
                        if (!Lc.Has("-lcxpburst") && LevelOf(host) < _uStartLevel + 2 && Time.timeScale > 0f && now >= _uFeed) { clone.GainXp(6f, false); _uFeed = now + 0.05f; return; }
                        if (LevelRounds.RoundsFinished < 2 && now < _uFeed + 40f) return;
                        if (now < _uFeed + 3f) return;
                        {
                            string picks = string.Join(",", LevelRounds.Picks.ToArray());
                            // every level is answered by the host first and by P2 afterwards: two of each, and P2 never answers before the host has answered as often
                            bool order = LevelRounds.Picks.Count == 4; int a1 = 0, a2 = 0;
                            foreach (var pk in LevelRounds.Picks) { if (pk.StartsWith("P1:")) a1++; else if (pk.StartsWith("P2:")) a2++; if (a2 > a1) order = false; }
                            order = order && a1 == 2 && a2 == 2;
                            string seq = ""; foreach (var pk in LevelRounds.Picks) { string w = pk.Substring(0, 2) + ","; if (!seq.EndsWith(w)) seq += w; }      // the test clicker may press a card twice: collapse repeats
                            // with several levels queued at once the rounds must alternate: P1, P2, P1, P2 (twin -lcnoalternate gives P1, P1, P2, P2)
                            if (Lc.Has("-lcxpburst")) order = seq == "P1,P2,P1,P2,";
                            var mgr = CoopStart.Resolve<LevelUpManager>();
                            bool back = mgr != null && mgr.player != null && mgr.player.Pointer == host.Pointer;
                            bool running = (Time.timeScale > 0f || (mgr != null && mgr.flowActive)) && host.isAlive;      // a further level-up may already be open (more XP since P2 collects the blue cubes)
                            bool xp = Lc.Has("-lcxpburst") ? LevelRounds.FormPaidByP2 : Share.XpRedirected > 0 && LevelOf(host) >= _uStartLevel + 2 && LevelRounds.FormPaidByP2;
                            Lc.Line("ui: levelup result: picks=[" + picks + "] order=" + order + " contextBack=" + back + " running=" + running + " xpRedirected=" + Share.XpRedirected + " (" + Share.XpRedirectedAmount + ") pickupsTouched=" + Share.PickupsAssigned + " claimedByClone=" + Share.ClaimedByClone + " hostLevel=" + LevelOf(host) + " " + LevelRounds.State);
                            Shots.Take("levelup-done");
                            bool banner = LevelBanner.Shown >= 2 && (LevelBanner.LastTitle.StartsWith("PLAYER") || LevelBanner.LastTitle.StartsWith("SPIELER"));
                            { var cr = LevelBanner.LastColorP1; bool red = cr.r > 0.9f && cr.g < 0.3f && cr.b < 0.3f; Check("player 1's level-up title is red (as the chooser frame), not yellow", red, "colour " + cr); banner = banner && red; }
                            Lc.Line("ui: levelup banner: forms=" + LevelBanner.Shown + " buzzes=" + LevelBanner.Buzzes + " last title='" + LevelBanner.LastTitle + "'");
                            Finish(banner && order && back && running && xp && LevelRounds.RoundsFinished == 2, "picks=[" + picks + "] contextBack=" + back + " running=" + running + " xpRedirected=" + xp);
                        }
                        break;
                }
            }
            catch (Exception e) { Finish(false, "threw " + e.GetType().Name + ": " + e.Message); }
        }

        // ---- pickups: the dwarf that touches one collects it ------------------------------------------------------------------
        private static int _kStep;
        private static float _kAt = -1f, _kInRun = -1f, _kLog;

        private static void PickupTick()
        {
            if (_done) return;
            float now = Time.realtimeSinceStartup;
            if (now < _kAt) return;
            if (_kStep == 0 && (MenuEntry.EntryButton == null || !CoopMenu.Built)) return;
            _kAt = now + 0.5f;
            try
            {
                switch (_kStep)
                {
                    case 0: MenuEntry.EntryButton.OnClick.Invoke(); _kStep++; break;
                    case 1: Click("P1class3"); Click("P2class1"); Click("sector0"); Click("danger1"); Click("biome3"); _kStep++; break;
                    case 2: Click("start"); _kStep++; _kAt = now + 3f; break;
                    default:
                        if (Lc.InRunAt < 0f) { if (Lc.EverInRun) Finish(false, "the run ended before the check"); else if (now > _kAt + 60f) Finish(false, "the run did not start"); return; }
                        if (_kInRun < 0f) _kInRun = now;
                        if (now > _kLog + 5f) { _kLog = now; Lc.Line("ui: pickups in the scene: " + UnityEngine.Object.FindObjectsOfType<Pickup>().Length + "; ui: pickup counters: touches=" + Share.TouchCalls + " assigned=" + Share.PickupsAssigned + " claimedHost=" + Share.ClaimedByHost + " claimedClone=" + Share.ClaimedByClone); }
                        if (now < _kInRun + 70f) return;
                        {
                            bool pass = Share.ClaimedByClone > 0 && Share.PickupsAssigned > 0;
                            Finish(pass, "touches=" + Share.TouchCalls + " assigned=" + Share.PickupsAssigned + " claimedHost=" + Share.ClaimedByHost + " claimedClone=" + Share.ClaimedByClone);
                        }
                        break;
                }
            }
            catch (Exception e) { Finish(false, "threw " + e.GetType().Name + ": " + e.Message); }
        }

        // ---- two wallets and the shop, twice -------------------------------------------------------------------------------------
        private static int _wStep;
        private static float _wAt = -1f, _wInRun = -1f, _wWait;
        private static int _wHost0, _wW20, _wPriceHost, _wPriceP2, _pinI = -1, _pinJ = -1;
        private static IntPtr _pinSet;

        private static UIShopScreen ShopScreen()
        {
            foreach (var f in UnityEngine.Object.FindObjectsOfType<UIShopScreen>(true)) if (f != null && f.gameObject.activeInHierarchy) return f;
            return null;
        }

        /// <summary>Press the first shop card the screen's current wallet can pay, through the card's own click; returns its price (0 = none).</summary>
        private static int BuyFirstAffordable(UIShopScreen screen)
        {
            var click = HarmonyLib.AccessTools.Method(typeof(UIShopButton), "OnButtonClick");
            foreach (var b in screen.GetComponentsInChildren<UIShopButton>(false))
            {
                if (b == null || !b.gameObject.activeInHierarchy || b.price == null || b.price.Value <= 0 || b.price.Type != ECurrency.GOLD) continue;   // gold-priced cards only: the checks compare gold
                if (!b.wallet.CanAfford(b.price)) continue;
                int price = b.price.Value;
                click.Invoke(b, null);
                return price;
            }
            return 0;
        }

        private static int W2Gold() { return Wallets.W2 != null ? Wallets.W2.Gold : -1; }

        private static int Digits(string t) { int v = 0; bool any = false; foreach (char c in t ?? "") if (c >= '0' && c <= '9') { v = v * 10 + (c - '0'); any = true; } return any ? v : -1; }

        private static void WalletTick()
        {
            if (_done) return;
            float now = Time.realtimeSinceStartup;
            if (now < _wAt) return;
            if (_wStep == 0 && (MenuEntry.EntryButton == null || !CoopMenu.Built)) return;
            _wAt = now + 0.6f;
            try
            {
                switch (_wStep)
                {
                    case 0: MenuEntry.EntryButton.OnClick.Invoke(); _wStep++; break;
                    case 1: Click("P1class3"); Click("P2class1"); Click("sector0"); Click("danger1"); Click("biome3"); _wStep++; break;
                    case 2: Click("start"); _wStep++; _wAt = now + 3f; break;
                    default:
                        if (Lc.InRunAt < 0f) { if (Lc.EverInRun) Finish(false, "the run ended before the check"); else if (now > _wAt + 60f) Finish(false, "the run did not start"); return; }
                        if (_wInRun < 0f) _wInRun = now;
                        if (now < _wInRun + 16f) return;
                        WalletStep(now);
                        break;
                }
            }
            catch (Exception e) { Finish(false, "threw " + e.GetType().Name + ": " + e.Message); }
        }

        private static void WalletStep(float now)
        {
            var host = Wallets.Host();
            switch (_wStep++)
            {
                case 3:
                    {
                        Wallets.Ensure();
                        int h0 = host.Gold, w0 = W2Gold();
                        host.Add(ECurrency.GOLD, Lc.Has("-lcdoc") ? 120 : 2000, false);
                        host.Add(new CurrencyValue(ECurrency.GOLD, 30));
                        host.Add(ECurrency.NITRA, 7, false);
                        Check("income reaches both wallets in full", Wallets.W2 != null && host.Gold - h0 == (Lc.Has("-lcdoc") ? 150 : 2030) && W2Gold() - w0 == (Lc.Has("-lcdoc") ? 150 : 2030) && Wallets.W2.Nitra >= 7, "host gold " + h0 + " -> " + host.Gold + ", P2 gold " + w0 + " -> " + W2Gold() + ", P2 nitra " + (Wallets.W2 != null ? Wallets.W2.Nitra : -1));
                    }
                    break;
                case 4:
                    {
                        // open the shop screen the way the game does between stages (UIShopScreen.Show with the skill collection and the offered types)
                        var ui = Find.One<UIManager>();
                        var types = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<ESkillType>(3);
                        types[0] = ESkillType.SUPPORT; types[1] = ESkillType.WEAPON_UPGRADE; types[2] = ESkillType.ARTIFACT;
                        ui.ShopScreen.Show(CoopStart.Resolve<SkillCollectionManager>(), types, true);
                    }
                    _wWait = now;
                    break;
                case 5:
                    {
                        var sc = ShopScreen();
                        if (sc == null && now < _wWait + 10f) { _wStep--; return; }
                        if (sc != null && Lc.Has("-lcdoc") && now < _wWait + 3.5f) { _wStep--; return; }      // doc shots: let the gold counter finish counting
                        Check("the shop does not save the run in a co-op run (no continue prompt without the second dwarf later)", Wallets.MidRunSkips >= 1, "skipped saves=" + Wallets.MidRunSkips);
                        Check("shop opens for the host with the host's wallet and player", sc != null && sc.wallet != null && sc.wallet.Pointer == host.Pointer && sc.player != null && !Share.IsClone(sc.player), sc != null ? "wallet is host: " + (sc.wallet.Pointer == host.Pointer) : "no shop");
                        _wHost0 = host.Gold; _wW20 = W2Gold();
                        if (Lc.Has("-lcdoc")) Shots.Take("doc-shop-p1");
                        _wPriceHost = sc != null ? BuyFirstAffordable(sc) : 0;
                        _pinI = -1; _pinJ = -1; _pinSet = IntPtr.Zero;
                        if (sc != null) for (int i = 0; i < sc.shopButtons.Length; i++) { var b = sc.shopButtons[i]; if (b != null && b.gameObject.activeInHierarchy && b.HasContent()) { _pinI = i; _pinSet = b.skillRaritySet.Pointer; b.pinnedToggle.ForceValue(true); break; } }
                    }
                    break;
                case 6:
                    Check("host's shop: PLAYER 1 above the title, the title in player 1's colour, no corner banner", Chooser.ShopTitleOk(1) && !Chooser.PanelShown, Chooser.ShopInfo + " who=" + Chooser.Who + " panel=" + Chooser.PanelShown);
                    Check("host's purchase is paid by the host only", _wPriceHost > 0 && host.Gold == _wHost0 - _wPriceHost && W2Gold() == _wW20, "price " + _wPriceHost + ", host gold " + _wHost0 + " -> " + host.Gold + ", P2 gold " + _wW20 + " -> " + W2Gold());
                    ShopScreen().OnPlayButton();                                   // leaving the host's shop opens P2's
                    _wWait = now;
                    break;
                case 7:
                    {
                        var sc = ShopScreen();
                        if (sc != null && Lc.Has("-lcdoc") && now < _wWait + 3.5f) { _wStep--; return; }
                        Check("leaving the host's shop opens P2's: its player and wallet", sc != null && Wallets.P2ShopActive && Wallets.W2 != null && sc.wallet.Pointer == Wallets.W2.Pointer && Share.IsClone(sc.player), sc != null ? "p2ShopActive=" + Wallets.P2ShopActive + " clonePlayer=" + Share.IsClone(sc.player) : "shop closed");
                        if (sc == null) { _wStep = 99; return; }
                        _wHost0 = host.Gold; _wW20 = W2Gold();
                        if (Lc.Has("-lcdoc")) Shots.Take("doc-shop-p2");
                        _wPriceP2 = BuyFirstAffordable(sc);
                        Check("the host's pinned card is not pinned in P2's shop", _pinI >= 0 && !Wallets.CardPinned(sc, _pinI), "card " + _pinI + " pinned=" + (_pinI >= 0 && Wallets.CardPinned(sc, _pinI)));
                        for (int i = 0; i < sc.shopButtons.Length; i++) { var b = sc.shopButtons[i]; if (i != _pinI && b != null && b.gameObject.activeInHierarchy && b.HasContent()) { _pinJ = i; b.pinnedToggle.ForceValue(true); break; } }
                        if (Lc.Has("-lcshoparrows")) { Bonus.ShopProbe(sc); Check("hovering a card of P2's shop never lights player 1's bar", Bonus.ShopProbeCards > 0 && Bonus.ShopProbeLit1 == 0, "cards=" + Bonus.ShopProbeCards + " lit bar1=" + Bonus.ShopProbeLit1 + " lit bar2=" + Bonus.ShopProbeLit2); }
                        Shots.Take("shop-p2");
                    }
                    break;
                case 8:
                    Check("P2's shop: PLAYER 2 above the title, the title in player 2's colour, no corner banner", Chooser.ShopTitleOk(2) && !Chooser.PanelShown, Chooser.ShopInfo + " who=" + Chooser.Who + " panel=" + Chooser.PanelShown);
                    Check("P2's purchase is paid by P2's wallet only", _wPriceP2 > 0 && W2Gold() == _wW20 - _wPriceP2 && host.Gold == _wHost0, "price " + _wPriceP2 + ", P2 gold " + _wW20 + " -> " + W2Gold() + ", host gold " + _wHost0 + " -> " + host.Gold);
                    ShopScreen().OnPlayButton();                                   // second leave: really leaves
                    _wWait = now;
                    break;
                case 9:
                    {
                        var sc = ShopScreen();
                        var any = UnityEngine.Object.FindObjectsOfType<UIShopScreen>(true);
                        bool back = any.Length > 0 && any[0].wallet != null && any[0].wallet.Pointer == host.Pointer && !Share.IsClone(any[0].player);
                        Check("shop closes and everything is back with the host", sc == null && back && !Wallets.P2ShopActive && Time.timeScale > 0f, "open=" + (sc != null) + " back=" + back + " timeScale=" + Time.timeScale);
                        bool pinsOk = any.Length > 0 && _pinI >= 0 && _pinJ >= 0 && Wallets.CardPinned(any[0], _pinI) && !Wallets.CardPinned(any[0], _pinJ) && any[0].shopButtons[_pinI].skillRaritySet.Pointer == _pinSet;
                        Check("after both shops the host's pin is back and P2's is not on the host's screen", pinsOk, "host card " + _pinI + " pinned=" + (any.Length > 0 && _pinI >= 0 && Wallets.CardPinned(any[0], _pinI)) + ", P2 card " + _pinJ + " pinned=" + (any.Length > 0 && _pinJ >= 0 && Wallets.CardPinned(any[0], _pinJ)));
                        _wHost0 = host.Gold; _wW20 = W2Gold();
                        host.Add(ECurrency.GOLD, 100, false);
                    }
                    break;
                case 10:
                    Check("after the shop, income still reaches both and they differ by what was spent", host.Gold - _wHost0 == 100 && W2Gold() - _wW20 == 100 && host.Gold != W2Gold() || (_wPriceHost == _wPriceP2 && host.Gold - _wHost0 == 100 && W2Gold() - _wW20 == 100), "host " + _wHost0 + " -> " + host.Gold + ", P2 " + _wW20 + " -> " + W2Gold() + ", prices " + _wPriceHost + "/" + _wPriceP2);
                    break;
                case 11:
                    _wHost0 = host.Gold; _wW20 = W2Gold();
                    Wallets.W2.Spend(ECurrency.GOLD, 100);                       // what a purchase of P2 does to P2's wallet
                    _wAt = now + 2.5f;
                    break;
                case 12:
                    {
                        int p1 = Digits(WalletHud.P1GoldText), p2 = Digits(WalletHud.P2GoldText);
                        Check("currency display has two lines: P1 = host wallet, P2 = second wallet", WalletHud.Built && p1 == host.Gold && p2 == W2Gold() && p1 != p2 && host.Gold == _wHost0 && W2Gold() == _wW20 - 100, "P1 line " + WalletHud.P1GoldText + " (host " + host.Gold + "), P2 line " + WalletHud.P2GoldText + " (P2 " + W2Gold() + ")");
                        Shots.Take("wallet-lines");
                        _wAt = now + 1.5f;
                    }
                    break;
                default:
                    _done = true;
                    Lc.QuitSent = true;
                    Lc.Line("RESULT " + Lc.Test + " " + (_fail == 0 && _ok == 13 ? "PASS" : "FAIL") + " co-op wallets ok=" + _ok + " fail=" + _fail);
                    Application.Quit();
                    break;
            }
        }

        // ---- probe: where the HUD keeps the currency counters ------------------------------------------------------------------
        private static int _hStep; private static float _hAt = -1f, _hIn = -1f;

        private static void HudProbeTick()
        {
            if (_done) return;
            float now = Time.realtimeSinceStartup;
            if (now < _hAt) return;
            if (_hStep == 0 && (MenuEntry.EntryButton == null || !CoopMenu.Built)) return;
            _hAt = now + 0.6f;
            try
            {
                switch (_hStep)
                {
                    case 0: MenuEntry.EntryButton.OnClick.Invoke(); _hStep++; break;
                    case 1: Click("start"); _hStep++; _hAt = now + 3f; break;
                    default:
                        if (Lc.InRunAt < 0f) return;
                        if (_hIn < 0f) _hIn = now;
                        if (now < _hIn + 12f) return;
                        var cw = Find.One<UICoreWallet>();
                        Lc.Line("hudprobe: UICoreWallet=" + (cw != null));
                        if (cw != null)
                        {
                            for (Transform t = cw.goldCounter.transform; t != null && t.parent != null; t = t.parent)
                            {
                                var r = t.GetComponent<RectTransform>();
                                var sb = new System.Text.StringBuilder();
                                foreach (var c in t.GetComponents<Component>()) sb.Append(c.GetIl2CppType().Name).Append(' ');
                                Lc.Line("hudprobe: " + t.name + " pos=" + (r != null ? r.anchoredPosition.ToString() : "-") + " size=" + (r != null ? r.sizeDelta.ToString() : "-") + " anchors=" + (r != null ? r.anchorMin + "/" + r.anchorMax : "-") + " children=" + t.childCount + " comps=" + sb);
                                if (t == cw.transform) break;
                            }
                            var row = cw.goldCounter.transform.parent;
                            for (int i = 0; i < row.childCount; i++) Lc.Line("hudprobe:   row child " + row.GetChild(i).name + " active=" + row.GetChild(i).gameObject.activeSelf);
                            Lc.Line("hudprobe: nitraCounter parent=" + cw.nitraCounter.transform.parent.name + " goldCounter=" + cw.goldCounter.name);
                        }
                        Shots.Take("hudprobe");
                        Finish(true, "probe");
                        break;
                }
            }
            catch (Exception e) { Finish(false, "threw " + e.GetType().Name + ": " + e.Message); }
        }

        // ---- the stage change with two dwarves: the game's own end of stage -> shop (twice) -> next stage ---------------------------
        private static int _gStep;
        private static float _gAt = -1f, _gIn = -1f, _gT0 = -1f, _gLog;
        private static IntPtr _gHost0, _gClone0;
        private static int _gGold0 = -1, _gCycle;
        private static int GCycles { get { float n = Lc.Num("-lcstages="); return n >= 1f ? (int)n : 1; } }

        private static bool _hitCloneOk;
        private static string HitInfo(Player target, Player attacker)
        {
            try
            {
                var b = target.currentHp;
                var r = target.TakeDamage(60, false, EDamageType.KINETIC, false, EDamageSource.PROJECTILE, attacker, false);
                bool ok = target.currentHp < b;
                if (target.Pointer == Phase1.Second.Pointer) _hitCloneOk = ok;
                return "hp " + b + " -> " + target.currentHp + " r=" + r;
            }
            catch (Exception ex) { return "threw " + ex.GetType().Name + ": " + ex.Message; }
        }

        private static string StageStatus(out bool good)
        {
            var host = Phase1.First(); var clone = Phase1.Second;
            int players = 0; bool cloneInScene = false, hostInScene = false;
            foreach (var p in UnityEngine.Object.FindObjectsOfType<Player>(false))
            {
                if (p == null) continue;
                players++;
                if (clone != null && p.Pointer == clone.Pointer) cloneInScene = true;
                if (host != null && p.Pointer == host.Pointer) hostInScene = true;
            }
            bool sameHost = host != null && host.Pointer == _gHost0, sameClone = clone != null && clone.Pointer == _gClone0;
            float sep = host != null && clone != null && cloneInScene ? Vector3.Distance(host.transform.position, clone.transform.position) : -1f;
            string stage = "?";
            try { var gc = Find.One<GameController>(); stage = gc != null ? gc.secondsInStage.ToString() : "?"; } catch { }
            bool w2 = Wallets.W2 != null && Wallets.W2.Gold >= 0;
            good = host != null && host.isAlive && clone != null && cloneInScene && clone.isAlive && players == 2 && sep >= 0f && sep < 30f && Hud2.Built && w2 && Time.timeScale > 0f && clone.weapons != null && clone.weapons.Count > 0;
            return "players=" + players + " hostInScene=" + hostInScene + " cloneInScene=" + cloneInScene + " sameHost=" + sameHost + " sameClone=" + sameClone + " sep=" + sep.ToString("F1")
                + " hud2=" + Hud2.Built + " W2gold=" + (Wallets.W2 != null ? Wallets.W2.Gold : -1) + " cloneWeapons=" + (clone != null && clone.weapons != null ? clone.weapons.Count : -1)
                + " secondsInStage=" + stage + " timeScale=" + Time.timeScale + " camK=" + Cam2.K.ToString("F2");
        }

        private static void StageTick()
        {
            if (_done) return;
            float now = Time.realtimeSinceStartup;
            if (now < _gAt) return;
            if (_gStep == 0 && (MenuEntry.EntryButton == null || !CoopMenu.Built)) return;
            _gAt = now + 0.6f;
            try
            {
                switch (_gStep)
                {
                    case 0: if (Lc.Has("-lctwopads")) VirtualPad.Pad1Touch(); MenuEntry.EntryButton.OnClick.Invoke(); _gStep++; break;
                    case 1: if (Lc.Has("-lcstageescort")) Click("mission1"); Click("P1class3"); Click("P2class1"); Click("sector0"); Click("danger1"); Click("biome3"); _gStep++; break;
                    case 2: Click("start"); _gStep++; _gAt = now + 3f; break;
                    case 3:
                        if (Lc.InRunAt < 0f) { if (Lc.EverInRun) Finish(false, "the run ended before the stage change"); else if (now > _gAt + 60f) Finish(false, "the run did not start"); return; }
                        if (_gIn < 0f) _gIn = now;
                        if (now < _gIn + 16f) return;
                        {
                            var gc = Find.One<GameController>();
                            _gHost0 = Phase1.First().Pointer; _gClone0 = Phase1.Second.Pointer;
                            Wallets.Ensure(); Wallets.Host().Add(ECurrency.GOLD, 300, false);
                            _gGold0 = Wallets.W2.Gold;
                            bool g0; Lc.Line("stage: before: " + StageStatus(out g0));
                            Lc.Line("stage: hits before the stage change: host " + HitInfo(Phase1.First(), Phase1.First()) + " | clone " + HitInfo(Phase1.Second, Phase1.First()));
                            if (Lc.Has("-lcstagekill")) Phase1.Second.gameObject.SetActive(false);   // twin: the second dwarf is lost at the stage change, the check must notice
                            gc.TransitionToShop(false);                      // what the game does when a stage ends
                            _gT0 = now; _gStep++;
                            Lc.Line("stage: TransitionToShop called (the game's own end of stage)");
                        }
                        break;
                    case 4:
                        {
                            bool good; string st = StageStatus(out good);
                            if (now > _gLog + 1.5f) { _gLog = now; try { var hh = Phase1.First(); var cc = Phase1.Second; Lc.Line("stage: weapons host/clone active=" + hh.weaponsActive + "/" + cc.weaponsActive + " allPaused=" + Revive.AllPaused(hh) + "/" + Revive.AllPaused(cc) + " pod=" + (Pod.Find1() != null ? Pod.Find1().state.ToString() : "-") + " form=" + AutoStart.ActiveFormName() + " timeScale=" + Time.timeScale); } catch { } Lc.Line("stage: " + st + " shops closed by the harness=" + AutoStart.ShopsClosed + " p2ShopActive=" + Wallets.P2ShopActive + " shopRounds=" + Wallets.ShopRoundsFinished + " form=" + AutoStart.ActiveFormName()); }
                            bool newStage = Wallets.ShopRoundsFinished >= _gCycle + 1 && Lc.InRunAt >= 0f && now > _gT0 + 20f && AutoStart.ActiveFormName() == "UICoreForm";
                            if (!newStage && now < _gT0 + 100f) return;
                            if (now < _gT0 + 30f) return;
                            if (_gCycle + 1 < GCycles)
                            {
                                bool same = Phase1.First() != null && Phase1.First().Pointer == _gHost0 && Phase1.Second != null && Phase1.Second.Pointer == _gClone0;
                                try
                                {
                                    var gcx = Find.One<GameController>();
                                    var eh = gcx.objectiveTracker != null ? gcx.objectiveTracker.eggHuntMissionHandler : null;
                                    var spx = eh != null ? eh.enemySpawner : null;
                                    Lc.Line("stage: mission=" + CoopStart.Rsm().MainMissionType + " IsBossStage=" + (eh != null ? eh.IsBossStage().ToString() : "n/a") + " liveBosses=" + (spx != null && spx.liveBosses != null ? spx.liveBosses.Count : -1) + " liveElites=" + (spx != null && spx.liveElites != null ? spx.liveElites.Count : -1));
                                }
                                catch (Exception ex) { Lc.Line("stage: boss probe threw " + ex.GetType().Name); }
                                Lc.Line("stage: cycle " + (_gCycle + 1) + "/" + GCycles + " done: good=" + good + " sameInstances=" + same + " newStage=" + newStage + " " + st);
                                Shots.Take("stage-cycle" + (_gCycle + 1));
                                if (!good || !same || !newStage) { Finish(false, "cycle " + (_gCycle + 1) + " failed: good=" + good + " same=" + same + " newStage=" + newStage + " " + st); return; }
                                _gCycle++;
                                Wallets.Host().Add(ECurrency.GOLD, 300, false);
                                Find.One<GameController>().TransitionToShop(false);
                                _gT0 = now;
                                return;
                            }
                            {
                                Shots.Take("stage2");
                                bool sameHost = Phase1.First() != null && Phase1.First().Pointer == _gHost0;
                                Lc.Line("stage: after: " + st);
                                // hand test: the second dwarf could not take damage from stage 2 on
                                bool dmgOk = false; string dmgInfo;
                                try
                                {
                                    var c2 = Phase1.Second; var h2 = Phase1.First();
                                    string inv = "invulnerable=" + c2.IsInvulnerable + " god=" + c2.GOD_MODE + " immuneUntil=" + c2.damageImmuneUntil + " now=" + Time.time + " hostInvulnerable=" + h2.IsInvulnerable;
                                    string hostHit = HitInfo(h2, h2), cloneHit = HitInfo(c2, h2);
                                    dmgOk = _hitCloneOk;
                                    dmgInfo = inv + " | host: " + hostHit + " | clone: " + cloneHit;
                                }
                                catch (Exception ex) { dmgInfo = "threw " + ex.GetType().Name + ": " + ex.Message; }
                                Lc.Line("stage: damage to the second dwarf in the new stage: ok=" + dmgOk + " " + dmgInfo);
                                _gFinalOk = good && newStage && Wallets.ShopRoundsFinished >= GCycles && Wallets.W2.Gold >= 0;
                                _gFinalInfo = "cycles=" + GCycles + " shopRounds=" + Wallets.ShopRoundsFinished + " " + st + " W2gold before/after " + _gGold0 + "/" + Wallets.W2.Gold;
                                _gH0 = Phase1.First().currentHp; _gC0 = Phase1.Second.currentHp; _gT0 = now; _gStep = 5;
                            }
                        }
                        break;
                    case 5:      // enemies of the new stage against both dwarves: the second one must lose hit points when the first one does (hand test)
                        {
                            var h = Phase1.First(); var c = Phase1.Second;
                            if (now > _gLog + 5f) { _gLog = now; Lc.Line("stage: watch: host hp " + h.currentHp + " clone hp " + c.currentHp + TargetInfoLine() + " | inPod host/clone=" + h.isInDropPod + "/" + c.isInDropPod + " invulnerable host/clone=" + h.IsInvulnerable + "/" + c.IsInvulnerable + " control host/clone=" + h._moveControlType + "/" + c._moveControlType + " alive=" + h.isAlive + "/" + c.isAlive); }
                            if (now < _gT0 + 45f) return;
                            bool hostLost = h.currentHp < _gH0, cloneLost = c.currentHp < _gC0;
                            Lc.Line("stage: watch result: host hp " + _gH0 + " -> " + h.currentHp + ", clone hp " + _gC0 + " -> " + c.currentHp + " | direct hits ok=" + _hitCloneOk);
                            Finish(_gFinalOk && (cloneLost || !hostLost), _gFinalInfo + " | hostLost=" + hostLost + " cloneLost=" + cloneLost);
                        }
                        break;
                }
            }
            catch (Exception e) { Finish(false, "threw " + e.GetType().Name + ": " + e.Message); }
        }

        private static bool _gFinalOk; private static string _gFinalInfo = ""; private static long _gH0, _gC0;
        private static string TargetInfoLine() { return Phase1.TargetInfo(); }

        private static int _sStep;
        private static int _wantBiome { get { float b = Lc.Num("-lcuibiome="); return b >= 1f ? (int)b - 1 : 3; } }   // -lcuibiome=1..6
        private static float _sAt = -1f, _inRunSince = -1f;

        /// <summary>`-lcui=start`: choose in the menu, press START RUN for real, and check the live run: the chosen biome, danger, class
        /// and mission type are what the game runs with, and the second dwarf exists.</summary>
        private static void StartTick()
        {
            if (_done) return;
            float now = Time.realtimeSinceStartup;
            if (now < _sAt) return;
            if (_sStep == 0 && (MenuEntry.EntryButton == null || !CoopMenu.Built)) return;
            _sAt = now + 0.6f;
            try
            {
                switch (_sStep)
                {
                    case 0: MenuEntry.EntryButton.OnClick.Invoke(); _sStep++; break;
                    case 1:
                        Click("P1class" + (Lc.Has("-lcgunners") ? 0 : Lc.Num("-lcp1class=") >= 1f ? (int)Lc.Num("-lcp1class=") : 3)); Click(Lc.Has("-lcgunners") ? "P2class0" : Lc.Has("-lcsameclass") ? "P2class3" : "P2class1"); if (Lc.Num("-lcsub=") >= 1f) { Click("P1sub" + (int)Lc.Num("-lcsub=")); Click("P2sub" + (int)Lc.Num("-lcsub=")); } Click(Lc.Has("-lcmission1") ? "mission1" : "mission0"); Click("sector0"); Click("danger1"); Click("biome" + _wantBiome);
                        Lc.Line("ui: chosen " + CoopMenu.Summary());
                        _sStep++; break;
                    case 2: Click("start"); _sStep++; _sAt = now + 3f; break;
                    default:
                        if (Lc.InRunAt < 0f && Lc.Has("-lcendtest") && !EndScreen2.TestDone) return; if (Lc.InRunAt < 0f) { if (Lc.EverInRun) { if (Lc.Has("-lcrevivetest") && (Revive.TestDone || Revive.BothDown > 0)) Finish(Revive.Passed && Revive.BothDown > 0, "revive test " + Revive.Summary + ", game over after both were down: " + (Revive.BothDown > 0) + " (the test ends the run itself)"); else Finish(false, "the run ended before the check"); } else if (now > _sAt + 60f) Finish(false, "the run did not start"); return; }
                        if (_inRunSince < 0f) _inRunSince = now;
                        if ((Lc.Has("-lcrevivetest") && !Revive.TestDone) || (Lc.Has("-lccratetest") && !Bonus.CrateDone) || (Lc.Has("-lcslotarrows") && !Bonus.ArrowDone) || (Lc.Has("-lcarrow2test") && !Arrow2.TestDone) || (Lc.Has("-lcdrilltest") && !DrillSound.TestDone) || (Lc.Has("-lcendtest") && !EndScreen2.TestDone) || (Lc.Has("-lcprogresstest") && !RunProgress.TestDone) || (Lc.Has("-lcsupplytest") && !LevelBanner.TestDone) || (Lc.Has("-lcbuff2test") && !Buffs2.TestDone) || (Lc.Has("-lcminimap2test") && !MiniMap2.TestDone) || (Lc.Has("-lcpausetest") && !PauseView.TestDone) || (Lc.Has("-lcpodtest") && !Pod.TestDone) || (Lc.Has("-lchotrocktest") && !Zones.HotDone) || (Lc.Has("-lcbobbytest") && !Zones.BobbyDone) || (Lc.Has("-lcplanttest") && !Zones.PlantDone)) return;      // these tests need a quiet run, the start check waits for them
                        if (now < _inRunSince + 22f) return;
                        {
                            var rsm = CoopStart.Rsm();
                            var gc = Find.One<GameController>();
                            string live = "mission=" + rsm.MainMissionType + " biome=" + CoopMenu.BiomeName(rsm.BiomeLevelData) + " danger=" + (rsm.HazardLevelIndex + 1)
                                + " class=" + rsm.CurrentClass.Dwarf + " mod=" + CoopMenu.SubName(rsm.CurrentClassMod);
                            Lc.Line("ui: live run settings: " + live);
                            bool settings = live.Contains("mission=Elimination") && live.Contains("biome=" + CoopMenu.BiomeName(CoopMenu.Biomes[_wantBiome])) && live.Contains("danger=2") && live.Contains("class=" + (Lc.Has("-lcgunners") ? "GUNNER" : Lc.Num("-lcp1class=") >= 1f ? CoopMenu.Classes[(int)Lc.Num("-lcp1class=")].Dwarf.ToString() : "SCOUT"));      // the expected host class follows the test flags
                            bool second = Phase1.Second != null && Phase1.Second.isAlive;
                            bool hostAlive = gc != null && gc.player != null && gc.player.isAlive;
                            var sec = Phase1.Second;
                            string cls2 = sec != null && sec.CurrentClass != null ? sec.CurrentClass.Dwarf.ToString() : "null";
                            string w2 = sec != null && sec.weapons != null && sec.weapons.Count > 0 ? sec.weapons[0].data.name : "none";
                            string w1 = gc != null && gc.player != null && gc.player.weapons.Count > 0 ? gc.player.weapons[0].data.name : "none";
                            var wantSub = CoopMenu.P2.Sub;
                            bool p2ok = cls2 == CoopMenu.Classes[CoopMenu.P2.ClassIndex].Dwarf.ToString() && wantSub != null && wantSub.StarterWeapon != null && w2 == wantSub.StarterWeapon.name && w2 != w1;
                            Lc.Line("ui: second dwarf class=" + cls2 + " weapon=" + w2 + " (host weapon " + w1 + ") wanted " + CoopMenu.Classes[CoopMenu.P2.ClassIndex].Dwarf + "/" + (wantSub != null && wantSub.StarterWeapon != null ? wantSub.StarterWeapon.name : "null"));
                            bool skinOk = Phase1.SkinReport.IndexOf("Model_" + CoopMenu.Classes[CoopMenu.P2.ClassIndex].Dwarf, StringComparison.OrdinalIgnoreCase) >= 0 && Phase1.SkinReport.Contains("skinLoaded=True");
                            Lc.Line("ui: second dwarf model: " + Phase1.SkinReport + " ok=" + skinOk);
                            bool chaseOk = Pod.ChaseFrames > 10 && Pod.MaxMoveSpeed > 0.5f && Pod.MaxLag > 0.5f;
                            Lc.Line("ui: CHECK " + (chaseOk ? "ok  " : "FAIL") + " the second dwarf ran after the host out of the pod with its run animation - chase frames=" + Pod.ChaseFrames + " run animation=" + Pod.MaxMoveSpeed.ToString("F2") + " behind=" + Pod.MaxLag.ToString("F1") + " m");
                            string wantCtl = CoopMenu.Classes[CoopMenu.P2.ClassIndex].Dwarf.ToString() == "DRILLER" ? "CTRL_Dwarves_Redux_DRILLER" : "CTRL_Dwarves_Redux"; string gotCtl = Phase1.CloneControllerName();
                            bool ctlOk = gotCtl == wantCtl;
                            Lc.Line("ui: CHECK " + (ctlOk ? "ok  " : "FAIL") + " the second dwarf runs on the animator controller of its own class - wanted " + wantCtl + " got " + gotCtl);
                            settings = settings && p2ok && skinOk && chaseOk && ctlOk;
                            Shots.Take("coop-run");
                            Finish(settings && second && hostAlive, live + " secondDwarf=" + second + " hostAlive=" + hostAlive);
                        }
                        break;
                }
            }
            catch (Exception e) { Finish(false, "threw " + e.GetType().Name + ": " + e.Message); }
        }

        internal static void FinishWith(bool pass, string detail) { Finish(pass, detail); }

        private static void Finish(bool pass, string detail)
        {
            _done = true;
            Lc.QuitSent = true;
            Lc.Line("RESULT " + Lc.Test + " " + (pass ? "PASS" : "FAIL") + " co-op start " + detail);
            Application.Quit();
        }

        private static bool ReadbackMatches()
        {
            string r = CoopStart.Readback;
            var biome = CoopMenu.BiomeName(CoopMenu.Biomes[3]);
            return r.Contains("mission=" + (EMainMissionType)0) && r.Contains("biome=" + biome) && r.Contains("danger=3")
                && r.Contains("class=" + CoopMenu.Classes[3].Dwarf) && r.Contains("mod=" + CoopMenu.SubName(CoopMenu.P1.Sub)) && r.Contains("sectorIdx=2");
        }
    }
}
