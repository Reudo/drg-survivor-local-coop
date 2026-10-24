using System;
using DRS.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace LocalCoop
{
    /// <summary>
    /// `-lcui=flow` (single pad / mouse path) and `-lcui=flowpad` (two virtual pads, the menu split between the controllers): the co-op start as the owner
    /// sees it. LOCAL CO-OP opens a lobby; the host's large mission page shows the goals; CONFIRM MISSION opens the split-screen pick where both players
    /// choose class and subclass at the same time; player 2 readies up on the final page, the host starts; two dwarves of the chosen classes are in the run.
    /// Twins: `-lcnobig` (goals not on the large page), `-lcpickseq` (player 2's half waits for player 1), `-lcnoready` (start without player 2's READY),
    /// `-lcnop2class` (clone keeps the host's class), `-lcpadsplitoff` (flowpad: every pad reaches everything).
    /// </summary>
    internal static class FlowTest
    {
        private static int _s;
        private static float _at = -1f, _t0, _inRun = -1f;
        private static int _ok, _fail;
        private static float _giveUp = 150f;
        private static bool Pad { get { return Lc.Str("-lcui=") == "flowpad"; } }
        private static int _c1, _c2, _sub1, _sub2;
        private static string _goalsBefore = "";

        private static void Check(string name, bool ok, string detail)
        {
            if (ok) _ok++; else _fail++;
            Lc.Line("flow: CHECK " + (ok ? "ok  " : "FAIL") + " " + name + " - " + detail);
        }

        private static bool _p2tog; private static int _readyWait;
        private static void Next(float now, float delay) { _s++; _t0 = 0f; _at = now + delay; }
        private static bool Active(string key) { UnityEngine.UI.Button b; return CoopMenu.Buttons.TryGetValue(key, out b) && b != null && b.gameObject.activeInHierarchy; }

        /// <summary>Press a co-op menu button the way the player would: pad 1 / pad 2 (focus set on the key, A pressed) or a click.</summary>
        private static bool _rev; private static int _revCheck;
        private static void Do(string key, int player)
        {
            if (!Pad) { UiTest.Click(key); return; }
            if (CoopMenu.PadHidden(key) && !Lc.Has("-lcpadfocusall"))
            {
                // the go-on and back buttons are not focused any more: the pad presses them with the menu button / B
                var btn = CoopMenu.PadHint(key) == "B" ? GamepadButton.East : (key == "start" ? GamepadButton.Start : GamepadButton.South);      // go-on buttons are A, only START RUN is the menu button
                if (player == 2) VirtualPad.Press(btn, 0.6f); else { MenuPad.PadUsed = true; VirtualPad.Press1(btn, 0.6f); }
                return;
            }
            // choices are made by moving onto them (no A): the test puts the focus there and lets the pad code select it, as a stick move would
            if (MenuPad.IsChoiceKey(key)) { if (player == 2) MenuPad.Focus2 = key; else { MenuPad.Focus = key; MenuPad.PadUsed = true; } MenuPad.FocusMoved(player == 2 ? 1 : 0); return; }
            if (player == 2) { MenuPad.Focus2 = key; VirtualPad.Press(GamepadButton.South); }
            else { MenuPad.Focus = key; MenuPad.PadUsed = true; VirtualPad.Press1(GamepadButton.South); }
        }

        internal static void Tick()
        {
            float now = Time.realtimeSinceStartup;
            if (now < _at) return;
            if (_s == 0 && (MenuEntry.EntryButton == null || !CoopMenu.Built)) return;
            _at = now + 0.3f;
            if (_s < 100 && now > _giveUp) { _giveUp = now + 1e9f; UiTest.FinishWith(false, "flow stalled at step " + _s + " (ok=" + _ok + " fail=" + _fail + ")"); return; }
            try { Step(now); }
            catch (Exception e) { Lc.Line("flow: tick threw " + e.GetType().Name + ": " + e.Message); UiTest.FinishWith(false, "threw " + e.GetType().Name + ": " + e.Message); }
        }

        private static string Cut(string t, int n) { t = t.Replace("\n", " / "); return t.Length > n ? t.Substring(0, n) : t; }

        private static void Step(float now)
        {
            switch (_s)
            {
                case 0:
                    if (Pad) VirtualPad.Pad1Touch();       // controller 1 = second virtual pad, controller 2 = the first one
                    Check("the old menu is not in use", !Flow.OldMenu, "oldMenu=" + Flow.OldMenu);
                    MenuEntry.EntryButton.OnClick.Invoke();
                    Next(now, 1.0f); break;
                case 1:
                    Check("the entry opens the lobby with the two mission types", CoopMenu.Visible && CoopMenu.Mode == "lobby" && Active("type0") && Active("type1") && !Active("start"), "visible=" + CoopMenu.Visible + " mode=" + CoopMenu.Mode);
                    Check("the lobby shows both players", CoopMenu.Values["lobbyP1"].text.Length > 0 && CoopMenu.Values["lobbyP2"].text.Length > 0, CoopMenu.Values["lobbyP1"].text + " | " + CoopMenu.Values["lobbyP2"].text);
                    Shots.Take("flow-lobby");
                    if (Pad)
                    {
                        Check("two controllers: the menu is split", MenuPad.Split || Lc.Has("-lcpadsplitoff"), "split=" + MenuPad.Split + " pads=" + Gamepad.all.Count);
                        MenuPad.Focus2 = "type0"; VirtualPad.Press(GamepadButton.South);       // controller 2 tries to choose the mission type
                    }
                    Next(now, 1.2f); break;
                case 2:
                    if (Pad) Check("controller 2 cannot choose the mission", CoopMenu.Mode == "lobby", "mode=" + CoopMenu.Mode);
                    Do("type0", 1);
                    Next(now, 1.0f); break;
                case 3:
                    Check("the mission type opens the sector page with its gates", CoopMenu.Mode == "sector" && Active("sec0") && Active("gat0") && Active("sec4"), "mode=" + CoopMenu.Mode);
                    Shots.Take("flow-sector");                                    // the picture is taken before the click
                    Next(now, 0.8f); break;
                case 4:
                    if (CoopMenu.Mode == "sector") { Do(Lc.Has("-lcflowgate") ? "gat0" : "sec0", 1); _at = now + 1.0f; break; }
                    Check("the sector opens the biome page", CoopMenu.Mode == "biome" && Active("bio0") && Active("bnext") && !Active("P1class0") && !Active("P1c0"), "mode=" + CoopMenu.Mode);
                    Check("no DLC biome is offered", !Active("bio5"), "bio5 active=" + Active("bio5"));
                    if (Lc.Has("-lcflowgate"))
                    {
                        int shown = 0, which = -1;
                        for (int i = 0; i < CoopMenu.Biomes.Length; i++) if (Active("bio" + i)) { shown++; which = i; }
                        Check("at a gate only the gate's biome is shown", CoopMenu.GateSel && shown == 1, "gate=" + CoopMenu.GateSel + " hexes shown=" + shown);
                        if (which >= 0) UiTest.Click("bio" + which);
                        Check("clicking the biome at a gate keeps the gate", CoopMenu.GateSel, "gate=" + CoopMenu.GateSel);
                        int shown2 = 0; for (int i = 0; i < CoopMenu.Biomes.Length; i++) if (Active("bio" + i)) shown2++;
                        Check("the whole choice does not come back", shown2 == 1, "hexes shown=" + shown2);
                    }
                    else UiTest.Click("bio3");
                    Next(now, 0.8f); break;
                case 5:
                    {
                        Check("the biome page shows the main goals of the choice", CoopMenu.Values["bgoals0"].text.Length > 4 && CoopMenu.Values["bgoals0"].text == CoopMenu.Values["goals0"].text && (CoopMenu.Values["btitle"].text.Contains(CoopMenu.BiomeName(CoopMenu.Biomes[CoopMenu.BiomeIndex]).ToUpperInvariant()) || (CoopMenu.Biomes[CoopMenu.BiomeIndex].BiomeData != null && CoopMenu.Values["btitle"].text.Contains(CoopMenu.Biomes[CoopMenu.BiomeIndex].BiomeData.DisplayName.ToUpperInvariant()))), "goals: " + Cut(CoopMenu.Values["bgoals0"].text, 120) + " | " + CoopMenu.Values["btitle"].text);
                        Shots.Take("flow-biome");
                        if (Pad) { MenuPad.Focus2 = "bnext"; VirtualPad.Press(GamepadButton.South); }     // controller 2 must not go on
                        Next(now, 0.8f); break;
                    }
                case 6:
                    if (Pad)
                    {
                        Check("controller 2 cannot go on from the biome page", CoopMenu.Mode == "biome", "mode=" + CoopMenu.Mode);
                        MenuPad.Focus = "bnext"; MenuPad.PadUsed = true; VirtualPad.Press1(GamepadButton.South);
                    }
                    else UiTest.Click("bnext");
                    Next(now, 1.0f); break;
                case 7:
                    {
                        Check("CONTINUE opens the danger page", CoopMenu.Mode == "danger" && Active("ddng0") && Active("confirm"), "mode=" + CoopMenu.Mode);
                        UiTest.Click("ddng1");
                        Check("the danger page shows the mission's goals and mutator and the chosen level", CoopMenu.Danger == 1 && CoopMenu.Values["bgoals1"].text.Length > 4 && CoopMenu.Values["bgoals1"].text == CoopMenu.Values["bgoals0"].text && CoopMenu.Values["bgoals2"].text.Length > 0 && CoopMenu.Values["dtitle"].text.Contains("DANGER 2"),
                            "goals: " + Cut(CoopMenu.Values["bgoals1"].text, 80) + " | " + CoopMenu.Values["dtitle"].text);
                        Shots.Take("flow-danger");
                        if (Pad) { MenuPad.Focus2 = "confirm"; VirtualPad.Press(GamepadButton.South); }     // controller 2 must not confirm
                        Next(now, 0.8f); break;
                    }
                case 8:
                    if (Pad)
                    {
                        Check("controller 2 cannot confirm the mission", CoopMenu.Mode == "danger", "mode=" + CoopMenu.Mode);
                        MenuPad.Focus = "confirm"; MenuPad.PadUsed = true; VirtualPad.Press1(GamepadButton.South);
                    }
                    else UiTest.Click("confirm");
                    Next(now, 1.0f); break;
                case 9:
                    {
                        Check("CONFIRM MISSION opens the split-screen pick: both halves at once", CoopMenu.Mode == "pick" && Active("P1c0") && Active("P2c0"), "mode=" + CoopMenu.Mode);
                        _c1 = -1; _c2 = -1;
                        for (int i = 0; i < 4; i++) if (CoopMenu.ClassOpen(i)) { if (_c1 < 0) _c1 = i; else if (_c2 < 0 && CoopMenu.Classes[i].Dwarf != CoopMenu.Classes[_c1].Dwarf) _c2 = i; }
                        if (_c1 < 0 || _c2 < 0) { UiTest.FinishWith(false, "need two open classes"); return; }
                        Do("P1c" + _c1, 1); Do("P2c" + _c2, 2);
                        Next(now, 1.0f); break;
                    }
                case 10:
                    Check("both players choose a class at the same time, independently", CoopMenu.P1.ClassIndex == _c1 && CoopMenu.P2.ClassIndex == _c2 && CoopMenu.Values["P1cdesc"].text.Length > 10 && CoopMenu.Values["P2cdesc"].text.Length > 10,
                        "P1=" + CoopMenu.Classes[CoopMenu.P1.ClassIndex].Dwarf + " P2=" + CoopMenu.Classes[CoopMenu.P2.ClassIndex].Dwarf + " | P1: " + Cut(CoopMenu.Values["P1cdesc"].text, 60));
                    Shots.Take("flow-pick-class");
                    if (Pad) { MenuPad.Focus = "P2next"; VirtualPad.Press1(GamepadButton.South); }          // controller 1 must not move player 2's page on
                    Next(now, 0.8f); break;
                case 11:
                    if (Pad) Check("controller 1 cannot touch player 2's half", CoopMenu.Stage[1] == "c", "P2 stage=" + CoopMenu.Stage[1]);
                    if (Pad) { MenuPad.Focus = "P1c" + _c1; MenuPad.PadUsed = true; MenuPad.FocusMoved(0); VirtualPad.Press1(GamepadButton.South); Do("P2next", 2); }       // A on the chosen class goes on (twin `-lcnoautoselect`: A only picks it again)
                    else { Do("P1next", 1); Do("P2next", 2); }
                    Next(now, 1.0f); break;
                case 12:
                    {
                        Check("both go on to the subclass page", CoopMenu.Stage[0] == "s" && CoopMenu.Stage[1] == "s" && Active("P1s0") && Active("P2s0"), "stages=" + CoopMenu.Stage[0] + CoopMenu.Stage[1]);
                        var s1 = CoopMenu.SubsFor(CoopMenu.Classes[_c1]); var s2 = CoopMenu.SubsFor(CoopMenu.Classes[_c2]);
                        _sub1 = s1.Count > 1 && CoopMenu.SubOpen(s1[1]) ? 1 : 0;
                        _sub2 = s2.Count > 1 && CoopMenu.SubOpen(s2[1]) ? 1 : 0;
                        Do("P1s" + _sub1, 1); Do("P2s" + _sub2, 2);
                        Next(now, 1.0f); break;
                    }
                case 13:
                    {
                        var w2 = CoopMenu.P2.Sub != null ? CoopMenu.P2.Sub.StarterWeapon : null;
                        Check("the subclass page explains the subclass and its starting weapon", CoopMenu.Values["P2t1"].text.Length > 1 && CoopMenu.Values["P2d1"].text.Length > 3 && w2 != null && CoopMenu.Values["P2t1"].text.Contains(CoopMenu.Clean(w2.Title)),
                            "P2 weapon box: " + CoopMenu.Values["P2t1"].text + " | " + Cut(CoopMenu.Values["P2d1"].text, 80));
                        Shots.Take("flow-pick-sub");
                        Do("P2ok", 2);                  // player 2 confirms first
                        Next(now, 1.0f); break;
                    }
                case 14:
                    Check("player 2 is confirmed and waits while player 1 is still choosing", CoopMenu.Confirmed[1] && !CoopMenu.Confirmed[0] && CoopMenu.Mode == "pick" && Active("P1ok"), "confirmed=" + CoopMenu.Confirmed[0] + CoopMenu.Confirmed[1] + " mode=" + CoopMenu.Mode);
                    Shots.Take("flow-pick-wait");
                    Do("P1ok", 1);
                    Next(now, 1.2f); break;
                case 15:
                    Check("both confirmed: the final page opens", CoopMenu.Mode == "final" && Active("P2ready") && Active("start"), "mode=" + CoopMenu.Mode);
                    Check("the overview shows the choices", CoopMenu.Values["ov5"].text.Contains(CoopMenu.Classes[_c1].DisplayName) && CoopMenu.Values["ov5"].text.Contains(CoopMenu.Classes[_c2].DisplayName) && CoopMenu.Values["ov3"].text == CoopMenu.BiomeName(CoopMenu.Biomes[CoopMenu.BiomeIndex]),
                        CoopMenu.Values["ov3"].text + " | " + CoopMenu.Values["ov5"].text);
                    Check("START is closed until player 2 is ready", !CoopMenu.IsOpen("start"), "start open=" + CoopMenu.IsOpen("start") + " hint=" + CoopMenu.Values["starthint"].text);
                    Shots.Take("flow-final");
                    Do("start", 1);                     // pressing it anyway must not start
                    Next(now, 1.0f); break;
                case 16:
                    Check("START without player 2's READY starts nothing", !CoopStart.Started && Lc.InRunAt < 0f, "started=" + CoopStart.Started);
                    if (Pad) { _rev = Revive.On; VirtualPad.Press(GamepadButton.North); }          // controller 2 tries the host's revive switch (Y)
                    Next(now, 1.0f); break;
                case 17:
                    if (Pad) { Check("controller 2 cannot flip the host's revive switch with Y", Revive.On == _rev && !CoopStart.Started && Lc.InRunAt < 0f, "revive " + _rev + " -> " + Revive.On); MenuPad.PadUsed = true; VirtualPad.Press1(GamepadButton.North); }          // controller 1: Y flips it
                    Do("P2ready", 2);
                    Next(now, 1.8f); break;
                case 18:
                    if (!CoopMenu.P2Ready && _readyWait++ < 8) { _at = now + 0.6f; return; }      // the menu button reaches the menu a moment after the press
                    if (Pad && _revCheck == 0) { _revCheck = 1; Check("controller 1 flips the revive switch with Y", Revive.On != _rev, "revive " + _rev + " -> " + Revive.On); Revive.On = _rev; CoopMenu.RefreshAll(); }
                    Check("player 2's READY opens START", CoopMenu.P2Ready && CoopMenu.IsOpen("start"), "ready=" + CoopMenu.P2Ready + " start open=" + CoopMenu.IsOpen("start") + " hint=" + CoopMenu.Values["starthint"].text);
                    Shots.Take("flow-final-ready");
                    if (Pad) VirtualPad.Press(GamepadButton.South);          // controller 2's A only toggles its own READY, it never starts (the menu button does nothing for it)
                    Next(now, 1.0f); break;
                case 19:
                    if (Pad && !_p2tog)
                    {
                        Check("controller 2's A toggles its READY and does not start the run", !CoopMenu.P2Ready && !CoopStart.Started && Lc.InRunAt < 0f, "ready=" + CoopMenu.P2Ready + " started=" + CoopStart.Started);
                        _p2tog = true; VirtualPad.Press(GamepadButton.South); _at = now + 1.0f; return;      // ready again
                    }
                    if (Pad) Check("pressed again, READY is back on", CoopMenu.P2Ready, "ready=" + CoopMenu.P2Ready);
                    Do("start", 1);                 // controller 1's menu button starts the run
                    Next(now, 2.0f); break;
                case 20:
                    if (Lc.InRunAt < 0f && now - _at < 90f) { if (Lc.EverInRun) UiTest.FinishWith(false, "the run ended early"); return; }
                    Check("START starts the run", Lc.InRunAt >= 0f && CoopStart.Started && !CoopMenu.Visible, "inRun=" + (Lc.InRunAt >= 0f) + " started=" + CoopStart.Started);
                    Lc.Line("flow: " + CoopStart.Readback);
                    if (Lc.Has("-lcflowgate")) Check("the run starts the gate level", CoopStart.Readback.Contains("node=GATE"), CoopStart.Readback);
                    _inRun = now; Next(now, 0.5f); break;
                case 21:
                    if (now < _inRun + 22f) return;
                    {
                        var host = Phase1.First(); var clone = Phase1.Second;
                        string hc = host != null && host.CurrentClass != null ? host.CurrentClass.Dwarf.ToString() : "?";
                        string cc = clone != null && clone.CurrentClass != null ? clone.CurrentClass.Dwarf.ToString() : "?";
                        Check("two dwarves are in the run", host != null && clone != null, "host=" + (host != null) + " clone=" + (clone != null));
                        Check("dwarf 1 has player 1's class", hc == CoopMenu.Classes[_c1].Dwarf.ToString(), "host class=" + hc + " wanted " + CoopMenu.Classes[_c1].Dwarf);
                        Check("dwarf 2 has player 2's class", cc == CoopMenu.Classes[_c2].Dwarf.ToString(), "clone class=" + cc + " wanted " + CoopMenu.Classes[_c2].Dwarf);
                        var wantSub = CoopMenu.P2.Sub;
                        string w1 = host != null && host.weapons != null && host.weapons.Count > 0 ? host.weapons[0].data.name : "none";
                        string w2 = clone != null && clone.weapons != null && clone.weapons.Count > 0 ? clone.weapons[0].data.name : "none";
                        Check("dwarf 2 has its subclass's starter weapon", wantSub != null && wantSub.StarterWeapon != null && w2 == wantSub.StarterWeapon.name && w2 != w1, "clone weapon=" + w2 + " host weapon=" + w1);
                        Shots.Take("flow-run");
                        UiTest.FinishWith(_fail == 0, "flow checks ok=" + _ok + " fail=" + _fail);
                        _s++;
                    }
                    break;
            }
        }
    }
}
