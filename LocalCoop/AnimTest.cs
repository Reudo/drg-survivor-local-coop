using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace LocalCoop
{
    /// <summary>
    /// `-lcui=anim`: the animations of the second dwarf, read from the clone's own Animator and skeleton, next to the host's as reference.
    /// A run is started from the co-op menu; then phases follow: stand, walk, stand again, a hit, death (clone only, last). Per phase the test
    /// samples the playing clip names (layer 0), the animator speed and a pose signature (the local rotations of the skinned mesh's bones),
    /// so "an animation is playing" does not rest on what the Animator claims alone: the skeleton has to move too.
    /// Twin: `-lcnoanim` switches the clone's Animator components off before the phases.
    /// </summary>
    internal static class AnimTest
    {
        private sealed class Phase
        {
            public string Name;
            public float Secs;
            public int Samples;
            public double Motion;                     // summed pose change between consecutive samples
            public float Moved;                       // distance covered
            public readonly HashSet<string> Clips = new HashSet<string>();
            public readonly List<string> ClipLog = new List<string>();
            public Vector3 Start;
            public string Last = "";
            public float MaxSpeed, MinSpeed = 99f;
            public readonly HashSet<string> Layer1 = new HashSet<string>();
            public double MotionPerSample { get { return Samples > 1 ? Motion / (Samples - 1) : 0.0; } }
        }

        private static int _step, _ph;
        private static float _at = -1f, _inRun = -1f, _phStart, _sampleAt;
        private static readonly string[] Names = { "stand", "walk", "stand2", "hit", "shoot", "death" };
        private static readonly Phase[] CloneP = new Phase[6], HostP = new Phase[6];
        private static float[] _prevClone, _prevHost;
        private static int _ok, _fail;

        private static void Check(string name, bool ok, string detail)
        {
            if (ok) _ok++; else _fail++;
            Lc.Line("anim: CHECK " + (ok ? "ok  " : "FAIL") + " " + name + " - " + detail);
        }

        private static Animator FindAnimator(Player p)
        {
            try
            {
                foreach (var a in p.GetComponentsInChildren<Animator>(false))
                    if (a != null && a.gameObject.activeInHierarchy && a.runtimeAnimatorController != null) return a;
            }
            catch { }
            return null;
        }

        private static string ClipNames(Animator a)
        {
            if (a == null) return "no-animator";
            try
            {
                var infos = a.GetCurrentAnimatorClipInfo(0);
                var sb = new StringBuilder();
                for (int i = 0; i < infos.Length; i++) { if (i > 0) sb.Append('+'); sb.Append(infos[i].clip != null ? infos[i].clip.name : "null"); }
                return sb.Length == 0 ? "none" : sb.ToString();
            }
            catch (Exception e) { return "err:" + e.GetType().Name; }
        }

        private static SkinnedMeshRenderer Skin(Player p)
        {
            try
            {
                SkinnedMeshRenderer best = null; int bones = -1;
                foreach (var r in p.GetComponentsInChildren<SkinnedMeshRenderer>(false))
                {
                    if (r == null || !r.gameObject.activeInHierarchy || r.bones == null) continue;
                    if (r.bones.Length > bones) { best = r; bones = r.bones.Length; }
                }
                return best;
            }
            catch { return null; }
        }

        private static float[] Pose(Player p)
        {
            var r = Skin(p);
            if (r == null) return null;
            var b = r.bones;
            var v = new float[Math.Min(b.Length, 60) * 3];
            for (int i = 0; i < v.Length / 3; i++)
            {
                if (b[i] == null) continue;
                var e = b[i].localEulerAngles;
                v[i * 3] = e.x; v[i * 3 + 1] = e.y; v[i * 3 + 2] = e.z;
            }
            return v;
        }

        private static double Diff(float[] a, float[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return 0.0;
            double s = 0.0;
            for (int i = 0; i < a.Length; i++) { float d = Math.Abs(a[i] - b[i]); if (d > 180f) d = 360f - d; s += d; }
            return s;
        }

        private static void Sample(Player p, Phase ph, ref float[] prev)
        {
            if (p == null || ph == null) return;
            var a = FindAnimator(p);
            string clip = ClipNames(a);
            ph.Clips.Add(clip);
            if (clip != ph.Last) { ph.ClipLog.Add(clip + "@" + (Time.realtimeSinceStartup - _phStart).ToString("F1")); ph.Last = clip; }
            try
            {
                if (a != null)
                {
                    float sp = a.GetFloat("moveSpeed");
                    if (sp > ph.MaxSpeed) ph.MaxSpeed = sp;
                    if (sp < ph.MinSpeed) ph.MinSpeed = sp;
                    if (a.layerCount > 1) { var li = a.GetCurrentAnimatorClipInfo(1); for (int i = 0; i < li.Length; i++) if (li[i].clip != null) ph.Layer1.Add(li[i].clip.name); }
                }
            }
            catch { }
            var pose = Pose(p);
            ph.Motion += Diff(prev, pose);
            prev = pose;
            ph.Samples++;
        }

        private static void Describe(string tag, Player p)
        {
            var a = FindAnimator(p);
            var r = Skin(p);
            Lc.Line("anim: " + tag + " animator=" + (a != null ? a.gameObject.name + " controller=" + a.runtimeAnimatorController.name + " enabled=" + a.enabled + " speed=" + a.speed + " layers=" + a.layerCount + " params=" + a.parameterCount : "none")
                + " skin=" + (r != null ? r.gameObject.name + " bones=" + r.bones.Length : "none") + " clip=" + ClipNames(a));
            if (a != null)
                for (int i = 0; i < a.parameterCount && i < 20; i++)
                {
                    var q = a.GetParameter(i);
                    Lc.Line("anim:   " + tag + " param " + q.name + " type=" + q.type);
                }
        }

        internal static void Tick()
        {
            float now = Time.realtimeSinceStartup;
            if (now < _at) return;
            if (_step == 0 && (MenuEntry.EntryButton == null || !CoopMenu.Built)) return;
            _at = now + 0.05f;
            try
            {
                switch (_step)
                {
                    case 0: MenuEntry.EntryButton.OnClick.Invoke(); _step++; _at = now + 0.6f; break;
                    case 1: UiTest.Click("P1class3"); UiTest.Click("P2class1"); UiTest.Click("sector0"); UiTest.Click("danger1"); UiTest.Click("biome3"); _step++; _at = now + 0.6f; break;
                    case 2: UiTest.Click("start"); _step++; _at = now + 3f; break;
                    case 3:
                        if (Lc.InRunAt < 0f) { if (Lc.EverInRun) UiTest.FinishWith(false, "the run ended before the animation phases"); else if (now > _at + 60f) UiTest.FinishWith(false, "the run did not start"); return; }
                        if (_inRun < 0f) _inRun = now;
                        if (now < _inRun + 14f || Phase1.Second == null) return;
                        Begin(now); _step++;
                        break;
                    case 4: Run(now); break;
                }
            }
            catch (Exception e) { Lc.Line("anim: tick threw " + e.GetType().Name + ": " + e.Message); UiTest.FinishWith(false, "threw " + e.GetType().Name + ": " + e.Message); }
        }

        private static void Begin(float now)
        {
            var clone = Phase1.Second; var host = Phase1.First();
            { var ha = FindAnimator(host); HostCtrl = ha != null ? ha.runtimeAnimatorController.name : "none"; }
            Describe("host ", host);
            Describe("clone", clone);
            if (Lc.Has("-lcnoanim"))
                foreach (var a in clone.GetComponentsInChildren<Animator>(true)) if (a != null) a.enabled = false;
            Silence(host);
            Start(0, now);
        }

        private static void Silence(Player host)
        {
            try { if (host != null && host.weaponsActive) { host.weaponsActive = false; host.ToggleWeapons(false); } } catch { }
        }

        private static void Start(int i, float now)
        {
            _ph = i; _phStart = now; _sampleAt = 0f;
            CloneP[i] = new Phase { Name = Names[i], Start = Phase1.Second.transform.position };
            HostP[i] = new Phase { Name = Names[i], Start = Phase1.First().transform.position };
            _prevClone = null; _prevHost = null;
            Lc.Line("anim: phase " + Names[i] + " start");
            if (i == 3)
            {
                try { Phase1.Second.TakeDamage(5, false, default(EDamageType), false, default(EDamageSource), null, false); Phase1.First().TakeDamage(5, false, default(EDamageType), false, default(EDamageSource), null, false); Lc.Line("anim: hit applied to both"); }
                catch (Exception e) { Lc.Line("anim: hit threw " + e.GetType().Name + ": " + e.Message); }
            }
            if (i == 4)
            {
                // shooting: both dwarves fight with their weapons switched on
                try { Phase1.First().weaponsActive = true; Phase1.First().ToggleWeapons(true); Phase1.Second.weaponsActive = true; Phase1.Second.ToggleWeapons(true); Lc.Line("anim: weapons switched on for both"); } catch (Exception e) { Lc.Line("anim: weapons on threw " + e.GetType().Name); }
            }
            if (i == 5)
            {
                try { var ca = FindAnimator(Phase1.Second); _cloneCtrlLast = ca != null ? ca.runtimeAnimatorController.name : null; } catch { }
                try { Phase1.Second.TakeDamage(1000000, false, default(EDamageType), false, default(EDamageSource), null, false); Lc.Line("anim: lethal hit applied to the clone, alive=" + Phase1.Second.isAlive); }
                catch (Exception e) { Lc.Line("anim: lethal hit threw " + e.GetType().Name + ": " + e.Message); }
            }
        }

        private static float _endAt = -1f;

        private static void Run(float now)
        {
            if (_ph == 6) { EndWait(now); return; }
            var clone = Phase1.Second; var host = Phase1.First();
            float len = _ph == 0 ? 4f : _ph == 1 ? 5f : _ph == 2 ? 3f : _ph == 3 ? 2.5f : _ph == 4 ? 5f : 3.5f;
            var cp = CloneP[_ph]; var hp = HostP[_ph];
            if (_ph < 4) Silence(host);
            // the walk phase pushes controller 2 to the right: the game's own input path, routed to the clone by device
            if (_ph == 1) VirtualPad.SetStick(new Vector2(1f, 0f));
            if (now >= _sampleAt)
            {
                _sampleAt = now + 0.1f;
                Sample(clone, cp, ref _prevClone);
                Sample(host, hp, ref _prevHost);
            }
            if (now < _phStart + len) return;
            cp.Secs = hp.Secs = now - _phStart;
            cp.Moved = (clone.transform.position - cp.Start).magnitude;
            hp.Moved = (host.transform.position - hp.Start).magnitude;
            if (_ph == 1) VirtualPad.SetStick(Vector2.zero);
            Shots.Take("anim-" + Names[_ph]);
            Lc.Line("anim: PHASE " + Names[_ph] + " clone motion/sample=" + cp.MotionPerSample.ToString("F2") + " moved=" + cp.Moved.ToString("F1") + " moveSpeed " + cp.MinSpeed.ToString("F2") + ".." + cp.MaxSpeed.ToString("F2") + " layer1=[" + string.Join("|", new List<string>(cp.Layer1).ToArray()) + "] clips=[" + string.Join(" | ", cp.ClipLog.ToArray()) + "]"
                + "  host motion/sample=" + hp.MotionPerSample.ToString("F2") + " moved=" + hp.Moved.ToString("F1") + " clips=[" + string.Join(" | ", hp.ClipLog.ToArray()) + "]");
            if (_ph < 5) { Start(_ph + 1, now); return; }
            _ph = 6; _endWait = now; 
        }

        private static float _endWait;
        private static void EndWait(float now)
        {
            string form = AutoStart.ActiveFormName() ?? "";
            if (form != "UIEndScreen" && now < _endWait + 15f) return;
            if (form == "UIEndScreen" && _endAt < 0f) { _endAt = now; return; }
            if (form == "UIEndScreen" && now < _endAt + 2f) return;
            Shots.Take("anim-endscreen");
            Check("on the end screen the second dwarf's panel is gone (no leftovers on top of it)", form == "UIEndScreen" && !Hud2.P2Visible, "form=" + form + " panel2 visible=" + Hud2.P2Visible);
            Judge();
        }

        private static string J(HashSet<string> h) { return string.Join("|", new List<string>(h).ToArray()); }

        private static void Judge()
        {
            var s = CloneP[0]; var w = CloneP[1]; var s2 = CloneP[2]; var hit = CloneP[3]; var shoot = CloneP[4]; var d = CloneP[5];
            var hostCtrl = HostCtrl;
            var cloneAn = FindAnimator(Phase1.Second);
            string cloneCtrl = cloneAn != null ? cloneAn.runtimeAnimatorController.name : (_cloneCtrlLast ?? "none");
            Check("the clone plays the run animator of the game (same controller as the host, not the menu one)", cloneCtrl == hostCtrl && s.Samples > 10, "host controller " + hostCtrl + ", clone controller " + cloneCtrl);
            Check("standing: the clone plays an idle clip", s.Clips.Count > 0 && Any(s.Clips, "idle") && !Any(s.Clips, "no-animator"), "clips [" + J(s.Clips) + "]");
            Check("walking by controller 2: the clone moves and its animator speed rises", w.Moved > 2f && w.MaxSpeed > 0.1f, "moved " + w.Moved.ToString("F1") + " m, moveSpeed " + w.MinSpeed.ToString("F2") + ".." + w.MaxSpeed.ToString("F2") + ", clips [" + J(w.Clips) + "], layer1 [" + J(w.Layer1) + "]");
            Check("walking: the skeleton moves more than standing", w.MotionPerSample > s.MotionPerSample * 1.5 + 1.0, "pose change per sample walking " + w.MotionPerSample.ToString("F2") + " vs standing " + s.MotionPerSample.ToString("F2"));
            Check("after stopping the clone is idle again and its speed falls back", Any(s2.Clips, "idle") && s2.MinSpeed < 0.1f, "clips [" + J(s2.Clips) + "], moveSpeed " + s2.MinSpeed.ToString("F2") + ".." + s2.MaxSpeed.ToString("F2"));
            Check("death: the clone plays a dying clip", !Phase1.Second.isAlive && (Any(d.Clips, "dying") || Any(d.Clips, "death") || Any(d.Clips, "dead")), "alive=" + Phase1.Second.isAlive + ", clips [" + J(d.Clips) + "]");
            // the game has no shoot or hurt animation: the host, who shoots and takes the same hit, plays nothing extra either, and the clone does the same as the host
            var hh = HostP[3]; var hs = HostP[4];
            var allowed = new HashSet<string>(s.Clips); allowed.UnionWith(HostP[0].Clips);
            Check("hit: no hurt animation exists in the game (host and clone play only idle clips)", Subset(hit.Clips, allowed) && Subset(hh.Clips, allowed), "clone [" + J(hit.Clips) + "] host [" + J(hh.Clips) + "]");
            Check("shooting: no shoot animation exists in the game (host and clone play only idle clips while their weapons fire)", Subset(shoot.Clips, allowed) && Subset(hs.Clips, allowed) && shoot.Layer1.Count <= s.Layer1.Count + 1, "clone [" + J(shoot.Clips) + "] host [" + J(hs.Clips) + "]");
            Lc.Line("anim: controller parameters: isIdle isDead isAngry moveSpeed isMining isPickUp flying flyType (no shoot, no hurt)");
            UiTest.FinishWith(_fail == 0, "animation checks ok=" + _ok + " fail=" + _fail);
        }

        private static bool Subset(HashSet<string> a, HashSet<string> b) { foreach (var x in a) if (!b.Contains(x)) return false; return true; }
        private static bool Any(HashSet<string> h, string part) { foreach (var c in h) if (c != null && c.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0) return true; return false; }
        private static string HostCtrl = "?", _cloneCtrlLast;

        private static bool SameSet(HashSet<string> a, HashSet<string> b) { return a.SetEquals(b); }
    }
}
