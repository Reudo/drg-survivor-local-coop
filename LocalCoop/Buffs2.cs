using System;
using System.Collections.Generic;
using System.Reflection;
using Assets.Scripts.SkillSystem;
using DRS.UI;
using HarmonyLib;
using UnityEngine;

namespace LocalCoop
{
    /// <summary>
    /// Buffs of the second dwarf. hand test: with two Weapon Specialists the buff showed twice on player 1's bar, and every shot of one dwarf also
    /// counted for the other one's "every 100th shot" counter.
    /// 1. Shots: the game raises one static event per volley (`GameEvents.TriggerPlayerShoot`, a count only, no shooter) and every
    ///    ClassArtifact_WeaponSpecialist adds it to its own counter. The weapon handlers that raise it are wrapped here so the shooter is known while
    ///    the event runs; a counter ignores volleys of the other dwarf. Twin `-lcnoshotowner`.
    /// 2. Icons: all buff icons are created in player 1's buff row. The icons whose buff belongs to the second dwarf are moved to the copy of
    ///    the row that sits at player 2's bar (Hud2.BuffBox2). Twin `-lcnobuffbox2`.
    /// </summary>
    internal static class Buffs2
    {
        internal static Player Shooter;
        internal static int Accepted1, Accepted2, Ignored, Moved;
        private static bool _hooked;

        // ---- who owns an object (a weapon handler, a coroutine of one, a buff) ------------------------------------------------------
        private static readonly Dictionary<Type, MemberInfo[]> _cache = new Dictionary<Type, MemberInfo[]>();

        private static object Get(MemberInfo m, object o)
        {
            var p = m as PropertyInfo; if (p != null) return p.GetValue(o, null);
            var f = m as FieldInfo; return f != null ? f.GetValue(o) : null;
        }

        private static MemberInfo[] OwnerMembers(Type t)
        {
            MemberInfo[] r;
            if (_cache.TryGetValue(t, out r)) return r;
            var l = new List<MemberInfo>();
            const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            for (var b = t; b != null && b != typeof(object); b = b.BaseType)
            {
                foreach (var p in b.GetProperties(F))
                    if (p.GetIndexParameters().Length == 0 && (p.PropertyType == typeof(Player) || typeof(WeaponHandler).IsAssignableFrom(p.PropertyType) || p.Name.EndsWith("4__this"))) l.Add(p);
                foreach (var f in b.GetFields(F))
                    if (f.FieldType == typeof(Player) || typeof(WeaponHandler).IsAssignableFrom(f.FieldType) || f.Name.EndsWith("4__this")) l.Add(f);
            }
            r = l.ToArray(); _cache[t] = r;
            return r;
        }

        /// <summary>The dwarf an object belongs to: its Player, or the one of its WeaponHandler, or (for a coroutine state class) of its `this`.</summary>
        internal static Player OwnerOf(object o, int depth = 0)
        {
            if (o == null || depth > 3) return null;
            try
            {
                var pl = o as Player; if (pl != null) return pl;
                var c = o as Component; Player viaParent = null;
                if (c != null) viaParent = c.GetComponentInParent<Player>();
                foreach (var m in OwnerMembers(o.GetType()))
                {
                    var v = Get(m, o);
                    if (v == null) continue;
                    var p2 = v as Player; if (p2 != null) return p2;
                    var q = OwnerOf(v, depth + 1); if (q != null) return q;
                }
                if (viaParent != null) return viaParent;
            }
            catch { }
            return null;
        }

        // ---- the wrapped shooters ----------------------------------------------------------------------------------------------
        private static void Enter(object __instance) { Shooter = OwnerOf(__instance); }
        private static void Leave() { Shooter = null; }

