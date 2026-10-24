using System;
using HarmonyLib;
using UnityEngine;

namespace LocalCoop
{
    /// <summary>
    /// The arrow that points at the supply capsule (TargetPointerLine) is one line that follows the host. The second dwarf gets a copy of it
    /// that follows them and is shown, hidden and coloured together with the original. Twin `-lcnoarrow2`.
    /// </summary>
    internal static class Arrow2
    {
        internal static TargetPointerLine Copy;
        internal static int Shows;

        // hand test: "no arrow for player 2" - the two dwarves stand close together and the two identical arrows lie on top of each other. The copy is player 2's blue (twin `-lcnoarrow2blue`).
        private static Color Tint(Color c) { return Lc.Has("-lcnoarrow2blue") ? c : new Color(CoopMenu.BlueP2.r, CoopMenu.BlueP2.g, CoopMenu.BlueP2.b, c.a); }

        private static bool IsCopy(TargetPointerLine l) { return Copy != null && l != null && l.Pointer == Copy.Pointer; }

        internal static TargetPointerLine Orig; internal static int Resyncs; private static bool _origShown; private static Transform _tgt; private static float _off; internal static int PlayerResets;
        private static TargetPointerLine Get(TargetPointerLine orig)
        {
            var c = Phase1.Second;
            if (c == null || Lc.Has("-lcnoarrow2") || IsCopy(orig)) return null;
            if (!IsCopy(orig)) Orig = orig;
            if (Copy == null)
            {
                var go = UnityEngine.Object.Instantiate(orig.gameObject, orig.transform.parent);
                go.name = orig.gameObject.name + " (P2)";
                Copy = go.GetComponent<TargetPointerLine>();
                Phase1.SetRef(Copy, typeof(TargetPointerLine), "cameraController", orig.cameraController.Pointer);
                Lc.Line("arrow2: copy of the capsule arrow made for player 2");
            }
            Phase1.SetRef(Copy, typeof(TargetPointerLine), "player", c.Pointer);
            return Copy;
        }

        [HarmonyPatch(typeof(TargetPointerLine), "Show")]
        internal static class ShowPatch
        {
            private static void Postfix(TargetPointerLine __instance, Transform target, float offset, Color c)
            {
                try { if (!IsCopy(__instance)) { _origShown = true; _tgt = target; _off = offset; }  var k = Get(__instance); if (k != null) { k.Show(target, offset, Tint(c)); Shows++; } }
                catch (Exception e) { Lc.LineOnce("arrow2: show threw " + e.GetType().Name + ": " + e.Message); }
            }
        }

        [HarmonyPatch(typeof(TargetPointerLine), "Hide")]
        internal static class HidePatch
        {
            private static void Postfix(TargetPointerLine __instance)
            {
                try { if (!IsCopy(__instance)) _origShown = false; if (Copy != null && !IsCopy(__instance)) Copy.Hide(); } catch { }
            }
        }

        [HarmonyPatch(typeof(TargetPointerLine), "SetColor")]
        internal static class ColorPatch
        {
            private static void Postfix(TargetPointerLine __instance, Color c)
            {
                try { if (Copy != null && !IsCopy(__instance)) Copy.SetColor(Tint(c)); } catch { }
            }
        }

        [HarmonyPatch(typeof(TargetPointerLine), "OnTimerTick")]
        internal static class TickPatch
        {
            private static void Postfix(TargetPointerLine __instance, bool isLeavingSoon)
            {
                try { if (Copy != null && !IsCopy(__instance)) Copy.OnTimerTick(isLeavingSoon); } catch { }
            }
        }

        /// <summary>
        /// Hand test (lc2.log): player 2's blue pod arrow was there and vanished after a level-up while the game's own arrow stayed. Whenever the original is shown and the copy is not,
        /// the copy is shown again. Twin `-lcnoarrowkeep`.
        /// </summary>
        /// <summary>Hand test (lc.log): the level-up hides the arrows through Hide() and the game brings back only its own arrow without calling Show() again, so the copy stayed hidden. "Shown" is read from the original's object itself.</summary>
        private static bool OrigUp() { try { return Orig.gameObject.activeInHierarchy; } catch { return false; } }
        private static float _dgAt;
        /// <summary>Diagnosis (hand test: the blue arrow still goes off after a level-up): every 2 s while the game shows its arrow, the state of both lines goes to the log.</summary>
        private static void Diag()
        {
            if (!(Settings.Debug || Lc.Has("-lcarrowdiag")) || Copy == null || Orig == null || !OrigUp()) return;
            float now = Time.realtimeSinceStartup; if (now < _dgAt) return; _dgAt = now + 2f;
            try
            {
                string D(TargetPointerLine l)
                {
                    var ln = l.line; if (ln == null) return "no line";
                    string pl = "-", tg = "-";
                    try { pl = l.player != null ? l.player.name + "@" + l.player.transform.position.ToString() : "null"; } catch { }
                    try { tg = l.target != null ? l.target.position.ToString() : "none"; if (l.target != null && l.player != null) { var dv = l.target.position - l.player.transform.position; dv.y = 0f; tg += " dist=" + dv.magnitude.ToString("F1") + " arrowLen=" + l.arrowLength.ToString("F1") + " offset=" + l.offsetFromPlayer.ToString("F1"); } } catch { }
                    return "active=" + l.gameObject.activeInHierarchy + " enabled=" + ln.enabled + " points=" + ln.positionCount + " baseA=" + l.baseColor.a.ToString("F2") + " at=" + l.transform.position.ToString() + " player=" + pl + " target=" + tg;
                }
                Lc.Line("arrow2: state timeScale=" + Time.timeScale.ToString("F2") + " | original " + D(Orig) + " | copy " + D(Copy));
            }
            catch (Exception e) { Lc.LineOnce("arrow2: diag threw " + e.GetType().Name + ": " + e.Message); }
        }

