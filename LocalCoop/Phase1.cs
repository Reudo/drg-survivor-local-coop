using System;
using System.Text;
using UnityEngine;
using Zenject;
using Assets.Scripts.SkillSystem;
using Assets.Scripts.LevelGeneration;

namespace LocalCoop
{
    /// <summary>
    /// Phase 1 experiments: can one game instance hold a second real Player?
    /// Every step is behind `-lcp1=&lt;name&gt;`, logged line by line, and wrapped: an experiment
    /// must report why it failed, never take the game down.
    ///
    /// Experiment "clone": instantiate the live dwarf's GameObject under an inactive parent (so Awake and
    /// Start do not run with empty dependencies), let Zenject inject it (`InjectGameObject`, the game's
    /// Player.Inject takes 28 services including the DiContainer), then activate it next to the first one.
    /// </summary>
    internal static class Phase1
    {
        internal static string Mode = "";
        internal static GameObject Clone;
        internal static Player Second;
        private static bool _done;
        private static float _startAt = -1f;

        internal static string AcceptKind = "";   // "near" / "kill": acceptance runs that end with a RESULT line
        internal static bool NoRetarget;           // test twin: enemies keep chasing the host
        internal static bool ForceAll;   // -lcp1=clone-force: every enemy targets the clone (proves the field steers the chase)

        internal static void Init()
        {
            Mode = Lc.Str("-lcp1=");
            if (Mode == "clone-force") { ForceAll = true; Mode = "clone-target"; }
            if (Mode == "accept-near") { AcceptKind = "near"; Mode = "clone-target"; }
            if (Mode == "accept-kill") { AcceptKind = "kill"; ForceAll = true; Mode = "clone-target"; }
            NoRetarget = Lc.Has("-lcnoretarget");
        }

        private static float _godAt;
        private static float _walkFrom = -1f;
        private static Vector3 _walkStart;
        private static float _walkLogAt;

        /// <summary>Test input for the second dwarf: walk right for 6 s, written into rawMoveInput every frame.</summary>
        private static void DriveWalk()
        {
            if (_walkFrom < 0f) return;
            float t = Time.realtimeSinceStartup - _walkFrom;
            float walkSeconds = Mode == "clone-target" ? 1.2f : 6f;
            if (t < walkSeconds)
            {
                Second.rawMoveInput = new Vector2(1f, 0f);
                if (Mode == "clone-route")
                {
                    // Input reaches the clone only through the game's own callbacks (routed by device); nothing is written into moveDir here.
                    // right, then left, then up: if the clone stops at a wall it must come back; a stuck state would not
                    VirtualPad.SetStick(t < 2f ? new Vector2(1f, 0f) : t < 4f ? new Vector2(-1f, 0f) : new Vector2(0f, 1f));
                }
                else if (Mode == "clone-pad")
                {
                    VirtualPad.SetStick(new Vector2(1f, 0f));            // the "controller" pushes right
                    Second.moveDir = VirtualPad.ReadStick();             // the plugin reads controller 2 and feeds the second dwarf
                }
                else
                {
                    // Move() reads moveDir when the control type is FULL_CONTROL (decompiled: forcedMoveDir only for FORCED).
                    Second.moveDir = new Vector2(1f, 0f);
                }
                if (Time.realtimeSinceStartup > _walkLogAt)
                {
                    _walkLogAt = Time.realtimeSinceStartup + 1f;
                    var d = Second.transform.position - _walkStart;
                    try
                    {
                        var rb = Second.rb;
                        Lc.Line("p1: inject state gameState=" + (Second.gameStateProvider != null) + " camera=" + (Second.cameraController != null) + " gameController=" + (Second.gameController != null)
                            + " movement=" + (Second.PlayerMovement != null) + " moveDelta=" + Second.moveDelta);
                        Lc.Line("p1: walk diag enabled=" + Second.enabled + " hasMoveInput=" + Second.HasMoveInput + " isMoving=" + Second.IsMoving
                            + " rawIn=" + Second.rawMoveInput + " rb=" + (rb != null ? "present" : "null")
                            + " speedStat=" + (Second._moveSpeed_k__BackingField != null ? Second._moveSpeed_k__BackingField.Value.ToString() : "null")
                            + " timeScale=" + Time.timeScale);
                    }
                    catch (Exception e) { Lc.Line("p1: walk diag threw: " + e.Message); }
                    Lc.Line("p1: walk t=" + t.ToString("F1") + " moved=" + d.magnitude.ToString("F2") + " moveDir=" + Second.moveDir
                        + " ctrl=" + Second._moveControlType);
                }
            }
            else if (t < walkSeconds + 0.2f) { Second.rawMoveInput = Vector2.zero; Second.moveDir = Vector2.zero; if (Mode == "clone-pad" || Mode == "clone-route") VirtualPad.SetStick(Vector2.zero); Lc.Line("p1: walk end moved=" + (Second.transform.position - _walkStart).magnitude.ToString("F2")); _walkFrom = -2f; }
        }


        private static string Names(Player p)
        {
            var sb = new StringBuilder();
            try { foreach (var c in p.GetComponents<Component>()) sb.Append(c == null ? "null" : c.GetType().Name).Append(' '); } catch { }
            return sb.ToString();
        }

        /// <summary>What the movement and animator behaviour of each dwarf point at (a clone keeps references to the original).</summary>
        private static void Diag(Player a, Player b)
        {
            try
            {
                Lc.Line("p1: comps first: " + Names(a));
                Lc.Line("p1: comps clone: " + Names(b));
                foreach (var pr in new[] { a, b })
                {
                    var m = pr.PlayerMovement; var an = pr.PlayerAnimator;
                    string mp = "null", ap = "null";
                    try { if (m != null) mp = m.GetType().Name + " ownerIsSelf=" + (m.player != null && m.player.gameObject == pr.gameObject); } catch (Exception e) { mp = "err " + e.Message; }
                    try { if (an != null) ap = an.GetType().Name; } catch (Exception e) { ap = "err " + e.Message; }
                    Lc.Line("p1: " + pr.gameObject.name + " movement=" + mp + " animator=" + ap);
                }
            }
            catch (Exception e) { Lc.Line("p1: diag threw: " + e.Message); }
        }


        /// <summary>The original gets its movement/animator objects at class setup; the clone has none.</summary>
        private static float _skinLogAt = -1f, _skinDeadline;
        internal static string SkinReport = "";

        private static string GfxChildren(Player p)
        {
            var sb = new StringBuilder();
            try
            {
                var gfx = p._gfxContainer;
                for (int i = 0; gfx != null && i < gfx.childCount; i++) { var c = gfx.GetChild(i); sb.Append(c.name).Append(c.gameObject.activeSelf ? "(on) " : "(off) "); }
            }
            catch { }
            return sb.ToString();
        }

        /// <summary>The clone is a copy of P1's hierarchy, including P1's class model. Switch the copies off and load the model of P2's subclass.</summary>
        private static void ApplyP2Skin(Player p)
        {
            try
            {
                Lc.Line("p1: skin before: host [" + GfxChildren(Phase1.First()) + "] clone [" + GfxChildren(p) + "]");
                var gfx = p._gfxContainer;
                for (int i = 0; gfx != null && i < gfx.childCount; i++) { var ch = gfx.GetChild(i); if (ch.name.StartsWith("Model_")) ch.gameObject.SetActive(false); }
                var sc = p.SkinController;
                if (sc == null) { Lc.Line("p1: clone has no skin controller"); return; }
                sc.LoadOrSetSkin(CoopMenu.P2.Sub);
                _skinLogAt = Time.realtimeSinceStartup + 0.2f; _skinDeadline = Time.realtimeSinceStartup + 5f;
                Lc.Line("p1: LoadOrSetSkin(" + CoopMenu.P2.Sub.name + ") called");
            }
            catch (Exception e) { Lc.Line("p1: ApplyP2Skin threw: " + e.GetType().Name + ": " + e.Message); }
        }

        internal static Player First()
        {
            var gc = Find.One<GameController>();
            return gc != null ? gc.player : null;
        }

        private static float _animFixAt;
        private static int _animFixTries;
        internal static string AnimFixReport = "";

        /// <summary>Test: name of the animator controller the second dwarf runs on.</summary>
        internal static string CloneControllerName() { try { var a = ActiveAnimator(Second); return a != null ? a.runtimeAnimatorController.name : ""; } catch { return ""; } }

        private static Animator ActiveAnimator(Player p)
        {
            foreach (var a in p.GetComponentsInChildren<Animator>(false))
                if (a != null && a.gameObject.activeInHierarchy && a.runtimeAnimatorController != null) return a;
            return null;
        }