        internal static void Install(Harmony h)
        {
            ArtifactOwner.Install(h);
            if (_hooked || Lc.Has("-lcnoshotowner")) return;
            _hooked = true;
            int n = 0;
            try
            {
                var pre = new HarmonyMethod(typeof(Buffs2), "Enter"); var fin = new HarmonyMethod(typeof(Buffs2), "Leave");
                const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                Action<Type, string> hook = (t, name) =>
                {
                    if (t == null) return;
                    var m = t.GetMethod(name, F);
                    if (m == null) { Lc.Line("buffs2: no " + t.Name + "." + name); return; }
                    h.Patch(m, prefix: pre, finalizer: fin); n++;
                };
                hook(typeof(AutoCannonWeaponHandler), "ShootProjectile");
                hook(typeof(ProjectileWeaponHandler), "ShootProjectileInDirection");
                hook(typeof(AlternatingProjectileWeaponHandler), "ShootProjectile");
                hook(typeof(LinkProjectileWeaponHandler), "ActivateWeapon");
                hook(typeof(StubbySMGWeaponHandler), "ShootProjectileInDirection");
                foreach (var host in new[] { typeof(LeadBursterGrenade), typeof(Lok1WeaponHandler), typeof(ProjectileWeaponHandler), typeof(AlternatingProjectileWeaponHandler) })
                    foreach (var nt in host.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                        if (nt.Name.Contains("IEBurstFire") || nt.Name.Contains("IEShootAllBolts")) hook(nt, "MoveNext");
                h.CreateClassProcessor(typeof(SpecialistPatch)).Patch();
                h.CreateClassProcessor(typeof(MovePatch)).Patch();
                h.CreateClassProcessor(typeof(Revive.HitDiag)).Patch();
                Lc.Line("buffs2: " + n + " shooters wrapped, weapon specialist counter patched");
            }
            catch (Exception e) { Lc.Line("buffs2: install failed: " + e.GetType().Name + ": " + e.Message); }
        }

        /// <summary>
        /// Hand test: "Deployable Tripod" (fire rate while standing still) went up for both dwarves although only player 1 stood still. The artifact listens to the static
        /// `Player.OnMovingChanged(bool)`, which carries no player: every copy of the artifact took the value of whoever raised it. Each copy now asks its own dwarf. Twin `-lcnomovebuff`.
        /// </summary>
        internal static int MoveFixes;
        [HarmonyPatch(typeof(MoveStateBuffArtifact), "OnMovingChanged")]
        internal static class MovePatch
        {
            private static void Prefix(MoveStateBuffArtifact __instance, ref bool isMoving)
            {
                try
                {
                    if (Lc.Has("-lcnomovebuff") || Phase1.Second == null) return;
                    var me = __instance.player; if (me == null) return;
                    bool own = me.IsMoving;
                    if (own != isMoving) MoveFixes++;
                    isMoving = own;
                }
                catch { }
            }
        }

        private static float _moveAt = -1f;
        private static readonly System.Collections.Generic.Dictionary<IntPtr, bool> _moveSeen = new System.Collections.Generic.Dictionary<IntPtr, bool>();
        /// <summary>The event only fires when a dwarf's state flips; a copy that missed the flip of its own dwarf is told here.</summary>
        internal static void MoveTick()
        {
            if (Lc.Has("-lcnomovebuff") || Phase1.Second == null) return;
            float now = Time.realtimeSinceStartup; if (now < _moveAt) return; _moveAt = now + 0.25f;
            try
            {
                foreach (var a in UnityEngine.Object.FindObjectsOfType<MoveStateBuffArtifact>())
                {
                    if (a == null || a.player == null) continue;
                    bool own = a.player.IsMoving; bool seen;
                    if (_moveSeen.TryGetValue(a.Pointer, out seen) && seen == own) continue;
                    _moveSeen[a.Pointer] = own; a.OnMovingChanged(own);
                }
            }
            catch (Exception e) { Lc.LineOnce("buffs2: move tick threw " + e.GetType().Name + ": " + e.Message); }
        }

        [HarmonyPatch(typeof(ClassArtifact_WeaponSpecialist), "OnPlayerShoot")]
        internal static class SpecialistPatch
        {
            private static bool Prefix(ClassArtifact_WeaponSpecialist __instance)
            {
                try
                {
                    var me = __instance.player; var s = Shooter;
                    if (me == null || s == null) return true;                         // shooter unknown: as the game does
                    if (me.Pointer == s.Pointer) { if (Share.IsClone(me)) Accepted2++; else Accepted1++; return true; }
                    if (Lc.Has("-lcnoshotowner")) { if (Share.IsClone(me)) Accepted2++; else Accepted1++; return true; }
                    Ignored++;
                    return false;
                }
                catch { return true; }
            }
        }

        /// <summary>The buff behind a UI icon is an interface wrapper; the owner is read from the concrete class it really is.</summary>
        private static object Concrete(IStackableBuff b)
        {
            var ob = (object)b as Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase;
            if (ob == null) return null;
            object r;
            if ((r = ob.TryCast<ClassArtifact_WeaponSpecialist>()) != null) return r;
            if ((r = ob.TryCast<ClassArtifact_Foreman>()) != null) return r;
            if ((r = ob.TryCast<ClassArtifact_Juggernaut>()) != null) return r;
            if ((r = ob.TryCast<ClassArtifact_ScoutRecon>()) != null) return r;
            if ((r = ob.TryCast<GroundZonesOnDmgTakenArtifact>()) != null) return r;
            if ((r = ob.TryCast<LuckRerollArtifact>()) != null) return r;
            if ((r = ob.TryCast<MoveStateBuffArtifact>()) != null) return r;
            if ((r = ob.TryCast<TimedBuffOnDamageTakenArtifact>()) != null) return r;
            return null;
        }

        /// <summary>hand test: the clone is a copy of player 1's object and brought player 1's class artifact component along (a second one, bound to nobody
        /// that exists), so the clone had two and counted every volley twice. Components under the clone that do not belong to the clone go.</summary>
        private static void DropStale(Player c)
        {
            foreach (var a in c.GetComponentsInChildren<ClassArtifact_WeaponSpecialist>(true)) if (a.player == null || a.player.Pointer != c.Pointer) { UnityEngine.Object.Destroy(a); Stale++; Lc.Line("buffs2: a stale Weapon Specialist component under player 2 removed"); }
            foreach (var a in c.GetComponentsInChildren<ClassArtifact_Foreman>(true)) if (a.owner == null || a.owner.Pointer != c.Pointer) { UnityEngine.Object.Destroy(a); Stale++; Lc.Line("buffs2: a stale Foreman component under player 2 removed"); }
            foreach (var a in c.GetComponentsInChildren<ClassArtifact_Juggernaut>(true)) if (a.owner == null || a.owner.Pointer != c.Pointer) { UnityEngine.Object.Destroy(a); Stale++; Lc.Line("buffs2: a stale Juggernaut component under player 2 removed"); }
            foreach (var a in c.GetComponentsInChildren<ClassArtifact_ScoutRecon>(true)) if (a.owner == null || a.owner.Pointer != c.Pointer) { UnityEngine.Object.Destroy(a); Stale++; Lc.Line("buffs2: a stale Scout Recon component under player 2 removed"); }
        }
        internal static int Stale;

        // ---- icons ---------------------------------------------------------------------------------------------------------------------
        private static float _at;

        internal static void Tick()
        {
            MoveTick();
            if (Phase1.Second == null) return;
            float now = Time.realtimeSinceStartup;
            if (now < _at) return;
            _at = now + 0.5f;
            if (!Lc.Has("-lcnostaleart")) { try { DropStale(Phase1.Second); } catch (Exception e) { Lc.LineOnce("buffs2: stale cleanup threw " + e.Message); } }
            if (Lc.Has("-lcnobuffbox2") || Hud2.BuffBox2 == null) return;
            try
            {
                var front = Hud2.Front; if (front == null || front.buffContainer == null) return;
                var box1 = front.buffContainer;
                for (int i = box1.childCount - 1; i >= 0; i--)
                {
                    var ui = box1.GetChild(i).GetComponent<UIStackableBuff>();
                    if (ui == null) continue;
                    var o = OwnerOf(Concrete(ui.stackableBuff));
                    if (o != null && Share.IsClone(o)) { ui.transform.SetParent(Hud2.BuffBox2, false); Moved++; Lc.Line("buffs2: a buff icon moved to player 2's row"); }
                }
            }
            catch (Exception e) { Lc.LineOnce("buffs2: tick threw " + e.GetType().Name + ": " + e.Message); }
        }

        // ---- test (`-lcbuff2test`, two Gunners with the Weapon Specialist): only player 1 shoots, player 2's counter must stay out ------
        internal static bool TestDone;
        private static int _step, _ok, _fail; private static float _in = -1f, _tat = -1f;

        private static void Ck(string what, bool pass, string detail)
        {
            if (pass) _ok++; else _fail++;
            Lc.Line("ui: CHECK " + (pass ? "ok  " : "FAIL") + " " + what + " - " + detail);
        }

        // ---- test (`-lcmovetest`): "stand still" artifact on both dwarves; the static moving signal says "moving" while both stand still ----------
        private static float _mvIn = -1f; private static bool _mvDone;
        internal static void MoveTest()
        {
            if (_mvDone || !Lc.Has("-lcmovetest")) return;
            float now = Time.realtimeSinceStartup;
            if (Lc.InRunAt < 0f || Phase1.Second == null) return;
            if (_mvIn < 0f) _mvIn = now;
            if (now < _mvIn + 12f) return;
            _mvDone = true;
            try
            {
                MoveStateBuffArtifactData data = null;
                foreach (var d in Resources.FindObjectsOfTypeAll<MoveStateBuffArtifactData>()) if (d != null && !d.IsMoving) { data = d; break; }
                if (data == null) { UiTest.FinishWith(false, "no stand-still artifact data in the game"); return; }
                var h = Phase1.First(); var c = Phase1.Second;
                data.AddHandler(h.gameObject, h); data.AddHandler(c.gameObject, c);
                var arts = UnityEngine.Object.FindObjectsOfType<MoveStateBuffArtifact>();
                int ok = 0, n = 0;
                foreach (var a in arts)
                {
                    if (a == null || a.player == null) continue;
                    n++; bool own = a.player.IsMoving;
                    a.OnMovingChanged(true);                       // the static event of the game: "somebody moves"
                    bool want = own == data.IsMoving;               // active when the own state matches the artifact's condition
                    bool got = a.isBuffActive;
                    Lc.Line("buffs2: move artifact of " + (Share.IsClone(a.player) ? "P2" : "P1") + " own moving=" + own + " active=" + got + " expected=" + want);
                    if (got == want) ok++;
                }
                Ck("each copy of the stand-still artifact follows its own dwarf, not the signal", n == 2 && ok == 2, "copies=" + n + " right=" + ok + " fixes=" + MoveFixes);
                UiTest.FinishWith(_fail == 0, "movetest ok=" + _ok + " fail=" + _fail);
            }
            catch (Exception e) { UiTest.FinishWith(false, "movetest threw " + e.GetType().Name + ": " + e.Message); }
        }

        internal static void Test()
        {
            if (TestDone || !Lc.Has("-lcbuff2test")) return;
            float now = Time.realtimeSinceStartup;
            if (Lc.InRunAt < 0f || Phase1.Second == null) return;
            if (_in < 0f) _in = now;
            if (now < _in + 12f || now < _tat) return;
            _tat = now + 1f;
            try
            {
                var h = Phase1.First(); var c = Phase1.Second;
                switch (_step)
                {
                    case 0:
                        {
                            var all = UnityEngine.Object.FindObjectsOfType<ClassArtifact_WeaponSpecialist>();
                            Lc.Line("buffs2: weapon specialist components=" + all.Length);
                            foreach (var a in all) Lc.Line("buffs2:   component on " + a.gameObject.name + " (parent " + (a.transform.parent != null ? a.transform.parent.name : "-") + ") player field clone=" + (a.player != null && Share.IsClone(a.player)) + " player is host=" + (a.player != null && a.player.Pointer == h.Pointer) + " owner by search clone=" + (OwnerOf(a) != null && Share.IsClone(OwnerOf(a))));
                            var box = Hud2.Front != null ? Hud2.Front.buffContainer : null; if (box != null) foreach (var u in box.GetComponentsInChildren<UIStackableBuff>()) { var o = OwnerOf(Concrete(u.stackableBuff)); Lc.Line("buffs2:   icon owner found=" + (o != null) + " clone=" + (o != null && Share.IsClone(o)) + " type=" + (u.stackableBuff != null ? u.stackableBuff.GetType().Name : "null")); }
                            if (all.Length < 2) { Ck("both dwarves have the Weapon Specialist", false, "components=" + all.Length); TestDone = true; UiTest.FinishWith(false, "buffs2 setup"); return; }
                            c.weaponsActive = false; c.ToggleWeapons(false);          // only player 1 shoots from now on
                            Accepted1 = Accepted2 = Ignored = 0;
                            _tat = now + 10f; _step++;
                        }
                        break;
                    case 1:
                        {
                            Ck("player 1 shot and counted", Accepted1 > 0, "accepted P1=" + Accepted1 + " P2=" + Accepted2 + " ignored=" + Ignored);
                            Ck("player 1's shots do not count for player 2", Accepted2 == 0, "accepted P2=" + Accepted2 + " ignored=" + Ignored);
                            int n1 = Hud2.Front != null && Hud2.Front.buffContainer != null ? Hud2.Front.buffContainer.GetComponentsInChildren<UIStackableBuff>().Length : -1;
                            int n2 = Hud2.BuffBox2 != null ? Hud2.BuffBox2.GetComponentsInChildren<UIStackableBuff>().Length : -1;
                            Ck("each bar has its own buff icon", n1 == 1 && n2 == 1, "icons at player 1's bar=" + n1 + ", at player 2's bar=" + n2);
                            Shots.Take("buffs-p2");
                            _step++;
                        }
                        break;
                    default:
                        TestDone = true;
                        UiTest.FinishWith(_fail == 0, "buffs2 ok=" + _ok + " fail=" + _fail);
                        break;
                }
            }
            catch (Exception e) { TestDone = true; UiTest.FinishWith(false, "buffs2 threw " + e.GetType().Name + ": " + e.Message); }
        }
    }
}