        private static float _kt = -1f; private static bool _kHid; private static int _kp;
        // test `-lcarrowkeeptest`: with the pod arrow showing (`-lcarrowpod`), only the copy is hidden: it must come back by itself
        private static void KeepTest()
        {
            float now = Time.realtimeSinceStartup;
            if (_kt < 0f) { _kt = now + 4f; return; }
            if (!_kHid && now > _kt) { _kHid = true; _kt = now + 2f; Copy.Hide(); Lc.Line("arrow2: test: only the copy was hidden"); return; }
            if (_kt == 1e9f && _kp == 0) { _kp = 1; Phase1.SetRef(Copy, typeof(TargetPointerLine), "player", Phase1.First().Pointer); Lc.Line("arrow2: test: the copy was pointed at the host"); return; }
            if (_kp == 2) { _kp = 3; Orig.Hide(); Lc.Line("arrow2: test: after Hide() the original is active=" + Orig.gameObject.activeInHierarchy + " enabled=" + (Orig.line != null && Orig.line.enabled) + " target=" + (Orig.target != null) + " | copy active=" + Copy.gameObject.activeInHierarchy + " enabled=" + (Copy.line != null && Copy.line.enabled)); Orig.gameObject.SetActive(true); Lc.Line("arrow2: test: the original was hidden and switched on again without Show (as after a level-up)"); return; }
            if (_kp == 3) { _kp = 4; bool ok2 = Copy.gameObject.activeInHierarchy; Lc.Line("arrow2: CHECK " + (ok2 ? "OK  " : "FAIL") + " the copy is back after the level-up flow | resyncs=" + Resyncs); return; }
            if (_kp == 1) { _kp = 2; var cp = Copy.player; bool ok = cp != null && Phase1.Second != null && cp.Pointer == Phase1.Second.Pointer; Lc.Line("arrow2: CHECK " + (ok ? "OK  " : "FAIL") + " the copy follows player 2 again | resets=" + PlayerResets); return; }
            if (_kHid && _kt > 0f && now > _kt) { _kt = 1e9f; bool back = Resyncs > 0; Lc.Line("arrow2: CHECK " + (back ? "OK  " : "FAIL") + " the hidden copy shows again by itself | resyncs=" + Resyncs); }
        }

        internal static void Keep()
        {
            try
            {
                if (Lc.Has("-lcarrowkeeptest") && Copy != null && Shows > 0) KeepTest();
                Diag();
                // hand test: after a level-up the blue arrow was gone again within seconds while the green one stayed: it is a copy that follows its `player`; if that was reset
                // to the host it lies exactly under the green arrow. Twin `-lcnoarrowplayer`.
                if (Copy != null && Phase1.Second != null && !Lc.Has("-lcnoarrowplayer")) { var cpl = Copy.player; if (cpl == null || cpl.Pointer != Phase1.Second.Pointer) { Phase1.SetRef(Copy, typeof(TargetPointerLine), "player", Phase1.Second.Pointer); PlayerResets++; if (PlayerResets <= 3) Lc.Line("arrow2: the copy followed another dwarf, set back to player 2 (" + PlayerResets + ")"); } }
                if (Copy == null || Orig == null || !OrigUp() || Lc.Has("-lcnoarrowkeep") || Phase1.Second == null) return;
                var lo = Orig.line; var lc = Copy.line; var tg = Orig.target; if (tg == null) tg = _tgt;
                if (lo == null || lc == null || tg == null || !Orig.gameObject.activeInHierarchy) return;
                if (Copy.gameObject.activeInHierarchy) return;      // the line itself is switched by the arrow's own Update (off when the dwarf is within about 5 arrow lengths of the target)
                Phase1.SetRef(Copy, typeof(TargetPointerLine), "player", Phase1.Second.Pointer);
                Copy.gameObject.SetActive(true);
                Copy.Show(tg, _off > 0f ? _off : Orig.offsetFromPlayer, Tint(Orig.baseColor)); Resyncs++;
                if (Resyncs <= 3) Lc.Line("arrow2: the second dwarf's arrow was gone while the original showed: shown again (" + Resyncs + ")");
            }
            catch (Exception e) { Lc.LineOnce("arrow2: keep threw " + e.GetType().Name + ": " + e.Message); }
        }

