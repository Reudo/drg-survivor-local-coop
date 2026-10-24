using System;
using System.Collections.Generic;
using System.Text;
using Cinemachine;
using UnityEngine;

namespace LocalCoop
{
    /// <summary>
    /// One camera for both dwarves. The game's CameraController follows exactly one Player through a Cinemachine virtual camera;
    /// this takes over its Follow target, puts it at the midpoint of the living dwarves and dollies the camera out with their
    /// distance up to a maximum. The original follow target and offset are restored when the second dwarf is gone.
    /// (Approach carried over from the network prototype's CoopCamera, where the focus + dolly variant was the one that fit
    /// this game's Transposer body.)
    /// </summary>
    internal static class Cam2
    {
        internal static bool Enabled = true;              // `-lcnocam` turns it off (test twin: vanilla follow)
        internal static float SepFree = 10f;               // no zoom below this distance between the dwarves
        internal static float SepScale = 25f;              // extra distance per +1.0 of zoom
        internal static float KMax = 1.8f;                 // zoom limit
        internal static bool Leash = true;                 // `-lcnoleash` (test twin): the dwarves may leave the screen
        internal static float LeashMargin = 0.08f;         // viewport margin the dwarves must keep at maximum zoom
        internal static int LeashPulls;                    // how often a dwarf was held at the screen edge

        internal static float K = 1f;
        internal static float LastSep;

        private static CameraController _cc;
        private static CinemachineVirtualCamera _vcam;
        private static CinemachineTransposer _tr;
        private static Camera _cam;
        private static Transform _focus;
        private static Transform _origFollow;
        private static Vector3 _origOffset;
        private static bool _installed, _hasOffset, _resolvedLogged;

        // visibility measurement, bucketed by dwarf distance (the test only reads it)
        internal static readonly Dictionary<int, int[]> Buckets = new Dictionary<int, int[]>();   // bucket -> {frames, bothVisible}

        internal static void Init() { Enabled = !Lc.Has("-lcnocam"); Leash = !Lc.Has("-lcnoleash"); float km = Lc.Num("-lckmax="); if (km >= 1f) KMax = km; float lm = Lc.Num("-lcmargin="); if (lm > 0.01f) LeashMargin = lm; }

        /// <summary>The gameplay camera (for screen positions over the dwarves).</summary>
        internal static Camera Cam { get { Resolve(); return _cam; } }

        private static bool Resolve()
        {
            if (_vcam != null && _cam != null) return true;
            try
            {
                _cc = Find.One<CameraController>();
                if (_cc == null) return false;
                _vcam = _cc.vCam;
                _cam = _cc.cam;
                if (!_resolvedLogged)
                {
                    _resolvedLogged = true;
                    Lc.Line("cam2: vcam=" + (_vcam != null) + " cam=" + (_cam != null) + " follow=" + (_vcam != null && _vcam.Follow != null ? _vcam.Follow.name : "null"));
                }
                return _vcam != null && _cam != null;
            }
            catch { return false; }
        }

        internal static void Tick()
        {
            try
            {
                var second = Phase1.Second;
                var gc = Find.One<GameController>();
                var first = gc != null ? gc.player : null;
                if (second == null || first == null || !Resolve() || !Enabled) { Restore(); Measure(first, second); return; }

                if (!_installed) Install();
                if (!_installed) return;

                Vector3 pf = first.transform.position, ps = second.transform.position;
                bool bothAlive = first.isAlive && second.isAlive;
                Vector3 mid = bothAlive ? (pf + ps) * 0.5f : (first.isAlive ? pf : ps);
                _focus.position = mid;
                if (_vcam.Follow != _focus) _vcam.Follow = _focus;      // the game puts its own target back; keep ours

                float sep = bothAlive ? Vector3.Distance(pf, ps) : 0f;
                LastSep = sep;
                K = Mathf.Clamp(1f + Mathf.Max(0f, sep - SepFree) / SepScale, 1f, KMax);
                if (_hasOffset) _tr.m_FollowOffset = _origOffset * K;
                // At maximum zoom the screen is the limit: whoever would leave it is held at the edge.
                if (Leash && bothAlive && K >= KMax - 0.02f) EnforceLeash(first, second);
                _prevF = first.transform.position; _prevS = second.transform.position;
                Measure(first, second);
            }
            catch (Exception e) { Lc.LineOnce("cam2: tick threw: " + e.GetType().Name + ": " + e.Message); }
        }


        private static Vector3 _prevF, _prevS;

        private static bool Inside(Vector3 world)
        {
            try
            {
                var v = _cam.WorldToViewportPoint(world + new Vector3(0f, 1f, 0f));
                return v.z > 0f && v.x > LeashMargin && v.x < 1f - LeashMargin && v.y > LeashMargin && v.y < 1f - LeashMargin;
            }
            catch { return true; }
        }

