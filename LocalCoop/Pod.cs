using System;
using HarmonyLib;
using UnityEngine;

namespace LocalCoop
{
    /// <summary>
    /// The drop pod (DropPod): it brings the dwarf into a stage and takes it out at the end. It knows one dwarf (its `player` field, the host).
    ///  - Arrival: the second dwarf is made as soon as the host rides the pod down (not after the host has left it) and rides along: until the host
    ///    has control again it is carried next to the host (same path, down the ramp). Twins `-lcnoearlyclone`, `-lcnopodride`.
    ///  - Departure: the pod only leaves once both dwarves stand inside (a dwarf that is down does not count), and it only counts as "exit finished"
    ///    when both are out. A dwarf that stands inside the pod cannot be hurt. Twins `-lcnopodboth`, `-lcnopodguard2`.
    /// `-lcpodprobe` writes the pod and both dwarves to lc.log every 0.25 s.
    /// </summary>
    internal static class Pod
    {
        private static float _probeAt;
        internal static int Rides, RideFrames, BothChecks, GuardSets;

        private static DropPod _podCache; private static float _podAt;

        /// <summary>The scene's drop pod. Searched with `FindObjectsOfTypeAll` (inactive ones too), which cost about 30 ms per frame when asked on every
        /// frame (measured with `-lcperf`: the frame-rate drop of the hand test); the pod is kept, a missing one is searched again once a second.</summary>
        internal static DropPod Find1()
        {
            if (_podCache != null) return _podCache;
            float now = Time.realtimeSinceStartup;
            if (now < _podAt) return null;
            _podAt = now + 1f;
            foreach (var p in Resources.FindObjectsOfTypeAll<DropPod>()) if (p != null && p.gameObject.scene.IsValid()) { _podCache = p; break; }
            return _podCache;
        }

        private static string V(Vector3 v) { return "(" + v.x.ToString("F1") + "," + v.y.ToString("F1") + "," + v.z.ToString("F1") + ")"; }

        private static string Dwarf(Player p)
        {
            if (p == null) return "-";
            try { return V(p.transform.position) + " pod=" + p.isInDropPod + " inv=" + p.IsInvulnerable + " hp=" + p.currentHp + " ctl=" + p._moveControlType + " alive=" + p.isAlive; }
            catch { return "?"; }
        }

        // ---- arrival -----------------------------------------------------------------------------------------------------------------
        /// <summary>True while the host rides the pod in or walks down its ramp: the second dwarf may be made now (Phase1).</summary>
        internal static bool EarlyOk()
        {
            if (Lc.Has("-lcnoearlyclone") || Lc.InRunAt < 0f) return false;
            try
            {
                var h = Phase1.First();
                if (h == null || !h.isInDropPod) return false;
                var pod = Find1();
                return pod != null && (pod.state == DropPod.EState.ANIMATING_IN || pod.state == DropPod.EState.WAITING_FOR_PLAYER_EXIT);
            }
            catch { return false; }
        }

        private static bool _ride, _rideInv;
        private static float _rideSince;

        private static void RideEnd(Player c, string why)
        {
            _ride = false;
            try
            {
                if (c != null)
                {
                    if (c._moveControlType == Player.EMoveControlType.DISABLED) c._moveControlType = Player.EMoveControlType.FULL_CONTROL;
                    try { c.moveDir = Vector2.zero; c.rawMoveInput = Vector2.zero; } catch { }      // the chase set them: the dwarf must not run on by itself
                    if (_rideInv) c.IsInvulnerable = false;
                    if (c.isInDropPod) c.OnExitDropPod();
                }
            }
            catch { }
            _rideInv = false;
            Lc.Line("pod: idle pose switched off " + IdleFixes + "x, chase frames " + ChaseFrames + ", farthest behind its place " + MaxLag.ToString("F1") + " m, run animation up to " + MaxMoveSpeed.ToString("F2") + ", clips " + Clips);
            Lc.Line("pod: the second dwarf's ride ended (" + why + ") after " + (Time.realtimeSinceStartup - _rideSince).ToString("F1") + " s, " + RideFrames + " frames");
        }

