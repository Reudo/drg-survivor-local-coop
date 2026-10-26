using System;
using Assets.Scripts.Milestones;
using Assets.Scripts.RankSystem;
using Assets.Scripts.SkillSystem;
using DRS.SaveSystem;
using UnityEngine;

namespace LocalCoop
{
    /// <summary>
    /// `-lcunlockprobe` (menu only, no run): hand test, a subclass stayed locked although its class showed level 9, also in the game's own menu. Logs every milestone
    /// that rewards a class artifact (subclass): its type, target, progress and state, its class-rank entries, and whether the save has the subclass unlocked, next to the
    /// class ranks. Then RESULT and quit.
    /// </summary>
    internal static class UnlockProbe
    {
        private static float _at = -1f; private static bool _done;

        internal static void Tick()
        {
            if (_done || !Lc.Has("-lcunlockprobe") || MenuEntry.EntryButton == null) return;
            float now = Time.realtimeSinceStartup;
            if (_at < 0f) { _at = now + 5f; return; }
            if (now < _at) return;
            _done = true;
            int n = 0;
            try
            {
                var save = CoopStart.Resolve<SaveDataManager>();
                var rm = CoopStart.Resolve<PlayerRankManager>();
                var tr = CoopStart.Resolve<MilestoneTracker>();
                if (rm != null) foreach (EDwarf d in new[] { EDwarf.SCOUT, EDwarf.GUNNER, EDwarf.ENGINEER, EDwarf.DRILLER }) { var r = rm.GetRankForClass(d); Lc.Line("unlock: class " + d + " rank=" + (r != null ? r.Rank.ToString() : "?") + " xp=" + (r != null ? r.Xp.ToString() : "?")); }
                if (save != null) foreach (var a in Resources.FindObjectsOfTypeAll<ClassArtifactData>()) Lc.Line("unlock: subclass " + a.name + " unlocked=" + save.IsUnlocked(a));
                if (tr == null) { Lc.Line("unlock: no milestone tracker"); }
                else
                {
                    var views = tr.Milestones;
                    for (int i = 0; views != null && i < views.Count; i++)
                    {
                        var v = views[i]; var d = v != null ? v.Data : null;
                        if (d == null) continue;
                        bool subReward = d.ArtifactReward != null && d.ArtifactReward.TryCast<ClassArtifactData>() != null;
                        if (!subReward && d.TryCast<MSDataClassRank>() == null) continue;
                        n++;
                        string extra = "";
                        var cr = d.TryCast<MSDataClassRank>();
                        if (cr != null)
                        {
                            extra = " mustReachSpecific=" + cr.MustReachSpecificRanks + " entries=[";
                            for (int k = 0; cr.SpecificRanks != null && k < cr.SpecificRanks.Length; k++) extra += cr.SpecificRanks[k].Dwarf + ":" + cr.SpecificRanks[k].Rank + " ";
                            extra += "]";
                        }
                        Lc.Line("unlock: milestone " + d.name + " type=" + d.GetIl2CppType().Name + " target=" + d.Target + " reward=" + d.RewardType + " artifact=" + (d.ArtifactReward != null ? d.ArtifactReward.name : "-")
                            + " progress=" + v.Progress + " completed=" + v.IsCompleted + " persistent=" + d.Persistent + " classReq=" + (d.ClassRequirement != null ? d.ClassRequirement.Dwarf.ToString() : "-") + " subReq=" + (d.ClassArtifactRequirement != null ? d.ClassArtifactRequirement.name : "-") + extra);
                    }
                }
            }
            catch (Exception e) { Lc.Line("unlock: probe threw " + e.GetType().Name + ": " + e.Message); }
            Lc.QuitSent = true;
            Lc.Line("RESULT " + Lc.Test + " " + (n > 0 ? "PASS" : "FAIL") + " unlock probe: " + n + " milestones listed");
            Application.Quit();
        }
    }
}