        // ---- evidence: the arrow the game itself shows (towards the drop pod at the end of a stage) gets a screenshot, with the state of both lines ----
        private static float _realAt = -1f; private static int _realShots;
        internal static void RealShot()
        {
            if (Copy == null || Shows == 0 || _realShots >= 3 || !Lc.Has("-lcarrowshots")) return;
            float now = Time.realtimeSinceStartup;
            if (_realAt < 0f) { _realAt = now + 2f; return; }
            if (now < _realAt) return;
            _realAt = now + 4f; _realShots++;
            try
            {
                var o = UnityEngine.Object.FindObjectsOfType<TargetPointerLine>(true);
                foreach (var l in o) if (l != null) Lc.Line("arrow2: real arrow " + l.name + " active=" + l.gameObject.activeInHierarchy + " line enabled=" + (l.line != null && l.line.enabled) + " points=" + (l.line != null ? l.line.positionCount : -1));
            }
            catch { }
            Shots.Take("arrow-real" + _realShots);
        }

        // ---- test (`-lcarrow2test`): show the original towards a far point, the copy must start at the second dwarf ---------------
        internal static bool TestDone;
        private static int _step; private static float _at = -1f, _in = -1f;
        private static GameObject _target;

        internal static void Test()
        {
            if (TestDone || !Lc.Has("-lcarrow2test")) return;
            float now = Time.realtimeSinceStartup;
            if (Lc.InRunAt < 0f || Phase1.Second == null) return;
            if (_in < 0f) _in = now;
            if (now < _in + 12f || now < _at) return;
            _at = now + (_step == 1 ? 3f : 1f);
            try
            {
                var h = Phase1.First(); var c = Phase1.Second;
                TargetPointerLine orig = null; foreach (var o in UnityEngine.Object.FindObjectsOfType<TargetPointerLine>(true)) if (o != null && !IsCopy(o)) { orig = o; break; }
                if (orig == null) { UiTest.FinishWith(false, "no TargetPointerLine in the scene"); TestDone = true; return; }
                switch (_step++)
                {
                    case 0:
                        if (Lc.Has("-lcarrowpod") && Pod.Find1() != null) { var pd = Pod.Find1(); pd.gameObject.SetActive(true); pd.AnimateIn(40f); Lc.Line("arrow2: the drop pod animates in (the game then shows its own arrow)"); _at = now + 9f; break; }      // the game's own call path
                        _target = new GameObject("LcArrowTarget");
                        _target.transform.position = h.transform.position + new Vector3(14f, 0f, 0f);
                        orig.Show(_target.transform, 1f, Color.green);
                        break;
                    case 1:
                        {
                            { var lo = orig.line; var lc2 = Copy != null ? Copy.line : null; Lc.Line("arrow2: original line enabled=" + (lo != null && lo.enabled) + " points=" + (lo != null ? lo.positionCount : -1) + " go=" + orig.gameObject.activeInHierarchy + " pos=" + orig.transform.position + " | copy enabled=" + (lc2 != null && lc2.enabled) + " points=" + (lc2 != null ? lc2.positionCount : -1) + " go=" + (Copy != null && Copy.gameObject.activeInHierarchy)); }
                            bool on = Copy != null && Copy.gameObject.activeSelf;
                            bool own = Copy != null && Copy.player != null && Copy.player.Pointer == c.Pointer && orig.player != null && orig.player.Pointer == h.Pointer;
                            Lc.Line("arrow2: copy shown=" + on + " follows player 2=" + own + " shows=" + Shows);
                            Ck("the second dwarf has its own capsule arrow, following them", on && own, "shown=" + on + " follows P2=" + own);
                            Shots.Take("arrow-p2");
                            orig.Hide();
                        }
                        break;
                    case 2:
                        {
                            // the game has two arrows (capsule and escort cargo); the copy shows while either is up and is hidden when none is
                            bool real = false; foreach (var l0 in UnityEngine.Object.FindObjectsOfType<TargetPointerLine>()) if (l0 != null && !IsCopy(l0) && l0.gameObject.activeInHierarchy) real = true;
                            bool cp0 = Copy != null && Copy.gameObject.activeSelf;
                            Ck("the copy is hidden when no real arrow is up", !cp0 || real, "copy active=" + cp0 + " real arrow up=" + real);
                        }
                        break;
                    default:
                        TestDone = true;
                        UiTest.FinishWith(_fail == 0, "arrow2 ok=" + _ok + " fail=" + _fail);
                        break;
                }
            }
            catch (Exception e) { TestDone = true; UiTest.FinishWith(false, "threw " + e.GetType().Name + ": " + e.Message); }
        }

        private static int _ok, _fail;
        private static void Ck(string what, bool pass, string detail)
        {
            if (pass) _ok++; else _fail++;
            Lc.Line("ui: CHECK " + (pass ? "ok  " : "FAIL") + " " + what + " - " + detail);
        }
    }
}
