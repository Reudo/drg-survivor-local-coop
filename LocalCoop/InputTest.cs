using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LocalCoop
{
    /// <summary>
    /// `-lcui=input`: two virtual gamepads, one per dwarf (needs `-lcpoll`, the test twin is `-lcnopoll`). Each scenario pushes and
    /// releases the sticks in an order that broke the first version in a real two-controller test ("both run on in one direction
    /// until I steer against it"), and checks that every dwarf follows only its own stick and stops when its own stick is at rest.
    /// The stick directions are picked from open ground (the game's NavMesh): a rock wall would stop a dwarf and look like a failure.
    /// A step during which the game was paused (level-up window) is repeated.
    /// </summary>
    internal static class InputTest
    {
        private struct Step { public string Name; public float P1, P2; public float Secs; }

        private static int _s, _i;
        private static float _at = -1f, _inRun = -1f, _stepStart;
        private static readonly List<Step> Steps = new List<Step>();
        private static int _ok, _fail;
        private static bool _built, _disturbed;
        private static int _retries;
        private static Vector3 _home, _hHalf, _cHalf;
        private static bool _halfTaken;
        private static Vector2 _dH, _dC;           // stick direction of dwarf 1 and of dwarf 2 (stick x, y = world x, z)
        private static float _dbgAt;

        private static void Check(string name, bool ok, string detail)
        {
            if (ok) _ok++; else _fail++;
            Lc.Line("input: CHECK " + (ok ? "ok  " : "FAIL") + " " + name + " - " + detail);
        }

        private static float Flat(Vector3 d) { return Mathf.Sqrt(d.x * d.x + d.z * d.z); }
        private static float Along(Vector3 d, Vector2 dir) { return d.x * dir.x + d.z * dir.y; }

        private static float FreeLen(Vector3 from, Vector2 dir)
        {
            float free = 0f;
            for (float dd = 1f; dd <= 14f; dd += 1f)
            {
                bool ok = false;
                try
                {
                    UnityEngine.AI.NavMeshHit hit;
                    ok = UnityEngine.AI.NavMesh.SamplePosition(new Vector3(from.x + dir.x * dd, from.y, from.z + dir.y * dd), out hit, 0.6f, -1);
                }
                catch { ok = false; }
                if (!ok) break;
                free = dd;
            }
            return free;
        }

        private static Vector2 BestDir(Vector3 from, Vector2? notNear, out float len)
        {
            Vector2 best = Vector2.right; len = -1f;
            for (int k = 0; k < 16; k++)
            {
                float ang = k * (float)Math.PI * 2f / 16f;
                var d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                if (notNear.HasValue && Vector2.Dot(d, notNear.Value) > 0.5f) continue;      // at least 60 degrees from the other dwarf's direction
                float f = FreeLen(from, d);
                if (f > len) { len = f; best = d; }
            }
            return best;
        }

        private static void Build()
        {
            _built = true;
            Steps.Add(new Step { Name = "both push: dwarf 1 and dwarf 2, each along its own direction", P1 = 1f, P2 = 1f, Secs = 2.0f });
            Steps.Add(new Step { Name = "dwarf 1 lets go, dwarf 2 keeps pushing", P1 = 0f, P2 = 1f, Secs = 2.0f });
            Steps.Add(new Step { Name = "dwarf 2 lets go: both at rest", P1 = 0f, P2 = 0f, Secs = 2.0f });
            // the reported case: dwarf 2 drives the action with the stronger stick, dwarf 1 lets go first, then dwarf 2
            Steps.Add(new Step { Name = "dwarf 1 weak, dwarf 2 strong", P1 = 0.5f, P2 = 1f, Secs = 1.8f });
            Steps.Add(new Step { Name = "dwarf 1 lets go first (dwarf 2 still pushes)", P1 = 0f, P2 = 1f, Secs = 1.8f });
            Steps.Add(new Step { Name = "then dwarf 2 lets go: nobody may keep running", P1 = 0f, P2 = 0f, Secs = 2.0f });
            // and the other way round: dwarf 2 lets go while dwarf 1 still pushes (the clone used to run on)
            Steps.Add(new Step { Name = "dwarf 1 weak, dwarf 2 strong (again)", P1 = 0.5f, P2 = 1f, Secs = 1.8f });
            Steps.Add(new Step { Name = "dwarf 2 lets go first (dwarf 1 still pushes)", P1 = 0.5f, P2 = 0f, Secs = 2.0f });
            Steps.Add(new Step { Name = "dwarf 1 lets go: both at rest again", P1 = 0f, P2 = 0f, Secs = 2.0f });
        }

        internal static void Tick()
        {
            float now = Time.realtimeSinceStartup;
            if (now < _at) return;
            if (_s == 0 && (MenuEntry.EntryButton == null || !CoopMenu.Built)) return;
            _at = now + 0.05f;
            try
            {
                switch (_s)
                {
                    case 0: MenuEntry.EntryButton.OnClick.Invoke(); _s++; _at = now + 0.6f; break;
                    case 1: UiTest.Click("P1class3"); UiTest.Click("P2class1"); UiTest.Click("sector0"); UiTest.Click("danger1"); UiTest.Click("biome3"); _s++; _at = now + 0.6f; break;
                    case 2: UiTest.Click("start"); _s++; _at = now + 3f; break;
                    case 3:
                        if (Lc.InRunAt < 0f) { if (Lc.EverInRun) UiTest.FinishWith(false, "the run ended before the input test"); else if (now > _at + 60f) UiTest.FinishWith(false, "the run did not start"); return; }
                        if (_inRun < 0f) _inRun = now;
                        if (now < _inRun + 12f || Phase1.Second == null) return;
                        if (!_built)
                        {
                            Build();
                            _home = Phase1.First().transform.position;
                            VirtualPad.Pad1Touch();
                            // open ground for both dwarves, picked once
                            float lh, lc2;
                            _dH = BestDir(_home, null, out lh);
                            _dC = BestDir(_home + new Vector3(0f, 0f, 3f), _dH, out lc2);
                            Lc.Line("input: open ground: dwarf 1 direction " + _dH + " (" + lh + " m free), dwarf 2 direction " + _dC + " (" + lc2 + " m free)");
                            if (lh < 8f || lc2 < 8f) { UiTest.FinishWith(false, "no open ground of 8 m for the test here"); return; }
                        }
                        BeginStep(now); _s++; break;
                    case 4: RunStep(now); break;
                }
            }
            catch (Exception e) { Lc.Line("input: tick threw " + e.GetType().Name + ": " + e.Message); UiTest.FinishWith(false, "threw " + e.GetType().Name + ": " + e.Message); }
        }

        private static void Sticks(Step st)
        {
            VirtualPad.SetStick1(_dH * st.P1);
            VirtualPad.SetStick(_dC * st.P2);
        }

        private static void BeginStep(float now)
        {
            var st = Steps[_i];
            _stepStart = now; _halfTaken = false; _disturbed = false;
            // every step starts from the same open spot, the dwarves close together (so the camera leash never plays a part)
            { var h = Phase1.First(); var c = Phase1.Second; h.transform.position = _home; c.transform.position = _home + new Vector3(0f, 0f, 3f); }
            Sticks(st);
            Lc.Line("input: step " + (_i + 1) + "/" + Steps.Count + ": " + st.Name);
        }

        private static void RunStep(float now)
        {
            var st = Steps[_i];
            var host = Phase1.First(); var clone = Phase1.Second;
            Sticks(st);     // keep the sticks held
            if (now - _dbgAt > 0.5f)
            {
                _dbgAt = now;
                Lc.Line("input:   t+" + (now - _stepStart).ToString("F1") + " host dir=" + host.moveDir + " | clone dir=" + clone.moveDir + " pos=" + clone.transform.position + " | p1vec=" + MoveInput.P1Vector() + " p2vec=" + MoveInput.P2Vector());
            }
            if (Time.timeScale < 0.5f || AutoStart.ActiveFormName() != "UICoreForm") _disturbed = true;       // a level-up window stops the game: that step proves nothing
            if (!_halfTaken && now >= _stepStart + st.Secs * 0.5f) { _halfTaken = true; _hHalf = host.transform.position; _cHalf = clone.transform.position; }
            if (now < _stepStart + st.Secs) return;
            if (_disturbed && _retries < 8) { _retries++; Lc.Line("input: step " + (_i + 1) + " was disturbed by a pause (level-up window), repeated"); BeginStep(now); return; }
            var dh = host.transform.position - _hHalf; var dc = clone.transform.position - _cHalf;      // movement during the second half of the step
            float mh = Along(dh, _dH), mc = Along(dc, _dC);       // progress along each dwarf's own direction
            bool ok = true;
            switch (_i)
            {
                case 0: ok = mh > 0.8f && mc > 0.8f; break;
                case 1: ok = Flat(dh) < 0.4f && mc > 0.8f; break;
                case 2: ok = Flat(dh) < 0.4f && Flat(dc) < 0.4f; break;
                case 3: ok = mc > 0.8f && mh > 0.25f; break;
                case 4: ok = Flat(dh) < 0.4f && mc > 0.8f; break;
                case 5: ok = Flat(dh) < 0.4f && Flat(dc) < 0.4f; break;
                case 6: ok = mc > 0.8f && mh > 0.25f; break;
                case 7: ok = mh > 0.25f && Flat(dc) < 0.4f; break;
                case 8: ok = Flat(dh) < 0.4f && Flat(dc) < 0.4f; break;
            }
            Check(st.Name, ok, "host along its own direction " + mh.ToString("F1") + " (moved " + Flat(dh).ToString("F1") + "), clone along its own direction " + mc.ToString("F1") + " (moved " + Flat(dc).ToString("F1") + ")");
            _i++;
            if (_i >= Steps.Count)
            {
                VirtualPad.SetStick1(Vector2.zero); VirtualPad.SetStick(Vector2.zero);
                Shots.Take("input-end");
                UiTest.FinishWith(_fail == 0, "input checks ok=" + _ok + " fail=" + _fail + (MoveInput.Active ? "" : " (MoveInput not active)"));
                return;
            }
            BeginStep(now);
        }
    }
}
