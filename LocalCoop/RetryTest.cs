using System;
using DRS.UI;
using UnityEngine;

namespace LocalCoop
{
    /// <summary>
    /// `-lcui=retry`: start a run from the co-op menu, let both dwarves die, press the end screen's RETRY button and wait for the next run to
    /// come up with two dwarves (loading must finish, the clone must be rebuilt, the new run must run on). Hand test finding: RETRY hung at
    /// the loading screen. Twin `-lcnoretry` skips the reset between runs (the old behaviour).
    /// </summary>
    internal static class RetryTest
    {
        private static int _step, _ok, _fail;
        private static float _at = -1f, _t0, _logAt, _retryAt = -1f, _run2At = -1f;
        private static bool _sawLoading;
        private static int _shots;

        private static void Check(string name, bool ok, string detail)
        {
            if (ok) _ok++; else _fail++;
            Lc.Line("retry: CHECK " + (ok ? "ok  " : "FAIL") + " " + name + " - " + detail);
        }

        internal static void Tick()
        {
            float now = Time.realtimeSinceStartup;
            if (_step == 0 && (MenuEntry.EntryButton == null || !CoopMenu.Built)) return;
            if (_retryAt > 0f && now >= _logAt)
            {
                _logAt = now + 4f;
                Lc.Line("retry: watch t=+" + (now - _retryAt).ToString("F0") + " form=" + (AutoStart.ActiveFormName() ?? "-") + " inRun=" + (Lc.InRunAt >= 0f) + " second=" + (Phase1.Second != null) + " fps=" + Lc.Fps.ToString("F0") + " frames=" + Lc.Frames);
            }
            if (now < _at) return;
            _at = now + 0.1f;
            try
            {
                switch (_step)
                {
                    case 0: MenuEntry.EntryButton.OnClick.Invoke(); _step++; _at = now + 0.6f; break;
                    case 1: UiTest.Click("P1class3"); UiTest.Click("P2class1"); UiTest.Click("sector0"); UiTest.Click("danger1"); UiTest.Click("biome3"); _step++; _at = now + 0.6f; break;
                    case 2: UiTest.Click("start"); _step++; _t0 = now; _at = now + 3f; break;
                    case 3:
                        if (Lc.InRunAt < 0f) { if (now > _t0 + 70f) UiTest.FinishWith(false, "the first run did not start"); return; }
                        if (Phase1.Second == null || !Phase1.Second.isAlive || now < Lc.InRunAt + 14f) return;
                        Check("run 1 has two dwarves", Phase1.Second != null, "second dwarf present");
                        Check("the second dwarf is made while the host rides the drop pod down (not after it has left the pod)", !Phase1.CreatedHostFree && Phase1.CloneAt - Lc.InRunAt < 3f, "made " + (Phase1.CloneAt - Lc.InRunAt).ToString("F1") + " s after the run began, hostFree=" + Phase1.CreatedHostFree);
                        Check("the second dwarf is shown only when complete, its panel is there from the start of the stage", Phase1.RevealAt > 0f && Phase1.RevealAt - Phase1.CloneAt < 6.5f && Hud2.BuiltAt > 0f && Hud2.BuiltAt <= Phase1.CloneAt + 1.5f, "revealed " + (Phase1.RevealAt - Phase1.CloneAt).ToString("F1") + " s after it was made, panel built " + (Hud2.BuiltAt - Phase1.CloneAt).ToString("F2") + " s after the dwarf was made (run start +" + (Hud2.BuiltAt - Lc.InRunAt).ToString("F1") + " s)");
                        Check("after landing the second dwarf stands next to the host (it rode the pod and the ramp with it)", Vector3.Distance(Phase1.First().transform.position, Phase1.Second.transform.position) < 5f, "distance to the host " + Vector3.Distance(Phase1.First().transform.position, Phase1.Second.transform.position).ToString("F1") + " m");
                        Shots.Take("retry-run1");
                        _step = 30; _t0 = now; Tags.ErrSum = 0; Tags.ErrMax = 0; Tags.ErrN = 0; break;
                    case 30:
                        VirtualPad.SetStick(new Vector2(1f, 0.3f));
                        if (now > _t0 + 0.8f && _shots < 3) { Shots.Take("retry-walk" + _shots); _shots++; _t0 = now; }
                        if (_shots >= 3)
                        {
                            VirtualPad.SetStick(Vector2.zero);
                            Check("the P2 tag sits on its dwarf while walking (mean pixel offset)", Tags.ErrN > 20 && Tags.ErrSum / Tags.ErrN < 25f, "mean " + (Tags.ErrN > 0 ? Tags.ErrSum / Tags.ErrN : -1f).ToString("F1") + " px, max " + Tags.ErrMax.ToString("F1") + " px, samples " + Tags.ErrN + ", visible=" + Tags.Visible);
                            _step = 31; _t0 = now;
                        }
                        break;
                    case 31:
                        Phase1.Second.TakeDamage(1000000, false, default(EDamageType), false, default(EDamageSource), null, false);
                        Phase1.First().TakeDamage(1000000, false, default(EDamageType), false, default(EDamageSource), null, false);
                        _step = 4; _t0 = now; break;
                    case 4:
                        if (AutoStart.ActiveFormName() != "UIEndScreen") { if (now > _t0 + 40f) UiTest.FinishWith(false, "no end screen"); return; }
                        if (_run2At < 0f) { _run2At = now; return; }
                        if (now < _run2At + 3f) return;
                        Shots.Take("retry-endscreen");
                        var es = Find.One<UIEndScreen>();
                        Lc.Line("retry: end screen buttons: " + AutoStart.Describe(AutoStart.ActiveButtons(es)));
                        _retryAt = now; _run2At = -1f;
                        es.OnRetryButton();
                        Lc.Line("retry: OnRetryButton called");
                        _step++; _t0 = now; _at = now + 1f; break;
                    case 5:
                        if (AutoStart.ActiveFormName() == "UILoadingForm") _sawLoading = true;
                        if (Phase1.LoadingActive() || Phase1.Second == null || Lc.InRunAt < 0f || Phase1.Second.currentHp <= 0 || !Phase1.Second.isAlive)
                        {
                            if (now > _t0 + 100f) { Shots.Take("retry-stuck"); Check("the retry reaches a new run with two dwarves", false, "after 100 s: form=" + (AutoStart.ActiveFormName() ?? "-") + " inRun=" + (Lc.InRunAt >= 0f) + " second=" + (Phase1.Second != null) + " sawLoading=" + _sawLoading); UiTest.FinishWith(false, "retry failed ok=" + _ok + " fail=" + _fail); }
                            return;
                        }
                        if (_run2At < 0f) { _run2At = now; Lc.Line("retry: run 2 has two dwarves after " + (now - _t0).ToString("F0") + " s"); return; }
                        if (now < _run2At + 10f) return;
                        Shots.Take("retry-run2");
                        Check("the retry reaches a new run with two dwarves", true, (now - _t0).ToString("F0") + " s after the button, sawLoading=" + _sawLoading);
                        Check("the new run runs on (both alive, fps)", Phase1.First().isAlive && Phase1.Second.isAlive && Lc.Fps > 3f, "fps=" + Lc.Fps.ToString("F0"));
                        UiTest.FinishWith(_fail == 0, "retry ok=" + _ok + " fail=" + _fail);
                        break;
                }
            }
            catch (Exception e) { Lc.Line("retry: tick threw " + e.GetType().Name + ": " + e.Message); UiTest.FinishWith(false, "threw " + e.GetType().Name + ": " + e.Message); }
        }
    }
}