        // hand test: at the mission start the second dwarf was dragged along statically (teleported next to the host every frame). While the host walks out of the pod
        // the second dwarf now runs after it with its own run animation (moveDir towards a spot behind and beside the host) and settles to its right. Twin `-lcnochase`.
        private static Vector3 _hostLast; private static float _hostFreeAt = -1f;
        internal static bool Chasing { get { return _ride && ChaseFrames > 0; } }
        internal static int IdleFixes; internal static int ChaseExits; internal static int ChaseFrames; internal static float MaxStep, MaxLag, MaxMoveSpeed, MinSpeedSeen = 99f; internal static string ChaseClip = "", Clips = ""; private static float _clipAt;

        private static Vector2 _mvS; private static Vector3 _stallPos; private static float _stallAt; internal static int ChaseStalls;
        private static bool Chase(Player c, Player h, DropPod pod, Vector3 side, float now)
        {
            if (Lc.Has("-lcnochase") || pod == null || pod.state != DropPod.EState.WAITING_FOR_PLAYER_EXIT) return false;
            // hand test (stage 2): player 2 stayed in the pod and was teleported out only when the ride ended: it still counted as inside the pod (isInDropPod), which blocks its
            // steering, so the chase did not move it. It leaves the pod as soon as the pod lets the dwarves out. Twin `-lcnochaseexit`.
            if (!Lc.Has("-lcnochaseexit") && c.isInDropPod) { try { c.OnExitDropPod(); ChaseExits++; } catch { } }
            // hand test (video, twice): P2 slid over the ground without moving its legs: the pod leaves the animator's "idle" pose switched on (the menu pose of the ride), the walk blend does not play while it is. Twin `-lcnochaseidle`.
            if (!Lc.Has("-lcnochaseidle")) { try { var pa = c.PlayerAnimator; if (pa != null && pa.GetIdle()) { pa.SetIdle(false); IdleFixes++; } } catch { } }
            var hp = h.transform.position;
            Vector3 dir = hp - _hostLast; dir.y = 0f; float step = dir.magnitude; _hostLast = hp;
            Vector3 face = step > 0.002f ? dir / step : h.transform.forward; face.y = 0f;
            bool hostMoving = step / Mathf.Max(Time.deltaTime, 0.001f) > 0.8f;
            Vector3 target = hostMoving ? hp - face.normalized * 1.3f + side * 0.9f : hp + side * 1.6f;
            var cp = c.transform.position; Vector3 d = target - cp; d.y = 0f;
            float dist = d.magnitude;
            c._moveControlType = Player.EMoveControlType.FULL_CONTROL;
            // hand test: P2 showed no run animation (it kept close, so the proportional speed stayed tiny): while the host walks it moves at least at a walk (twin `-lcnochasemin`)
            float mag = Mathf.Clamp01(dist / 1.2f); if (hostMoving && dist > 0.4f && !Lc.Has("-lcnochasemin")) mag = Mathf.Max(mag, 0.8f);
            Vector2 mv = dist < 0.15f ? Vector2.zero : new Vector2(d.x, d.z) / Mathf.Max(dist, 0.001f) * mag;
            // hand test (run2.log): the second dwarf stood still at the pod for 2.7 s while the host walked off (7 m behind, then teleported). Stuck for 0.6 s and more than 2.5 m behind: put it beside the host. Twin `-lcnochasestall`.
            if (!Lc.Has("-lcnochasestall"))
            {
                if (now - _stallAt > 0.6f) { if (dist > 2.5f && hostMoving && (cp - _stallPos).magnitude < 0.3f) { dist = 99f; ChaseStalls++; } _stallPos = cp; _stallAt = now; }
            }
            if (dist > 7f) { var pos = hp + side * 1.6f; c.transform.position = pos; try { var rb = c.rb; if (rb != null) rb.position = pos; } catch { } mv = Vector2.zero; }       // never left behind far
            // hand test (video): the second dwarf started late and ran in jerks behind the host (speed followed the distance, which jumps while the host walks in bursts); the
            // steering now changes at a limited rate. Twin `-lcnochasesmooth`.
            if (!Lc.Has("-lcnochasesmooth")) { float dx0 = mv.x - _mvS.x, dy0 = mv.y - _mvS.y, dl = Mathf.Sqrt(dx0 * dx0 + dy0 * dy0), stp = 5f * Mathf.Max(Time.deltaTime, 0.001f); _mvS = dl <= stp ? mv : new Vector2(_mvS.x + dx0 / dl * stp, _mvS.y + dy0 / dl * stp); mv = _mvS; } else _mvS = mv;
            c.moveDir = mv; try { c.rawMoveInput = mv; } catch { }
            { var lag = (c.transform.position - (hp + side * 1.6f)); lag.y = 0f; if (lag.magnitude > MaxLag) MaxLag = lag.magnitude; if (now > _clipAt) { _clipAt = now + 0.25f; try { Animator an = null; try { var pa2 = c.PlayerAnimator; if (pa2 != null) an = pa2.animator; } catch { } if (an == null) foreach (var a0 in c.GetComponentsInChildren<Animator>(false)) if (a0 != null && a0.gameObject.activeInHierarchy && a0.runtimeAnimatorController != null) { an = a0; break; } var ci = an != null ? an.GetCurrentAnimatorClipInfo(0) : null; string prm = ""; if (Lc.Has("-lcchasetrace") && an != null) { try { for (int pi = 0; pi < an.parameterCount; pi++) { var pr = an.GetParameter(pi); if (pr.type == AnimatorControllerParameterType.Float) prm += pr.name + "=" + an.GetFloat(pr.name).ToString("F2") + " "; } for (int li = 0; li < an.layerCount; li++) prm += "L" + li + "=" + an.GetLayerWeight(li).ToString("F1") + " "; } catch { } }
                if (Lc.Has("-lcchasetrace")) Lc.Line("pod: chase params " + prm + " speed=" + an.speed.ToString("F2"));
                if (Lc.Has("-lcchasetrace")) Lc.Line("pod: chase trace host=" + hp + " clone=" + c.transform.position + " lag=" + lag.magnitude.ToString("F2") + " mv=" + mv + " ctl=" + c._moveControlType + " clip=" + (ci != null && ci.Length > 0 ? ci[0].clip.name : "-")); try { if (an != null) { float ms = an.GetFloat("moveSpeed"); if (ms > MaxMoveSpeed) MaxMoveSpeed = ms; } } catch { }
                if (ci != null && ci.Length > 0) { ChaseClip = ci[0].clip.name; if (Clips.IndexOf(ChaseClip + ";") < 0) Clips += ChaseClip + ";"; } } catch { } } }
            ChaseFrames++;
            return true;
        }

