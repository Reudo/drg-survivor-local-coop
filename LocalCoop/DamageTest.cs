using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace LocalCoop
{
    /// <summary>
    /// `-lcui=dmg`: how much damage do the two dwarves deal in a normal run (both fighting, enemies chase the nearer one)? Every
    /// `Enemy.TakeDamage` is recorded with its source dwarf (host / clone / none), damage type and amount, and the histogram per source
    /// is logged. The same weapon on both dwarves must hurt about the same.
    /// </summary>
    internal static class DamageTest
    {
        private sealed class Acc { public int N; public long Sum; public readonly Dictionary<long, int> Amounts = new Dictionary<long, int>(); }
        private static readonly Dictionary<string, Acc> Table = new Dictionary<string, Acc>();
        private static bool _counting, _installed;
        private static int _ok, _fail;
        private static void Check(string name, bool ok, string detail) { if (ok) _ok++; else _fail++; Lc.Line("dmg: CHECK " + (ok ? "ok  " : "FAIL") + " " + name + " - " + detail); }
        private static int _s;
        private static float _at = -1f, _inRun = -1f, _t0;

        private static string Where()
        {
            try { var st = new System.Diagnostics.StackTrace(2, false); var f = st.GetFrames(); var sb = new StringBuilder(); for (int i = 0; f != null && i < f.Length && i < 5; i++) { var m = f[i].GetMethod(); sb.Append(m.DeclaringType != null ? m.DeclaringType.Name : "?").Append('.').Append(m.Name).Append(" < "); } return sb.ToString(); }
            catch { return "?"; }
        }

        private static Projectile _cur; private static bool _hitOwnerClone;
        private static string _ctx = "";
        private static int _ctxLogged, _ctxLoggedClone;

        [HarmonyPatch(typeof(Projectile), "ProjectileHit")]
        private static class PH
        {
            private static void Prefix(Projectile __instance)
            {
                _cur = __instance;
                _ctx = "";
                if (!_counting || __instance == null) return;
                if (__instance.owner != null) { if (Phase1.Second != null && __instance.owner.Pointer == Phase1.Second.Pointer) HitClone++; else HitHost++; }
                _hitOwnerClone = __instance.owner != null && Phase1.Second != null && __instance.owner.Pointer == Phase1.Second.Pointer;
                try
                {
                    string ov = "?"; try { var o = __instance.damageOverride; ov = o.HasValue ? o.Value.ToString() : "none"; } catch { }
                    double gt = -1; bool crit = false; try { if (__instance.weaponHandler != null) gt = __instance.weaponHandler.damage; } catch { }
                    _ctx = "pierce=" + __instance.pierce + " scalar=" + __instance.damageScalar + " override=" + ov + " handler.damage=" + gt + " isEnemy=" + __instance.isEnemy;
                }
                catch (Exception e) { _ctx = "ctx err " + e.GetType().Name; }
            }

            private static void Postfix() { _ctx = ""; }
        }

        // calls of Projectile.OnTriggerEnter / ProjectileHit per owner (does the clone's bullet touch an enemy at all, and does damage follow?)
        internal static int TriggerHost, TriggerClone, HitHost, HitClone, DmgInHitHost, DmgInHitClone;
        private static int _dmgBefore;

        [HarmonyPatch(typeof(Projectile), "OnTriggerEnter")]
        private static class PT
        {
            private static void Prefix(Projectile __instance)
            {
                if (!_counting || __instance == null || __instance.owner == null) return;
                if (Phase1.Second != null && __instance.owner.Pointer == Phase1.Second.Pointer) TriggerClone++; else TriggerHost++;
            }
        }

        [HarmonyPatch(typeof(Enemy), "TakeDamage")]
        private static class P
        {
            private static void Prefix(long __0, EDamageSource __4, Entity __5)
            {
                if (!_counting) return;
                if (_ctx.Length > 0) { if (_hitOwnerClone) DmgInHitClone++; else DmgInHitHost++; }
                {
                    bool isC = Phase1.Second != null && __5 != null && __5.Pointer == Phase1.Second.Pointer;
                    if (isC ? _ctxLoggedClone++ < 10 : _ctxLogged++ < 6) Lc.Line("dmg: hit by " + (isC ? "CLONE" : "other") + " amount=" + __0 + " | " + (_ctx.Length > 0 ? _ctx : "NOT inside Projectile.ProjectileHit") + " | stack " + Where());
                }
                string who = __5 == null ? "none" : (Phase1.Second != null && __5.Pointer == Phase1.Second.Pointer) ? "clone" : (Phase1.First() != null && __5.Pointer == Phase1.First().Pointer) ? "host" : "other";
                string key = who + "/" + __4;
                Acc a; if (!Table.TryGetValue(key, out a)) { a = new Acc(); Table[key] = a; }
                a.N++; a.Sum += __0;
                int c; a.Amounts.TryGetValue(__0, out c); a.Amounts[__0] = c + 1;
            }
        }

        internal static void Tick()
        {
            float now = Time.realtimeSinceStartup;
            if (now < _at) return;
            if (_s == 0 && (MenuEntry.EntryButton == null || !CoopMenu.Built)) return;
            _at = now + 0.5f;
            try
            {
                switch (_s)
                {
                    case 0: MenuEntry.EntryButton.OnClick.Invoke(); _s++; break;
                    case 1: UiTest.Click("P1class3"); UiTest.Click("P2class1"); UiTest.Click("sector0"); UiTest.Click("danger1"); UiTest.Click("biome3"); _s++; break;
                    case 2: UiTest.Click("start"); _s++; _at = now + 3f; break;
                    case 3:
                        if (Lc.InRunAt < 0f) { if (Lc.EverInRun) UiTest.FinishWith(false, "the run ended early"); else if (now > _at + 60f) UiTest.FinishWith(false, "the run did not start"); return; }
                        if (_inRun < 0f) _inRun = now;
                        if (now < _inRun + 14f || Phase1.Second == null || Phase1.Second.weapons == null || Phase1.Second.weapons.Count == 0) return;
                        if (!_installed) { _installed = true; var hh = new Harmony("localcoop.dmgtest"); hh.CreateClassProcessor(typeof(PH)).Patch(); hh.CreateClassProcessor(typeof(PT)).Patch(); hh.CreateClassProcessor(typeof(P)).Patch(); }
                        if (Lc.Num("-lcdmgx=") > 0f) { var cw = Phase1.Second.weapons[0]; cw.damage = cw.damage * Lc.Num("-lcdmgx="); Lc.Line("dmg: clone weapon damage x" + Lc.Num("-lcdmgx=") + " -> " + cw.damage); }
                        Phase1.ForceAll = true;      // every enemy chases the clone, so its weapon has targets
                        _counting = true; _t0 = now; _s++; break;
                    case 4:
                        ProbeProjectiles(now);
                        Overlaps(now);
                        if (now < _t0 + 45f) return;
                        _counting = false;
                        foreach (var kv in Table)
                        {
                            var top = new List<KeyValuePair<long, int>>(kv.Value.Amounts);
                            top.Sort((x, y) => y.Value.CompareTo(x.Value));
                            var sb = new StringBuilder();
                            for (int i = 0; i < top.Count && i < 8; i++) sb.Append(top[i].Key).Append('x').Append(top[i].Value).Append(' ');
                            Lc.Line("dmg: " + kv.Key + " events=" + kv.Value.N + " total=" + kv.Value.Sum + " avg=" + (kv.Value.N > 0 ? (kv.Value.Sum / kv.Value.N).ToString() : "0") + " top amounts: " + sb);
                        }
                        try
                        {
                            Enemy target = null;
                            foreach (var en in UnityEngine.Object.FindObjectsOfType<Enemy>()) if (en != null && en.isAlive) { target = en; break; }
                            foreach (var who in new[] { Phase1.First(), Phase1.Second })
                            {
                                var w = who.weapons[0];
                                bool crit; var d = w.GetTotalDamage(out crit, target, false, 1f);
                                Lc.Line("dmg: GetTotalDamage of " + (who.Pointer == Phase1.Second.Pointer ? "CLONE" : "HOST") + " " + w.data.name + " = " + d + " (crit=" + crit + ") handler.damage=" + w.damage + " owner is player=" + (w.owner != null) + " enemyOwner=" + (w.enemyOwner != null));
                            }
                        }
                        catch (Exception ex) { Lc.Line("dmg: GetTotalDamage probe threw " + ex.GetType().Name + ": " + ex.Message); }
                        Lc.Line("dmg: BulletHits calls=" + BulletHits.Calls + " failures=" + BulletHits.Failures);
                        Lc.Line("dmg: bullet samples host/clone=" + _bulletsHost + "/" + _bulletsClone + ", of them overlapping an enemy (< 0.9 m) host/clone=" + _ovHost + "/" + _ovClone);
                        Lc.Line("dmg: projectile OnTriggerEnter calls host/clone=" + TriggerHost + "/" + TriggerClone + "; ProjectileHit calls host/clone=" + HitHost + "/" + HitClone + "; damage events inside ProjectileHit host/clone=" + DmgInHitHost + "/" + DmgInHitClone);
                        var h = Phase1.First(); var c = Phase1.Second;
                        Lc.Line("dmg: host weapons: " + WeaponList(h) + " | clone weapons: " + WeaponList(c));
                        Lc.Line("dmg: HOST handler fields: " + Fields(h.weapons[0]));
                        Lc.Line("dmg: CLONE handler fields: " + Fields(c.weapons[0]));
                        {
                            double wd = c.weapons[0].damage;
                            int atFull = 0; Acc ca; if (Table.TryGetValue("clone/PROJECTILE", out ca)) foreach (var kv in ca.Amounts) if (kv.Key >= (long)wd) atFull += kv.Value;
                            Check("the clone's bullets reach the game's hit routine", HitClone > 0 && DmgInHitClone > 0, "ProjectileHit calls for the clone's bullets=" + HitClone + ", damage events inside=" + DmgInHitClone + " (host " + HitHost + "/" + DmgInHitHost + ")");
                            Check("the clone's hits deal its weapon's damage (" + wd + "), not just body contact", atFull >= 5, "hits of at least " + (long)wd + ": " + atFull);
                            UiTest.FinishWith(_fail == 0, "damage checks ok=" + _ok + " fail=" + _fail);
                        }
                        _s++; break;
                }
            }
            catch (Exception e) { Lc.Line("dmg: tick threw " + e.GetType().Name + ": " + e.Message); UiTest.FinishWith(false, "threw " + e.GetType().Name); }
        }

        private static float _pAt; private static int _pLogged, _pCloneLogged;

        private static void ProbeProjectiles(float now)
        {
            if (now < _pAt || (_pLogged >= 4 && _pCloneLogged >= 4)) return;
            _pAt = now + 1.7f;
            try
            {
                int hostSeen = 0, cloneSeen = 0;
                foreach (var pr in UnityEngine.Object.FindObjectsOfType<Projectile>())
                {
                    if (pr == null || pr.isInPool || pr.owner == null) continue;
                    bool isClone = Phase1.Second != null && pr.owner.Pointer == Phase1.Second.Pointer;
                    bool isHost = Phase1.First() != null && pr.owner.Pointer == Phase1.First().Pointer;
                    if (!(isClone ? (cloneSeen++ == 0 && _pCloneLogged < 4) : (isHost && hostSeen++ == 0 && _pLogged < 4))) continue;
                    var sb = new StringBuilder(isClone ? "clone" : "host");
                    sb.Append(" projectile '" + pr.gameObject.name + "':");
                    foreach (var pi in pr.GetType().GetProperties())
                    {
                        string n = pi.Name.ToLower();
                        if (!(n.Contains("damage") || n.Contains("crit") || n.Contains("pierce") || n.Contains("handler") || n.Contains("weapon") || n.Contains("data"))) continue;
                        if (pi.GetIndexParameters().Length > 0) continue;
                        try { var v = pi.GetValue(pr, null); sb.Append(' ').Append(pi.Name).Append('=').Append(v == null ? "null" : v.ToString()); } catch { }
                    }
                    try
                    {
                        var go = pr.gameObject;
                        sb.Append(" || layer=").Append(go.layer);
                        foreach (var cc in go.GetComponentsInChildren<Component>(true))
                        {
                            if (cc == null) continue;
                            string tn = cc.GetType().Name;
                            if (!(tn.Contains("Collider") || tn.Contains("Rigidbody"))) continue;
                            sb.Append(" ").Append(tn).Append("[");
                            foreach (var pn in new[] { "enabled", "isTrigger", "isKinematic", "detectCollisions", "useGravity" }) sb.Append(pn).Append("=").Append(Prop(cc, pn)).Append(' ');
                            sb.Append("layer=").Append(cc.gameObject.layer).Append("]");
                        }
                        { var names = new StringBuilder(); foreach (var cc in go.GetComponents<Component>()) if (cc != null) names.Append(cc.GetType().Name).Append(','); sb.Append(" components=[").Append(names).Append("]"); }
                        { var names = new StringBuilder(); foreach (var cc in go.GetComponentsInChildren<Component>(true)) if (cc != null && cc.gameObject != go) names.Append(cc.gameObject.name).Append(':').Append(cc.GetType().Name).Append(','); sb.Append(" childComponents=[").Append(names).Append("]"); }
                        sb.Append(" rb=").Append(pr.rb != null);
                        try { var m = pr.projectileMovementHandler; sb.Append(" movement[customMovement=").Append(m.usingCustomMovement).Append(" radius=").Append(m.Radius).Append(" vel=").Append(m.Velocity).Append("]"); } catch { }
                        sb.Append(" pos=").Append(pr.transform.position).Append(" ownerPos=").Append(pr.owner.transform.position).Append(" lifeLeft=").Append((pr.lifeEndAt - Time.time).ToString("F2"));
                    }
                    catch (Exception e) { sb.Append(" ctxerr ").Append(e.GetType().Name); }
                    Lc.Line("dmg: " + sb); if (isClone) _pCloneLogged++; else _pLogged++;
                }
            }
            catch (Exception e) { Lc.LineOnce("dmg: projectile probe threw " + e.GetType().Name + ": " + e.Message); }
        }

        // does a bullet ever overlap an enemy (distance below bullet radius + a body radius)? counted per owner, 10 samples per second
        private static float _oAt; private static int _ovHost, _ovClone, _bulletsHost, _bulletsClone;

        private static void Overlaps(float now)
        {
            if (now < _oAt) return;
            _oAt = now + 0.1f;
            try
            {
                var enemies = UnityEngine.Object.FindObjectsOfType<Enemy>();
                foreach (var pr in UnityEngine.Object.FindObjectsOfType<Projectile>())
                {
                    if (pr == null || pr.isInPool || pr.owner == null) continue;
                    bool isClone = Phase1.Second != null && pr.owner.Pointer == Phase1.Second.Pointer;
                    if (isClone) _bulletsClone++; else _bulletsHost++;
                    var pp = pr.transform.position;
                    foreach (var e in enemies)
                    {
                        if (e == null || !e.isAlive) continue;
                        var d = e.transform.position - pp; d.y = 0f;
                        if (d.magnitude < 0.9f) { if (isClone) _ovClone++; else _ovHost++; break; }
                    }
                }
            }
            catch { }
        }

        private static string Prop(object o, string name)
        {
            try { var pi = o.GetType().GetProperty(name); return pi != null ? (pi.GetValue(o, null) ?? "null").ToString() : "-"; } catch { return "?"; }
        }

        private static string Fields(WeaponHandler w)
        {
            var sb = new StringBuilder();
            try
            {
                sb.Append("targetMask=").Append(w.targetMask.value).Append(" layerMaskLibrary=").Append(w.layerMaskLibrary != null).Append(" diContainer=").Append(w.diContainer != null)
                  .Append(" physicsQuery=").Append(w.physicsQuerySystem != null).Append(" massFX=").Append(w.massFXSystem != null).Append(" vfx=").Append(w.vfxManager != null)
                  .Append(" firePoint=").Append(w.firePoint != null ? w.firePoint.position.ToString() : "null").Append(" owner=").Append(w.owner != null).Append(" paused=").Append(w.paused).Append(" canTargetRocks=").Append(w.canTargetRocks);
                var pw = w.TryCast<ProjectileWeaponHandler>();
                if (pw != null)
                {
                    sb.Append(" | ProjectileWeaponHandler: projectileProvider=").Append(pw.projectileProvider != null).Append(" projectileSettings=").Append(pw.projectileSettings != null).Append(" pData=").Append(pw.pData != null)
                      .Append(" clipSize=").Append(pw.clipSize).Append(" bulletsLeft=").Append(pw.bulletsLeft);
                    var ps = pw.projectileSettings;
                    if (ps != null) foreach (var pi in ps.GetType().GetProperties()) { if (pi.GetIndexParameters().Length > 0) continue; try { var v = pi.GetValue(ps, null); sb.Append(' ').Append(pi.Name).Append('=').Append(v == null ? "null" : v.ToString()); } catch { } }
                }
            }
            catch (Exception e) { sb.Append("err ").Append(e.GetType().Name); }
            return sb.ToString();
        }

        private static string WeaponList(Player p)
        {
            var sb = new StringBuilder();
            for (int i = 0; p.weapons != null && i < p.weapons.Count; i++) { var w = p.weapons[i]; if (w != null) sb.Append(w.data.name).Append("(damage ").Append(w.damage).Append(", tracked ").Append(w.trackedTotalDamage).Append(") "); }
            return sb.ToString();
        }
    }
}
