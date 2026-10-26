using System;
using DRS.UI;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace LocalCoop
{
    /// <summary>
    /// What the second dwarf gets from the run: pickup reach (EXP), artifacts and their effects (max HP, Red Sugar).
    /// `-lcbonusprobe` only reads and logs (and equips one artifact on the host to see what happens), see Probe.
    /// </summary>
    internal static class Bonus
    {
        private static string Who(Player p) { return Share.IsClone(p) ? "P2" : "P1"; }

        private static string Reach(Player p)
        {
            var sb = new StringBuilder(Who(p) + ":");
            try { sb.Append(" pickupRadius=").Append(p.pickupRadius != null ? p.pickupRadius.Value.ToString("F2") : "null"); } catch (Exception e) { sb.Append(" radius-err=").Append(e.Message); }
            try { sb.Append(" sqr=").Append(p.sqrPickupRadius.ToString("F2")); } catch { }
            try
            {
                var c = p._pickupCollider;
                sb.Append(" collider=").Append(c != null ? (c.name + " r=" + c.radius.ToString("F2") + " enabled=" + c.enabled + " parent=" + (c.transform.parent != null ? c.transform.parent.name : "-")
                    + " ownedBy=" + (c.GetComponentInParent<Player>() != null && c.GetComponentInParent<Player>().Pointer == p.Pointer ? "self" : "OTHER") + " layer=" + c.gameObject.layer) : "null");
            }
            catch (Exception e) { sb.Append(" collider-err=").Append(e.Message); }
            return sb.ToString();
        }

        private static string Arts(Player p)
        {
            var sb = new StringBuilder(Who(p) + " artifacts:");
            try
            {
                var l = p.EquippedArtifacts;
                sb.Append(" count=").Append(l != null ? l.Count : -1);
                for (int i = 0; l != null && i < l.Count && i < 12; i++) { var h = l[i]; sb.Append(" [").Append(h != null && h.Data != null ? h.Data.name : "?").Append(']'); }
            }
            catch (Exception e) { sb.Append(" err=").Append(e.Message); }
            return sb.ToString();
        }

        private static string MaxHp(Player p) { try { return Who(p) + " maxHp=" + p._maxHp_k__BackingField.Value.ToString("F1") + " hp=" + p.currentHp; } catch { return Who(p) + " maxHp=?"; } }

        // ---- artifacts: whatever one dwarf equips, the other one gets too ------------------------------------------------------
        internal static int Mirrored;
        private static bool _mirroring;

        /// <summary>
        /// Artifacts were only ever equipped on the host (loot crate, level-up card of the host): the Red Sugar artifact then raised the host's max HP
        /// for every pickup of either dwarf (its handler ignores who picked up) and the second dwarf got nothing. Every artifact one dwarf equips is
        /// now equipped on the other one as well (starter artifacts, the subclass, are not touched). Both handlers then react to the pickup event,
        /// so a pickup by either dwarf raises both. Twin `-lcnoartmirror`.
        /// </summary>
        [HarmonyPatch(typeof(Player), "EquipArtifact")]
        internal static class ArtifactMirrorPatch
        {
            private static void Postfix(Player __instance, ArtifactSkillData __0, bool __1)
            {
                try
                {
                    if (_mirroring || __1 || __0 == null || Lc.Has("-lcnoartmirror") || Phase1.Second == null) return;
                    var other = Share.IsClone(__instance) ? Phase1.First() : Phase1.Second;
                    if (other == null || other.Pointer == __instance.Pointer) return;
                    _mirroring = true;
                    try { other.EquipArtifact(__0, false); Mirrored++; Lc.Line("bonus: artifact " + __0.name + " from " + Who(__instance) + " also equipped on " + Who(other) + " (stacks " + __instance.GetArtifactStacks(__0) + "/" + other.GetArtifactStacks(__0) + ")"); }
                    finally { _mirroring = false; }
                }
                catch (Exception e) { Lc.LineOnce("bonus: artifact mirror threw " + e.GetType().Name + ": " + e.Message); }
            }
        }

        // ---- general stat upgrades (crit chance, move speed ...): a level-up card of this kind is for both dwarves --------------------
        internal static int SupportMirrored;
        private static SkillRaritySet _pendSrs;
        private static Player _pendWho;
        private static bool _supportMirroring;

        private static string Snap(Player p)
        {
            try { return Who(p) + " maxHp=" + p._maxHp_k__BackingField.Value.ToString("F1") + " moveSpeed=" + (p._moveSpeed_k__BackingField != null ? p._moveSpeed_k__BackingField.Value.ToString("F2") : "?"); } catch { return Who(p) + " ?"; }
        }

        /// <summary>Every stat value of a dwarf (game's `StatHolder.allStats`) and the length of its upgrade history, to see what a mirrored card really changed.</summary>
        private static System.Collections.Generic.Dictionary<string, double> StatPrint(Player p, out int hist)
        {
            hist = -1;
            var d = new System.Collections.Generic.Dictionary<string, double>();
            try
            {
                var l = p.stats.allStats;
                for (int i = 0; i < l.Count; i++) { var st = l[i]; if (st != null) d[st.Type.ToString()] = st.Value; }
                try { hist = p.stats.playerUpgradeHistory.Count; } catch { }
            }
            catch { }
            return d;
        }

        internal static int SupportEffects, SupportToHost, SupportToClone;

        /// <summary>
        /// A card of type SUPPORT (the general stat upgrades) chosen by one dwarf is applied to the other one as well with the game's own
        /// `StatHolder.AddPlayerUpgrade(SkillRaritySet)`; the card is tagged "COOP BONUS" (see Tag). Weapons, weapon upgrades, tag upgrades and
        /// overclocks stay with the dwarf that chose them, artifacts are mirrored by `ArtifactMirrorPatch`. Twin `-lcnosupportmirror`.
        /// </summary>
        [HarmonyPatch(typeof(UISkillButton), "OnButtonClick")]
        internal static class SupportMirrorPatch
        {
            private static void Prefix(UISkillButton __instance)
            {
                _pendSrs = null; _pendWho = null;
                try
                {
                    if (Lc.Has("-lcnosupportmirror") || Phase1.Second == null || __instance.skillData == null || __instance.skillData.Type != ESkillType.SUPPORT) return;
                    _pendSrs = __instance.skillRaritySet; _pendWho = __instance.player;
                }
                catch { }
            }

            private static void Postfix(UISkillButton __instance)
            {
                try
                {
                    var srs = _pendSrs; var who = _pendWho; _pendSrs = null; _pendWho = null;
                    if (srs == null || who == null || _supportMirroring) return;
                    var other = Share.IsClone(who) ? Phase1.First() : Phase1.Second;
                    if (other == null || other.Pointer == who.Pointer || other.stats == null) return;
                    _supportMirroring = true;
                    try
                    {
                        string before = Snap(other);
                        int h0, h1;
                        var p0 = StatPrint(other, out h0);
                        other.stats.AddPlayerUpgrade(srs);
                        other.RefreshStats();
                        SupportMirrored++;
                        var p1 = StatPrint(other, out h1);
                        var ch = new StringBuilder();
                        foreach (var kv in p1) { double o; if (p0.TryGetValue(kv.Key, out o) && Math.Abs(o - kv.Value) > 1e-9) ch.Append(kv.Key).Append(' ').Append(o.ToString("G5")).Append("->").Append(kv.Value.ToString("G5")).Append("; "); }
                        bool eff = ch.Length > 0 || h1 > h0;
                        if (eff) SupportEffects++;
                        if (Share.IsClone(other)) SupportToClone++; else SupportToHost++;
                        Check("support card " + (__instance.skillData != null ? __instance.skillData.name : "?") + " chosen by " + Who(who) + " really changed " + Who(other), eff, "upgrade history " + h0 + "->" + h1 + ", stats changed: " + ch);
                        Lc.Line("bonus: support card " + (__instance.skillData != null ? __instance.skillData.name : "?") + " (" + __instance.rarity + ") chosen by " + Who(who) + " also applied to " + Who(other) + ": " + before + " -> " + Snap(other));
                    }
                    finally { _supportMirroring = false; }
                }
                catch (Exception e) { Lc.LineOnce("bonus: support mirror threw " + e.GetType().Name + ": " + e.Message); }
            }
        }

        /// <summary>Tags the SUPPORT cards of the level-up screen: "COOP BONUS" and what it means.</summary>
        [HarmonyPatch]
        internal static class SupportTagPatch
        {
            private static System.Reflection.MethodBase TargetMethod()
            {
                foreach (var m in typeof(UISkillButton).GetMethods())
                    if (m.Name == "Setup" && m.GetParameters().Length > 0 && m.GetParameters()[0].ParameterType == typeof(SkillRaritySet)) return m;
                return null;
            }

            private static void Postfix(UISkillButton __instance)
            {
                try
                {
                    if (Lc.Has("-lcnosupportmirror") || Phase1.Second == null || __instance.skillData == null || __instance.skillData.Type != ESkillType.SUPPORT || __instance.descriptionText == null) return;
                    string tag = "COOP BONUS";
                    string line = Loc.T(tag) + ": " + Loc.T("both dwarves get this upgrade");
                    var t = __instance.descriptionText;
                    if (t.text != null && t.text.Contains(line)) return;
                    t.text = (t.text ?? "") + "\n<color=#FFC600>" + line + "</color>";
                }
                catch (Exception e) { Lc.LineOnce("bonus: support tag threw " + e.GetType().Name + ": " + e.Message); }
            }
        }

        private static int _ok, _bad;
        private static void Check(string name, bool ok, string info)
        {
            if (ok) _ok++; else _bad++;
            Lc.Line("bonus: CHECK " + (ok ? "OK   " : "FAIL ") + name + " | " + info);
        }

        private static double Hp(Player p) { try { return p._maxHp_k__BackingField.Value; } catch { return -1; } }

        private static float _xpAt = -1f, _xpCheckAt = -1f;
        private static int _xpClaim0, _xpRedirect0;

        /// <summary>
        /// `-lcxptest`: the owner saw player 2 collect only the large (purple) XP orbs; the small (blue) ones stayed on the ground for player 1.
        /// 12 s after the clone is ready: 6 small and 3 large XP orbs are dropped around player 2 (host far away), 8 s later the check asks whether
        /// they are gone and who collected them.
        /// </summary>
        private static int _xpStage;
        private static float _xpGiveUp = -1f;
        private static readonly System.Collections.Generic.HashSet<long> _killed = new System.Collections.Generic.HashSet<long>();
        private static string _xpSpawned = "";
        private static string XpTypes()
        {
            var counts = new System.Collections.Generic.SortedDictionary<string, int>();
            foreach (var pk in UnityEngine.Object.FindObjectsOfType<Pickup>())
                if (pk != null && pk.gameObject.activeInHierarchy) { string k = pk.type + "/" + pk.gameObject.name.Replace("(Clone)", "") + "/" + pk.state; int n; counts.TryGetValue(k, out n); counts[k] = n + 1; }
            var sb = new StringBuilder();
            foreach (var kv in counts) sb.Append(kv.Key).Append('=').Append(kv.Value).Append("; ");
            return sb.ToString();
        }

        internal static void XpTest()
        {
            if (!Lc.Has("-lcxptest") || Phase1.Second == null || _xpStage >= 5) return;
            var host = Phase1.First(); var clone = Phase1.Second;
            if (host == null || clone == null || host.pickupSpawner == null) return;
            float now = Time.realtimeSinceStartup;
            try
            {
                if (_xpAt < 0f) { _xpAt = now + 12f; return; }
                if (now < _xpAt) return;
                var c = clone.transform.position;
                var dir = c - host.transform.position; dir.y = 0f; dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.right;      // on the far side of P2, away from the host
                var perp = new Vector3(-dir.z, 0f, dir.x);
                if (_xpStage == 0 && _xpGiveUp < 0f) _xpGiveUp = now + 60f;
                if (_xpStage == 0)
                {
                    int alive = 0;
                    foreach (var en in UnityEngine.Object.FindObjectsOfType<Enemy>()) if (en != null && en.gameObject.activeInHierarchy && en.isAlive && !_killed.Contains(en.Pointer.ToInt64())) alive++;
                    if (alive < 6 && now < _xpGiveUp) { _xpAt = now + 3f; Lc.LineOnce("bonus: xptest waits for enemies (alive " + alive + ")"); return; }
                    Lc.Line("bonus: xptest enemies alive: " + alive);
                }
                if (_xpStage == 0 || _xpStage == 2)
                {
                    bool small = _xpStage == 0;
                    _xpClaim0 = Share.ClaimedByClone; _xpRedirect0 = Share.XpRedirected;
                    string before = XpTypes();
                    for (int i = 0; i < 6; i++)
                    {
                        var pos = c + dir * (small ? 1.5f + i * 0.2f : 2.5f) + perp * ((i - 2.5f) * (small ? 0.4f : 0.8f));
                        if (small)
                        {
                            // the way the game drops XP: a real enemy dies next to P2
                            foreach (var en in UnityEngine.Object.FindObjectsOfType<Enemy>())
                            {
                                if (en == null || !en.gameObject.activeInHierarchy || !en.isAlive || _killed.Contains(en.Pointer.ToInt64())) continue;
                                _killed.Add(en.Pointer.ToInt64());
                                en.transform.position = pos;
                                host.pickupSpawner.OnEnemyDeath(en, 12);
                                break;
                            }
                        }
                        else host.pickupSpawner.SpawnLargeXP(pos, 20, default(EXPSource), 0f);
                    }
                    _xpSpawned = XpTypes();
                    Lc.Line("bonus: xptest " + (small ? "SMALL" : "LARGE") + ": dropped 6 around P2 (host " + Vector3.Distance(c, host.transform.position).ToString("F1") + " m away); before: " + before + " | right after: " + XpTypes());
                    _xpStage++; _xpAt = now + 8f; return;
                }
                bool wasSmall = _xpStage == 1;
                int left = 0; var det = new StringBuilder();
                foreach (var pk in UnityEngine.Object.FindObjectsOfType<Pickup>())
                    if (pk != null && pk.gameObject.activeInHierarchy && (pk.type == EPickupType.XP || pk.type == EPickupType.XP_LARGE || pk.type == EPickupType.XP_DUD || pk.type == EPickupType.XP_LARGE_DUD))
                    {
                        if (Vector3.Distance(pk.transform.position, clone.transform.position) > 8f) continue;      // flung far away by the spawn force: nobody is meant to reach those
                        left++;
                        if (left <= 8) det.Append(pk.type).Append("/").Append(pk.gameObject.name.Replace("(Clone)", "")).Append("/").Append(pk.state).Append("/own=").Append(pk.player != null ? (Share.IsClone(pk.player) ? "P2" : "P1") : "-")
                            .Append(" dP2=").Append(Vector3.Distance(pk.transform.position, clone.transform.position).ToString("F1")).Append(" dP1=").Append(Vector3.Distance(pk.transform.position, host.transform.position).ToString("F1"))
                            .Append(" collider=").Append(pk.collider != null ? pk.collider.enabled.ToString() : "null").Append(" delay=").Append(pk.pickupDelay.ToString("F1")).Append("; ");
                    }
                if (wasSmall && !_xpSpawned.Contains("XP/")) { Lc.Line("bonus: xptest SMALL not evaluated: killing enemies next to P2 produced no XP orbs in this harness (the blue orbs could not be reproduced)"); _xpStage++; _xpAt = now + 1f; return; }
                Check("the " + (wasSmall ? "small" : "large") + " XP orbs dropped within 8 m of P2 are all collected", left == 0,
                    "left=" + left + " claimedByP2=" + (Share.ClaimedByClone - _xpClaim0) + " xpRedirected=" + (Share.XpRedirected - _xpRedirect0) + " | still there: " + XpTypes() + " | first leftovers: " + det);
                _xpStage++; _xpAt = now + 1f;
                if (_xpStage >= 4) { _xpStage = 5; Lc.Line("bonus: SUMMARY ok=" + _ok + " fail=" + _bad); }
            }
            catch (Exception e) { Lc.LineOnce("bonus: xptest threw " + e.GetType().Name + ": " + e.Message); _xpStage = 5; }
        }

        // ---- XP orbs belong to the nearer dwarf ---------------------------------------------------------------------------------
        internal static int OwnerFlips;
        private static float _ownerAt;

        /// <summary>
        /// Hand test: player 2 could collect the large (purple) XP orbs but not the small (blue) ones, those stayed on the ground for player 1.
        /// The large ones are claimed through the dwarves' pickup triggers (`Share.TouchPatch` already hands them to the dwarf that touched them); the
        /// small ones never showed up in any trigger log, so they are most likely claimed by their own check against `Pickup.player`, which is the host
        /// for every pooled orb. Until the cause is proven: every XP orb that waits to be claimed gets the dwarf as owner that is nearer to it and within
        /// that dwarf's reach (`sqrPickupRadius`). Four times a second. Twin `-lcnoownertick`.
        /// </summary>
        internal static void OwnerTick()
        {
            if (Phase1.Second == null || Lc.Has("-lcnoownertick") || Time.realtimeSinceStartup < _ownerAt) return;
            _ownerAt = Time.realtimeSinceStartup + 0.25f;
            try
            {
                var host = Phase1.First(); var clone = Phase1.Second;
                if (host == null || clone == null || !host.isAlive || !clone.isAlive) return;
                var hp = host.transform.position; var cp = clone.transform.position;
                float rh = host.sqrPickupRadius, rc = clone.sqrPickupRadius;
                foreach (var pk in Find.Many<Pickup>(0.5f))
                {
                    if (pk == null || !pk.gameObject.activeInHierarchy) continue;
                    var t = pk.type;
                    if (t != EPickupType.XP && t != EPickupType.XP_LARGE && t != EPickupType.XP_DUD && t != EPickupType.XP_LARGE_DUD) continue;
                    if (pk.state != Pickup.EState.WAITING_TO_BE_CLAIMED && pk.state != Pickup.EState.SPAWNING) continue;
                    var pos = pk.transform.position;
                    float dh = (pos - hp).sqrMagnitude, dc = (pos - cp).sqrMagnitude;
                    bool mine = pk.player != null && Share.IsClone(pk.player);
                    if (dc < dh && dc <= rc && !mine) { Phase1.SetRef(pk, typeof(Pickup), "player", clone.Pointer); OwnerFlips++; Lc.LineOnce("bonus: XP orb " + pk.gameObject.name + " handed to P2 (dP2 " + Mathf.Sqrt(dc).ToString("F1") + " dP1 " + Mathf.Sqrt(dh).ToString("F1") + ")"); }
                    else if (dh <= dc && dh <= rh && mine) { Phase1.SetRef(pk, typeof(Pickup), "player", host.Pointer); OwnerFlips++; }
                }
            }
            catch (Exception e) { Lc.LineOnce("bonus: owner tick threw " + e.GetType().Name + ": " + e.Message); }
        }

        // ---- the small (blue) XP orbs are cubes of the XPManager, not pickups ---------------------------------------------------------
        internal static int CubeClaims;
        private static bool _cubeLogged;

        /// <summary>
        /// Hand test (log with Debug on): player 2 never collected the small blue orbs. They are not `Pickup` objects at all: the `XPManager` keeps them as
        /// positions in a job and every frame claims the cubes around ONE point, the host's pickup collider (`Update` calls `ClaimInRadius(pickupTransform,
        /// collider radius)`). Nobody claimed the cubes around the second dwarf. Now the same claim runs for player 2's position and reach every frame; the
        /// claimed cubes fly to the shared pickup point and the XP goes to the one pool, as for every other source. Twin `-lcnocubes`.
        /// </summary>
        internal static void CubeTick()
        {
            if (Phase1.Second == null || Lc.Has("-lcnocubes")) return;
            var clone = Phase1.Second;
            if (!clone.isAlive) return;
            var xm = XPManager.Instance;
            if (xm == null) return;
            float r = xm.pickupCollider != null ? xm.pickupCollider.radius : Mathf.Sqrt(clone.sqrPickupRadius);
            xm.ClaimInRadius(clone.transform.position, r);
            CubeClaims++;
            if (!_cubeLogged) { _cubeLogged = true; Lc.Line("bonus: XP cubes are also claimed around the second dwarf (radius " + r.ToString("F1") + ")"); }
        }

        private static int _cubeStage; private static float _cubeAt = -1f; private static int _cubeBase, _cubeSpawned;
        private static int CubesOnGround(XPManager xm) { return xm.stationaryCount + xm.spawningCount; }

        /// <summary>`-lccubetest`: twelve cubes are dropped around player 2 (host far away); after a few seconds they must be gone.</summary>
        internal static void CubeTest()
        {
            if (!Lc.Has("-lccubetest") || Phase1.Second == null || _cubeStage >= 3 || Lc.InRunAt < 0f) return;
            var host = Phase1.First(); var clone = Phase1.Second; var xm = XPManager.Instance;
            if (host == null || clone == null || xm == null) return;
            float now = Time.realtimeSinceStartup;
            try
            {
                if (_cubeAt < 0f) { _cubeAt = now + 10f; return; }
                if (now < _cubeAt) return;
                if (_cubeStage == 0)
                {
                    var c = clone.transform.position;
                    var dir = c - host.transform.position; dir.y = 0f; dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.right;
                    _cubeBase = CubesOnGround(xm);
                    for (int i = 0; i < 12; i++) xm.Spawn(c + dir * 2.2f + new Vector3((i % 4 - 1.5f) * 0.4f, 0f, (i / 4 - 1f) * 0.4f), 0f);
                    _cubeSpawned = CubesOnGround(xm) - _cubeBase;
                    Lc.Line("bonus: cubetest dropped 12 cubes around P2 (host " + Vector3.Distance(c, host.transform.position).ToString("F1") + " m away), on the ground now +" + _cubeSpawned);
                    _cubeStage = 1; _cubeAt = now + 6f; return;
                }
                int left = CubesOnGround(xm) - _cubeBase;
                Check("the small XP cubes dropped around player 2 are collected by player 2", _cubeSpawned >= 6 && left <= 3, "dropped=" + _cubeSpawned + " left=" + left + " claims=" + CubeClaims);
                Lc.Line("bonus: SUMMARY ok=" + _ok + " fail=" + _bad + (Lc.Has("-lcnocubes") ? " (twin: FAIL expected)" : ""));
                _cubeStage = 3;
            }
            catch (Exception e) { Lc.LineOnce("bonus: cubetest threw " + e.GetType().Name + ": " + e.Message); _cubeStage = 3; }
        }

        // ---- `-lccratetest`: the host opens a legendary loot crate; the second dwarf must get its own round -------------------------------
        private static int _crateStage;
        internal static bool CrateDone { get { return _crateStage >= 5; } }
        private static float _crateAt = -1f;
        private static int _crateOkBase;

        internal static void CrateTest()
        {
            if (!Lc.Has("-lccratetest") || Phase1.Second == null || _crateStage >= 5) return;
            var host = Phase1.First(); var clone = Phase1.Second;
            if (host == null || clone == null || host.pickupSpawner == null || Lc.InRunAt < 0f) return;
            float now = Time.realtimeSinceStartup;
            try
            {
                if (_crateAt < 0f) { _crateAt = now + 8f; return; }
                if (now < _crateAt) return;
                switch (_crateStage)
                {
                    case 0:
                        {
                            var pos = host.transform.position + new Vector3(1.2f, 0f, 0f);
                            host.pickupSpawner.SpawnLootCrate(pos, ERarity.LEGENDARY, LootCrate.ESource.ELITE);
                            Lc.Line("bonus: cratetest: legendary crate spawned next to the host");
                            _crateStage = 1; _crateAt = now + 2f; break;
                        }
                    case 1:
                        {
                            LootCrate crate = null;
                            foreach (var lc in UnityEngine.Object.FindObjectsOfType<LootCrate>()) if (lc != null && lc.gameObject.activeInHierarchy) { crate = lc; break; }
                            Lc.Line("bonus: cratetest: crate found=" + (crate != null));
                            if (crate != null) host.transform.position = crate.transform.position;
                            _crateStage = 2; _crateAt = now + 25f; break;
                        }
                    case 2:
                        Check("the crate the host opened is followed by a crate round for P2", LevelRounds.CrateRoundsStarted >= 1 && LevelRounds.CrateRoundsFinished >= 1,
                            "owed=" + LevelRounds.OwedCrates.Count + " started=" + LevelRounds.CrateRoundsStarted + " finished=" + LevelRounds.CrateRoundsFinished + " picks=[" + string.Join(",", LevelRounds.Picks) + "]");
                        Lc.Line("bonus: SUMMARY ok=" + _ok + " fail=" + _bad + (Lc.Has("-lcnocrateshare") ? " (twin: no crate round, FAIL expected)" : ""));
                        _crateStage = 5; break;
                }
            }
            catch (Exception e) { Lc.LineOnce("bonus: cratetest threw " + e.GetType().Name + ": " + e.Message); _crateStage = 5; }
        }

        // hand test: "Ancient Knowledge" (BookOfExperience) should give 3 levels but gave 6: it grants its levels to the one shared level counter when it is applied, and the mirrored copy
        // on player 2 did that a second time. Only the host's application counts. Twin `-lcbookbug`.
        internal static int BookSkips, BookApplies;
        [HarmonyPatch(typeof(BookOfExperienceArtifactData), "Apply")]
        internal static class BookPatch
        {
            private static bool Prefix(Player p)
            {
                try
                {
                    if (Lc.Has("-lcbookbug") || Phase1.Second == null || p == null) return true;
                    if (Share.IsClone(p)) { BookSkips++; return false; }
                    BookApplies++;
                }
                catch { }
                return true;
            }
        }

        // ---- the upgrade arrows of the equipment bar -------------------------------------------------------------------------------
        internal static int ArrowRedirects, SharedArrows, ArrowCleans;
        private static bool _inArrow;          // the patch calls the original itself: do not run the patch again for those calls
        internal static UISkillButton HoverCard;          // the card of the level-up screen that is selected right now (its OnSelect runs the arrow calls)

        internal static bool TestShared;         // test only (`-lcslotarrows`): treat the arrow call as coming from a general card
        internal static bool IsShared(UISkillButton b) { try { return TestShared || b != null && b.skillData != null && b.skillData.Type == ESkillType.SUPPORT; } catch { return false; } }
        private static string CardInfo(UISkillButton b) { try { return b == null ? "-" : (b.skillData != null ? b.skillData.name + "/" + b.skillData.Type : "?"); } catch { return "?"; } }

        [HarmonyPatch(typeof(UISkillButton), "OnSelect")]
        internal static class HoverSelectPatch { private static void Prefix(UISkillButton __instance) { HoverCard = __instance; } }

        [HarmonyPatch(typeof(UISkillButton), "OnDeselect")]
        internal static class HoverDeselectPatch { private static void Postfix(UISkillButton __instance) { if (HoverCard != null && __instance != null && HoverCard.Pointer == __instance.Pointer) HoverCard = null; } }

        /// <summary>
        /// Hand test: hovering a card of the level-up screen shows arrows (which weapon the upgrade is for) in the equipment bar. The game addresses the slots
        /// of its own bar, i.e. player 1's, also in player 2's round. While player 2's round runs every such call is sent to the same slot of the second
        /// bar instead (that slot knows player 2's weapon) and the first bar stays quiet. Twin `-lcnoslotredirect`.
        /// </summary>
        [HarmonyPatch]
        internal static class SlotIndicatorPatch
        {
            private static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
            {
                foreach (var m in typeof(UIActionBarSlot).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
                    if (m.Name == "ToggleUpgradeIndicator" || m.Name == "ToggleWeaponTagIndicator" || m.Name == "HideIndicators") yield return m;
            }

            private static bool Prefix(UIActionBarSlot __instance, System.Reflection.MethodBase __originalMethod, object[] __args)
            {
                try
                {
                    if (Phase1.Second == null || _inArrow) return true;
                    var other = Hud2.CounterpartOf(__instance);
                    if (other == null) return true;          // not a slot of the first bar (or already the second one)
                    // hand test: the dots of the weapon tag indicator (like the arrows) stayed on player 2's slot after the card was left. Every "off" reaches both bars.
                    if (!Lc.Has("-lcnostalearrow") && (__originalMethod.Name == "HideIndicators" || (__originalMethod.Name == "ToggleWeaponTagIndicator" && __args != null && __args.Length > 0 && __args[0] is bool && !(bool)__args[0])))
                    { _inArrow = true; try { __originalMethod.Invoke(other, __args); } catch { } finally { _inArrow = false; } }
                    if (Lc.Has("-lcarrowlog")) Lc.Line("arrow: " + __originalMethod.Name + "(" + (__args != null && __args.Length > 0 && __args[0] != null ? __args[0].GetType().Name : "-") + ") card=" + CardInfo(HoverCard) + " p2round=" + LevelRounds.P2Round);
                    if (IsShared(HoverCard) && !Lc.Has("-lcnoarrowshared"))
                    {
                        // a general (COOP BONUS) upgrade counts for both dwarves: both bars show which weapons it touches (each slot decides for its own weapon)
                        SharedArrows++;
                        _inArrow = true;
                        try { __originalMethod.Invoke(__instance, __args); __originalMethod.Invoke(other, __args); } finally { _inArrow = false; }
                        return false;
                    }
                    // hand test: in player 2's shop the arrow lit the slot of player 1's bar - the redirect holds there too
                    if (Lc.Has("-lcnoslotredirect") || !(LevelRounds.P2Round || Wallets.P2ShopActive)) return true;
                    ArrowRedirects++;
                    __originalMethod.Invoke(other, __args);
                    // hand test: an arrow lit on player 1's bar before player 2's turn (his own shop or round) stayed there - the hide call went to the other bar only
                    if (!Lc.Has("-lcnostalearrow") && (__originalMethod.Name == "HideIndicators" || (__originalMethod.Name == "ToggleUpgradeIndicator" && __args != null && __args.Length == 1 && __args[0] is bool && !(bool)__args[0])))
                    { _inArrow = true; try { __originalMethod.Invoke(__instance, __args); } finally { _inArrow = false; } }
                    // hand test: player 2 hovered an upgrade for a weapon that player 1 has too and the arrow stayed lit at player 1's slot (a leftover of player 1's own shop turn).
                    // While player 2 chooses, the same call switches the host's slot off. Twin `-lcnoarrowclean`.
                    else if (!Lc.Has("-lcnoarrowclean") && __originalMethod.Name == "ToggleUpgradeIndicator" && __args != null && __args.Length == 1 && __args[0] is bool)
                    { _inArrow = true; try { __originalMethod.Invoke(__instance, new object[] { false }); ArrowCleans++; } finally { _inArrow = false; } }
                    return false;
                }
                catch (Exception e) { Lc.LineOnce("bonus: slot indicator redirect threw " + e.GetType().Name + ": " + e.Message); return true; }
            }
        }

        // Hand test: "an upgrade for a weapon that player 1 has too lights player 1's slot" and "upgrades only player 2's weapon has show nothing": the game decides which slot gets an
        // arrow from the weapon handler its own slot holds (player 1's bar), and the calls were then sent to the second bar by index. While player 2 chooses, player 1's slots hold
        // player 2's weapon handlers (and flags), so the game's own decision is made for player 2's weapons; the calls still go to the second bar by index. EXPERIMENT, only with `-lcslotswap`: it did not work (the game's own call lit player 1's slot and was not caught by the redirect).
        private static bool _swapped; internal static int SlotSwaps;
        private static IntPtr[] _hand; private static bool[] _av, _has;
        internal static void SlotSwapTick()
        {
            if (Phase1.Second == null || !Hud2.Built) { _swapped = false; return; }
            bool want = Lc.Has("-lcslotswap") && (LevelRounds.P2Round || Wallets.P2ShopActive);       // experiment, off: P1's slot lit but the call was not redirected (inlined inner call?)
            if (want == _swapped) return;
            try
            {
                var front = Find.One<UICoreFront>(); if (front == null || front.actionBarSlots == null) return;
                var s1 = front.actionBarSlots; int n = s1.Length;
                if (want)
                {
                    _hand = new IntPtr[n]; _av = new bool[n]; _has = new bool[n];
                    for (int i = 0; i < n; i++)
                    {
                        var a = s1[i]; var b = Hud2.CounterpartOf(a); if (a == null || b == null) continue;
                        _hand[i] = a.weaponHandler != null ? a.weaponHandler.Pointer : IntPtr.Zero; _av[i] = a.hasAvailableWeapon; _has[i] = a.HasWeapon;
                        Phase1.SetRef(a, typeof(UIActionBarSlot), "weaponHandler", b.weaponHandler != null ? b.weaponHandler.Pointer : IntPtr.Zero);
                    }
                    SlotSwaps++;
                }
                else
                {
                    for (int i = 0; i < n && _hand != null && i < _hand.Length; i++)
                    {
                        var a = s1[i]; if (a == null) continue;
                        Phase1.SetRef(a, typeof(UIActionBarSlot), "weaponHandler", _hand[i]);
                    }
                    _hand = null;
                }
                _swapped = want;
            }
            catch (Exception e) { Lc.LineOnce("bonus: slot swap threw " + e.GetType().Name + ": " + e.Message); _swapped = want; }
        }

        /// <summary>Both bars lose every upgrade arrow (a turn changes: the other dwarf's leftovers must not stay lit). Twin `-lcnoarrowclean`.</summary>
        internal static void ClearArrows()
        {
            if (Lc.Has("-lcnoarrowclean") || Phase1.Second == null) return;
            try
            {
                var front = Find.One<UICoreFront>(); if (front == null || front.actionBarSlots == null) return;
                _inArrow = true;
                try { foreach (var s in front.actionBarSlots) { if (s == null) continue; s.HideIndicators(); var o = Hud2.CounterpartOf(s); if (o != null) o.HideIndicators(); } }
                finally { _inArrow = false; }
            }
            catch (Exception e) { Lc.LineOnce("bonus: clear arrows threw " + e.GetType().Name + ": " + e.Message); }
        }

        // ---- probe/test `-lcarrowprobe`: select every card of an open level-up screen and see which slots of both bars light up ----------
        private static int _probedShown = -1; private static float _probeAt = -1f;
        internal static string ProbeSummary = "";
        internal static int ProbeSharedCards, ProbeSharedBothBars, ProbeOtherCards, ProbeOtherOnlyOwn;

        private static string LitSet(UIActionBarSlot[] slots)
        {
            var sb = new StringBuilder();
            if (slots != null) for (int i = 0; i < slots.Length; i++) sb.Append(Lit(slots[i]) ? "1" : "0");
            return sb.ToString();
        }

        internal static void ArrowProbe()
        {
            if (!Lc.Has("-lcarrowprobe") || Phase1.Second == null || !Hud2.Built) return;
            try
            {
                float now = Time.realtimeSinceStartup;
                if (LevelBanner.Shown == _probedShown) return;
                UILevelUpForm form = null;
                foreach (var f in Find.Forms<UILevelUpForm>()) if (f != null && f.gameObject.activeInHierarchy) { form = f; break; }
                if (form == null) return;
                if (_probeAt < 0f) { _probeAt = now + 1.6f; return; }
                if (now < _probeAt) return;
                _probeAt = -1f; _probedShown = LevelBanner.Shown;
                var front = Find.One<UICoreFront>(); if (front == null) return;
                var s1 = front.actionBarSlots; UIActionBarSlot[] s2 = new UIActionBarSlot[s1.Length];
                for (int i = 0; i < s1.Length; i++) s2[i] = Hud2.CounterpartOf(s1[i]);
                foreach (var b in form.skillButtons)
                {
                    if (b == null || !b.gameObject.activeInHierarchy) continue;
                    b.OnSelect(new UnityEngine.EventSystems.BaseEventData(UnityEngine.EventSystems.EventSystem.current));
                    string a = LitSet(s1), c = LitSet(s2); bool shared = IsShared(b);
                    Lc.Line("arrowprobe: round=" + (LevelRounds.P2Round ? "P2" : "P1") + " card=" + CardInfo(b) + " shared=" + shared + " bar1=" + a + " bar2=" + c + " weapons1=" + SlotWeapons(front, true) + " weapons2=" + SlotWeapons(front, false));
                    if (shared) { ProbeSharedCards++; if (a.IndexOf('1') >= 0 && c.IndexOf('1') >= 0) ProbeSharedBothBars++; }
                    else if (a.IndexOf('1') >= 0 || c.IndexOf('1') >= 0) { ProbeOtherCards++; if ((LevelRounds.P2Round ? a : c).IndexOf('1') < 0) ProbeOtherOnlyOwn++; }
                    b.OnDeselect(new UnityEngine.EventSystems.BaseEventData(UnityEngine.EventSystems.EventSystem.current));
                }
            }
            catch (Exception e) { Lc.LineOnce("bonus: arrow probe threw " + e.GetType().Name + ": " + e.Message); }
        }

        /// <summary>`-lcshoparrows`: hover every card of the open shop (player 2's turn): which slots of both bars light up. Returns "bad" if a card lit only the host's bar.</summary>
        internal static int ShopProbeLit1, ShopProbeLit2, ShopProbeCards;
        internal static void ShopProbe(UIShopScreen sc)
        {
            try
            {
                var front = Find.One<UICoreFront>(); if (front == null || sc == null) return;
                var s1 = front.actionBarSlots; var s2 = new UIActionBarSlot[s1.Length];
                for (int i = 0; i < s1.Length; i++) s2[i] = Hud2.CounterpartOf(s1[i]);
                string base1 = LitSet(s1), base2 = LitSet(s2);          // slots that are lit without any hover (unused slots) do not count
                foreach (var b in sc.shopButtons)
                {
                    if (b == null || !b.gameObject.activeInHierarchy || !b.HasContent() || b.skillButton == null) continue;
                    b.OnSelect(new UnityEngine.EventSystems.BaseEventData(UnityEngine.EventSystems.EventSystem.current));
                    string a = LitSet(s1), c = LitSet(s2); ShopProbeCards++;
                    if (a != base1) ShopProbeLit1++; if (c != base2) ShopProbeLit2++;
                    Lc.Line("shoparrows: card=" + CardInfo(b.skillButton) + " bar1=" + a + " bar2=" + c + " weapons1=" + SlotWeapons(front, true) + " weapons2=" + SlotWeapons(front, false));
                    b.OnDeselect(new UnityEngine.EventSystems.BaseEventData(UnityEngine.EventSystems.EventSystem.current));
                }
            }
            catch (Exception e) { Lc.LineOnce("bonus: shop probe threw " + e.GetType().Name + ": " + e.Message); }
        }

        private static string SlotWeapons(UICoreFront front, bool first)
        {
            var sb = new StringBuilder();
            try
            {
                for (int i = 0; i < front.actionBarSlots.Length; i++)
                {
                    var s = first ? front.actionBarSlots[i] : Hud2.CounterpartOf(front.actionBarSlots[i]);
                    sb.Append(s != null && s.data != null ? s.data.name : "-").Append(',');
                }
            }
            catch { }
            return sb.ToString();
        }

        private static int _arrowStage; private static bool _staleDone;
        internal static bool ArrowDone { get { return _arrowStage >= 6; } }
        private static float _arrowAt = -1f;

        private static bool Lit(UIActionBarSlot s) { try { return s != null && s.upgradeIndicator != null && s.upgradeIndicator.gameObject.activeSelf; } catch { return false; } }

        /// <summary>`-lcslotarrows`: forces "player 2's round" and asks for the upgrade arrow on slot 0 of the game's own bar: it must light up on the second bar.</summary>
        internal static void SlotArrowTest()
        {
            if (!Lc.Has("-lcslotarrows") || Phase1.Second == null || _arrowStage >= 6 || !Hud2.Built) return;
            float now = Time.realtimeSinceStartup;
            try
            {
                if (_arrowAt < 0f) { _arrowAt = now + 8f; return; }
                if (now < _arrowAt) return;
                var front = Find.One<UICoreFront>();
                var s1 = front != null && front.actionBarSlots != null && front.actionBarSlots.Length > 0 ? front.actionBarSlots[0] : null;
                var s2 = Hud2.CounterpartOf(s1);
                if (s1 == null || s2 == null) { Check("both bars have a slot 0", false, "s1=" + (s1 != null) + " s2=" + (s2 != null)); _arrowStage = 6; return; }
                if (_arrowStage == 0)
                {
                    LevelRounds.TestForceP2 = true;
                    s1.ToggleUpgradeIndicator(true);
                    Check("in player 2's round the arrow lights up on player 2's bar, not on player 1's", Lit(s2) && !Lit(s1), "P1 slot lit=" + Lit(s1) + " P2 slot lit=" + Lit(s2) + " redirects=" + ArrowRedirects);
                    s1.ToggleUpgradeIndicator(false);             // goes to player 2's slot as well
                    _arrowStage = 1; _arrowAt = now + 2f; return;
                }
                if (_arrowStage == 1)
                {
                    LevelRounds.TestForceP2 = false;
                    s1.ToggleUpgradeIndicator(true);
                    _arrowStage = 2; _arrowAt = now + 1f; return;
                }
                if (_arrowStage == 2 && !_staleDone)
                {
                    Check("in player 1's round the arrow lights up on player 1's bar as before", Lit(s1) && !Lit(s2), "P1 slot lit=" + Lit(s1) + " P2 slot lit=" + Lit(s2));
                    LevelRounds.TestForceP2 = true; s1.ToggleUpgradeIndicator(false); LevelRounds.TestForceP2 = false;          // player 2's turn begins with an arrow still lit on player 1's bar
                    _staleDone = true; _arrowAt = now + 1.5f; return;          // the arrow fades out, look again later
                }
                if (_arrowStage == 2 && _staleDone)
                {
                    Check("player 2's turn clears an arrow left on player 1's bar", !Lit(s1), "P1 slot lit=" + Lit(s1));
                    s1.ToggleUpgradeIndicator(false);
                    TestShared = true; s1.ToggleUpgradeIndicator(true);          // a general (COOP BONUS) card: both bars
                    _arrowStage = 3; _arrowAt = now + 1f; return;
                }
                if (_arrowStage == 3)
                {
                    Check("a general upgrade lights the arrow on both bars (player 1's round)", Lit(s1) && Lit(s2), "P1 slot lit=" + Lit(s1) + " P2 slot lit=" + Lit(s2) + " shared=" + SharedArrows);
                    s1.ToggleUpgradeIndicator(false);
                    _arrowStage = 4; _arrowAt = now + 1.5f; return;
                }
                if (_arrowStage == 4)
                {
                    Check("... and both go dark again", !Lit(s1) && !Lit(s2), "P1 slot lit=" + Lit(s1) + " P2 slot lit=" + Lit(s2));
                    LevelRounds.TestForceP2 = true; s1.ToggleUpgradeIndicator(true);
                    _arrowStage = 5; _arrowAt = now + 1f; return;
                }
                Check("a general upgrade lights the arrow on both bars (player 2's round)", Lit(s1) && Lit(s2), "P1 slot lit=" + Lit(s1) + " P2 slot lit=" + Lit(s2));
                s1.ToggleUpgradeIndicator(false); TestShared = false; LevelRounds.TestForceP2 = false;
                Lc.Line("bonus: SUMMARY ok=" + _ok + " fail=" + _bad + (Lc.Has("-lcnoslotredirect") || Lc.Has("-lcnoarrowshared") || Lc.Has("-lcnostalearrow") ? " (twin: FAIL expected)" : ""));                _arrowStage = 6;
            }
            catch (Exception e) { Lc.LineOnce("bonus: slotarrow test threw " + e.GetType().Name + ": " + e.Message); _arrowStage = 6; }
        }

        private static float _watchAt;

        /// <summary>`-lcpickupwatch`: every 4 s the loose pickups by name and state, how many lie inside the clone's reach and how many of those sit unclaimed.</summary>
        internal static void PickupWatch()
        {
            if (!Lc.Has("-lcpickupwatch") || Phase1.Second == null || Time.realtimeSinceStartup < _watchAt) return;
            _watchAt = Time.realtimeSinceStartup + 4f;
            try
            {
                var host = Phase1.First(); var clone = Phase1.Second;
                if (host == null || clone == null) return;
                float r2 = clone.sqrPickupRadius, r1 = host.sqrPickupRadius;
                var groups = new System.Collections.Generic.Dictionary<string, int[]>();
                foreach (var pk in UnityEngine.Object.FindObjectsOfType<Pickup>())
                {
                    if (pk == null || !pk.gameObject.activeInHierarchy) continue;
                    string key = pk.type + "/" + pk.gameObject.name.Replace("(Clone)", "") + "/" + pk.state + "/own=" + (pk.player != null ? (Share.IsClone(pk.player) ? "P2" : "P1") : "-");
                    int[] g; if (!groups.TryGetValue(key, out g)) { g = new int[3]; groups[key] = g; }
                    g[0]++;
                    float d2c = (pk.transform.position - clone.transform.position).sqrMagnitude, d2h = (pk.transform.position - host.transform.position).sqrMagnitude;
                    if (d2c <= r2) g[1]++;
                    if (d2h <= r1) g[2]++;
                }
                var sb = new StringBuilder("pickupwatch: r1=" + r1.ToString("F1") + " r2=" + r2.ToString("F1") + " spawner host/clone=" + (host.pickupSpawner != null) + "/" + (clone.pickupSpawner != null) + " |");
                foreach (var kv in groups) sb.Append(" ").Append(kv.Key).Append(" n=").Append(kv.Value[0]).Append(" inP2=").Append(kv.Value[1]).Append(" inP1=").Append(kv.Value[2]).Append(" ;");
                Lc.Line(sb.ToString());
            }
            catch (Exception e) { Lc.LineOnce("pickupwatch threw " + e.GetType().Name + ": " + e.Message); }
        }

        private static bool _probed, _swept;

        private static double[] Stats(Player p)
        {
            var l = new System.Collections.Generic.List<double>();
            void Add(Func<double> f) { try { l.Add(f()); } catch { l.Add(double.NaN); } }
            Add(() => p._maxHp_k__BackingField.Value); Add(() => p._moveSpeed_k__BackingField.Value); Add(() => p._lifeRegen_k__BackingField.Value);
            Add(() => p._dodge_k__BackingField.Value); Add(() => p._armor_k__BackingField.Value); Add(() => p._miningSpeed_k__BackingField.Value);
            Add(() => p._xpGain_k__BackingField.Value); Add(() => p._luck_k__BackingField.Value); Add(() => p._pickupRadius_k__BackingField.Value);
            Add(() => p._critChance_k__BackingField.Value); Add(() => p._critDamage_k__BackingField.Value);
            return l.ToArray();
        }
        private static int Changed(double[] a, double[] b) { int n = 0; for (int i = 0; i < a.Length; i++) if (!double.IsNaN(a[i]) && !double.IsNaN(b[i]) && Math.Abs(a[i] - b[i]) > 1e-6) n++; return n; }
        private static int Handlers(Player p) { try { return p.GetComponentsInChildren<AbstractArtifactTrigger>(true).Length; } catch { return -1; } }

        private static bool _trigDone;
        private static readonly string[] StatNames = { "maxHp", "moveSpeed", "lifeRegen", "dodge", "armor", "miningSpeed", "xpGain", "luck", "pickupRadius", "critChance", "critDamage" };
        private static string DeltaText(double[] a, double[] b, long hp0, long hp1)
        {
            var s = ""; for (int i = 0; i < a.Length; i++) if (!double.IsNaN(a[i]) && Math.Abs(a[i] - b[i]) > 1e-6) s += StatNames[i] + " " + a[i].ToString("F2") + "->" + b[i].ToString("F2") + " ";
            if (hp0 != hp1) s += "hp " + hp0 + "->" + hp1 + " ";
            return s.Length > 0 ? s.Trim() : "no change";
        }

        /// <summary>One trigger artifact: hand its handlers an event that names the host, then one that names the clone, and look at who reacts. A handler that filters on the
        /// dwarf reacts only to its own. Both react = the filter is missing (the other dwarf's event also pays for this one); only the host's = the clone never gets it.
        /// The event goes straight into the handler's method (not through the game's static event), so nothing else in the run reacts.</summary>
        private static void TrigMatrix<THandler>(Player host, Player clone, string label, string dataName, Func<Player, Action<THandler>> make, bool argsNameDwarf, Action<Player> prepare = null) where THandler : UnityEngine.Component
        {
            try
            {
                ArtifactSkillData data = null;
                foreach (var a in Resources.FindObjectsOfTypeAll<ArtifactSkillData>()) if (a != null && a.name == dataName) { data = a; break; }
                if (data == null) { Check("trig " + label + ": data exists", false, dataName + " not loaded"); return; }
                host.EquipArtifact(data, false);
                var hs = UnityEngine.Object.FindObjectsOfType<THandler>();
                var own = new System.Collections.Generic.Dictionary<THandler, Player>();
                foreach (var hd in hs) own[hd] = hd.transform.IsChildOf(clone.transform) ? clone : hd.transform.IsChildOf(host.transform) ? host : null;
                int nh = 0, nc = 0; foreach (var kv in own) { if (kv.Value == host) nh++; else if (kv.Value == clone) nc++; }
                Check("trig " + label + ": one handler on each dwarf", nh == 1 && nc == 1, "handlers host/clone=" + nh + "/" + nc);
                if (nh != 1 || nc != 1) return;
                foreach (var who in new[] { host, clone })
                {
                    var react = new bool[2]; var lines = "";
                    int k = 0;
                    foreach (var kv in own)
                    {
                        var o = kv.Value; if (o == null) continue;
                        prepare?.Invoke(o);
                        var s0 = Stats(o); long h0 = 0; try { h0 = o.currentHp; } catch { }
                        try { make(who)(kv.Key); } catch (Exception e) { lines += " [threw " + e.GetType().Name + "]"; }
                        var s1 = Stats(o); long h1 = 0; try { h1 = o.currentHp; } catch { }
                        bool ch = Changed(s0, s1) > 0 || h0 != h1;
                        lines += " | handler of " + Who(o) + ": " + DeltaText(s0, s1, h0, h1);
                        if (o.Pointer == host.Pointer) react[0] = ch; else react[1] = ch;
                        k++;
                    }
                    bool wantHost = who.Pointer == host.Pointer, wantClone = !wantHost;
                    Lc.Line("bonus: trig " + label + " event names " + Who(who) + ":" + lines);
                    if (argsNameDwarf) Check("trig " + label + ": an event of " + Who(who) + " is for " + Who(who) + "'s handler only", react[0] == wantHost && react[1] == wantClone, "host reacts=" + react[0] + " clone reacts=" + react[1]);
                    else Lc.Line("bonus: trig " + label + " (the event carries no dwarf) host reacts=" + react[0] + " clone reacts=" + react[1]);
                }
            }
            catch (Exception e) { Check("trig " + label, false, e.GetType().Name + ": " + e.Message); }
        }

        /// <summary>`-lcarttrig`: per trigger artifact, who reacts to an event of which dwarf (see TrigMatrix). Twin `-lcnoartmirror`.</summary>
        internal static void Trig(Player host, Player clone)
        {
            if (_trigDone || host == null || clone == null) return;
            _trigDone = true; _ok = 0; _bad = 0;
            Action<Player> hurt = p => { try { p.currentHp = (long)(Hp(p) * 0.5); } catch { } };
            // level-up is shared (the game raises the event once, for the host): both copies reacting is right, so these two only report
            TrigMatrix<HealOnLevelUpArtifact>(host, clone, "HealOnLevelUp", "HealOnLevelUpArtifactData", who => h => h.OnLevelGain(new PlayerLevelGainArgs(who, 2)), false, hurt);
            TrigMatrix<ScaleStatWithMissingHpArtifact>(host, clone, "MissingHp(armor)", "ScaleStatWithMissingHpArtifactData_Armor", who => h => { long m = (long)Hp(who); who.currentHp = m / 2; h.OnPlayerDamageTaken(new PlayerDamageTakenArgs(1L, who, m / 2, m, default(EDamageSource))); }, false, hurt);
            // damage taken: only the dwarf it happened to. Twin `-lcnoartowner`
            TrigMatrix<TimedBuffOnDamageTakenArtifact>(host, clone, "TimedBuffOnDamage(dodge)", "TimedBuffOnDamageTakenArtifactData_Dodge", who => h => h.OnDamageTaken(new PlayerDamageTakenArgs(1L, who, (long)Hp(who), (long)Hp(who), default(EDamageSource))), true);
            // reroll: only the dwarf who is choosing
            TrigMatrix<LuckRerollArtifact>(host, clone, "LuckReroll", "LuckRerollArtifactData", who => h => { Chooser.Who = who.Pointer == host.Pointer ? 1 : 2; try { h.OnPreReroll(new PreRerollArgs(false, default(ECurrency))); } finally { Chooser.Who = 0; } }, true);
            // per-level artifact: the host levels up twice; both dwarves' copies must have applied the same number of levels (max HP -3 per level, see the data)
            try
            {
                ArtifactSkillData lvl = null; foreach (var a in Resources.FindObjectsOfTypeAll<ArtifactSkillData>()) if (a != null && a.name == "StatModPrPlayerLevelArtifactData") { lvl = a; break; }
                if (lvl == null) Check("trig PerLevel: data exists", false, "not loaded");
                else
                {
                    clone.currentLevel = host.currentLevel;
                    host.EquipArtifact(lvl, false);
                    var hs = UnityEngine.Object.FindObjectsOfType<StatModPrPlayerLevelArtifact>();
                    double h0 = Hp(host), c0 = Hp(clone);
                    for (int k = 0; k < 2; k++) { host.currentLevel = host.currentLevel + 1; var args = new PlayerLevelGainArgs(host, host.currentLevel); foreach (var hd in hs) { try { hd.OnPlayerLevelUpStatUpdate(args); } catch (Exception e) { Lc.Line("bonus: level handler threw " + e.GetType().Name); } } }
                    double dh = Hp(host) - h0, dc = Hp(clone) - c0;
                    Lc.Line("bonus: trig PerLevel after two host levels: max HP change host " + dh.ToString("F1") + " clone " + dc.ToString("F1") + " (clone level " + clone.currentLevel + ", host level " + host.currentLevel + ")");
                    Check("trig PerLevel: the copy of player 2 has applied as many levels as the host's", Math.Abs(dh) > 0.01 && dc * dh > 0 && dc / dh > 0.7 && dc / dh < 1.3, "host " + dh.ToString("F1") + " (" + (100 * dh / h0).ToString("F2") + " %) clone " + dc.ToString("F1") + " (" + (100 * dc / c0).ToString("F2") + " %); the two dwarves have different base max HP, so the amounts differ and the percentage is compared");
                }
            }
            catch (Exception e) { Check("trig PerLevel", false, e.GetType().Name + ": " + e.Message); }
            // wallet artifact (crit chance per nitra): 100 nitra for both, then player 2 spends 60 of its own: only player 2's crit chance may fall
            try
            {
                ArtifactSkillData wa = null; foreach (var a in Resources.FindObjectsOfTypeAll<ArtifactSkillData>()) if (a != null && a.name == "ScaleStatWithWalletArtifactData_NitraToCritChance") { wa = a; break; }
                var hw = Wallets.Host(); var w2 = Wallets.Ensure();
                if (wa == null || hw == null || w2 == null) Check("trig Wallet: setup", false, "artifact=" + (wa != null) + " hostWallet=" + (hw != null) + " w2=" + (w2 != null));
                else
                {
                    var hb = Stats(host)[9]; var cb = Stats(clone)[9];
                    host.EquipArtifact(wa, false);
                    hw.Add(ECurrency.NITRA, 100, false);                  // the plugin credits the second wallet as well
                    double h1 = Stats(host)[9] - hb, c1 = Stats(clone)[9] - cb;
                    w2.Spend(ECurrency.NITRA, 60);
                    double h2 = Stats(host)[9] - hb, c2 = Stats(clone)[9] - cb;
                    Lc.Line("bonus: trig Wallet nitra host/P2 = " + hw.Nitra + "/" + w2.Nitra + " crit chance gain: after +100 host " + h1.ToString("F3") + " clone " + c1.ToString("F3") + " | after P2 spent 60: host " + h2.ToString("F3") + " clone " + c2.ToString("F3") + " (fixes " + ArtifactOwner.WalletFixes + ")");
                    Check("trig Wallet: after player 2 spent, only player 2's bonus fell (the host's stayed, the clone's is 40/100 of it)", h1 > 0.0001 && Math.Abs(h2 - h1) < 1e-4 && Math.Abs(c2 - 0.4 * h2) < 0.02 * Math.Max(1.0, h2 * 100), "host " + h1.ToString("F3") + "->" + h2.ToString("F3") + " clone " + c1.ToString("F3") + "->" + c2.ToString("F3"));
                }
            }
            catch (Exception e) { Check("trig Wallet", false, e.GetType().Name + ": " + e.Message); }
            Lc.Line("bonus: TRIG ok=" + _ok + " fail=" + _bad + (Lc.Has("-lcnoartmirror") ? " (twin: mirror off, FAIL expected)" : ""));
        }

        /// <summary>`-lcartsweep`: every artifact the game has, equipped once on the host. Each one must end up on both dwarves (stacks), a trigger artifact must get its own
        /// handler on each dwarf, and an artifact with stat mods must change a stat on each dwarf. Twin `-lcnoartmirror` (the mirror off: the clone gets nothing, FAIL expected).
        /// What each trigger does when its event fires is a second step (per class), not covered here.</summary>
        internal static void Sweep(Player host, Player clone)
        {
            if (_swept || host == null || clone == null) return;
            _swept = true; _ok = 0; _bad = 0;
            try
            {
                var all = Resources.FindObjectsOfTypeAll<ArtifactSkillData>();
                int n = 0;
                foreach (var a in all)
                {
                    if (a == null) continue;
                    string nm = a.name;
                    // class artifacts are the dwarf's own starter (not mirrored on purpose); the others change the run's state on equip (a weapon, a swarm) or need their own event
                    if (nm.StartsWith("ClassArtifactData") || nm.StartsWith("BookOfExperience") || nm.StartsWith("WeaponBox") || nm.StartsWith("SpawnHuuli")) { Lc.Line("bonus: sweep skip " + nm); continue; }
                    n++;
                    var hs0 = Stats(host); var cs0 = Stats(clone); int hh0 = Handlers(host), ch0 = Handlers(clone);
                    try { host.EquipArtifact(a, false); } catch (Exception e) { Check("sweep " + nm + ": equip", false, e.GetType().Name + ": " + e.Message); continue; }
                    int hst = host.GetArtifactStacks(a), cst = clone.GetArtifactStacks(a);
                    var hs1 = Stats(host); var cs1 = Stats(clone); int hh1 = Handlers(host), ch1 = Handlers(clone);
                    bool trig = a is TriggerArtifactData, mods = false; try { mods = a.statMods != null && a.statMods.Length > 0; } catch { }
                    int hc = Changed(hs0, hs1), cc = Changed(cs0, cs1);
                    bool ok = hst >= 1 && cst == hst;
                    string why = "stacks host/clone=" + hst + "/" + cst;
                    if (trig) { ok &= (hh1 - hh0) >= 1 && (ch1 - ch0) >= 1; why += " handlers +" + (hh1 - hh0) + "/+" + (ch1 - ch0); }
                    if (mods) { ok &= hc >= 1 && cc >= 1; why += " statsChanged host/clone=" + hc + "/" + cc; }
                    Check("sweep " + nm + " (" + (trig ? "trigger" : "stat") + ")", ok, why);
                }
                Lc.Line("bonus: SWEEP artifacts=" + n + " ok=" + _ok + " fail=" + _bad + (Lc.Has("-lcnoartmirror") ? " (twin: mirror off, FAIL expected)" : ""));
            }
            catch (Exception e) { Lc.Line("bonus: sweep threw " + e.GetType().Name + ": " + e.Message); }
        }

        /// <summary>Read-only look at the open questions (twin of nothing: it changes no game state apart from the one artifact it equips on the host).</summary>
        internal static void Probe(Player host, Player clone)
        {
            if (_probed || host == null || clone == null) return;
            _probed = true;
            try
            {
                Lc.Line("bonus: " + Reach(host));
                Lc.Line("bonus: " + Reach(clone));
                Lc.Line("bonus: " + Arts(host));
                Lc.Line("bonus: " + Arts(clone));
                Lc.Line("bonus: " + MaxHp(host) + " | " + MaxHp(clone));

                var all = Resources.FindObjectsOfTypeAll<ArtifactSkillData>();
                Lc.Line("bonus: ArtifactSkillData assets=" + all.Length);
                ArtifactSkillData sugar = null, plain = null;
                foreach (var a in all)
                {
                    if (a == null) continue;
                    string title = ""; try { title = a.Title; } catch { }
                    int mods = 0; try { mods = a.statMods != null ? a.statMods.Length : 0; } catch { }
                    int stacks = 0; try { stacks = a.MaxStacks; } catch { }
                    string desc = ""; try { desc = a.Description; } catch { }
                    if (desc.Length > 90) desc = desc.Substring(0, 90);
                    Lc.Line("bonus: art " + a.name + " | " + title + " | stacks=" + stacks + " mods=" + mods + " | " + desc.Replace('\n', ' '));
                    if (sugar == null && (a.name.IndexOf("sugar", StringComparison.OrdinalIgnoreCase) >= 0 || title.IndexOf("sugar", StringComparison.OrdinalIgnoreCase) >= 0)) sugar = a;
                    if (plain == null && mods > 0 && a.name.IndexOf("health", StringComparison.OrdinalIgnoreCase) >= 0) plain = a;
                }
                Lc.Line("bonus: sugar=" + (sugar != null ? sugar.name : "none") + " plain(health)=" + (plain != null ? plain.name : "none"));

                // check 1: a plain stat artifact the host equips is on the clone too, with the same effect
                ArtifactSkillData beef = null;
                foreach (var a in all) if (a != null && a.name == "ArtifactSkill_BeefyBoy") beef = a;
                if (beef != null)
                {
                    double h0 = Hp(host), c0 = Hp(clone);
                    host.EquipArtifact(beef, false);
                    double h1 = Hp(host), c1 = Hp(clone);
                    Check("a stat artifact of the host is equipped on the clone", clone.GetArtifactStacks(beef) == host.GetArtifactStacks(beef) && host.GetArtifactStacks(beef) == 1,
                        "stacks host/clone=" + host.GetArtifactStacks(beef) + "/" + clone.GetArtifactStacks(beef));
                    Check("the stat artifact raises the clone's max HP (the two dwarves start from different bases, so the amounts differ)", (h1 > h0 + 0.01) && (c1 > c0 + 0.01),
                        "maxHp host " + h0.ToString("F1") + "->" + h1.ToString("F1") + " clone " + c0.ToString("F1") + "->" + c1.ToString("F1"));
                }
                else Check("BeefyBoy artifact exists", false, "not found");

                // check 2: Red Sugar. Equip on the host (mirrored), a pickup by the clone raises BOTH dwarves' max HP
                if (sugar != null)
                {
                    host.EquipArtifact(sugar, false);
                    Check("the Red Sugar artifact is on both dwarves", host.GetArtifactStacks(sugar) == 1 && clone.GetArtifactStacks(sugar) == 1, "stacks host/clone=" + host.GetArtifactStacks(sugar) + "/" + clone.GetArtifactStacks(sugar));
                    double h0 = Hp(host), c0 = Hp(clone);
                    CoreGameEvents.TriggerPlayerRedSugarPickup(new PlayerRedSugarPickupArgs(clone));
                    double h1 = Hp(host), c1 = Hp(clone);
                    Check("a red sugar pickup by P2 raises P2's max HP", c1 > c0 + 0.01, "clone " + c0.ToString("F1") + "->" + c1.ToString("F1") + " | host " + h0.ToString("F1") + "->" + h1.ToString("F1"));
                    h0 = Hp(host); c0 = Hp(clone);
                    CoreGameEvents.TriggerPlayerRedSugarPickup(new PlayerRedSugarPickupArgs(host));
                    h1 = Hp(host); c1 = Hp(clone);
                    Check("a red sugar pickup by P1 raises P2's max HP too", c1 > c0 + 0.01 && h1 > h0 + 0.01, "host " + h0.ToString("F1") + "->" + h1.ToString("F1") + " | clone " + c0.ToString("F1") + "->" + c1.ToString("F1"));
                }
                else Check("Red Sugar artifact exists", false, "not found");
                // check 3: the book of experience applied for the clone does nothing (its levels come once, from the host)
                try
                {
                    var books = Resources.FindObjectsOfTypeAll<BookOfExperienceArtifactData>();
                    if (books != null && books.Length > 0) { int b0 = BookSkips; try { books[0].Apply(clone, null); } catch { } Check("Ancient Knowledge applied for player 2 is skipped (its levels come once)", BookSkips == b0 + 1, "skips " + b0 + " -> " + BookSkips); }
                    else Check("Ancient Knowledge data found", false, "none loaded");
                }
                catch (Exception e) { Check("Ancient Knowledge check", false, e.GetType().Name); }
                // check 4 (hand test: the "max HP per level" artifact worked for player 1 only): equip it on the host (mirrored), then hand every handler the host's level-up
                // and log what each dwarf's max HP does. First question: does the handler of the clone react to an event that names the host?
                try
                {
                    StatModPrPlayerLevelArtifactData lvl = null;
                    foreach (var d in Resources.FindObjectsOfTypeAll<StatModPrPlayerLevelArtifactData>()) if (d != null) { lvl = d; break; }
                    if (lvl == null) Check("a per-level stat artifact exists", false, "none loaded");
                    else
                    {
                        host.EquipArtifact(lvl, false);
                        var hs = UnityEngine.Object.FindObjectsOfType<StatModPrPlayerLevelArtifact>();
                        Check("the per-level artifact has a handler on each dwarf", hs.Length == 2, "handlers=" + hs.Length + " stacks host/clone=" + host.GetArtifactStacks(lvl) + "/" + clone.GetArtifactStacks(lvl));
                        try
                        {
                            var sb = new System.Text.StringBuilder("bonus: level artifact " + lvl.name + " mods per level:");
                            var pm = lvl.StatModsPrPlayerLevel; if (pm != null) foreach (var m in pm) if (m != null) sb.Append(' ').Append(m.StatType).Append('=').Append(m.value.ToString("F3"));
                            string ds = ""; try { ds = System.Text.RegularExpressions.Regex.Replace(lvl.Description, "<[^>]*>", ""); } catch { }
                            Lc.Line(sb + " | " + ds.Replace('\n', ' '));
                        }
                        catch (Exception e) { Lc.Line("bonus: level artifact mods threw " + e.GetType().Name); }
                        // who owns which handler, then three cases: (a) the event the game fires (names the host) to every handler, (b) the clone's handler given an event that names the clone
                        Player OwnerOf(StatModPrPlayerLevelArtifact x) { return x.transform.IsChildOf(clone.transform) ? clone : x.transform.IsChildOf(host.transform) ? host : null; }
                        string Delta(double[] a, double[] b) { var s = ""; string[] nmz = { "maxHp", "moveSpeed", "lifeRegen", "dodge", "armor", "miningSpeed", "xpGain", "luck", "pickupRadius", "critChance", "critDamage" }; for (int i = 0; i < a.Length; i++) if (!double.IsNaN(a[i]) && Math.Abs(a[i] - b[i]) > 1e-6) s += nmz[i] + " " + a[i].ToString("F2") + "->" + b[i].ToString("F2") + " "; return s.Length > 0 ? s : "no change"; }
                        int lv = 2;
                        foreach (var hd in hs)
                        {
                            var own = OwnerOf(hd); if (own == null) { Lc.Line("bonus: level handler of unknown owner"); continue; }
                            var s0 = Stats(own); try { hd.OnPlayerLevelUpStatUpdate(new PlayerLevelGainArgs(host, lv)); } catch (Exception e) { Lc.Line("bonus: level handler threw " + e.GetType().Name); }
                            var s1 = Stats(own);
                            Lc.Line("bonus: level event naming the HOST reaches the handler of " + Who(own) + ": " + Delta(s0, s1));
                            if (own.Pointer == clone.Pointer) Check("the per-level artifact on player 2 reacts to the level event the game fires (it names the host)", Changed(s0, s1) > 0, Delta(s0, s1));
                        }
                        foreach (var hd in hs)
                        {
                            var own = OwnerOf(hd); if (own == null || own.Pointer != clone.Pointer) continue;
                            var s0 = Stats(own); try { hd.OnPlayerLevelUpStatUpdate(new PlayerLevelGainArgs(clone, lv + 1)); } catch (Exception e) { Lc.Line("bonus: level handler threw " + e.GetType().Name); }
                            var s1 = Stats(own);
                            Lc.Line("bonus: level event naming the CLONE reaches its own handler: " + Delta(s0, s1));
                        }
                    }
                }
                catch (Exception e) { Check("per-level artifact check", false, e.GetType().Name + ": " + e.Message); }
                Lc.Line("bonus: SUMMARY ok=" + _ok + " fail=" + _bad + (Lc.Has("-lcnoartmirror") || Lc.Has("-lcbookbug") ? " (twin: mirror off, FAIL expected)" : ""));
            }
            catch (Exception e) { Lc.Line("bonus: probe threw " + e.GetType().Name + ": " + e.Message); }
        }
    }
}