        /// <summary>After the host has control again the second dwarf may still be on its way to its place (up to 2 s): it keeps running, then the ride ends.</summary>
        private static bool Settling(Player c, Player h, DropPod pod, float now)
        {
            if (Lc.Has("-lcnochase") || ChaseFrames == 0) return false;
            if (_hostFreeAt < 0f) _hostFreeAt = now;
            if (now > _hostFreeAt + 2f) return false;
            Vector3 side = Vector3.right;
            try { if (pod != null && pod.podTransform != null) { side = pod.podTransform.right; side.y = 0f; side.Normalize(); } } catch { }
            var target = h.transform.position + side * 1.6f; var d = target - c.transform.position; d.y = 0f;
            if (d.magnitude < 1.0f) return false;
            c._moveControlType = Player.EMoveControlType.FULL_CONTROL;
            c.moveDir = new Vector2(d.x, d.z) / d.magnitude * Mathf.Clamp01(d.magnitude / 1.2f); try { c.rawMoveInput = c.moveDir; } catch { }
            ChaseFrames++;
            return true;
        }

        private static void Ride()
        {
            var c = Phase1.Second; var h = Phase1.First();
            if (c == null || h == null || Lc.Has("-lcnopodride") || !c.isAlive) { if (_ride) RideEnd(c, "no dwarf"); return; }
            float now = Time.realtimeSinceStartup;
            var pod = Find1();
            if (!_ride)
            {
                if (!EarlyOk()) return;
                _ride = true; _rideSince = now; Rides++; RideFrames = 0; _hostFreeAt = -1f; ChaseFrames = 0; _mvS = Vector2.zero; _hostLast = h.transform.position; MaxStep = 0f; MaxLag = 0f; MaxMoveSpeed = 0f; Clips = "";
                Lc.Line("pod: the second dwarf rides along with the host (pod state " + pod.state + ")");
            }
            else if ((h._moveControlType == Player.EMoveControlType.FULL_CONTROL && now > _rideSince + 0.5f && !Settling(c, h, pod, now)) || now > _rideSince + 14f) { RideEnd(c, now > _rideSince + 14f ? "timeout (host ctl " + h._moveControlType + " pod " + h.isInDropPod + ")" : "host has control"); return; }

            Vector3 side = Vector3.right;
            try { if (pod != null && pod.podTransform != null) { side = pod.podTransform.right; side.y = 0f; side.Normalize(); } } catch { }
            if (Chase(c, h, pod, side, now)) { if (!c.IsInvulnerable) { c.IsInvulnerable = true; _rideInv = true; } RideFrames++; return; }
            var pos = h.transform.position + side * 1.6f;
            c.transform.position = pos;
            try { var rb = c.rb; if (rb != null) rb.position = pos; } catch { }
            c._moveControlType = Player.EMoveControlType.DISABLED;
            try { c.moveDir = Vector2.zero; } catch { }
            if (!c.IsInvulnerable) { c.IsInvulnerable = true; _rideInv = true; }
            RideFrames++;
        }

