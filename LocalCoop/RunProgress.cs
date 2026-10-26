using System;
using Assets.Scripts.Milestones;
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
                    if (Lc.Num("-lcp2xp=") > 0f) xp = (int)Lc.Num("-lcp2xp=");          // test: enough experience for player 2's class to pass a milestone
                    var opened = Lc.Has("-lcnomilestonefix") ? null : OpenRankMilestones(cd.Dwarf);
                    rs.selectedClass = cd; rm.classRankAtStart = new ClassRankInfo(now);
                    try { rm.GainRunEndRankXp(xp); }
                    finally { rs.selectedClass = hostClass; rm.classRankAtStart = atStart; Close(opened); }
                    if (opened != null && opened.Count > 0) SaveMilestones();
                    P2Awards++; LastXp = xp;
                    Last = "player 2's class " + cd.Dwarf + " got " + xp + " xp (rank/xp " + before + " -> " + Key(rm, cd.Dwarf) + ")";
                    Lc.Line("progress: " + Last);
                }
                catch (Exception e) { Lc.LineOnce("progress: threw " + e.GetType().Name + ": " + e.Message); }
            }
        }

        // ---- class-rank milestones of player 2's class ------------------------------------------------------------------------------------
        // Hand test: a subclass stayed locked although its class showed level 9 (owner's save slot 1). The unlocks are milestones `MSDataClassRank_<Class>_CM1/CM2` (rank 9 / 18,
        // reward UNLOCK_CLASS_ARTIFACT) and `_W1.._W7` (weapons); their progress is set from PlayerRankManager.OnClassRankUp, but MilestoneView.SetStateProgress (RVA 0x9D18C0)
        // returns at once when `runRequirementsMet` is false, and a milestone that requires GUNNER is "not met" in a run whose class is player 1's SCOUT. So player 2's class
        // rose in rank and its milestones stayed behind. For player 2's award those milestones count as met, and a menu pass catches up on saves where it already went wrong.

        private static System.Collections.Generic.List<MilestoneView> OpenRankMilestones(EDwarf dwarf)
        {
            var list = new System.Collections.Generic.List<MilestoneView>();
            try
            {
                var tr = CoopStart.Resolve<MilestoneTracker>(); var views = tr != null ? tr.Milestones : null;
                for (int i = 0; views != null && i < views.Count; i++)
                {
                    var v = views[i]; if (!IsRankMilestoneOf(v, dwarf) || v.RunRequirementsMet) continue;
                    Phase1.SetBool(v, typeof(MilestoneView), "runRequirementsMet", true); list.Add(v);
                }
                Lc.Line("progress: " + list.Count + " class-rank milestones of " + dwarf + " count for player 2's award");
            }
            catch (Exception e) { Lc.Line("progress: opening milestones threw " + e.GetType().Name + ": " + e.Message); }
            return list;
        }

        private static void Close(System.Collections.Generic.List<MilestoneView> list)
        {
            if (list == null) return;
            foreach (var v in list) try { Phase1.SetBool(v, typeof(MilestoneView), "runRequirementsMet", false); } catch { }
        }

        private static bool IsRankMilestoneOf(MilestoneView v, EDwarf dwarf)
        {
            var d = v != null ? v.Data : null; if (d == null) return false;
            var cr = d.TryCast<MSDataClassRank>();
            return cr != null && !cr.MustReachSpecificRanks && d.ClassRequirement != null && d.ClassRequirement.Dwarf == dwarf;
        }

        private static void SaveMilestones()
        {
            try { var tr = CoopStart.Resolve<MilestoneTracker>(); if (tr != null) tr.SetSaveData(true); } catch (Exception e) { Lc.Line("progress: milestone save threw " + e.GetType().Name + ": " + e.Message); }
            try { var sm = CoopStart.Resolve<DRS.SaveSystem.SaveDataManager>(); if (sm != null) sm.Save(true); } catch (Exception e) { Lc.Line("progress: save threw " + e.GetType().Name + ": " + e.Message); }
        }

        internal static int Repaired;
        private static bool _repairDone;

        /// <summary>Menu, once per launch: every class-rank milestone whose class has already reached its target but is not complete gets the class's rank, and with it its
        /// reward, as the game would have done had the class been player 1's. Twin `-lcnomilestonerepair`.</summary>
        internal static void Repair()
        {
            if (_repairDone || MenuEntry.EntryButton == null || Lc.Has("-lcnomilestonerepair")) return;
            DoRepair();
        }

        private static void DoRepair()
        {
            var tr = CoopStart.Resolve<MilestoneTracker>(); var rm = CoopStart.Resolve<PlayerRankManager>();
            if (tr == null || rm == null || !rm.DataLoaded || tr.Milestones == null || tr.Milestones.Count == 0) return;
            _repairDone = true;
            try
            {
                var views = tr.Milestones;
                for (int i = 0; i < views.Count; i++)
                {
                    var v = views[i]; if (v == null || v.IsCompleted || v.Data == null || v.Data.TryCast<MSDataClassRank>() == null) continue;
                    var d = v.Data; if (d.ClassRequirement == null || d.TryCast<MSDataClassRank>().MustReachSpecificRanks) continue;
                    var r = rm.GetRankForClass(d.ClassRequirement.Dwarf); if (r == null || r.Rank < d.Target) continue;       // only what is due: a bar short of its target catches up at the class's next rank-up anyway
                    int before = v.Progress; bool met = v.RunRequirementsMet;
                    if (!met) Phase1.SetBool(v, typeof(MilestoneView), "runRequirementsMet", true);
                    try { v.SetStateProgress(r.Rank); } finally { if (!met) Phase1.SetBool(v, typeof(MilestoneView), "runRequirementsMet", false); }
                    Repaired++;
                    Lc.Line("progress: repaired " + d.name + " progress " + before + " -> " + v.Progress + " (class rank " + r.Rank + ", target " + d.Target + ") completed=" + v.IsCompleted);
                }
                if (Repaired > 0) SaveMilestones();
                Lc.Line("progress: milestone repair done, " + Repaired + " class-rank milestones caught up");
            }
            catch (Exception e) { Lc.Line("progress: repair threw " + e.GetType().Name + ": " + e.Message); }
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
                if (Lc.Has("-lcmilestonecheck"))
                {
                    if (Lc.Has("-lcrepairnow")) DoRepair();       // test: the menu pass, run right here on the state the award left
                    int behind = 0, done = 0, locked = 0; var save = CoopStart.Resolve<DRS.SaveSystem.SaveDataManager>(); int rank = rm.GetRankForClass(_pd).Rank;
                    var views = CoopStart.Resolve<MilestoneTracker>().Milestones;
                    for (int i = 0; i < views.Count; i++)
                    {
                        var v = views[i]; if (!IsRankMilestoneOf(v, _pd) || v.Data.Target > rank) continue;
                        if (v.IsCompleted) done++; else behind++;
                        var art = v.Data.ArtifactReward != null ? v.Data.ArtifactReward.TryCast<ClassArtifactData>() : null;
                        if (art != null && save != null && !save.IsUnlocked(art)) locked++;
                        Lc.Line("progress: milestone " + v.Data.name + " target " + v.Data.Target + " progress " + v.Progress + " completed=" + v.IsCompleted + (art != null ? " subclass " + art.name + " unlocked=" + (save != null && save.IsUnlocked(art)) : ""));
                    }
                    bool mok = behind == 0 && locked == 0 && done > 0;
                    Lc.Line("ui: CHECK " + (mok ? "ok  " : "FAIL") + " player 2's class milestones up to its rank " + rank + " are complete and their subclasses open | complete=" + done + " behind=" + behind + " subclasses still locked=" + locked + " repaired=" + Repaired);
                    ok = ok && mok; what += "; milestones complete=" + done + " behind=" + behind + " locked=" + locked;
                }
                TestDone = true;
                UiTest.FinishWith(ok, "progress " + what);
            }
            catch (Exception e) { TestDone = true; UiTest.FinishWith(false, "progress threw " + e.GetType().Name + ": " + e.Message); }
        }
    }
}
