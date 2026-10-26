using System;
using Assets.Scripts.SkillSystem;
using UnityEngine;

namespace LocalCoop
{
    /// <summary>
    /// `-lcturrettest` (with -lcui=start -lcgod): hand test, end screen of a 12:36 run: player 1's LMG turret (level 20) had 57,983 damage at 352/s (= 165 s active), player 2's
    /// (level 4) 834,466 at 1,659/s (= 503 s active). Both dwarves get the LMG turret, stand 30 m apart, and for 40 s each turret's tracked damage, active time and turrets on the
    /// field are measured. Expected: both turrets are active about as long as the window and both deal damage.
    /// </summary>
    internal static class TurretTest
    {
        internal static bool TestDone;
        private static int _step; private static float _at = -1f, _t0;
        private static WeaponHandler _w1, _w2;
        private static long _d1, _d2; private static float _a1, _a2;

        private static string Info(WeaponHandler w)
        {
            try
            {
                var s = w.TryCast<SpawnWeaponHandler>();
                return w.data.name + " level=" + w.weaponLevel + " tracked=" + w.trackedTotalDamage + " activeTime=" + w.activeTime.ToString("F1") + (s != null ? " charges=" + s.CurrentCharges + "/" + s.MaxCharges + " onField=" + (s.activeSpawns != null ? s.activeSpawns.Count : -1) : "");
            }
            catch (Exception e) { return "? " + e.GetType().Name; }
        }

        internal static void Tick()
        {
            if (TestDone || !Lc.Has("-lcturrettest")) return;
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
                        WeaponSkillData lmg = null;
                        foreach (var w in Resources.FindObjectsOfTypeAll<WeaponSkillData>()) if (w != null && w.name == "SpawnWeaponSkill_Turret_LMG") { lmg = w; break; }
                        if (lmg == null) { Finish(false, "no LMG turret data"); return; }
                        _w1 = h.EquipNewWeapon(lmg, false); _w2 = c.EquipNewWeapon(lmg, false);
                        c.transform.position = h.transform.position + new Vector3(30f, 0f, 0f);
                        Lc.Line("turret: equipped P1 " + (_w1 != null ? Info(_w1) : "null") + " | P2 " + (_w2 != null ? Info(_w2) : "null"));
                        _step = 1; _at = now + 5f; break;
                    case 1:
                        if (_w1 == null || _w2 == null) { Finish(false, "turret not equipped on both"); return; }
                        _d1 = _w1.trackedTotalDamage; _d2 = _w2.trackedTotalDamage; _a1 = _w1.activeTime; _a2 = _w2.activeTime; _t0 = now;
                        _step = 2; _at = now + 10f; break;
                    case 2:
                        Lc.Line("turret: " + (now - _t0).ToString("F0") + " s: P1 " + Info(_w1) + " | P2 " + Info(_w2));
                        if (now < _t0 + 40f) { _at = now + 10f; break; }
                        float win = now - _t0, a1 = _w1.activeTime - _a1, a2 = _w2.activeTime - _a2;
                        long d1 = _w1.trackedTotalDamage - _d1, d2 = _w2.trackedTotalDamage - _d2;
                        bool act = a1 > win * 0.7f && a2 > win * 0.7f && a1 < win * 1.3f && a2 < win * 1.3f;
                        bool dmg = d1 > 0 && d2 > 0;
                        Lc.Line("turret: CHECK " + (act ? "ok  " : "FAIL") + " both turrets are active for about the window | window " + win.ToString("F1") + " s, P1 " + a1.ToString("F1") + " s, P2 " + a2.ToString("F1") + " s");
                        Lc.Line("turret: CHECK " + (dmg ? "ok  " : "FAIL") + " both turrets deal damage | P1 " + d1 + " P2 " + d2);
                        Shots.Take("turrets");
                        Finish(act && dmg, "turrets: active P1 " + a1.ToString("F0") + " s / P2 " + a2.ToString("F0") + " s of " + win.ToString("F0") + " s, damage P1 " + d1 + " / P2 " + d2);
                        return;
                }
            }
            catch (Exception e) { Finish(false, "turret test threw " + e.GetType().Name + ": " + e.Message); }
        }

        private static void Finish(bool ok, string info) { TestDone = true; UiTest.FinishWith(ok, info); }
    }
}