        // ---- boarding: the game walks only the host to its spot in the pod (hand test: P1 walked on alone while P2 stayed at the door) -----------
        internal static int Boards; private static bool _board; internal static bool TestBoard;
        private static void Board()
        {
            if (Lc.Has("-lcnopodboard")) return;
            var c = Phase1.Second; var h = Phase1.First();
            var pod = c != null && h != null ? Find1() : null;
            bool on = false;
            try
            {
                if (pod != null && c.isAlive && h.isAlive && !_ride)
                    // hand test (lc.log): P2 was set free while P1 still stood in the pod (the host's forced walk ended before the pod started): P2 walked off and the pod waited again. Held until the pod has gone.
                    on = TestBoard || ((pod.state == DropPod.EState.WAITING_FOR_PLAYER || pod.state == DropPod.EState.ANIMATING_OUT) && h.isInDropPod);
            }
            catch { }
            if (!on)
            {
                if (_board) { _board = false; try { if (c != null && c._moveControlType == Player.EMoveControlType.DISABLED) c._moveControlType = Player.EMoveControlType.FULL_CONTROL; } catch { } }
                return;
            }
            _board = true; Boards++;
            Vector3 side = Vector3.right;
            try { if (pod.podTransform != null) { side = pod.podTransform.right; side.y = 0f; side.Normalize(); } } catch { }
            var pos = Vector3.MoveTowards(c.transform.position, h.transform.position + side * 1.6f, 6f * Time.deltaTime);
            c.transform.position = pos;
            try { var rb = c.rb; if (rb != null) rb.position = pos; } catch { }
            c._moveControlType = Player.EMoveControlType.DISABLED;
            try { c.moveDir = Vector2.zero; } catch { }
        }

        // ---- departure: both dwarves inside --------------------------------------------------------------------------------------------
        private static bool _busy;
        internal static Func<Player, bool> TestInside;          // test only: stand-in for the pod's own geometric "inside" test (its colliders only work while it is in a stage)

        private static float _half = -1f;
        private static Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Collider> _hits;
        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string name);

        /// <summary>Half the edge of the box the pod tests (a float constant of the game, read from GameAssembly.dll; the pod's own test uses a cube).</summary>
        private static float HalfExtent()
        {
            if (_half > 0f) return _half;
            float v = 0f;
            try { var m = GetModuleHandle("GameAssembly.dll"); if (m != IntPtr.Zero) v = BitConverter.ToSingle(BitConverter.GetBytes(System.Runtime.InteropServices.Marshal.ReadInt32(m + 0x4474D40)), 0); } catch { }
            if (!(v > 0.5f && v < 30f)) { Lc.LineOnce("pod: box size not readable (" + v + "), using 3"); v = 3f; }
            _half = v; Lc.Line("pod: the pod tests a box of half edge " + v.ToString("F2") + " m");
            return _half;
        }