        /// <summary>The model that LoadOrSetSkin loads for the clone comes with the menu's animator controller (one idle clip, three parameters), and the
        /// clone's PlayerAnimator still points at the copied model's animator. Give the new model the host's run controller and bind the PlayerAnimator to it.
        /// Twin: `-lcnoanimfix`.</summary>
        private static void FixAnimator()
        {
            if (Second == null || !P2ClassChosen() || Lc.Has("-lcnoanimfix") || _animFixTries >= 6) return;
            float now = Time.realtimeSinceStartup;
            if (now < _animFixAt) return;
            _animFixAt = now + 0.25f;
            try
            {
                var host = ActiveAnimator(First());
                var mine = ActiveAnimator(Second);
                if (host == null || mine == null) return;
                Lc.LineOnce("p1: animator controllers host=" + host.runtimeAnimatorController.name + " (" + host.gameObject.name + ", layers " + host.layerCount + ") clone=" + mine.runtimeAnimatorController.name + " (" + mine.gameObject.name + ", layers " + mine.layerCount + ")");
                // hand test: the Driller as P2 had the wrong mining/attack animation. Each dwarf kind runs on its own controller; the Driller on
                // CTRL_Dwarves_Redux_DRILLER, the others on CTRL_Dwarves_Redux. The clone gets the one of its own class, not simply the host's. Twin `-lcnodrillerctl`.
                var want = host.runtimeAnimatorController;
                if (!Lc.Has("-lcnodrillerctl"))
                {
                    string wn = CoopMenu.P2.ClassIndex == 1 ? "CTRL_Dwarves_Redux_DRILLER" : "CTRL_Dwarves_Redux";
                    if (want.name != wn) foreach (var c in Resources.FindObjectsOfTypeAll<RuntimeAnimatorController>()) if (c != null && c.name == wn) { want = c; break; }
                }
                if (mine.runtimeAnimatorController.name == want.name) { if (_animFixTries > 0) _animFixTries = 99; return; }
                _animFixTries++;
                mine.runtimeAnimatorController = want;
                mine.Rebind();
                var an = Second.PlayerAnimator;
                if (an != null) an.Init(Second, Second._gfxContainer, Second.classArtifactData);
                AnimFixReport = "controller " + mine.runtimeAnimatorController.name + " set on " + mine.gameObject.name + ", PlayerAnimator re-initialised";
                Lc.Line("p1: " + AnimFixReport);
            }
            catch (Exception e) { Lc.Line("p1: animator fix threw: " + e.GetType().Name + ": " + e.Message); }
        }

        private static void SkinTick()
        {
            if (_skinLogAt < 0f || Time.realtimeSinceStartup < _skinLogAt || Second == null) return;
            try
            {
                var sc = Second.SkinController;
                if (!(sc != null && sc.SkinLoaded) && Time.realtimeSinceStartup < _skinDeadline) { _skinLogAt = Time.realtimeSinceStartup + 0.2f; return; }
                _skinLogAt = -1f;
                SkinReport = "clone [" + GfxChildren(Second) + "] skinLoaded=" + (sc != null && sc.SkinLoaded) + " activeSkin=" + (sc != null && sc.ActiveSkin != null ? sc.ActiveSkin.name : "null");
                Lc.Line("p1: skin after: " + SkinReport);
            }
            catch (Exception e) { Lc.Line("p1: skin tick threw: " + e.GetType().Name + ": " + e.Message); }
        }

        private static void CreateBehaviours(Player p, Player template)
        {
            try { p.CreatePlayerMovement<WalkPlayerMovement>(); Lc.Line("p1: CreatePlayerMovement<WalkPlayerMovement> ok"); }
            catch (Exception e) { Lc.Line("p1: CreatePlayerMovement threw: " + e.GetType().Name + ": " + e.Message); }
            try { p.CreatePlayerAnimator<WalkPlayerAnimator>(); Lc.Line("p1: CreatePlayerAnimator<WalkPlayerAnimator> ok"); }
            catch (Exception e) { Lc.Line("p1: CreatePlayerAnimator threw: " + e.GetType().Name + ": " + e.Message); }
            try
            {
                var m = p.PlayerMovement; var an = p.PlayerAnimator;
                Lc.Line("p1: after create: movement=" + (m != null ? "set owner=" + (m.player != null) : "null") + " animator=" + (an != null ? "set" : "null"));
                // The clone has no class artifact data and its movement has no rigidbody; the original has both.
                // Init(player, gfx, artifact) stores them and runs InitSelf, which caches the rigidbody.
                p.classArtifactData = P2ClassChosen() && CoopMenu.P2.Sub != null ? CoopMenu.P2.Sub : template.classArtifactData;
                if (m != null)
                {
                    m.Init(p, p._gfxContainer, p.classArtifactData);
                    Lc.Line("p1: movement.Init(clone, gfx, template artifact) -> rb=" + (m.rb != null) + " cad=" + (m.classArtifactData != null));
                }
                if (an != null)
                {
                    try { an.Init(p, p._gfxContainer, p.classArtifactData); Lc.Line("p1: animator.Init ok"); }
                    catch (Exception e) { Lc.Line("p1: animator.Init threw: " + e.GetType().Name + ": " + e.Message); }
                }
            }
            catch (Exception e) { Lc.Line("p1: post-create threw: " + e.GetType().Name + ": " + e.Message); }
        }


        private static float _fwStart = -1f, _fwLog;
        private static Vector3 _fwFrom;

        /// <summary>Control test: does writing rawMoveInput move the ORIGINAL dwarf? Decides whether that field is the right lever.</summary>
        private static void FirstWalk()
        {
            if (Lc.InRunAt < 0f) return;
            float now = Time.realtimeSinceStartup;
            if (_fwStart < 0f) _fwStart = now + 8f;
            if (now < _fwStart || now > _fwStart + 6f) return;
            var gc = Find.One<GameController>();
            var f = gc != null ? gc.player : null;
            if (f == null) return;
            if (_fwFrom == Vector3.zero) { _fwFrom = f.transform.position; Lc.Line("p1: first-walk start at " + _fwFrom + " ctrl=" + f._moveControlType); }
            f.rawMoveInput = new Vector2(1f, 0f);
            try { f.ForceMove(new Vector2(1f, 0f), 0.2f); } catch (Exception e) { Lc.LineOnce("p1: ForceMove threw: " + e.GetType().Name + ": " + e.Message); }
            if (now > _fwLog)
            {
                _fwLog = now + 1f;
                Lc.Line("p1: first-walk moved=" + (f.transform.position - _fwFrom).magnitude.ToString("F2") + " hasMoveInput=" + f.HasMoveInput + " isMoving=" + f.IsMoving + " moveDir=" + f.moveDir + " forcedLeft=" + f.forcedMoveDurationLeft + " ctrl=" + f._moveControlType);
            }
        }


        private static bool _rvaDone;

