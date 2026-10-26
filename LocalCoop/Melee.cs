using System;
using System.Collections.Generic;
using Assets.Scripts.SkillSystem;
using HarmonyLib;
using UnityEngine;

namespace LocalCoop
{
    /// <summary>
    /// Hand test: while player 2 was down, player 1 took hits again and again (and died in some runs), far away from any enemy. Cause, in the game's code
    /// (AIMeleeSkillHandler, RVA 0xA18740/0xA188B0/0xA18650): the melee trigger only remembers "a dwarf touches me" (any Player collider), and the hit then goes to
    /// the handler's own `player`, the dwarf the enemy is after. With one dwarf down every enemy is sent after the other one (Phase1.Retarget), so enemies walking over
    /// the body hit the dwarf that is up, wherever it stands. The same can happen with both up: an enemy touching player 2 while it goes for player 1.
    /// The guard: a melee hit only lands on the dwarf the enemy really touches. Target out of reach: the other dwarf takes it if it is up and in reach, else the hit is
    /// dropped (the downed dwarf takes nothing). Reach = the enemy's own trigger size plus a margin, so big enemies and bosses hit as before. Twin `-lcnomeleeguard`.
    /// </summary>
    internal static class Melee
    {
        internal static int Remote, Blocked, Redirected, Applied, Wide, Near, Cleared;

        private static string ColliderInfo(AIMeleeSkillHandler m)
        {
            var sb = new System.Text.StringBuilder();
            try { foreach (var c in m.gameObject.GetComponents<Collider>()) if (c != null) sb.Append(c.GetType().Name).Append(" ext=").Append(c.bounds.extents.ToString()).Append(" off=").Append(Flat(c.bounds.center - m.transform.position).ToString("F1")).Append("; "); } catch { }
            return sb.ToString();
        }
        private static float _logAt;

        private static float Flat(Vector3 d) { return Mathf.Sqrt(d.x * d.x + d.z * d.z); }

        /// <summary>
        /// Where the enemy's melee trigger is and how far it reaches: OnTriggerEnter sits on the handler, so its trigger is a collider of the handler's own object
        /// (the interop has no isTrigger getter). The collider's bounds are what physics uses; the object's transform can be elsewhere for a moment (measured: 24 m
        /// apart right after an enemy was moved). Reach = its largest horizontal extent plus the dwarf's width and a margin; no collider: the transform and 3.5 m.
        /// </summary>
        private static Vector3 Trigger(AIMeleeSkillHandler m, out float reach)
        {
            reach = 3.5f; var pos = m.transform.position;
            try
            {
                foreach (var c in m.gameObject.GetComponents<Collider>())
                {
                    if (c == null || !c.enabled) continue;
                    var b = c.bounds; pos = b.center; reach = Mathf.Max(b.extents.x, b.extents.z) + 1.5f;
                    break;
                }
            }
            catch { }
            return pos;
        }

        [HarmonyPatch(typeof(AIMeleeSkillHandler), "ActivateWeapon")]
        internal static class Guard
        {
            private static bool Prefix(AIMeleeSkillHandler __instance)
            {
                try
                {
                    var h = Phase1.First(); var c = Phase1.Second;
                    if (h == null || c == null || __instance == null) return true;
                    var target = __instance.player;
                    if (target == null) return true;
                    float reach;
                    var pos = Trigger(__instance, out reach);
                    var en = __instance.GetComponentInParent<Enemy>();
                    Vector3 epos = en != null ? en.transform.position : pos;
                    float dt = Flat(pos - target.transform.position), dtEnemy = Flat(epos - target.transform.position);                    if (dt <= reach)                                     // it touches the dwarf it is after: as in the game
                    {
                        Near++;
                        if (dt > 4f) { Wide++; Log("melee: hit in reach but " + dt.ToString("F1") + " m away: " + __instance.gameObject.name + " reach " + reach.ToString("F1") + " colliders " + ColliderInfo(__instance)); }
                        return true;
                    }
                    Remote++;
                    var other = target.Pointer == h.Pointer ? c : h;
                    float dOther = Flat(pos - other.transform.position);
                    string info = "target " + (Share.IsClone(target) ? "P2" : "P1") + " " + dt.ToString("F1") + " m away (enemy object " + dtEnemy.ToString("F1") + " m, " + (en != null ? en.name : "-") + "), other dwarf " + dOther.ToString("F1") + " m (hp " + other.currentHp + "), reach " + reach.ToString("F1") + " m";
                    if (Lc.Has("-lcnomeleeguard")) { Applied++; Log("melee: remote hit let through (twin): " + info); return true; }
                    if (other.currentHp > 0 && dOther <= reach)
                    {
                        Phase1.SetRef(__instance, typeof(AIMeleeSkillHandler), "player", other.Pointer);
                        Redirected++; Log("melee: hit goes to the dwarf it touches: " + info);
                        return true;
                    }
                    Blocked++; Log("melee: remote hit dropped: " + info);
                    // the "touches a dwarf" flag is stale (test: enemies kept it while walking 20-30 m from both dwarves; no trigger exit comes when a body stops counting);
                    // cleared, the enemy walks on and sets it again when it really reaches a dwarf. Twin of this part `-lcnomeleeclear`.
                    if (!Lc.Has("-lcnomeleeclear")) { Phase1.SetBool(__instance, typeof(AIMeleeSkillHandler), "playerInDamageRange", false); Cleared++; }
                    return false;
                }
                catch (Exception e) { Lc.LineOnce("melee: guard threw " + e.GetType().Name + ": " + e.Message); return true; }
            }
        }