        /// <summary>
        /// Is this dwarf inside the pod? The pod's own `IsPlayerInside` ignores its `player` field: it asks `Physics.CheckBox(pod centre, cube, player layer)`,
        /// so ONE dwarf in the box was enough for both (hand test: "one is enough"). Here the same box is asked which colliders it holds, and the answer is
        /// whether one of them belongs to this dwarf.
        /// </summary>
        internal static bool InsideFor(DropPod pod, Player who)
        {
            if (pod == null || who == null) return false;
            if (TestInside != null) return TestInside(who);
            try
            {
                var pc = pod.podCenter; var lib = pod.layerMaskLibrary;
                if (pc == null || lib == null) return false;
                float h = HalfExtent();
                var cols = _hits ?? (_hits = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Collider>(16));
                int n = Physics.OverlapBoxNonAlloc(pc.position, new Vector3(h, h, h), cols, pc.rotation, lib.playerMask.value);
                for (int i = 0; i < n; i++)
                {
                    var c = cols[i]; if (c == null) continue;
                    var pl = c.GetComponentInParent<Player>();
                    if (pl != null && pl.Pointer == who.Pointer) return true;
                }
            }
            catch (Exception e) { Lc.LineOnce("pod: inside test threw " + e.GetType().Name + ": " + e.Message); }
            return false;
        }

        [HarmonyPatch(typeof(DropPod), "IsPlayerInside")]
        internal static class InsidePatch
        {
            private static void Postfix(DropPod __instance, ref bool __result)
            {
                try
                {
                    if (_busy || Lc.Has("-lcnopodboth") || Phase1.Second == null) return;
                    var c = Phase1.Second; var h = Phase1.First();
                    if (c == null || h == null) return;
                    // leaving: the game says "somebody is in the box (and has been on the ramp)"; every dwarf that is up must be in it. Arriving
                    // (WAITING_FOR_PLAYER_EXIT) the pod waits until nobody is in the box any more: the game's own answer is already right for two dwarves.
                    if (__instance.state != DropPod.EState.WAITING_FOR_PLAYER || (!__result && TestInside == null && !(!Lc.Has("-lcnopodlatch") && h.isInDropPod))) return;
                    BothChecks++;
                    bool needH = h.isAlive, needC = c.isAlive;
                    // hand test (lc.log): P1 first, then P2 - the pod takes the host in (isInDropPod) and walks it to its spot, which lies outside the box test; "inside" then fell back
                    // to false for ~6 s and P1 seemed to walk out. A dwarf the pod has taken in counts as inside. Twin -lcnopodlatch.
                    bool hostIn = needH && (InsideFor(__instance, h) || (!Lc.Has("-lcnopodlatch") && h.isInDropPod)), cloneIn = needC && (InsideFor(__instance, c) || (!Lc.Has("-lcnopodlatch") && (c.isInDropPod || (_board && h.isInDropPod))));          // hand test: host and held clone both stood outside the box test at the pod spot: the pod waited for ever
                    __result = (needH || needC) && (!needH || hostIn) && (!needC || cloneIn);
                }
                catch (Exception e) { Lc.LineOnce("pod: inside patch threw " + e.GetType().Name + ": " + e.Message); }
            }
        }

        private static bool _setH, _setC;

        /// <summary>A dwarf standing inside the pod while it waits for the other cannot be hurt; the flag is given back when it steps out.</summary>
        private static void Guard()
        {
            if (Lc.Has("-lcnopodguard2")) return;
            var c = Phase1.Second; var h = Phase1.First();
            if (c == null || h == null) return;
            var pod = Find1();
            bool active = pod != null && pod.gameObject.activeInHierarchy && pod.state == DropPod.EState.WAITING_FOR_PLAYER;
            Apply(h, active && h.isAlive && InsideFor(pod, h), ref _setH);
            Apply(c, active && c.isAlive && InsideFor(pod, c), ref _setC);
        }

        private static void Apply(Player p, bool inside, ref bool we)
        {
            try
            {
                if (inside && !p.IsInvulnerable) { p.IsInvulnerable = true; we = true; GuardSets++; }
                else if (!inside && we) { p.IsInvulnerable = false; we = false; }
            }
            catch { }
        }

        internal static bool GuardOn { get { return _setH || _setC; } }