        /// <summary>Writes the RVA (inside GameAssembly.dll) of Player's native methods, so their code can be read offline.</summary>
        private static void DumpRva()
        {
            try
            {
                long baseAddr = 0;
                foreach (System.Diagnostics.ProcessModule m in System.Diagnostics.Process.GetCurrentProcess().Modules)
                    if (m.ModuleName.Equals("GameAssembly.dll", StringComparison.OrdinalIgnoreCase)) baseAddr = (long)m.BaseAddress;
                Lc.Line("p1: GameAssembly base=0x" + baseAddr.ToString("X"));
                foreach (var t in new[] { typeof(Player), typeof(Entity), typeof(Enemy) })
                    foreach (var f in t.GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly))
                    {
                        if (!f.Name.StartsWith("NativeFieldInfoPtr_") || f.FieldType != typeof(IntPtr)) continue;
                        var fi = (IntPtr)f.GetValue(null);
                        if (fi == IntPtr.Zero) continue;
                        uint off = Il2CppInterop.Runtime.IL2CPP.il2cpp_field_get_offset(fi);
                        Lc.Line("off " + t.Name + "." + f.Name.Substring("NativeFieldInfoPtr_".Length) + " 0x" + off.ToString("X"));
                    }
                foreach (var t in new[] { typeof(Player), typeof(Entity), typeof(WalkPlayerMovement), typeof(BasePlayerMovement), typeof(Enemy), typeof(EnemySpawner) })
                {
                    foreach (var f in t.GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly))
                    {
                        if (!f.Name.StartsWith("NativeMethodInfoPtr_") || f.FieldType != typeof(IntPtr)) continue;
                        var mi = (IntPtr)f.GetValue(null);
                        if (mi == IntPtr.Zero) continue;
                        long fn = (long)System.Runtime.InteropServices.Marshal.ReadIntPtr(mi);
                        Lc.Line("rva " + t.Name + "." + f.Name.Substring("NativeMethodInfoPtr_".Length) + " 0x" + (fn - baseAddr).ToString("X"));
                    }
                }
            }
            catch (Exception e) { Lc.Line("p1: rva dump threw: " + e.GetType().Name + ": " + e.Message); }
        }


        /// <summary>The second dwarf gets the class and subclass chosen for P2 in the co-op menu, when the run was started from there
        /// (`-lcnop2class` is the twin: the clone then stays a copy of P1's class).</summary>
        internal static bool P2ClassChosen() { return CoopStart.Started && !Lc.Has("-lcnop2class") && CoopMenu.Built; }

        private static void EquipWeapon(Player p, Player template)
        {
            try
            {
                var wh = template.weapons != null && template.weapons.Count > 0 ? template.weapons[0] : null;
                WeaponSkillData data = wh != null ? wh.data : null;
                if (P2ClassChosen() && CoopMenu.P2.Sub != null && CoopMenu.P2.Sub.StarterWeapon != null)
                {
                    data = CoopMenu.P2.Sub.StarterWeapon;           // the chosen subclass's own starter weapon
                    try { p.EquipArtifact(CoopMenu.P2.Sub, true); Lc.Line("p1: class artifact " + CoopMenu.P2.Sub.name + " equipped on the clone"); }
                    catch (Exception e) { Lc.Line("p1: EquipArtifact threw: " + e.GetType().Name + ": " + e.Message); }
                }
                if (data == null) { Lc.Line("p1: template has no weapon data"); return; }
                Lc.Line("p1: equipping '" + data.name + "' on the clone");
                var made = p.EquipNewWeapon(data, false);
                Lc.Line("p1: EquipNewWeapon returned " + (made != null) + ", clone weapons=" + (p.weapons != null ? p.weapons.Count : -1)
                    + " weaponsActive=" + p.weaponsActive);
                p.weaponsActive = true;
                try { p.ToggleWeapons(true); Lc.Line("p1: ToggleWeapons(true) ok"); } catch (Exception e) { Lc.Line("p1: ToggleWeapons threw: " + e.GetType().Name + ": " + e.Message); }
            }
            catch (Exception e) { Lc.Line("p1: EquipNewWeapon threw: " + e.GetType().Name + ": " + e.Message); }
        }

        private static string WeaponDamage(Player p)
        {
            try
            {
                if (p == null || p.weapons == null) return "n/a";
                var sb = new StringBuilder();
                for (int i = 0; i < p.weapons.Count; i++)
                {
                    var w = p.weapons[i];
                    if (i > 0) sb.Append('+');
                    sb.Append(w != null ? w.trackedTotalDamage.ToString() : "null");
                }
                return sb.Length == 0 ? "0weapons" : sb.ToString();
            }
            catch { return "err"; }
        }


        private static string WeaponState(Player p, string tag)
        {
            try
            {
                if (p == null || p.weapons == null || p.weapons.Count == 0) return " | " + tag + " w=none";
                var w = p.weapons[0];
                return " | " + tag + " weaponsActive=" + p.weaponsActive + " paused=" + w.paused + " range=" + w.range + " attackSpeed=" + w.attackSpeed
                    + " target=" + (w.target != null) + " owner=" + (w.owner != null && w.owner.gameObject == p.gameObject) + " reload=" + w.reloadTimeLeft + " jam=" + w.isJammed;
            }
            catch { return " | " + tag + " err"; }
        }


        private static readonly System.Collections.Generic.HashSet<int> _seenFirst = new System.Collections.Generic.HashSet<int>();
        private static readonly System.Collections.Generic.HashSet<int> _seenSecond = new System.Collections.Generic.HashSet<int>();
        private static float _projAt;

        /// <summary>Counts distinct projectiles per owner (instance ids can repeat through the pool, so a flight is counted when first seen active).</summary>
        private static void SampleProjectiles()
        {
            float now = Time.realtimeSinceStartup;
            if (now < _projAt) return;
            _projAt = now + 0.1f;
            try
            {
                var gc = Find.One<GameController>();
                var first = gc != null ? gc.player : null;
                var all = Find.Many<Projectile>(0.1f);
                for (int i = 0; i < all.Length; i++)
                {
                    var pr = all[i];
                    if (pr == null || pr.isInPool || pr.owner == null) continue;
                    int id = pr.GetInstanceID();
                    // A pooled projectile is reused; remember (id, spawn time) so repeated flights count again.
                    int key = id ^ (int)(pr.lifeEndAt * 100f);
                    if (pr.owner.gameObject == Second.gameObject) _seenSecond.Add(key);
                    else if (first != null && pr.owner.gameObject == first.gameObject) _seenFirst.Add(key);
                }
            }
            catch { }
        }

        internal static int ShotsSecond { get { return _seenSecond.Count; } }

        internal static string ShotCounts() { return " shots first/second=" + _seenFirst.Count + "/" + _seenSecond.Count; }


        /// <summary>The run is live for the second dwarf's weapons: the host is alive and no end screen is up (hand test: P2's weapons kept firing over "mission failed").</summary>
        private static bool RunOn()
        {
            if (Lc.Has("-lcweaponsalways")) return true;     // twin
            try { var h = First(); if (h != null && !h.isAlive) return false; return !Find.AnyActive<DRS.UI.UIEndScreen>(); } catch { return true; }
        }

        private static float _vitalsAt, _podSince = -1f;

        /// <summary>
        /// Hand test: after the supply pod the second dwarf took no more damage from stage 2 on. The drop pod flags the dwarf that flies in
        /// (isInDropPod); if the second dwarf keeps that flag while the host is out, it is let out (OnExitDropPod). Every 20 s the hit points and flags
        /// of both go to lc.log, so a repeat shows what was set. Twin `-lcnopodguard`.
        /// </summary>
        private static void PodGuard()
        {
            try
            {
                var h = First(); var c = Second;
                if (h == null || c == null || !_ready) return;
                float now = Time.realtimeSinceStartup;
                if (now > _vitalsAt) { _vitalsAt = now + 20f; Lc.Line("p1: vitals host hp " + h.currentHp + " clone hp " + c.currentHp + " | inPod " + h.isInDropPod + "/" + c.isInDropPod + " invulnerable " + h.IsInvulnerable + "/" + c.IsInvulnerable + " control " + h._moveControlType + "/" + c._moveControlType + " alive " + h.isAlive + "/" + c.isAlive); }
                if (Lc.Has("-lcnopodguard")) return;
                if (c.isInDropPod && !h.isInDropPod)
                {
                    if (_podSince < 0f) _podSince = now;
                    else if (now > _podSince + 3f) { _podSince = -1f; c.OnExitDropPod(); Lc.Line("p1: the second dwarf was still flagged as in the drop pod, let out"); }
                }
                else _podSince = -1f;
            }
            catch (Exception e) { Lc.LineOnce("p1: pod guard threw " + e.GetType().Name + ": " + e.Message); }
        }

        private static float _wlog;

        /// <summary>
        /// The game ticks the clone's weapons itself (PLAN.md: `-lcnoweapontick` stayed green). The extra tick from here made them run at twice the speed: hand test, the
        /// end screen showed player 2's damage too high and its damage per second too low; measured: the clone's weapon `activeTime` was 2.07x the other dwarf's and longer than
        /// the run itself. Off by default now. Twin `-lcweapontick` (the old extra tick).
        /// </summary>
        private static void TickCloneWeapons()
        {
            if (!Lc.Has("-lcweapontick")) return;
            try
            {
                if (Second.weapons == null) return;
                float dt = Time.deltaTime;
                for (int i = 0; i < Second.weapons.Count; i++)
                {
                    var w = Second.weapons[i];
                    if (w == null) continue;
                    w.DoUpdate(dt);
                }
            }
            catch (Exception e)
            {
                if (Time.realtimeSinceStartup > _wlog) { _wlog = Time.realtimeSinceStartup + 5f; Lc.Line("p1: weapon tick threw: " + e.GetType().Name + ": " + e.Message); }
            }
        }


        private static System.Reflection.FieldInfo _enemyPlayerFi;
        private static float _retargetAt;
        internal static int RetargetedToClone;
        private static int _enemiesOnClone;

        /// <summary>Enemy.player is read once at spawn, and the interop setter writes the old value back (it passes the getter, not
        /// `value`), so the field is written through the pointer with the GC write barrier.</summary>
        private static readonly System.Collections.Generic.Dictionary<string, uint> _offsets = new System.Collections.Generic.Dictionary<string, uint>();

        /// <summary>Writes an object reference field through its pointer (with the GC write barrier), by the interop's field-info pointer.</summary>
        internal static void SetRef(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase obj, Type t, string field, IntPtr value)
        {
            if (obj == null) return;
            string key = t.FullName + "." + field;
            uint off;
            if (!_offsets.TryGetValue(key, out off))
            {
                var fi = t.GetField("NativeFieldInfoPtr_" + field, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                off = Il2CppInterop.Runtime.IL2CPP.il2cpp_field_get_offset((IntPtr)fi.GetValue(null));
                _offsets[key] = off;
            }
            IntPtr o = obj.Pointer;
            Il2CppInterop.Runtime.IL2CPP.il2cpp_gc_wbarrier_set_field(o, o + (int)off, value);
        }

        /// <summary>Writes a bool field through its pointer (same reason as SetRef: the interop's setters are not trusted).</summary>
        internal static void SetBool(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase obj, Type t, string field, bool value)
        {
            if (obj == null) return;
            string key = t.FullName + "." + field;
            uint off;
            if (!_offsets.TryGetValue(key, out off))
            {
                var fi = t.GetField("NativeFieldInfoPtr_" + field, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                off = Il2CppInterop.Runtime.IL2CPP.il2cpp_field_get_offset((IntPtr)fi.GetValue(null));
                _offsets[key] = off;
            }
            System.Runtime.InteropServices.Marshal.WriteByte(obj.Pointer + (int)off, (byte)(value ? 1 : 0));
        }

        /// <summary>An enemy's target lives in four places: its own field, the behaviour's target, the melee handler's player, and each ranged handler's target.</summary>
        private static void SetEnemyTarget(Enemy e, Player p)
        {
            SetRef(e, typeof(Enemy), "player", p.Pointer);
            var b = e.behaviour;
            if (b == null) return;
            SetRef(b, typeof(BaseEnemyBehaviour), "target", p.Pointer);
            var melee = b.AIMeleeSkillHandler;
            if (melee != null) SetRef(melee, typeof(AIMeleeSkillHandler), "player", p.Pointer);
            var wl = b.weaponHandlers;
            if (wl != null)
                for (int k = 0; k < wl.Count; k++)
                    if (wl[k] != null) SetRef(wl[k], typeof(AIWeaponHandler), "target", p.Pointer);
        }

        private static System.Reflection.FieldInfo _behaviourTargetFi;
        private static bool _verifiedTarget;

        /// <summary>Reads the targets back through the game's own getters once, so "set" is not assumed.</summary>
        private static void VerifyTarget(Enemy e, Player want)
        {
            if (_verifiedTarget) return;
            _verifiedTarget = true;
            try
            {
                var b = e.behaviour;
                Lc.Line("p1: verify enemy.player is target=" + (e.player != null && e.player.Pointer == want.Pointer)
                    + " behaviour.target is target=" + (b != null && b.target != null && b.target.Pointer == want.Pointer));
            }
            catch (Exception ex) { Lc.Line("p1: verify threw: " + ex.GetType().Name + ": " + ex.Message); }
        }

        /// <summary>Every enemy goes for the nearer living dwarf.</summary>
        internal static int MaxEnemiesOnClone { get { return RetargetedToClone; } }

        private static void Retarget()
        {
            float now = Time.realtimeSinceStartup;
            if (now < _retargetAt || NoRetarget) return;
            _retargetAt = now + 0.25f;
            try
            {
                var gc = Find.One<GameController>();
                var first = gc != null ? gc.player : null;
                if (first == null || Second == null) return;
                // hand test: with one dwarf down the enemies stayed on the place of the fall. They all go for the one that is up.
                bool hostUp = first.isAlive, cloneUp = Second.isAlive;
                if (!hostUp && !cloneUp) return;
                if (!cloneUp && Lc.Has("-lcnoretargetdown")) return;
                var pf = first.transform.position; var ps = Second.transform.position;
                var all = Find.Many<Enemy>(0.2f);
                int onClone = 0;
                for (int i = 0; i < all.Length; i++)
                {
                    var e = all[i];
                    if (e == null || !e.isAlive) continue;
                    var pe = e.transform.position;
                    float df = (pe - pf).sqrMagnitude, ds = (pe - ps).sqrMagnitude;
                    var want = !cloneUp ? first : !hostUp ? Second : ((ForceAll || ds < df) ? Second : first);
                    if (want == Second) onClone++;
                    var cur = e.player;
                    if (cur == null || cur.Pointer != want.Pointer) SetEnemyTarget(e, want);
                    VerifyTarget(e, want);
                }
                _enemiesOnClone = onClone;
                if (onClone > RetargetedToClone) RetargetedToClone = onClone;
            }
            catch (Exception e) { Lc.LineOnce("p1: retarget threw: " + e.GetType().Name + ": " + e.Message); }
        }

        internal static string TargetInfo() { return " enemiesOnClone=" + _enemiesOnClone + " (max " + RetargetedToClone + ")"; }


        private static string NearInfo(Player p)
        {
            try
            {
                int near = 0, n5 = 0;
                foreach (var e in UnityEngine.Object.FindObjectsOfType<Enemy>())
                {
                    if (e == null || !e.isAlive) continue;
                    float d = (e.transform.position - p.transform.position).magnitude;
                    if (d < 2f) near++;
                    if (d < 5f) n5++;
                }
                return " enemies<2m=" + near + " <5m=" + n5;
            }
            catch { return ""; }
        }


        // ---- acceptance bookkeeping ------------------------------------------------------------------
        private static Vector3 _accStart;
        private static float _accMaxMoved, _accCloneMinHp = 1e9f, _accHostMinHp = 1e9f, _accStartAt = -1f, _accSampleAt;
        private static int _accTargetOk, _accTargetN, _accCloneTargets, _accHostTargets;
        private static bool _accDeath, _accEnd, _accDone, _accSawAlive, _accLevelRecovered;
        private static float _accDeathAt = -1f;

        /// <summary>Collects what the acceptance runs assert and writes the RESULT line. Reads only; no verdict is guessed from one frame.</summary>
        private static void AcceptTick()
        {
            if (_accDone) return;
            float now = Time.realtimeSinceStartup;
            try
            {
                var gc = Find.One<GameController>();
                var first = gc != null ? gc.player : null;
                if (_accStartAt < 0f) { _accStartAt = now; _accStart = Second.transform.position; }
                float mv = (Second.transform.position - _accStart).magnitude;
                if (mv > 300f) Lc.LineOnce("p1: ignoring an implausible clone position jump of " + mv.ToString("F0") + " m at " + Second.transform.position);
                else _accMaxMoved = Mathf.Max(_accMaxMoved, mv);
                if (Second.isAlive) { _accSawAlive = true; _accCloneMinHp = Mathf.Min(_accCloneMinHp, Second.currentHp); }
                if (first != null && first.isAlive) _accHostMinHp = Mathf.Min(_accHostMinHp, first.currentHp);
                if (_accSawAlive && !Second.isAlive && !_accDeath) { _accDeath = true; _accDeathAt = now; }
                string form = AutoStart.ActiveFormName() ?? "";
                if (AutoStart.LevelUpsAnswered >= 1 && form == "UICoreForm" && Time.timeScale > 0.5f) _accLevelRecovered = true;
                if (_accDeath && (form == "CoreOutro" || form == "UIEndScreen" || Lc.InRunAt < 0f)) _accEnd = true;

                // Does each enemy chase the dwarf the rule says? (sampled, only after the first assignment pass)
                if (now >= _accSampleAt && RetargetedToClone > 0 && first != null && Second.isAlive)
                {
                    _accSampleAt = now + 1f;
                    foreach (var e in UnityEngine.Object.FindObjectsOfType<Enemy>())
                    {
                        if (e == null || !e.isAlive || e.behaviour == null || e.behaviour.target == null) continue;
                        bool wantClone = ForceAll || (e.transform.position - Second.transform.position).sqrMagnitude < (e.transform.position - first.transform.position).sqrMagnitude;
                        var want = wantClone ? Second.Pointer : first.Pointer;
                        _accTargetN++;
                        if (wantClone) _accCloneTargets++; else _accHostTargets++;
                        if (e.behaviour.target.Pointer == want) _accTargetOk++;
                    }
                }

                bool near = AcceptKind == "near";
                float runFor = near ? 100f : 170f;
                bool finished = near ? now - _accStartAt > runFor : (_accEnd && now - _accDeathAt > 3f) || now - _accStartAt > runFor;
                if (!finished) return;
                _accDone = true;

                int shots = _seenSecond.Count;
                double ratio = _accTargetN > 0 ? (double)_accTargetOk / _accTargetN : 0.0;
                bool moved = _accMaxMoved >= 3f;
                bool shot = shots >= 3;
                bool targets = RetargetedToClone > 0 && ratio >= 0.95;
                bool cloneHurt = _accCloneMinHp < 145f;
                bool hostHurt = _accHostMinHp < 145f;
                bool levelup = AutoStart.LevelUpsAnswered >= 1 && _accLevelRecovered;
                bool pass;
                string why;
                if (near)
                {
                    pass = moved && shot && targets && _accCloneTargets > 0 && _accHostTargets > 0 && cloneHurt && levelup && Lc.Errors == 0;
                    why = "moved=" + moved + "(" + _accMaxMoved.ToString("F1") + "m) shot=" + shot + "(" + shots + ") targets=" + targets + "(ratio " + ratio.ToString("F2") + " n=" + _accTargetN
                        + ") enemiesOnClone/onHost=" + _accCloneTargets + "/" + _accHostTargets + " cloneHurt=" + cloneHurt + "(min " + _accCloneMinHp + ") hostMinHp=" + _accHostMinHp + " levelup=" + levelup + "(answered " + AutoStart.LevelUpsAnswered + ", run continued " + _accLevelRecovered + ")";
                }
                else
                {
                    pass = moved && shot && targets && _accDeath && _accEnd && !(first != null && !first.isAlive) && Lc.Errors == 0;
                    why = "moved=" + moved + " shot=" + shot + "(" + shots + ") targets=" + targets + "(ratio " + ratio.ToString("F2") + ") cloneDied=" + _accDeath
                        + " runEnded=" + _accEnd + " hostAlive=" + (first != null && first.isAlive) + " hostHp=" + (first != null ? first.currentHp : -1);
                }
                Lc.QuitSent = true;
                Lc.Line("RESULT " + Lc.Test + " " + (pass ? "PASS" : "FAIL") + " p1-" + AcceptKind + " " + why + " errors=" + Lc.Errors);
                UnityEngine.Application.Quit();
            }
            catch (Exception e) { Lc.LineOnce("p1: accept tick threw: " + e.GetType().Name + ": " + e.Message); }
        }


        private static bool _camResult;
        private static bool _camStarted;

        /// <summary>Camera acceptance: both dwarves must be on screen at every distance up to the zoom limit's reach.</summary>
        private static void CamAccept()
        {
            if (_camResult) return;
            var gc = Find.One<GameController>();
            var first = gc != null ? gc.player : null;
            if (first == null || !first.isAlive || !Second.isAlive) return;
            if (!_camStarted) { _camStarted = true; CamTest.Active = true; Lc.Line("camtest: started, camera " + (Cam2.Enabled ? "on" : "OFF (twin)")); }
            CamTest.Tick(first, Second);
            if (!CamTest.Done) return;
            _camResult = true;
            // pass: at distances 6..24 m both dwarves are visible in at least 95% of the settled frames
            bool pass = true;
            var sb = new StringBuilder();
            foreach (var k in new[] { 6, 12, 18, 24 })
            {
                int[] a2;
                if (!Cam2.Buckets.TryGetValue(k, out a2) || a2[0] < 20) { pass = false; sb.Append(k).Append("m:no-data "); continue; }
                double pct = 100.0 * a2[1] / a2[0];
                if (pct < 95.0) pass = false;
            }
            Lc.QuitSent = true;
            Lc.Line("RESULT " + Lc.Test + " " + (pass ? "PASS" : "FAIL") + " p1-cam camera=" + (Cam2.Enabled ? "on" : "off") + " visibleBothByDistance: " + Cam2.Table() + " kMax=" + Cam2.KMax + " " + sb);
            UnityEngine.Application.Quit();
        }


        private static bool _hudResult;
        private static float _hudBuildBy = -1f;
        private static float _hudAt = -1f;
        private static int _hudStage;

        /// <summary>HUD acceptance: two panels, P1 left of the screen centre and P2 right of it, P2's hp bar follows the clone's hp,
        /// P2's weapon slot is filled, P1's slots are untouched.</summary>
        private static void HudAccept()
        {
            if (_hudResult) return;
            if (_hudBuildBy < 0f) _hudBuildBy = Time.realtimeSinceStartup + 25f;
            if (!Hud2.Built && Time.realtimeSinceStartup > _hudBuildBy)
            {
                _hudResult = true;
                Lc.QuitSent = true;
                Lc.Line("RESULT " + Lc.Test + " FAIL p1-hud hud=" + (Hud2.Enabled ? "on" : "off") + " built=False (the second panel was not created)");
                UnityEngine.Application.Quit();
                return;
            }
            if (!Hud2.Built || !Second.isAlive) return;
            float now = Time.realtimeSinceStartup;
            if (_hudAt < 0f) { _hudAt = now + 3f; return; }
            if (now < _hudAt) return;
            _hudAt = now + 2f;
            _hudStage++;
            if (_hudStage == 1)
            {
                long before = Second.currentHp;
                try { Second.TakeDamage(60, false, EDamageType.KINETIC, false, EDamageSource.PROJECTILE, null, false); } catch (Exception e) { Lc.Line("p1: hud damage threw: " + e.Message); }
                Lc.Line("hudtest: damaged the clone " + before + " -> " + Second.currentHp + " | " + Hud2.Describe());
                return;
            }
            if (_hudStage < 3) return;
            _hudResult = true;
            string d = Hud2.Describe();
            long max = 1; try { max = (long)Second._maxHp_k__BackingField.Value; } catch { }
            float want = Mathf.Clamp01((float)Second.currentHp / Mathf.Max(1L, max));
            bool pass = Hud2.HudChecks(want, out string detail);
            Lc.QuitSent = true;
            Lc.Line("RESULT " + Lc.Test + " " + (pass ? "PASS" : "FAIL") + " p1-hud hud=" + (Hud2.Enabled ? "on" : "off") + " " + detail + " | " + d);
            UnityEngine.Application.Quit();
        }


        /// <summary>Player.Awake writes `RunSettingsManager.player` (0x80) with the new player; after the clone's Awake the HUD and other
        /// readers of that field saw the clone as "the" player (the left panel showed the clone's hp). Give it back to the original.</summary>
        private static void RestoreRunSettingsPlayer(Player first)
        {
            if (Lc.Has("-lcnorsmfix")) return;   // test twin: leave the clone registered as the run settings player
            try
            {
                var rsm = first.runSettingsManager;
                if (rsm == null) { Lc.Line("p1: no runSettingsManager on the original"); return; }
                bool wasClone = rsm.player != null && Second != null && rsm.player.Pointer == Second.Pointer;
                SetRef(rsm, typeof(RunSettingsManager), "player", first.Pointer);
                Lc.Line("p1: RunSettingsManager.player was the clone=" + wasClone + ", now the original=" + (rsm.player != null && rsm.player.Pointer == first.Pointer));
            }
            catch (Exception e) { Lc.Line("p1: restoring RunSettingsManager.player threw: " + e.GetType().Name + ": " + e.Message); }
        }


        private static bool _leashResult;
        private static float _leashAt = -1f;
        private static int _leashFrames, _leashBoth;
        private static float _leashMaxSep;
        private static float _leashStart = -1f;
        private static bool _leashPlacedOnce;

        /// <summary>
        /// Leash acceptance (`-lckmax=1.0` so the screen holds only about 20 m): the clone starts 4 m from the host and WALKS away along
        /// the longest free line, for as long as the test lasts. With the leash it must stop at the screen edge and both dwarves must be on
        /// screen in at least 95% of the later frames; without it (twin) it walks out of the picture.
        /// </summary>
        private static void LeashAccept()
        {
            if (_leashResult) return;
            var gc = Find.One<GameController>();
            var first = gc != null ? gc.player : null;
            if (first == null || !first.isAlive || !Second.isAlive) return;
            float now = Time.realtimeSinceStartup;
            if (_leashStart < 0f) { _leashStart = now; }
            if (_leashDir == Vector3.zero) { _leashDir = FreeDirection(gc, first.transform.position, out _leashLen); }

            if (!_leashPlacedOnce && now >= _leashStart + 1f)
            {
                _leashPlacedOnce = true;
                Second.transform.position = first.transform.position + _leashDir * 4f;
                Lc.Line("leashtest: clone placed 4 m from the host, walking along the free line (" + _leashLen.ToString("F0") + " m)");
            }
            if (_leashPlacedOnce && now < _leashStart + 14f)
                Second.moveDir = new Vector2(_leashDir.x, _leashDir.z);          // FULL_CONTROL: Move() reads moveDir
            else if (_leashPlacedOnce)
                Second.moveDir = Vector2.zero;

            float sep = Vector3.Distance(first.transform.position, Second.transform.position);
            if (now >= _leashStart + 2f && now - _leashDbg > 1f)
            {
                _leashDbg = now;
                Lc.Line("leashdbg: sep=" + sep.ToString("F1") + " zoom=" + Cam2.K.ToString("F2") + " host vp=" + Cam2.Viewport(first.transform.position) + " clone vp=" + Cam2.Viewport(Second.transform.position) + " pulls=" + Cam2.LeashPulls);
            }
            if (now >= _leashStart + 9f)
            {
                _leashFrames++;
                if (Cam2.BothInsideMargin(first.transform.position, Second.transform.position, 0.04f)) _leashBoth++;
                _leashMaxSep = Mathf.Max(_leashMaxSep, sep);
            }
            if (now < _leashStart + 14f) return;
            _leashResult = true;
            double pct = _leashFrames > 0 ? 100.0 * _leashBoth / _leashFrames : 0.0;
            // testable only if the free line is clearly longer than the screen holds, otherwise nothing could leave the picture
            bool testable = _leashLen >= 28f;
            bool pass = testable && _leashFrames > 20 && pct >= 95.0 && (!Cam2.Leash || Cam2.LeashPulls > 0);   // pct = both inside the leash margin
            Lc.QuitSent = true;
            Lc.Line("RESULT " + Lc.Test + " " + (pass ? "PASS" : "FAIL") + " p1-leash leash=" + (Cam2.Leash ? "on" : "off") + " margin=" + Cam2.LeashMargin.ToString("F2") + " bothInsideMargin=" + pct.ToString("F0") + "% (" + _leashFrames
                + " frames) freeLine=" + _leashLen.ToString("F0") + "m testable=" + testable + " maxSeparation=" + _leashMaxSep.ToString("F1") + "m pulls=" + Cam2.LeashPulls
                + " zoom=" + Cam2.K.ToString("F2") + "/" + Cam2.KMax.ToString("F2"));
            UnityEngine.Application.Quit();
        }

        private static Vector3 _leashDir;
        private static float _leashLen, _leashPlaced, _leashDbg;

        /// <summary>Longest free (no rock block) straight line from a point, sampled every 2 m in 16 directions.</summary>
        private static Vector3 FreeDirection(GameController gc, Vector3 from, out float length)
        {
            Vector3 best = Vector3.right; length = 0f;
            var mg = gc.mapGenerator;
            for (int k = 0; k < 16; k++)
            {
                float ang = k * (float)Math.PI * 2f / 16f;
                var dir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                float free = 0f;
                for (float dd = 2f; dd <= 70f; dd += 2f)
                {
                    // walkable = on the game's NavMesh (the enemies walk on it); "no rock block" also holds outside the cave
                    bool ok = false;
                    try
                    {
                        UnityEngine.AI.NavMeshHit hit;
                        var pt = new Vector3(from.x + dir.x * dd, from.y, from.z + dir.z * dd);
                        ok = UnityEngine.AI.NavMesh.SamplePosition(pt, out hit, 0.6f, -1);
                    }
                    catch { ok = false; }
                    if (!ok) break;
                    free = dd;
                }
                if (free > length) { length = free; best = dir; }
            }
            Lc.Line("p1: longest free line from the host: " + length.ToString("F0") + " m, direction (" + best.x.ToString("F2") + ", " + best.z.ToString("F2") + ")");
            return best;
        }

        /// <summary>
        /// The upgrades bought in the main menu (max HP +x ...) are stat mods of the MetaManager that the game puts on the host. The second dwarf
        /// was built from its class' base stats only, so it missed them. They are added here unless the clone already carries them.
        /// Twin `-lcnometa`.
        /// </summary>
        internal static void ApplyMeta(Player p)
        {
            if (Lc.Has("-lcnometa") || p == null) return;
            try
            {
                var mods = p.metaManager != null ? p.metaManager.GetStatUpgradeMods() : null;
                if (mods == null || mods.Count == 0) { Lc.Line("meta: no meta stat upgrades to give the second dwarf"); return; }
                bool present = false;
                try
                {
                    var arr = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<EStatType>((EStatType[])Enum.GetValues(typeof(EStatType)));
                    var have = p.stats.GetAllPlayerMods(arr);
                    for (int i = 0; have != null && i < have.Count && !present; i++)
                        for (int k = 0; k < mods.Count; k++) if (have[i] != null && mods[k] != null && have[i].Pointer == mods[k].Pointer) { present = true; break; }
                }
                catch (Exception e) { Lc.LineOnce("meta: presence check threw " + e.GetType().Name + ": " + e.Message); }
                long before = (long)p._maxHp_k__BackingField.Value;
                if (!present) { p.stats.AddMods(mods.Cast<Il2CppSystem.Collections.Generic.IEnumerable<StatMod>>()); p.RefreshStats(); }
                long after = (long)p._maxHp_k__BackingField.Value;
                Lc.Line("meta: " + mods.Count + " meta stat mods, already on the second dwarf=" + present + ", max hp " + before + " -> " + after);
                if (after != before) p.currentHp = (long)p.maxHp.Value;
            }
            catch (Exception e) { Lc.Line("meta: apply threw " + e.GetType().Name + ": " + e.Message); }
        }

        /// <summary>
        /// The gear set equipped for the dwarf's subclass: the game adds its stat mods to the host only (hand test: P1 had armor, speed and luck from the gear, P2 had none).
        /// The mods of the subclass chosen for player 2 are added to the clone, which then starts at full health again. Twin `-lcnogear`.
        /// Gear quirks (special effects) are not handled here.
        /// </summary>
        internal static int GearModsApplied;
        internal static void ApplyGear(Player p, ClassArtifactData cad)
        {
            if (Lc.Has("-lcnogear") || p == null || cad == null) return;
            try
            {
                var gm = First() != null ? First().gearManager : null;
                if (gm == null) { Lc.Line("gear: no gear manager"); return; }
                var mods = gm.GetGearStatMods(cad);
                if (mods == null || mods.Count == 0) { Lc.Line("gear: nothing equipped for " + cad.name); return; }
                long before = (long)p._maxHp_k__BackingField.Value;
                p.stats.AddMods(mods.Cast<Il2CppSystem.Collections.Generic.IEnumerable<StatMod>>());
                p.RefreshStats();
                long after = (long)p._maxHp_k__BackingField.Value;
                GearModsApplied += mods.Count;
                Lc.Line("gear: " + mods.Count + " stat mods of the gear set of " + cad.name + " given to the second dwarf, max hp " + before + " -> " + after);
                p.currentHp = (long)p.maxHp.Value;
            }
            catch (Exception e) { Lc.Line("gear: apply threw " + e.GetType().Name + ": " + e.Message); }
        }

        private static void MetaProbe(Player p)
        {
            try
            {
                var rx = new System.Text.RegularExpressions.Regex("meta|stat|mod|gear|perm|upgrade", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                void Dump(Type t, string tag)
                {
                    var sb = new System.Text.StringBuilder("metaprobe " + tag + ":");
                    foreach (var m in t.GetMembers(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
                        if ((m is System.Reflection.MethodInfo || m is System.Reflection.PropertyInfo) && rx.IsMatch(m.Name) && !m.Name.StartsWith("NativeMethodInfoPtr") && !m.Name.StartsWith("get_") && !m.Name.StartsWith("set_")) sb.Append(' ').Append(m.Name);
                    Lc.Line(sb.ToString());
                }
                Dump(typeof(Player), "Player");
                if (p.metaManager != null) Dump(p.metaManager.GetType(), "MetaManager");
                if (p.stats != null) Dump(p.stats.GetType(), "stats:" + p.stats.GetType().Name);
                foreach (var mi in p.metaManager.GetType().GetMethods())
                    if (mi.Name == "GetStatUpgradeMods" || mi.Name == "GetStatUpgradeLevel") Lc.Line("metaprobe sig: " + mi.ReturnType.Name + " " + mi.Name + "(" + string.Join(", ", Array.ConvertAll(mi.GetParameters(), q => q.ParameterType.Name + " " + q.Name)) + ")");
                try
                {
                    var mods = p.metaManager.GetStatUpgradeMods();
                    Lc.Line("metaprobe: meta mods=" + (mods != null ? mods.Count : -1) + " host maxHp=" + p._maxHp_k__BackingField.Value + " clone maxHp=" + (Second != null ? Second._maxHp_k__BackingField.Value.ToString() : "-"));
                    for (int i = 0; mods != null && i < mods.Count && i < 6; i++)
                    {
                        var m = mods[i]; var sb = new System.Text.StringBuilder("metaprobe mod" + i + " " + m.GetType().Name + ":");
                        foreach (var pr in m.GetType().GetProperties()) { try { if (pr.PropertyType.IsPrimitive || pr.PropertyType.IsEnum) sb.Append(' ').Append(pr.Name).Append('=').Append(pr.GetValue(m)); } catch { } }
                        Lc.Line(sb.ToString());
                    }
                }
                catch (Exception e) { Lc.Line("metaprobe mods threw " + e.GetType().Name + ": " + e.Message); }
                foreach (var mi in p.stats.GetType().GetMethods())
                    if (mi.Name == "GetAllPlayerMods" || mi.Name == "AddMods") Lc.Line("metaprobe sig: " + mi.ReturnType.Name + " " + mi.Name + "(" + string.Join(", ", Array.ConvertAll(mi.GetParameters(), q => q.ParameterType.Name + " " + q.Name)) + ")");
            }
            catch (Exception e) { Lc.Line("metaprobe threw " + e.GetType().Name + ": " + e.Message); }
        }

        private static float _stage2At;
        private static int _stage2;

        /// <summary>After activation: report what the clone has, give it the original's hp, watch it.</summary>
        private static void Stage2()
        {
            float now = Time.realtimeSinceStartup;
            if (_stage2At <= 0f) { _stage2At = now + 0.3f; return; }
            if (now < _stage2At || _stage2 >= 8) return;
            _stage2At = now + 4f;
            _stage2++;
            try
            {
                var gc = Find.One<GameController>();
                var first = gc != null ? gc.player : null;
                int w1 = first != null && first.weapons != null ? first.weapons.Count : -1;
                int w2 = Second.weapons != null ? Second.weapons.Count : -1;
                Lc.Line("p1: stage " + _stage2 + " second hp=" + Second.currentHp + " alive=" + Second.isAlive
                    + " weapons first/second=" + w1 + "/" + w2 + " dmg first/second=" + WeaponDamage(first) + "/" + WeaponDamage(Second) + ShotCounts() + TargetInfo() + NearInfo(Second) + WeaponState(first, "first") + WeaponState(Second, "second")
                    + " moveControl=" + Second._moveControlType
                    + " pos=" + Second.transform.position);
                if (_stage2 == 1 && first != null)
                {
                    try
                    {
                        var fr = first.rb; var sr = Second.rb;
                        Lc.Line("p1: rb first=" + (fr != null) + " second=" + (sr != null) + " same=" + (fr != null && sr != null && fr.gameObject == sr.gameObject)
                            + " secondRbOwnsClone=" + (sr != null && sr.gameObject == Second.gameObject));
                        var fm = first.PlayerMovement; var sm = Second.PlayerMovement;
                        var fmr = fm != null ? fm.rb : null; var smr = sm != null ? sm.rb : null;
                        Lc.Line("p1: movement.rb first=" + (fmr != null) + " (is first's own: " + (fmr != null && fmr.gameObject == first.gameObject) + ")"
                            + " second=" + (smr != null) + " (is clone's own: " + (smr != null && smr.gameObject == Second.gameObject) + ", is original's: " + (smr != null && smr.gameObject == first.gameObject) + ")");
                        Lc.Line("p1: classArtifactData first=" + (first.classArtifactData != null) + " second=" + (Second.classArtifactData != null)
                            + " movement.cad first=" + (fm != null && fm.classArtifactData != null) + " second=" + (sm != null && sm.classArtifactData != null));
                    }
                    catch (Exception e) { Lc.Line("p1: rb diag threw: " + e.GetType().Name + ": " + e.Message); }
                }
                if (_stage2 == 3 && Lc.Has("-lcp1diag"))
                {
                    long before = Second.currentHp;
                    string r;
                    try { r = Second.TakeDamage(10, false, EDamageType.KINETIC, false, EDamageSource.PROJECTILE, first, false).ToString(); }
                    catch (Exception e) { r = "threw " + e.GetType().Name + ": " + e.Message; }
                    Lc.Line("p1: direct TakeDamage(10) on clone -> " + r + " hp " + before + " -> " + Second.currentHp
                        + " immuneUntil=" + Second.damageImmuneUntil + " now=" + Time.time + " invulnerable=" + Second.IsInvulnerable);
                }
                if (_stage2 == 2 && AcceptKind == "near" && first != null)
                {
                    try { first.GainXp(5000f, false); Lc.Line("p1: gave the host xp to force a level-up with the clone in the run"); }
                    catch (Exception e) { Lc.Line("p1: GainXp threw: " + e.GetType().Name + ": " + e.Message); }
                }
                if (_stage2 == 1) Diag(first, Second);
                if (_stage2 == 2 && Lc.Has("-lcmetaprobe") && first != null) MetaProbe(first);
                if (_stage2 == 2 && Lc.Has("-lcbonusprobe") && first != null) Bonus.Probe(first, Second);
                if (_stage2 == 2 && Lc.Has("-lcartsweep") && first != null) Bonus.Sweep(first, Second);
                if (_stage2 == 2 && Lc.Has("-lcarttrig") && first != null) Bonus.Trig(first, Second);
                if (_stage2 == 1 && (Mode == "clone-walk" || Mode == "clone-pad" || Mode == "clone-route" || Mode == "clone-cam" || Mode == "clone-hud" || Mode == "clone-leash" || Mode == "play" || Mode == "clone-weapon" || Mode == "clone-target")) { _walkStart = Second.transform.position; Lc.Line("p1: clone stays where it was placed; pos=" + Second.transform.position); }
                if (_stage2 >= 2 && first != null) Lc.Line("p1: update probe stage " + _stage2 + " clone lastPos=" + Second.lastPos + " pos=" + Second.transform.position + " walkDist=" + Second.walkDistanceMoved + " | first lastPos=" + first.lastPos + " pos=" + first.transform.position + " walkDist=" + first.walkDistanceMoved);
                if (_stage2 == 1 && (Mode == "clone-walk" || Mode == "clone-pad" || Mode == "clone-route" || Mode == "clone-cam" || Mode == "clone-hud" || Mode == "clone-leash" || Mode == "play" || Mode == "clone-weapon" || Mode == "clone-target"))
                {
                    string Snap(Player q) { try { return "hp=" + q.currentHp + " moveSpeed=" + (q._moveSpeed_k__BackingField != null ? q._moveSpeed_k__BackingField.Value.ToString() : "null") + " maxHp=" + (q._maxHp_k__BackingField != null ? q._maxHp_k__BackingField.Value.ToString() : "null") + " stats=" + (q.stats != null) + " weapons=" + (q.weapons != null ? q.weapons.Count : -1); } catch (Exception e) { return "err " + e.Message; } }
                    Lc.Line("p1: before Init (post-activation): " + Snap(Second));
                    try { Second.Init(); Lc.Line("p1: Init() ok"); }
                    catch (Exception e) { Lc.Line("p1: Init() threw: " + e.GetType().Name + ": " + e.Message); }
                    Lc.Line("p1: after Init: " + Snap(Second));
                    try
                    {
                        var bs = first._baseStats;
                        if (P2ClassChosen()) { bs = CoopMenu.Classes[CoopMenu.P2.ClassIndex].BaseStats; Second.CurrentClass = CoopMenu.Classes[CoopMenu.P2.ClassIndex]; Lc.Line("p1: P2 class " + Second.CurrentClass.Dwarf + " from the co-op menu"); }
                        Lc.Line("p1: first baseStats=" + (bs != null));
                        Second.SetupBaseStats(bs);
                        Lc.Line("p1: SetupBaseStats ok -> " + Snap(Second));
                    }
                    catch (Exception e) { Lc.Line("p1: SetupBaseStats threw: " + e.GetType().Name + ": " + e.Message); }
                    try { Second.RefreshStats(); Lc.Line("p1: RefreshStats ok -> " + Snap(Second)); }
                    catch (Exception e) { Lc.Line("p1: RefreshStats threw: " + e.GetType().Name + ": " + e.Message); }
                }
                if (_stage2 == 1 && first != null)
                {
                    Second.currentHp = P2ClassChosen() ? (long)Second.maxHp.Value : first.currentHp;
                    Lc.Line("p1: clone GOD_MODE was " + Second.GOD_MODE + ", pacifist=" + Second.PACIFIST_MODE + ", invulnerable=" + Second.IsInvulnerable); Second.GOD_MODE = false; Second.PACIFIST_MODE = false;
                    Lc.Line("p1: set second hp to " + Second.currentHp + " alive=" + Second.isAlive);
                    if ((Mode == "clone-walk" || Mode == "clone-pad" || Mode == "clone-route" || Mode == "clone-cam" || Mode == "clone-hud" || Mode == "clone-leash" || Mode == "play" || Mode == "clone-weapon" || Mode == "clone-target"))
                    {
                        try
                        {
                            CreateBehaviours(Second, first);
                            if (P2ClassChosen() && !Lc.Has("-lcnoskin")) ApplyP2Skin(Second);
                            if ((Mode == "clone-weapon" || Mode == "clone-target" || Mode == "clone-hud" || Mode == "play")) EquipWeapon(Second, first);
                            try { Second.Start(); Lc.Line("p1: Start() re-run ok -> hasMoveInput=" + Second.HasMoveInput + " hp=" + Second.currentHp); }
                            catch (Exception e) { Lc.Line("p1: Start() threw: " + e.GetType().Name + ": " + e.Message); }
                            ApplyMeta(Second);
                            ApplyGear(Second, CoopMenu.P2.Sub);
                            Second.SetMoveControlType(Player.EMoveControlType.FULL_CONTROL);
                            if (Second._moveControlType != Player.EMoveControlType.FULL_CONTROL)
                            {
                                Lc.Line("p1: SetMoveControlType left it at " + Second._moveControlType + ", writing the field");
                                Second._moveControlType = Player.EMoveControlType.FULL_CONTROL;
                            }
                            _walkFrom = Time.realtimeSinceStartup;
                            if (Mode == "clone-weapon" || Mode == "clone-cam" || Mode == "clone-hud" || Mode == "clone-leash" || Mode == "play") _walkFrom = -2f;   // weapon test: stay next to the host, where the enemies are
                            _walkStart = Second.transform.position;
                            Lc.Line("p1: control type now " + Second._moveControlType + ", walking right");
                        }
                        catch (Exception e) { Lc.Line("p1: SetMoveControlType threw: " + e.GetType().Name + ": " + e.Message); }
                    }
                }
            }
            catch (Exception e) { Lc.Line("p1: stage2 threw: " + e.GetType().Name + ": " + e.Message); }
        }

        private static bool _cloneMade, _playMode, _ready;
        private static float _cloneAt, _hideAt;
        internal static float CloneAt { get { return _cloneAt; } }
        internal static float RevealAt = -1f;
        internal static Vector3 CreatedPos, RevealPos;
        internal static bool CreatedHostFree;     // the clone was made because the host had control (not by the 9 s fallback)
        private static readonly System.Collections.Generic.List<Renderer> _hidden = new System.Collections.Generic.List<Renderer>();

        /// <summary>The second dwarf is complete (class model, animator, weapon, stats) and visible; its panel, tags and the enemies' attention wait for this.</summary>
        internal static bool Ready { get { return _ready && Second != null; } }

        private static bool HostFree()
        {
            try
            {
                var h = First();
                return h != null && h.isAlive && h._moveControlType == Player.EMoveControlType.FULL_CONTROL && Time.realtimeSinceStartup > Lc.InRunAt + 2f;
            }
            catch { return false; }
        }

        /// <summary>A spot next to the host that is not inside rock: the first free of right, left, up, down at 2 m.</summary>
        private static Vector3 FreeSide(Vector3 at)
        {
            var dirs = new[] { new Vector3(2f, 0f, 0f), new Vector3(-2f, 0f, 0f), new Vector3(0f, 0f, 2f), new Vector3(0f, 0f, -2f) };
            foreach (var d in dirs)
            {
                try { if (!Physics.Raycast(at + Vector3.up * 0.8f, d.normalized, 2.4f)) return d; } catch { return d; }
            }
            return dirs[0];
        }

        /// <summary>Everything of the clone that draws is switched off until it is complete (a copy of the host's model, then its own class model, would be seen).</summary>
        private static void HideAll()
        {
            try
            {
                if (Second == null || Lc.Has("-lcnohide")) return;
                foreach (var r in Second.GetComponentsInChildren<Renderer>(true))
                    if (r != null && r.enabled) { r.enabled = false; _hidden.Add(r); }
            }
            catch { }
        }

        private static void PrepTick()
        {
            float now = Time.realtimeSinceStartup;
            if (now < _hideAt) return;
            _hideAt = now + 0.1f;
            HideAll();
            bool skinWanted = P2ClassChosen() && !Lc.Has("-lcnoskin");
            bool skinDone = !skinWanted || (SkinReport.Contains("skinLoaded=True") && (AnimFixReport.Length > 0 || _animFixTries > 0 || now > _cloneAt + 4f));
            bool ready = _stage2 >= 1 && now > _cloneAt + 0.6f && skinDone;
            if (!ready && now < _cloneAt + 7f) return;
            foreach (var r in _hidden) { try { if (r != null) r.enabled = true; } catch { } }
            _hidden.Clear();
            _ready = true; RevealAt = now; try { RevealPos = Second.transform.position; } catch { }
            Lc.Line("p1: second dwarf revealed " + (now - _cloneAt).ToString("F1") + " s after it was made (skin " + SkinReport.Contains("skinLoaded=True") + ")");
        }

        internal static bool LoadingActive()
        {
            try
            {
                foreach (var f in Find.Forms<DRS.UI.UILoadingForm>())
                    if (f != null && f.gameObject.activeInHierarchy) return true;
            }
            catch { }
            return false;
        }

        /// <summary>The run is over (scene gone, the clone destroyed with it): everything that belonged to that run is let go, so the next run
        /// (retry, or START again from the menu) builds a second dwarf from scratch.</summary>
        private static void ResetRun()
        {
            _cloneMade = false; _done = false; _startAt = -1f; _stage2 = 0; _stage2At = 0f; _walkFrom = -1f;
            _skinLogAt = -1f; _animFixTries = 0; _animFixAt = 0f; SkinReport = ""; AnimFixReport = "";
            _seenSecond.Clear(); Clone = null; Second = null; _verifiedTarget = false; _ready = false; _hidden.Clear();
            Wallets.ResetRun(); LevelRounds.Reset(); Revive.Reset(); WalletHud.Reset(); Chooser.Release(); Haptics.Silence();
            Lc.Line("p1: run over, co-op state reset (next run starts clean)");
        }

        private static int _godTestStage; private static float _godTestAt = -1f;
        /// <summary>`-lcgodtest`: both dwarves are put to 10 hp; 1.5 s later the check says whether god mode (setting or flag) topped them up again.</summary>
        private static void GodTest()
        {
            if (!Lc.Has("-lcgodtest") || _godTestStage >= 2 || Second == null || !_ready || Lc.InRunAt < 0f) return;
            float now = Time.realtimeSinceStartup;
            if (_godTestAt < 0f) _godTestAt = now + 10f;
            if (now < _godTestAt) return;
            var h = First();
            if (h == null) return;
            if (_godTestStage == 0) { h.currentHp = 10; Second.currentHp = 10; _godTestStage = 1; _godTestAt = now + 1.5f; return; }
            bool ok = h.currentHp > 100 && Second.currentHp > 100;
            Lc.Line("god: CHECK " + (ok ? "OK   " : "FAIL ") + "both dwarves were put to 10 hp and are topped up again | host " + h.currentHp + ", clone " + Second.currentHp + " (god=" + Lc.God + ")");
            _godTestStage = 2;
        }

        internal static void Tick()
        {
            if (_cloneMade && Second == null) ResetRun();
            GodTest();
            if (Lc.God && Lc.InRunAt >= 0f)
            {
                // GOD_MODE alone does not stop hp loss in this build; the test host is topped up instead (test flag only).
                try
                {
                    var gc0 = Find.One<GameController>();
                    if (gc0 != null && gc0.player != null)
                    {
                        gc0.player.GOD_MODE = true;
                        double hmax = 145.0, cmax = 145.0;
                        try { hmax = Math.Max(1.0, gc0.player._maxHp_k__BackingField.Value); } catch { }
                        try { if (Second != null) cmax = Math.Max(1.0, Second._maxHp_k__BackingField.Value); } catch { }
                        if (Time.realtimeSinceStartup > _godAt && gc0.player.currentHp < hmax && gc0.player.currentHp > 0)
                        {
                            _godAt = Time.realtimeSinceStartup + 0.2f;
                            gc0.player.currentHp = (long)hmax;
                        }
                        if (Second != null && Second.isAlive && Time.realtimeSinceStartup > _godAt - 0.1f && Second.currentHp < cmax && Second.currentHp > 0) Second.currentHp = (long)cmax;
                    }
                }
                catch { }
            }
            if (Mode.Length == 0 && CoopStart.Started) { Mode = "play"; _playMode = true; }
            else if (_playMode && !CoopStart.Started && Second == null) { Mode = ""; _playMode = false; }     // back in the menu: a plain run is plain
            if (Second != null && Mode != "clone" && Mode != "clone-init") Stage2();
            if (Second != null) DriveWalk();
            if (Second != null && Mode == "clone-cam") CamAccept();
            if (Second != null && Mode == "clone-hud") HudAccept();
            if (Second != null && Mode == "clone-leash") LeashAccept();
            if (Second != null && !_ready) PrepTick();
            if (Second != null) SampleProjectiles();
            SkinTick();
            if (Second != null && SkinReport.Contains("skinLoaded=True")) FixAnimator();
            if (Second != null && _ready && (Mode == "clone-target" || Mode == "play")) Retarget();      // "play" is the shipped path: enemies go for the nearer dwarf
            if (Second != null && AcceptKind.Length > 0) AcceptTick();
            if (Second != null && Chooser.Who == 0 && Time.timeScale > 0.01f && RunOn() && (Mode == "clone-weapon" || Mode == "clone-target" || Mode == "clone-hud" || Mode == "play")) TickCloneWeapons();
            if (Second != null && Mode == "play") PodGuard();
            if (Mode == "first-walk") FirstWalk();
            if (Mode == "rva" && Lc.InRunAt >= 0f && !_rvaDone) { _rvaDone = true; DumpRva(); }
            if (Mode.Length == 0 || _done) return;
            if (Lc.InRunAt < 0f) return;
            // The loading screen of a retry is shown while the new run's player already exists; a clone made then stops the load for good.
            if (!Lc.Has("-lcnoloadwait") && LoadingActive()) { _startAt = -1f; return; }
            if (_startAt < 0f) _startAt = Time.realtimeSinceStartup + 9f;   // fallback: the run has had time to settle
            if (Time.realtimeSinceStartup < _startAt && !HostFree() && !Pod.EarlyOk()) return;     // as soon as the host rides the drop pod down (the second dwarf rides along), at the latest when it has control
            _done = true;

            try
            {
                if (Mode.StartsWith("clone") || Mode == "play") DoClone();
                else Lc.Line("p1: unknown mode '" + Mode + "'");
            }
            catch (Exception e)
            {
                Lc.Line("p1: experiment threw: " + e.GetType().Name + ": " + e.Message);
            }
        }

        private static void DoClone()
        {
            var gc = Find.One<GameController>();
            var first = gc != null ? gc.player : null;
            if (first == null) { Lc.Line("p1: no first player"); return; }
            Lc.Line("p1: first player '" + first.gameObject.name + "' at " + first.transform.position + " components=" + first.GetComponents<Component>().Length);

            var ctx = Find.One<SceneContext>();
            if (ctx == null) { Lc.Line("p1: no SceneContext (cannot inject)"); return; }
            DiContainer container = ctx.Container;
            Lc.Line("p1: container " + (container != null ? "ok" : "null"));
            if (container == null) return;

            // The holder is inactive, so anything instantiated under it stays inactive: no Awake yet.
            var holder = new GameObject("lc-holder");
            holder.SetActive(false);

            Clone = UnityEngine.Object.Instantiate(first.gameObject, holder.transform);
            Clone.name = "Player2";
            Lc.Line("p1: cloned, activeInHierarchy=" + Clone.activeInHierarchy);

            try
            {
                container.InjectGameObject(Clone);
                Lc.Line("p1: injected");
            }
            catch (Exception e) { Lc.Line("p1: InjectGameObject threw: " + e.GetType().Name + ": " + e.Message); }

            Clone.transform.SetParent(null, true);
            Clone.transform.position = first.transform.position + FreeSide(first.transform.position);
            _cloneAt = Time.realtimeSinceStartup; _ready = false; RevealAt = -1f; CreatedHostFree = HostFree(); CreatedPos = Clone.transform.position;
            Clone.SetActive(true);
            UnityEngine.Object.Destroy(holder);

            Second = Clone.GetComponent<Player>();
            _cloneMade = Second != null;
            HideAll();
            RestoreRunSettingsPlayer(first);
            VirtualPad.Isolate();
            InputRouting.Install();
            HudPatches.Install();
            Share.Install();
            Zones.Install();
            Haptics.Install();
            if (Second != null)
            {
                Lc.Line("p1: second hp=" + Second.currentHp + " alive=" + Second.isAlive + " (before Init)");
                if (Mode == "clone-init")
                {
                    try { Second.Init(); Lc.Line("p1: Init() ok"); }
                    catch (Exception e) { Lc.Line("p1: Init() threw: " + e.GetType().Name + ": " + e.Message); }
                    Lc.Line("p1: second hp=" + Second.currentHp + " alive=" + Second.isAlive + " (after Init)");
                }
            }
            Lc.Line("p1: activated, second player component=" + (Second != null)
                + " players now=" + UnityEngine.Object.FindObjectsOfType<Player>().Length);
        }

        /// <summary>One line describing every Player in the scene, for the state dump.</summary>
        internal static string Describe()
        {
            var sb = new StringBuilder();
            try
            {
                var all = UnityEngine.Object.FindObjectsOfType<Player>();
                sb.Append('[');
                for (int i = 0; i < all.Length; i++)
                {
                    var p = all[i];
                    if (i > 0) sb.Append(',');
                    var pos = p.transform.position;
                    sb.Append("{\"n\":\"").Append(p.gameObject.name).Append("\",\"x\":").Append(pos.x.ToString("F2", System.Globalization.CultureInfo.InvariantCulture))
                      .Append(",\"z\":").Append(pos.z.ToString("F2", System.Globalization.CultureInfo.InvariantCulture))
                      .Append(",\"hp\":").Append(p.currentHp).Append(",\"alive\":").Append(p.isAlive ? "true" : "false").Append('}');
                }
                sb.Append(']');
            }
            catch { return "[]"; }
            return sb.ToString();
        }
    }
}