        /// <summary>
        /// At maximum zoom the screen is the limit. When one of the two is outside the margin:
        ///  - if the dwarf that moved most this frame moved AWAY from the other, its step is simply taken back (it stops at the edge,
        ///    walls and all, nothing is dragged through rock);
        ///  - otherwise (camera lag, knockback, a teleport) it is pulled toward the other in small steps until both fit.
        /// </summary>
        private static void EnforceLeash(Player a, Player b)
        {
            Vector3 pa = a.transform.position, pb = b.transform.position;
            if (Inside(pa) && Inside(pb)) return;
            bool aMoved = (pa - _prevF).sqrMagnitude > (pb - _prevS).sqrMagnitude;
            Player mover = aMoved ? a : b, other = aMoved ? b : a;
            Vector3 pm = mover.transform.position, po = other.transform.position;
            Vector3 prevM = aMoved ? _prevF : _prevS;
            float sepNow = Vector3.Distance(pa, pb);
            float sepPrev = Vector3.Distance(_prevF, _prevS);

            Vector3 target;
            if (sepNow > sepPrev + 0.0005f)
            {
                target = prevM;                      // moving apart: undo this frame's step
                target.y = pm.y;
            }
            else
            {
                Vector3 toOther = po - pm;
                toOther.y = 0f;
                float dist = toOther.magnitude;
                if (dist < 0.5f) return;
                target = pm + toOther / dist * Mathf.Min(0.35f, dist);
            }
            mover.transform.position = target;
            try { var rb = mover.rb; if (rb != null) rb.position = target; } catch { }
            LeashPulls++;
        }

        private static void Install()
        {
            _origFollow = null;
            try { _origFollow = _vcam.Follow; } catch { }
            _tr = _vcam.GetCinemachineComponent<CinemachineTransposer>();
            if (_tr != null) { _origOffset = _tr.m_FollowOffset; _hasOffset = true; }
            _focus = new GameObject("LcCameraFocus").transform;
            _installed = true;
            Lc.Line("cam2: installed, transposer=" + (_tr != null) + " offset=" + _origOffset + " origFollow=" + (_origFollow != null ? _origFollow.name : "null"));
        }

        private static void Restore()
        {
            if (!_installed) return;
            _installed = false;
            try { if (_vcam != null && _origFollow != null) _vcam.Follow = _origFollow; } catch { }
            try { if (_tr != null && _hasOffset) _tr.m_FollowOffset = _origOffset; } catch { }
            try { if (_focus != null) UnityEngine.Object.Destroy(_focus.gameObject); } catch { }
            _focus = null; _tr = null; _hasOffset = false; K = 1f;
            Lc.Line("cam2: restored");
        }

        internal static string Viewport(Vector3 world)
        {
            try { var v = _cam.WorldToViewportPoint(world + new Vector3(0f, 1f, 0f)); return "(" + v.x.ToString("F2") + "," + v.y.ToString("F2") + "," + v.z.ToString("F1") + ")"; }
            catch { return "?"; }
        }

        /// <summary>Both dwarves inside the leash margin (what the leash itself enforces).</summary>
        internal static bool BothInsideMargin(Vector3 a, Vector3 b, float slack)
        {
            try
            {
                foreach (var w in new[] { a, b })
                {
                    var v = _cam.WorldToViewportPoint(w + new Vector3(0f, 1f, 0f));
                    float m = LeashMargin - slack;
                    if (!(v.z > 0f && v.x > m && v.x < 1f - m && v.y > m && v.y < 1f - m)) return false;
                }
                return true;
            }
            catch { return true; }
        }

        internal static bool Visible(Vector3 world)
        {
            try
            {
                var v = _cam.WorldToViewportPoint(world + new Vector3(0f, 1f, 0f));   // head height
                return v.z > 0f && v.x > 0.04f && v.x < 0.96f && v.y > 0.04f && v.y < 0.96f;
            }
            catch { return false; }
        }

        private static void Measure(Player first, Player second)
        {
            if (!CamTest.Active || first == null || second == null || _cam == null || !first.isAlive || !second.isAlive) return;
            if (!CamTest.Settled) return;                    // only count frames after the camera had time to follow
            float sep = Vector3.Distance(first.transform.position, second.transform.position);
            int b = (int)Mathf.Round(sep / 6f) * 6;
            int[] acc;
            if (!Buckets.TryGetValue(b, out acc)) { acc = new int[2]; Buckets[b] = acc; }
            acc[0]++;
            if (Visible(first.transform.position) && Visible(second.transform.position)) acc[1]++;
        }

        internal static string Table()
        {
            var sb = new StringBuilder();
            var keys = new List<int>(Buckets.Keys); keys.Sort();
            foreach (var k in keys) { var a = Buckets[k]; if (a[0] == 0) continue; sb.Append(k).Append("m:").Append((100.0 * a[1] / a[0]).ToString("F0")).Append("%(").Append(a[0]).Append(") "); }
            return sb.ToString().Trim();
        }
    }

    /// <summary>`-lcp1=clone-cam`: puts the clone at fixed distances from the host, one after the other, and counts frames in which both are on screen.</summary>
    internal static class CamTest
    {
        internal static bool Active;
        internal static bool Settled;
        private static readonly float[] Steps = { 6f, 12f, 18f, 24f, 30f, 36f };
        private static int _i = -1;
        private static float _nextAt = -1f, _settleAt;
        private static bool _done;

        internal static void Tick(Player first, Player second)
        {
            if (!Active || _done || first == null || second == null) return;
            float now = Time.realtimeSinceStartup;
            if (_nextAt < 0f) _nextAt = now + 3f;
            // keep the clone where the step says (it stands still, the host is not moved)
            if (_i >= 0 && _i < Steps.Length)
                second.transform.position = first.transform.position + new Vector3(Steps[_i], 0f, 0f);
            Settled = _i >= 0 && now >= _settleAt;
            if (now < _nextAt) return;
            _i++;
            if (_i >= Steps.Length) { _done = true; Active = false; return; }
            _nextAt = now + 5f;
            _settleAt = now + 2f;
            Lc.Line("camtest: step " + _i + " distance " + Steps[_i] + " m");
        }

        internal static bool Done { get { return _done; } }
    }
}