        // hand test (lc.log, stage 2): the pod came down 21 m beside the host, the dwarves fell out of the air next to it and the pod was only seen at the screen edge. The game
        // sets the host's place at the pod once; when that did not happen the host is put under the pod. Twin `-lcnopodfix`; test `-lcpodfixtest` (moves the host away first).
        private static bool _fixDone, _fixTested; private static int _fixFrames; internal static int StartFixes;
        private static void FixStart()
        {
            bool noFix = Lc.Has("-lcnopodfix");
            var h = Phase1.First(); if (h == null) return;
            var pod = Find1(); if (pod == null || pod.podTransform == null) return;
            var st = pod.state;
            if (st != DropPod.EState.ANIMATING_IN) { if (st == DropPod.EState.WAITING_FOR_PLAYER_EXIT && Lc.Has("-lcpodfixtest") && _fixTested) { _fixTested = false; var q = pod.podTransform.position; var hp0 = h.transform.position; float dd = Mathf.Sqrt((q.x - hp0.x) * (q.x - hp0.x) + (q.z - hp0.z) * (q.z - hp0.z)); Lc.Line("pod: CHECK " + (dd < 3.5f ? "OK  " : "FAIL") + " the host stands under the pod when it has landed | distance " + dd.ToString("F1") + " m fixes=" + StartFixes); } if (st != DropPod.EState.WAITING_FOR_PLAYER_EXIT) { _fixDone = false; _fixFrames = 0; } return; }
            if (_fixDone || !h.isInDropPod) return;
            var pp = pod.podTransform.position; var hp = h.transform.position;
            if (Lc.Has("-lcpodfixtest") && !_fixTested) { _fixTested = true; var moved = new Vector3(hp.x + 20f, hp.y, hp.z); h.transform.position = moved; try { var rb0 = h.rb; if (rb0 != null) rb0.position = moved; } catch { } Lc.Line("pod: test: the host was moved 20 m away from the pod"); return; }
            if (_fixFrames++ < 2) return;
            float d = Mathf.Sqrt((pp.x - hp.x) * (pp.x - hp.x) + (pp.z - hp.z) * (pp.z - hp.z));
            _fixDone = true;
            if (d < 4f || noFix) return;
            var to = new Vector3(pp.x, hp.y, pp.z); h.transform.position = to; try { var rb = h.rb; if (rb != null) rb.position = to; } catch { }
            StartFixes++; Lc.Line("pod: the host was " + d.ToString("F1") + " m from the pod when it came down, put under it");
        }

        internal static void Tick()
        {
            try
            {
                FixStart();
                Test();
                DroneProbe();
                Ride();
                Board();
                if (Phase1.Second != null) Guard();
            }
            catch (Exception e) { Lc.LineOnce("pod: tick threw " + e.GetType().Name + ": " + e.Message); }
            Probe();
        }

        // ---- test `-lcpodtest` (twins `-lcnopodboth`, `-lcnopodguard2`, `-lcnopodride`, `-lcnoearlyclone`) --------------------------------------
        private static int _tStage, _tOk, _tBad;
        private static float _tAt = -1f;
        private static Vector3 _hostBack, _cloneBack, _inPos; private static int _cand; private static bool _found, _leashWas;
        internal static bool TestDone { get { return _tStage >= 99; } }
        private static void Check(string name, bool ok, string info) { if (ok) _tOk++; else _tBad++; Lc.Line("pod: CHECK " + (ok ? "OK   " : "FAIL ") + name + " | " + info); }

        private static void Place(Player p, Vector3 at) { p.transform.position = at; try { var rb = p.rb; if (rb != null) rb.position = at; } catch { } }