        private static void Log(string s)
        {
            float now = Time.realtimeSinceStartup;
            if (now < _logAt) return;
            _logAt = now + 0.5f;
            Lc.Line(s + " (remote " + Remote + ", dropped " + Blocked + ", redirected " + Redirected + ", let through " + Applied + ")");
        }

        // ---- test `-lcmeleetest` (with -lcui=start): player 2 is down, player 1 stands 14 m away, enemies are put onto the body again and again ----------------
        internal static bool TestDone;
        private static int _step; private static float _at = -1f, _until;
        private static long _hp0; private static int _moved, _hits0;
        private static bool _counters, _ok1; private static int _near0, _far0, _nearHits0;
        internal static int Shots, Shots0, Booms, Booms0;

        // test only: what else of the enemies can hurt player 1 at a distance (ranged shots, explosions)
        [HarmonyPatch(typeof(AIProjectileHandler), "ActivateWeapon")]
        internal static class ShotCount { private static void Prefix() { Shots++; } }
        [HarmonyPatch(typeof(ExplosionWeaponHandler), "ActivateWeapon")]
        internal static class BoomCount { private static void Prefix() { Booms++; } }

        // test only: every hit on player 1, split by whether any enemy stood within 3 m of it at that moment (melee hits carry no source)
        internal static int FarHits, NearHits;
        [HarmonyPatch(typeof(Player), "TakeDamage")]
        internal static class HitSplit
        {
            private static void Prefix(Player __instance, long __0)
            {
                try
                {
                    if (__instance == null || Share.IsClone(__instance) || __0 <= 0) return;
                    var p = __instance.transform.position; float best = 1e9f;
                    foreach (var e in UnityEngine.Object.FindObjectsOfType<Enemy>()) { if (e == null || !e.isAlive) continue; float d = Flat(e.transform.position - p); if (d < best) best = d; }
                    if (best > 3f) FarHits++; else NearHits++;
                }
                catch { }
            }
        }

