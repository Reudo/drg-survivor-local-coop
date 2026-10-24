using System;
using System.Text;
using UnityEngine;

namespace LocalCoop
{
    /// <summary>
    /// Debug mode (or `-lcweaponlog`): every 20 s one line per dwarf with each weapon's level, state and damage dealt so far. Hand test: a weapon of player 1 did nothing for a whole run
    /// (0 damage in the end screen); this tells which weapon stood still and why (paused, jammed, no target).
    /// </summary>
    internal static class WeaponLog
    {
        private static float _at;

        private static string One(Player p)
        {
            var sb = new StringBuilder();
            try
            {
                for (int i = 0; p.weapons != null && i < p.weapons.Count; i++)
                {
                    var w = p.weapons[i]; if (w == null) { sb.Append("[null] "); continue; }
                    string n = ""; try { n = w.data != null ? w.data.name : "?"; } catch { }
                    sb.Append('[').Append(n).Append(" lv").Append(w.WeaponLevel).Append(" dmg").Append(w.damage.ToString("F0")).Append(" rng").Append(w.range.ToString("F0"))
                      .Append(w.paused ? " PAUSED" : "").Append(w.isJammed ? " JAMMED" : "").Append(w.target != null ? " target" : "").Append(" reload").Append(w.reloadTimeLeft.ToString("F1")).Append("] ");
                }
            }
            catch (Exception e) { sb.Append("err ").Append(e.GetType().Name); }
            return sb.ToString();
        }

        internal static void Tick()
        {
            if (!(Settings.Debug || Lc.Has("-lcweaponlog")) || Phase1.Second == null || Lc.InRunAt < 0f) return;
            float now = Time.realtimeSinceStartup;
            if (now < _at) return;
            _at = now + 20f;
            var h = Phase1.First();
            if (h == null) return;
            Lc.Line("weaponlog: P1 wa=" + h.weaponsActive + " " + One(h) + "| P2 wa=" + Phase1.Second.weaponsActive + " " + One(Phase1.Second));
        }
    }
}