        internal static void Test()
        {
            if (!Lc.Has("-lcpodtest") || Phase1.Second == null || _tStage >= 99 || Lc.InRunAt < 0f) return;
            float now = Time.realtimeSinceStartup;
            try
            {
                if (_tAt < 0f) { _tAt = now + 12f; return; }
                if (now < _tAt) return;
                var pod = Find1(); var h = Phase1.First(); var c = Phase1.Second;
                if (pod == null || h == null || pod.podCenter == null) { Check("the pod exists", false, "pod=" + (pod != null)); _tStage = 99; return; }
                Vector3 center = pod.podCenter.position, far = center + new Vector3(60f, 0f, 60f);
                switch (_tStage)
                {
                    case 0:
                        Check("the second dwarf rode the pod in with the host (ride happened)", Rides > 0 && RideFrames > 5, "rides=" + Rides + " frames=" + RideFrames);
                        try { Lc.Line("pod: dwarf bodies: layers " + h.gameObject.layer + "/" + c.gameObject.layer + " host rb mass=" + h.rb.mass + " | clone rb mass=" + c.rb.mass + " | colliders host/clone=" + h.GetComponentsInChildren<Collider>().Length + "/" + c.GetComponentsInChildren<Collider>().Length); } catch (Exception e) { Lc.Line("pod: body probe threw " + e.GetType().Name); }
                        _hostBack = h.transform.position; _cloneBack = c.transform.position; _leashWas = Cam2.Leash; Cam2.Leash = false;          // the leash would pull the far dwarf back
                        pod.gameObject.SetActive(true); pod.state = DropPod.EState.WAITING_FOR_PLAYER; pod.enabled = false;      // its own Update would start the departure or the time-out
                        _inPos = center; try { pod.hasPlayerBeenOnRamp = true; } catch { }        // the real box test of the game (the colliders of the dwarves), the ramp flag set by hand
                        Place(h, _inPos); Place(c, far); _tStage = 1; _tAt = now + 0.5f; break;
                    case 1:
                        Check("only the host stands inside: the pod does not count it as inside", !pod.IsPlayerInside(), "inside=" + pod.IsPlayerInside() + " both-checks=" + BothChecks);
                        Check("the host inside the waiting pod cannot be hurt", h.IsInvulnerable, "host invulnerable=" + h.IsInvulnerable + " guard=" + GuardSets);
                        Check("the second dwarf outside is not protected", !c.IsInvulnerable, "clone invulnerable=" + c.IsInvulnerable);
                        Place(c, _inPos); _tStage = 2; _tAt = now + 0.5f; break;
                    case 2:
                        Check("both stand inside: the pod counts it", pod.IsPlayerInside(), "inside=" + pod.IsPlayerInside());
                        Check("both inside: both cannot be hurt", h.IsInvulnerable && c.IsInvulnerable, "host=" + h.IsInvulnerable + " clone=" + c.IsInvulnerable);
                        Place(h, far); _tStage = 3; _tAt = now + 0.5f; break;
                    case 3:
                        Check("the host stepped out: its protection is gone again", !h.IsInvulnerable, "host invulnerable=" + h.IsInvulnerable);
                        Place(c, _inPos); try { h.isInDropPod = true; } catch { }
                        Check("a dwarf the pod has taken in still counts as inside when it is walked off the box", pod.IsPlayerInside(), "inside=" + pod.IsPlayerInside() + " flag=" + h.isInDropPod);
                        try { h.isInDropPod = false; } catch { }
                        pod.state = DropPod.EState.INACTIVE; pod.enabled = true; pod.gameObject.SetActive(false);
                        Place(h, _hostBack); Place(c, _cloneBack); _tStage = 4; _tAt = now + 0.5f; break;
                    case 4:
                        Check("with the pod gone nobody keeps the protection", !h.IsInvulnerable && !c.IsInvulnerable, "host=" + h.IsInvulnerable + " clone=" + c.IsInvulnerable);
                        Cam2.Leash = _leashWas;
                        pod.gameObject.SetActive(true); pod.enabled = false; pod.state = DropPod.EState.ANIMATING_OUT;       // the pod leaving: the tags must be gone (hand test: they stayed through the fade)
                        _tStage = 5; _tAt = now + 1.0f; break;
                    case 5:
                        Check("while the pod leaves the dwarf tags are hidden", !Tags.Visible, "tags visible=" + Tags.Visible);
                        pod.gameObject.SetActive(true); pod.enabled = false; pod.state = DropPod.EState.WAITING_FOR_PLAYER; TestBoard = true;       // stands in for the game walking the host to its spot in the pod
                        try { h.isInDropPod = true; } catch { } Place(h, center); _cloneBack = center + new Vector3(8f, 0f, 0f); Place(c, _cloneBack); _tStage = 6; _tAt = now + 2.0f; break;
                    case 6:
                        Check("host taken in, clone held by the pod walk: the pod still counts both as inside", Boards > 10 && pod.IsPlayerInside(), "inside=" + pod.IsPlayerInside() + " boards=" + Boards); { float dd = Vector3.Distance(c.transform.position, h.transform.position); Check("the second dwarf walks after the host while the game walks it into the pod", dd < 16f && Boards > 10, "distance " + dd.ToString("F1") + " m, boards=" + Boards); TestBoard = false; }
                        try { h.isInDropPod = false; } catch { } pod.state = DropPod.EState.INACTIVE; pod.enabled = true; pod.gameObject.SetActive(false); h._moveControlType = Player.EMoveControlType.FULL_CONTROL; c._moveControlType = Player.EMoveControlType.FULL_CONTROL;
                        Lc.Line("pod: SUMMARY ok=" + _tOk + " fail=" + _tBad + (Lc.Has("-lcnopodboth") || Lc.Has("-lcnopodguard2") || Lc.Has("-lcnopodride") || Lc.Has("-lcnopodlatch") || Lc.Has("-lcnopodboard") || Lc.Has("-lcnotagfade") ?" (twin: FAIL expected)" : ""));
                        _tStage = 99; break;
                }
            }
            catch (Exception e) { Lc.LineOnce("pod: test threw " + e.GetType().Name + ": " + e.Message); _tStage = 99; }
        }

