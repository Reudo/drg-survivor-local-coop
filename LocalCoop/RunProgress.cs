using System;
using Assets.Scripts.RankSystem;
using HarmonyLib;
using UnityEngine;

namespace LocalCoop
{
    /// <summary>
    /// Class experience for both dwarves. hand test: only the host's class got experience (and so unlocks) after a run. The game awards it once, at the end
    /// of the run, in `EconomyManager.OnRunEnd`: `PlayerRankManager.GainRunEndRankXp(xp)` for `RunSettingsManager.selectedClass`, counted from the rank snapshot
    /// `classRankAtStart`. After the game has done that for the host, the same award is made once more for the class of player 2: the selected class is
    /// switched to player 2's for that call and the snapshot is a copy of that class's rank as it is at that moment (so two dwarves of the same class add up).
    /// Only experience and what follows from it (class levels, unlocks), not a second run. Twin `-lcnoclassprogress`.
    /// </summary>
    internal static class RunProgress
    {
        internal static int P2Awards, LastXp;
        internal static string Last = "";
        private static bool _coop;

        internal static void Tick()
        {
            if (Phase1.Second != null && Lc.InRunAt >= 0f) _coop = true;
        }

        internal static long Key(PlayerRankManager rm, EDwarf d)
        {
            var i = rm.GetRankForClass(d);
            return i != null ? (long)i.Rank * 1000000L + i.Xp : -1;
        }

        [HarmonyPatch(typeof(EconomyManager), "OnRunEnd")]
        internal static class EndPatch
        {
            private static void Postfix(EconomyManager __instance)
            {
                try
                {
                    bool coop = _coop; _coop = false;
                    if (!coop || Lc.Has("-lcnoclassprogress") || CoopMenu.Classes == null) return;
                    int xp = __instance.rankXpEarned;
                    if (xp <= 0) { Last = "no rank xp this run"; return; }
                    var rm = __instance.playerRankManager; var rs = __instance.runSettingsManager;
                    var cd = CoopMenu.Classes[CoopMenu.P2.ClassIndex];
                    var now = rm.GetRankForClass(cd.Dwarf);
                    if (now == null) return;
                    long before = Key(rm, cd.Dwarf);
                    var hostClass = rs.selectedClass; var atStart = rm.classRankAtStart;
                    rs.selectedClass = cd; rm.classRankAtStart = new ClassRankInfo(now);
                    try { rm.GainRunEndRankXp(xp); }
                    finally { rs.selectedClass = hostClass; rm.classRankAtStart = atStart; }
                    P2Awards++; LastXp = xp;
                    Last = "player 2's class " + cd.Dwarf + " got " + xp + " xp (rank/xp " + before + " -> " + Key(rm, cd.Dwarf) + ")";
                    Lc.Line("progress: " + Last);
                }
                catch (Exception e) { Lc.LineOnce("progress: threw " + e.GetType().Name + ": " + e.Message); }
            }
        }

        // ---- test (`-lcprogresstest`): the run ends through the game's own handler; both classes must have gained --------------------------
        internal static bool TestDone;
        private static int _step; private static float _in = -1f, _at = -1f;
        private static long _h0, _p0; private static EDwarf _hd, _pd; private static int _xp;

        internal static void Test()
        {
            if (TestDone || !Lc.Has("-lcprogresstest")) return;
            float now = Time.realtimeSinceStartup;
            if (Lc.InRunAt < 0f || Phase1.Second == null) return;
            if (_in < 0f) _in = now;
            if (now < _in + 12f || now < _at) return;
            _at = now + 1.5f;
            try
            {
                var eco = CoopStart.Resolve<EconomyManager>(); var rm = CoopStart.Resolve<PlayerRankManager>();
                var rs = CoopStart.Resolve<RunSettingsManager>();
                if (eco == null || rm == null || rs == null) { TestDone = true; UiTest.FinishWith(false, "progress: managers missing"); return; }
                if (_step == 0)
                {
                    _hd = rs.selectedClass.Dwarf; _pd = CoopMenu.Classes[CoopMenu.P2.ClassIndex].Dwarf;
                    _h0 = Key(rm, _hd); _p0 = Key(rm, _pd);
                    Lc.Line("progress: before: host " + _hd + " " + _h0 + ", player 2 " + _pd + " " + _p0);
                    // a short test run earns nothing: put numbers into the game's own run statistics so that its formula gives experience
                    try { var tr = CoopStart.Resolve<CoreStatTracker>(); foreach (var nm in new[] { "EnemiesKilled", "StagesCleared", "PlayerLevelsGained" }) { var pi = typeof(CoreStatTracker).GetProperty(nm, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic); if (pi != null) pi.SetValue(tr, 6); } } catch (Exception e) { Lc.Line("progress: could not set run statistics: " + e.Message); }
                    eco.OnRunEnd(ECoreRunEndCondition.WIN);
                    _xp = eco.rankXpEarned; _step++;
                    return;
                }
                long h1 = Key(rm, _hd), p1 = Key(rm, _pd);
                bool same = _hd == _pd;
                Lc.Line("progress: after: host " + _hd + " " + h1 + ", player 2 " + _pd + " " + p1 + ", xp of the run " + _xp);
                bool sameRank = same ? (h1 / 1000000L == _h0 / 1000000L) : (h1 / 1000000L == _h0 / 1000000L && p1 / 1000000L == _p0 / 1000000L);
                bool ok;
                string what;
                if (!same)
                {
                    ok = _xp > 0 && h1 > _h0 && p1 > _p0 && (!sameRank || (h1 - _h0 == _xp && p1 - _p0 == _xp));
                    what = "both classes gained the run's experience (host " + (h1 - _h0) + ", player 2 " + (p1 - _p0) + ", run " + _xp + ")";
                }
                else
                {
                    ok = _xp > 0 && h1 > _h0 && (!sameRank || h1 - _h0 == 2 * _xp);
                    what = "two dwarves of the same class gain twice (" + (h1 - _h0) + " for " + _xp + ")";
                }
                Lc.Line("ui: CHECK " + (ok ? "ok  " : "FAIL") + " " + what);
                TestDone = true;
                UiTest.FinishWith(ok, "progress " + what);
            }
            catch (Exception e) { TestDone = true; UiTest.FinishWith(false, "progress threw " + e.GetType().Name + ": " + e.Message); }
        }
    }
}
