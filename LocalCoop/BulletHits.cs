using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace LocalCoop
{
    /// <summary>
    /// The second dwarf's bullets do not hit: the game's own hit detection never calls `Projectile.ProjectileHit` for them (measured:
    /// 277 calls for the host's bullets, 0 for the clone's, while the clone's bullets overlap enemies as often as the host's). The cause
    /// inside the game's native code is not known; this is the working fix: every frame, each live bullet of the clone that overlaps an enemy
    /// gets the game's OWN hit routine called with that enemy's hit box (`ProjectileHit(Collider)`), once per bullet and enemy, so damage,
    /// pierce, knockback, status effects and recall are the game's. Twin `-lcnobullethits`.
    /// </summary>
    internal static class BulletHits
    {
        internal static int Calls, Failures;
        private static Type _colliderType;
        private static MethodInfo _hit, _getComps;
        private static ConstructorInfo _colliderCtor;
        private static bool _init, _bad;
        private static readonly Dictionary<int, HashSet<int>> Done = new Dictionary<int, HashSet<int>>();
        private static readonly Dictionary<int, object> ColliderOf = new Dictionary<int, object>();
        private static float _cleanAt;

        internal static bool Enabled { get { return !Lc.Has("-lcnobullethits") && Phase1.Second != null; } }

        private static void Init()
        {
            _init = true;
            try
            {
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var t = a.GetType("UnityEngine.Collider", false);
                    if (t != null) { _colliderType = t; break; }
                }
                _hit = typeof(Projectile).GetMethod("ProjectileHit", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (_colliderType == null || _hit == null) { _bad = true; Lc.Line("bullets: init failed (collider type " + (_colliderType != null) + ", ProjectileHit " + (_hit != null) + ")"); return; }
                _colliderCtor = _colliderType.GetConstructor(new[] { typeof(IntPtr) });
                // Component.GetComponentsInChildren(Il2CppSystem.Type, bool)
                foreach (var m in typeof(Component).GetMethods())
                    if (m.Name == "GetComponentsInChildren" && m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType.Name == "Type" && m.GetParameters()[1].ParameterType == typeof(bool)) { _getComps = m; break; }
                Lc.Line("bullets: ready (collider ctor " + (_colliderCtor != null) + ", GetComponentsInChildren " + (_getComps != null) + ")");
                if (_colliderCtor == null || _getComps == null) _bad = true;
            }
            catch (Exception e) { _bad = true; Lc.Line("bullets: init threw " + e.GetType().Name + ": " + e.Message); }
        }

        /// <summary>The hit box collider of an enemy (the object the game's own bullets hit), as the interop's Collider object.</summary>
        private static object ColliderFor(Enemy e)
        {
            int id = e.GetInstanceID();
            object c;
            if (ColliderOf.TryGetValue(id, out c)) return c;
            c = null;
            try
            {
                var il2Type = Il2CppInterop.Runtime.Il2CppType.From(_colliderType);
                var arr = (System.Collections.IEnumerable)_getComps.Invoke(e, new object[] { il2Type, true });
                Component first = null, box = null;
                foreach (var o in arr)
                {
                    var comp = (Component)o;
                    if (comp == null) continue;
                    if (first == null) first = comp;
                    if (comp.gameObject.name == "HitBox") { box = comp; break; }
                }
                var pick = box ?? first;
                if (pick != null) c = _colliderCtor.Invoke(new object[] { pick.Pointer });
            }
            catch (Exception ex) { Lc.LineOnce("bullets: collider lookup threw " + ex.GetType().Name + ": " + ex.Message); }
            ColliderOf[id] = c;
            return c;
        }

        /// <summary>The bullet is the clone's own, or of a drone / turret / summon whose weapon handler belongs to the clone (owner chain).</summary>
        internal static bool BelongsToClone(Projectile pr, Player clone)
        {
            try
            {
                if (pr.owner != null && pr.owner.Pointer == clone.Pointer) return true;
                var h = pr.weaponHandler;
                for (int i = 0; h != null && i < 4; i++)
                {
                    if (h.owner != null && h.owner.Pointer == clone.Pointer) return true;
                    h = h.weaponHandlerOwner;
                }
            }
            catch { }
            return false;
        }

        internal static void Tick()
        {
            if (!Enabled) return;
            if (!_init) Init();
            if (_bad) return;
            try
            {
                var clone = Phase1.Second;
                Enemy[] enemies = null;
                foreach (var pr in Find.Many<Projectile>(0.05f))
                {
                    if (pr == null || pr.isInPool || !BelongsToClone(pr, clone)) continue;
                    if (enemies == null) enemies = Find.Many<Enemy>(0.15f);
                    int pid = pr.GetInstanceID() ^ (int)(pr.lifeEndAt * 100f);       // pooled bullets come back: a new flight is a new key
                    var pp = pr.transform.position;
                    float reach = pr.Radius + 0.6f;
                    HashSet<int> done;
                    if (!Done.TryGetValue(pid, out done)) { done = new HashSet<int>(); Done[pid] = done; }
                    foreach (var e in enemies)
                    {
                        if (e == null || !e.isAlive) continue;
                        var d = e.transform.position - pp; d.y = 0f;
                        if (d.magnitude > reach) continue;
                        int eid = e.GetInstanceID();
                        if (!done.Add(eid)) continue;
                        var col = ColliderFor(e);
                        if (col == null) { Failures++; continue; }
                        try { _hit.Invoke(pr, new[] { col }); Calls++; }
                        catch (Exception ex) { Failures++; Lc.LineOnce("bullets: ProjectileHit threw " + (ex.InnerException != null ? ex.InnerException.GetType().Name + ": " + ex.InnerException.Message : ex.Message)); }
                        if (pr.isInPool) break;        // the hit used up the bullet's pierce: it went back to the pool
                    }
                }
                float now = Time.realtimeSinceStartup;
                if (now > _cleanAt) { _cleanAt = now + 20f; if (Done.Count > 4000) Done.Clear(); if (ColliderOf.Count > 4000) ColliderOf.Clear(); }
            }
            catch (Exception e) { Lc.LineOnce("bullets: tick threw " + e.GetType().Name + ": " + e.Message); }
        }
    }
}