        // ---- companion drone probe `-lcdroneprobe`: does the second dwarf have a Bosco of its own, and does it work? ------------------------
        private static float _droneAt = -1f; private static int _droneN;

        private static string BoscoInfo(Player p)
        {
            try
            {
                var b = p.bosco;
                int ws = p.boscoWeapons != null ? p.boscoWeapons.Count : -1;
                if (b == null) return "bosco=null weapons=" + ws;
                var tr = b.transform;
                return "bosco='" + b.gameObject.name + "' ptr=" + b.Pointer.ToInt64().ToString("X") + " active=" + b.gameObject.activeInHierarchy + " pos=" + V(tr.position) + " parent=" + (tr.parent != null ? tr.parent.name : "-") + " weapons=" + ws + " starter=" + (p.boscoStarterWeaponData != null ? p.boscoStarterWeaponData.name : "-");
            }
            catch (Exception e) { return "err " + e.GetType().Name; }
        }

        private static void DroneProbe()
        {
            if (!Lc.Has("-lcdroneprobe") || Phase1.Second == null || !Phase1.Ready) return;
            float now = Time.realtimeSinceStartup;
            if (_droneAt < 0f) _droneAt = now + 3f;
            if (now < _droneAt || _droneN >= 4) return;
            _droneAt = now + 6f; _droneN++;
            var h = Phase1.First(); var c = Phase1.Second;
            Lc.Line("drone: host " + BoscoInfo(h) + " | clone " + BoscoInfo(c));
            int n = 0;
            foreach (var d in UnityEngine.Object.FindObjectsOfType<Drone>()) { if (d == null) continue; n++; string ow = "?"; try { ow = d.owner != null ? (d.owner.Pointer == h.Pointer ? "host" : d.owner.Pointer == c.Pointer ? "clone" : "other") : "none"; } catch { } Lc.Line("drone: Drone '" + d.gameObject.name + "' owner=" + ow + " active=" + d.gameObject.activeInHierarchy + " pos=" + V(d.transform.position)); }
            Lc.Line("drone: " + n + " Drone objects in the scene");
        }

        private static void Probe()
        {
            if (!(Lc.Has("-lcpodprobe") || Settings.Debug) || Lc.InRunAt < 0f) return;
            float now = Time.realtimeSinceStartup;
            if (now < _probeAt) return;
            _probeAt = now + 0.25f;
            try
            {
                var pod = Find1();
                var h = Phase1.First();
                if (h == null) return;
                if (pod == null) { Lc.Line("podprobe: no pod | host " + Dwarf(h) + " | clone " + Dwarf(Phase1.Second)); return; }
                string ptr = "-";
                try { ptr = pod.podTransform != null ? V(pod.podTransform.position) : "-"; } catch { }
                bool inside = false; try { inside = pod.IsPlayerInside(); } catch { }
                Lc.Line("podprobe: state=" + pod.state + " inside=" + inside + " pod=" + ptr + " | host " + Dwarf(h) + " | clone " + Dwarf(Phase1.Second) + " t=" + (now - Lc.InRunAt).ToString("F1"));
            }
            catch (Exception e) { Lc.LineOnce("podprobe: threw " + e.GetType().Name + ": " + e.Message); }
        }
    }
}