        internal static void Test()
        {
            if (TestDone || !Lc.Has("-lcmeleetest")) return;
            var h = Phase1.First(); var c = Phase1.Second;
            if (h == null || c == null || Lc.InRunAt < 0f) return;
            float now = Time.realtimeSinceStartup;
            if (_at < 0f) { _at = now + 10f; return; }
            if (now < _at) return;
            try
            {
                switch (_step)
                {
                    case 0:
                        h.transform.position = c.transform.position + new Vector3(14f, 0f, 0f);
                        try { c.GOD_MODE = false; c.damageImmuneUntil = 0f; } catch { }
                        c.TakeDamage(99999, false, EDamageType.KINETIC, false, EDamageSource.PROJECTILE, h, false);
                        _step = 1; _at = now + 2f; break;
                    case 1:
                        if (Revive.Downed == null || Revive.Downed.Pointer != c.Pointer) { Finish(false, "player 2 did not go down (downed=" + (Revive.Downed != null) + ")"); return; }
                        try { h.GOD_MODE = false; h.damageImmuneUntil = 0f; } catch { }
                        _hp0 = h.currentHp; Remote = Blocked = Redirected = Applied = 0; _until = now + 14f; _hits0 = Revive.HitsWhileDown;
                        if (!_counters) { _counters = true; var hh = new Harmony("localcoop.meleetest"); hh.CreateClassProcessor(typeof(ShotCount)).Patch(); hh.CreateClassProcessor(typeof(BoomCount)).Patch(); hh.CreateClassProcessor(typeof(HitSplit)).Patch(); }
                        _far0 = FarHits; _nearHits0 = NearHits;
                        Shots0 = Shots; Booms0 = Booms;
                        Lc.Line("melee: test window starts, P1 hp " + _hp0);
                        _step = 2; _at = now; break;
                    case 2:
                        // keep player 1 far from the body and put the nearest enemies onto it; each lands on the body's trigger, goes after player 1 (the one that is up)
                        var body = c.transform.position;
                        h.transform.position = body + new Vector3(14f, 0f, 0f);
                        if (h.currentHp < 40) h.currentHp = 40;              // the twin must not end the run by killing player 1 before the window is over
                        int n = 0;
                        foreach (var e in UnityEngine.Object.FindObjectsOfType<Enemy>())
                        {
                            if (e == null || !e.isAlive || !e.gameObject.activeInHierarchy) continue;
                            bool atHost = Flat(e.transform.position - h.transform.position) < 8f;       // enemies that reached player 1 go back too: player 1 must stay untouched
                            if (!atHost && n >= 6) continue;
                            Put(e, body + new Vector3(UnityEngine.Random.Range(-0.6f, 0.6f), 0f, UnityEngine.Random.Range(-0.6f, 0.6f))); n++;
                        }
                        _moved += n;
                        if (now >= _until)
                        {
                            Lc.Line("melee: window over: enemies put on the body " + _moved + ", remote hits " + Remote + " (dropped " + Blocked + ", redirected " + Redirected + ", let through " + Applied + "), P1 hp " + _hp0 + " -> " + h.currentHp + " (refilled to 40 when lower)"
                                + ", hits on P1 " + (Revive.HitsWhileDown - _hits0) + ", melee hits in reach but over 4 m " + Wide + ", enemy shots fired " + (Shots - Shots0) + ", enemy explosions " + (Booms - Booms0));
                            bool ok = Remote > 0 && Applied == 0 && Blocked + Redirected == Remote;
                            Lc.Line("melee: CHECK " + (ok ? "OK  " : "FAIL") + " no melee hit lands on a dwarf the enemy does not touch | remote=" + Remote + " letThrough=" + Applied);
                            int hits = Revive.HitsWhileDown - _hits0, far = FarHits - _far0;
                            Lc.Line("melee: CHECK " + (far == 0 ? "OK  " : "FAIL") + " player 1 takes no hit while no enemy stands within 3 m of it (player 2 lies down) | hits with no enemy near=" + far + " with one near=" + (NearHits - _nearHits0) + " all hits=" + hits + " hp " + _hp0 + " -> " + h.currentHp);
                            _ok1 = ok && far == 0;
                            _hits0 = Revive.HitsWhileDown; _near0 = Near; _far0 = FarHits; _nearHits0 = NearHits; _until = now + 15f; _step = 3; _at = now;
                            try { h.ToggleWeapons(false); h.weaponsActive = false; } catch { }          // flaky run: player 1 shot the enemies before they reached it (0 hits in 10 s)
                            break;
                        }
                        _at = now + 0.5f; break;
                    case 3:
                        // the other way round: enemies that walk up to player 1 must still hit it (the guard only drops hits on a dwarf out of reach). They walk there
                        // by themselves: enemies moved right onto a dwarf did not set off their trigger (seen: QronarYoungling inRange=False at 0.3 m, with and without the guard).
                        if (h.currentHp < 40) h.currentHp = 40;
                        if (now < _until) { _at = now + 0.5f; break; }
                        {
                            int hits = Revive.HitsWhileDown - _hits0;
                            int near = Near - _near0;
                            Lc.Line("melee: CHECK " + (hits > 0 && near > 0 ? "OK  " : "FAIL") + " enemies that walk up to player 1 still hit it | hits=" + hits + " melee hits in reach=" + near + " hits with no enemy near=" + (FarHits - _far0) + " stale flags cleared=" + Cleared);
                            bool ok = _ok1 && hits > 0 && near > 0;
                            Finish(ok, "melee test: remote hits " + Remote + ", let through " + Applied + ", hits from enemies at player 1 " + hits + (Lc.Has("-lcnomeleeguard") ? " (twin: FAIL expected)" : ""));
                        }
                        return;
                }
            }
            catch (Exception e) { Finish(false, "melee test threw " + e.GetType().Name + ": " + e.Message); }
        }

        /// <summary>Moves an enemy so physics agrees (first test: only the transform moved, the body stayed where it was and kept hitting player 1 for real).</summary>
        private static void Put(Enemy e, Vector3 to)
        {
            e.transform.position = to;
            try { var rb = e.GetComponent<Rigidbody>(); if (rb != null) { rb.position = to; rb.linearVelocity = Vector3.zero; } } catch { }
        }

        private static void Finish(bool ok, string info) { TestDone = true; UiTest.FinishWith(ok, info); }
    }
}
