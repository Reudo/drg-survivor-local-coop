using System;
using System.Text;
using UnityEngine;

namespace LocalCoop
{
    /// <summary>
    /// `-lcstatlog` (also in debug mode, once per run): both dwarves' class artifact, the gear mods the game's GearManager returns for it, and the final stat values.
    /// Hand test question: does the equipped gear (one set per subclass) reach player 2? With the same subclass for both (`-lcsameclass`) the numbers must be equal.
    /// </summary>
    internal static class StatLog
    {
        private static float _at = -1f; private static bool _done;
        internal static string Last = "";

        private static string V(Stat s) { try { return s != null ? s.Value.ToString("F2") : "null"; } catch { return "?"; } }

        private static string One(Player p, ClassArtifactData cad)
        {
            var sb = new StringBuilder();
            try
            {
                sb.Append("cad=").Append(cad != null ? cad.name : "null");
                int mods = -1, gearsEquipped = -1;
                try { var gm = p.gearManager; sb.Append(" gm=").Append(gm != null); if (gm != null) { var l = gm.GetGearStatMods(cad); mods = l != null ? l.Count : -1; gearsEquipped = gm.GearEquippedList != null ? gm.GearEquippedList.Count : -1; } } catch (Exception e) { sb.Append(" gmErr=").Append(e.GetType().Name); }
                sb.Append(" gearMods=").Append(mods).Append(" equippedGear=").Append(gearsEquipped);
                sb.Append(" | maxHp=").Append(V(p._maxHp_k__BackingField)).Append(" move=").Append(V(p._moveSpeed_k__BackingField)).Append(" regen=").Append(V(p._lifeRegen_k__BackingField))
                  .Append(" dodge=").Append(V(p._dodge_k__BackingField)).Append(" armor=").Append(V(p._armor_k__BackingField)).Append(" mining=").Append(V(p._miningSpeed_k__BackingField))
                  .Append(" xp=").Append(V(p._xpGain_k__BackingField)).Append(" luck=").Append(V(p._luck_k__BackingField)).Append(" pickup=").Append(V(p._pickupRadius_k__BackingField))
                  .Append(" crit=").Append(V(p._critChance_k__BackingField)).Append(" critDmg=").Append(V(p._critDamage_k__BackingField));
            }
            catch (Exception e) { sb.Append(" err ").Append(e.GetType().Name).Append(": ").Append(e.Message); }
            return sb.ToString();
        }

        internal static void Tick()
        {
            if (_done || !(Lc.Has("-lcstatlog") || Settings.Debug) || Phase1.Second == null || Lc.InRunAt < 0f) return;
            float now = Time.realtimeSinceStartup;
            if (_at < 0f) _at = now + 12f;
            if (now < _at) return;
            _done = true;
            var h = Phase1.First();
            Last = "statlog: P1 " + One(h, CoopMenu.P1.Sub) + "\nstatlog: P2 " + One(Phase1.Second, CoopMenu.P2.Sub);
            Lc.Line(Last.Split('\n')[0]); Lc.Line(Last.Split('\n')[1]);

            try
            {
                var p1 = h; var p2 = Phase1.Second;
                if (Lc.Has("-lcsameclass") && CoopMenu.P1.Sub != null && CoopMenu.P1.Sub == CoopMenu.P2.Sub)
                {
                    bool eq = Math.Abs(p1._armor_k__BackingField.Value - p2._armor_k__BackingField.Value) < 0.01 && Math.Abs(p1._maxHp_k__BackingField.Value - p2._maxHp_k__BackingField.Value) < 0.01
                              && Math.Abs(p1._moveSpeed_k__BackingField.Value - p2._moveSpeed_k__BackingField.Value) < 0.01 && Math.Abs(p1._luck_k__BackingField.Value - p2._luck_k__BackingField.Value) < 0.01;
                    Lc.Line("statlog: CHECK " + (eq ? "OK   " : "FAIL ") + "same subclass: both dwarves have the same max hp, armor, speed and luck (gear included) | gear mods given to P2: " + Phase1.GearModsApplied + (Lc.Has("-lcnogear") ? " (twin: FAIL expected)" : ""));
                }
            }
            catch (Exception e) { Lc.Line("statlog: check threw " + e.GetType().Name); }            try
            {
                var gm = h.gearManager; var sb = new StringBuilder("statlog: subclasses with gear mods:");
                foreach (var c in Resources.FindObjectsOfTypeAll<ClassArtifactData>()) { if (c == null) continue; var l = gm.GetGearStatMods(c); int n = l != null ? l.Count : 0; if (n > 0) sb.Append(' ').Append(c.name).Append('=').Append(n); }
                Lc.Line(sb.ToString());
                Lc.Line("statlog: gear inventory " + (gm.AllGear != null ? gm.AllGear.Count : -1) + " items, found this run " + (gm.GearFoundDuringRun != null ? gm.GearFoundDuringRun.Count : -1));
                var sb2 = new StringBuilder("statlog: equipped sets (guid items mods):");
                foreach (var ge in gm.GearEquippedList) { if (ge == null) continue; int items = ge.GetAllEquippedGear().Count; var cm = ge.GetCombinedStatMods(); sb2.Append(" [").Append(ge.GUID).Append(' ').Append(items).Append(' ').Append(cm != null ? cm.Count : -1).Append(']'); }
                Lc.Line(sb2.ToString());
            }
            catch (Exception e) { Lc.Line("statlog: gear scan threw " + e.GetType().Name + ": " + e.Message); }
        }
    }
}
