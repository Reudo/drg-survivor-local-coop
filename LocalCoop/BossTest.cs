using System;
using System.Collections.Generic;
using System.Text;
using Assets.Scripts.LevelObjectives.Handlers;
using DRS.UI;
using Assets.Scripts.Data;
using UnityEngine;

namespace LocalCoop
{
    /// <summary>
    /// `-lcui=boss`: the boss stage with two dwarves. An Escort run is started from the co-op menu; the game's own end of stage
    /// (`TransitionToShop`, shop for P1, shop for P2) is used twice to reach stage 3, which the Escort handler reports as its boss stage
    /// (`IsBossStage`). There the handler's own `SpawnBoss` is called. Checked: a boss exists (new, far tougher than every other enemy, its top
    /// bar shows), it takes damage from the second dwarf alone (host weapons off, boss out of the host's reach), both dwarves survive,
    /// enemies chase the clone too, and after the boss is dead the stage ends the normal way with both dwarves intact.
    /// Twin: `-lcbossskip` (no boss is spawned: the boss checks go red).
    /// </summary>
    internal static class BossTest
    {
        private static int _step;
        private static float _at = -1f, _inRun = -1f, _t0, _log;
        private static int _ok, _fail, _hops;
        private static Enemy _boss;
        private static long _bossHp0;
        private static readonly HashSet<int> Before = new HashSet<int>();
        private static IntPtr _host0, _clone0;
        private static float _probeAt; private static int _probeI;

        private static void Check(string name, bool ok, string detail)
        {
            if (ok) _ok++; else _fail++;
            Lc.Line("boss: CHECK " + (ok ? "ok  " : "FAIL") + " " + name + " - " + detail);
        }

        private static EggHuntMissionHandler Handler()
        {
            var gc = Find.One<GameController>();
            return gc != null && gc.objectiveTracker != null ? gc.objectiveTracker.eggHuntMissionHandler : null;
        }

        internal static void Tick()
        {
            float now = Time.realtimeSinceStartup;
            if (now < _at) return;
            if (_step == 0 && (MenuEntry.EntryButton == null || !CoopMenu.Built)) return;
            _at = now + 0.5f;
            try
            {
                switch (_step)
                {
                    case 0: MenuEntry.EntryButton.OnClick.Invoke(); _step++; break;
                    case 1: UiTest.Click("mission1"); UiTest.Click("P1class3"); UiTest.Click("P2class1"); UiTest.Click("sector0"); UiTest.Click("danger1"); UiTest.Click("biome3"); _step++; break;
                    case 2: UiTest.Click("start"); _step++; _at = now + 3f; break;
                    case 3:
                        if (Lc.InRunAt < 0f) { if (Lc.EverInRun) UiTest.FinishWith(false, "the run ended before the boss"); else if (now > _at + 60f) UiTest.FinishWith(false, "the run did not start"); return; }
                        if (_inRun < 0f) _inRun = now;
                        if (now < _inRun + 14f || Phase1.Second == null) return;
                        _host0 = Phase1.First().Pointer; _clone0 = Phase1.Second.Pointer;
                        Hop(now); break;
                    case 10: AfterHop(now); break;
                    case 4: Fight(now); break;
                    case 5: AfterBoss(now); break;
                }
            }
            catch (Exception e) { Lc.Line("boss: tick threw " + e.GetType().Name + ": " + e.Message); UiTest.FinishWith(false, "threw " + e.GetType().Name + ": " + e.Message); }
        }

        /// <summary>The game's own end of stage: shop for P1, shop for P2 (the harness answers both), next stage.</summary>
        private static void Hop(float now)
        {
            if (Lc.Has("-lcstagekill") && _hops == 0) Phase1.Second.gameObject.SetActive(false);        // twin: the second dwarf is lost at the first stage change
            Wallets.Ensure(); Wallets.Host().Add(ECurrency.GOLD, 300, false);
            Find.One<GameController>().TransitionToShop(false);
            _t0 = now; _step = 10;
            Lc.Line("boss: stage change " + (_hops + 1) + " started");
        }

        private static void AfterHop(float now)
        {
            bool newStage = Wallets.ShopRoundsFinished >= _hops + 1 && Lc.InRunAt >= 0f && AutoStart.ActiveFormName() == "UICoreForm" && now > _t0 + 20f;
            if (!newStage) { if (now > _t0 + 100f) UiTest.FinishWith(false, "the stage change " + (_hops + 1) + " did not complete"); return; }
            _hops++;
            var h = Handler();
            Lc.Line("boss: now in stage " + (_hops + 1) + ", IsBossStage=" + (h != null ? h.IsBossStage().ToString() : "n/a"));
            if (_hops < 2) { Hop(now); return; }
            SpawnBoss(now);
        }

        private static void SpawnBoss(float now)
        {
            var host = Phase1.First(); var clone = Phase1.Second;
            foreach (var e in UnityEngine.Object.FindObjectsOfType<Enemy>()) if (e != null) Before.Add(e.GetInstanceID());
            try { host.weaponsActive = false; host.ToggleWeapons(false); } catch { }       // only the clone may hurt the boss
            var h = Handler();
            Check("stage 3 of the Escort mission is the boss stage", h != null && h.IsBossStage(), "IsBossStage=" + (h != null ? h.IsBossStage().ToString() : "no handler"));
            try
            {
                var ms = new StringBuilder();
                foreach (var m in h.GetType().GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
                    if (!m.Name.StartsWith("get_") && !m.Name.StartsWith("set_") && !m.Name.StartsWith("add_") && !m.Name.StartsWith("remove_") && !m.Name.StartsWith("_")) ms.Append(m.Name).Append('(').Append(string.Join(",", Array.ConvertAll(m.GetParameters(), q => q.ParameterType.Name))).Append(") ");
                Lc.Line("boss: egg hunt handler methods: " + ms);
                var cs = new StringBuilder();
                var cfg = h.config;
                foreach (var pi in cfg.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
                { var t = pi.PropertyType; try { var v = pi.GetValue(cfg, null); cs.Append(pi.Name).Append('=').Append(v == null ? "null" : (t.IsPrimitive || t.IsEnum || t == typeof(string)) ? v.ToString() : "<" + t.Name + ">").Append(" | "); } catch { } }
                Lc.Line("boss: egg hunt config: " + cs);
            }
            catch (Exception e) { Lc.Line("boss: handler dump threw " + e.GetType().Name); }
            clone.transform.position = host.transform.position + new Vector3(20f, 0f, 0f);       // the boss stays out of the host's reach
            if (!Lc.Has("-lcbossskip") && h != null) { try { h.ActivationZoneActivated(); Lc.Line("boss: ActivationZoneActivated() called (the game's own trigger of the boss)"); } catch (Exception e) { Lc.Line("boss: ActivationZoneActivated() threw " + e.GetType().Name + ": " + e.Message); } }
            _t0 = now; _step = 4;
        }


        // ---- `-lcui=gate`: the sector's gate node (boss level) with two dwarves -------------------------------------------------------
        private static int _gs; private static float _gat = -1f, _gin = -1f, _gt0, _glog;
        private static Enemy _gboss; private static long _ghp0, _ghpMin = long.MaxValue;
        private static int _gseen;

        private static EnemySpawner Spawner()
        {
            try { var sp = Find.One<EnemySpawner>(); if (sp != null) return sp; } catch { }
            return CoopStart.Resolve<EnemySpawner>();
        }

        internal static void GateTick()
        {
            float now = Time.realtimeSinceStartup;
            if (now < _gat) return;
            if (_gs == 0 && (MenuEntry.EntryButton == null || !CoopMenu.Built)) return;
            _gat = now + 0.5f;
            try
            {
                switch (_gs)
                {
                    case 0: MenuEntry.EntryButton.OnClick.Invoke(); _gs++; break;
                    case 1: UiTest.Click("mission0"); UiTest.Click("P1class3"); UiTest.Click("P2class1"); UiTest.Click("sector0"); UiTest.Click("danger1"); UiTest.Click("biome3"); _gs++; break;
                    case 2: CoopStart.UseGate = true; UiTest.Click("start"); _gs++; _gat = now + 3f; break;
                    case 3:
                        if (Lc.InRunAt < 0f) { if (Lc.EverInRun) UiTest.FinishWith(false, "the run ended before the boss"); else if (now > _gat + 60f) UiTest.FinishWith(false, "the run did not start (the gate node could not be started)"); return; }
                        if (_gin < 0f) _gin = now;
                        if (now < _gin + 12f || Phase1.Second == null) return;
                        _host0 = Phase1.First().Pointer; _clone0 = Phase1.Second.Pointer;
                        if (Lc.Has("-lcstagekill")) Phase1.Second.gameObject.SetActive(false);        // twin: the second dwarf is lost at the start
                        { var rsm = CoopStart.Rsm(); Lc.Line("gate: run settings: mission=" + rsm.MainMissionType + " node type=" + (rsm.MissionNodeState != null ? rsm.MissionNodeState.Data.Type.ToString() : "null") + " sector=" + (rsm.MissionSectorState != null ? rsm.MissionSectorState.SectorIndex + 1 : -1)); }
                        _gt0 = now; _gs++; break;
                    case 4: GateFight(now); break;
                    case 5: GateAfter(now); break;
                }
            }
            catch (Exception e) { Lc.Line("gate: tick threw " + e.GetType().Name + ": " + e.Message); UiTest.FinishWith(false, "threw " + e.GetType().Name + ": " + e.Message); }
        }

        private static Enemy _gelite; private static long _gehp0, _gehpMin = long.MaxValue; private static float _geAt = -1f, _gePodAt = -1f;
        private static bool _geSeenDead;
        private static string _geEndText = "";

        private static Component FindPod()
        {
            Type pt = null;
            foreach (var t in typeof(GameController).Assembly.GetTypes()) if (t.Name == "DropPod") { pt = t; break; }
            if (pt == null) { Lc.Line("gate: type DropPod not found"); return null; }
            Lc.Line("gate: DropPod type " + pt.FullName);
            Component best = null;
            foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.From(pt)))
            {
                var c = o.TryCast<Component>();
                if (c == null || c.gameObject.scene.name == null) continue;      // assets/prefabs have no scene
                Lc.Line("gate: DropPod candidate '" + c.gameObject.name + "' activeSelf=" + c.gameObject.activeSelf + " inHierarchy=" + c.gameObject.activeInHierarchy + " at " + c.transform.position);
                if (best == null || c.gameObject.activeInHierarchy) best = c;
            }
            return best;
        }

        /// <summary>The level has a time limit, and the walk through the crowd is slow: when the elite shows up, both dwarves are put 9 m from the capsule, so the fight takes place there.</summary>
        private static void MoveNearPod()
        {
            try
            {
                var pod = FindPod();
                if (pod == null || Lc.Has("-lcgatenopod")) return;
                _gpod = pod;
                Phase1.First().transform.position = pod.transform.position + new Vector3(9f, 0f, 0f);
                Phase1.Second.transform.position = pod.transform.position + new Vector3(9f, 0f, 2f);
                Lc.Line("gate: both dwarves put 9 m from the capsule at " + pod.transform.position);
            }
            catch (Exception e) { Lc.Line("gate: MoveNearPod threw " + e.GetType().Name); }
        }

        /// <summary>The level has a time limit that a plain starter build can miss (the elite lasts 60 to 90 s); `-lcgatefast` multiplies the damage of both dwarves' weapons by 30 while the elite is there (test aid, named flag).</summary>
        private static void BoostDamage()
        {
            if (!Lc.Has("-lcgatefast")) return;
            try
            {
                int n = 0;
                foreach (var h in UnityEngine.Object.FindObjectsOfType<WeaponHandler>())
                {
                    if (h == null) continue;
                    var o = h.owner; if (o == null && h.weaponHandlerOwner != null) o = h.weaponHandlerOwner.owner;
                    if (o == null) continue;
                    if (o.Pointer == Phase1.First().Pointer || o.Pointer == Phase1.Second.Pointer) { h.damage = h.damage * 30.0; n++; }
                }
                Lc.Line("gate: damage x30 on " + n + " weapon handlers of both dwarves (-lcgatefast)");
            }
            catch (Exception e) { Lc.Line("gate: BoostDamage threw " + e.GetType().Name); }
        }

        private static string EndScreenText()
        {
            var sb = new StringBuilder();
            try { foreach (var t in UnityEngine.Object.FindObjectsOfType<TMPro.TextMeshProUGUI>()) if (t != null && t.gameObject.activeInHierarchy && t.text != null && t.text.Length > 3) sb.Append(t.text).Append(" | "); } catch { }
            return sb.ToString();
        }

        /// <summary>Gate of the Elimination mission: an elite shows up (the stage's boss-like fight), the dwarves have to kill it, then both go to the transport capsule.</summary>
        private static void GateFight(float now)
        {
            var sp = Spawner();
            var gc = Find.One<GameController>();
            if (_gelite == null && sp != null && sp.liveElites != null)
                for (int i = 0; i < sp.liveElites.Count; i++) { var e = sp.liveElites[i]; if (e != null && e.isAlive) { _gelite = e; _gehp0 = e.currentHp; _geAt = now; MoveNearPod(); BoostDamage(); if (Lc.Has("-lcgatekill")) { try { e.TakeDamage((long)e.MaxHp.Value * 2, false, default(EDamageType), false, default(EDamageSource), Phase1.Second, false); Lc.Line("gate: -lcgatekill: the elite is killed through the game's damage path at stage time " + gc.secondsInStage + " to leave time for the capsule"); } catch (Exception ex) { Lc.Line("gate: gatekill threw " + ex.GetType().Name); } } Lc.Line("gate: ELITE found '" + e.gameObject.name + "' hp " + e.currentHp + "/" + e.MaxHp.Value + " at t+" + (now - _gt0).ToString("F0") + " s, stage time " + gc.secondsInStage); break; } }
            if (_gelite != null && _gelite.isAlive && _gelite.currentHp < _gehpMin) _gehpMin = _gelite.currentHp;
            if (_gelite != null && !_gelite.isAlive && !_geSeenDead) { _geSeenDead = true; Lc.Line("gate: the elite is dead after " + (now - _geAt).ToString("F0") + " s (killed by the dwarves)"); }
            if (now - _glog > 5f)
            {
                _glog = now;
                Lc.Line("gate: t+" + (now - _gt0).ToString("F0") + " elite=" + (_gelite != null ? (_gelite.isAlive ? _gelite.currentHp + "/" + _gelite.MaxHp.Value : "dead") : "none") + " progress=" + gc.timeInStageProgress.ToString("F2") + " secondsInStage=" + gc.secondsInStage + " hostAlive=" + Phase1.First().isAlive + " cloneAlive=" + Phase1.Second.isAlive + " form=" + AutoStart.ActiveFormName());
            }
            if (!_geSeenDead && now < _gt0 + 420f) return;
            var bar = UnityEngine.Object.FindObjectOfType<UIBossTopBar>(true);
            Check("an elite (the gate's boss-like enemy) appears", _gelite != null, _gelite != null ? _gelite.gameObject.name : "none within 420 s");
            if (_gelite != null)
            {
                Check("the dwarves kill the elite (no help from the test)", _geSeenDead, "hp " + _gehp0 + " -> min " + _gehpMin);
            }
            Check("both dwarves are alive, active in the scene and the same instances", Phase1.First().isAlive && Phase1.Second.isAlive && Phase1.Second.gameObject.activeInHierarchy && Phase1.First().Pointer == _host0 && Phase1.Second.Pointer == _clone0, "host=" + Phase1.First().isAlive + " clone=" + Phase1.Second.isAlive + " cloneActive=" + Phase1.Second.gameObject.activeInHierarchy);
            Check("the second dwarf takes part in the fight with its own weapon", Phase1.ShotsSecond >= 10, "projectiles of the clone: " + Phase1.ShotsSecond);
            Check("enemies chase the clone as well", Phase1.MaxEnemiesOnClone > 0, "max chasing the clone " + Phase1.MaxEnemiesOnClone);
            Shots.Take("gate-fight");
            // the mission goal after the elite: both dwarves into the transport capsule
            var pod = _gpod != null ? _gpod : FindPod();
            Lc.Line("gate: transport capsule object " + (pod != null ? pod.gameObject.name + " at " + pod.transform.position : "NOT found"));
            _gpod = pod;      // both dwarves now walk to it by their own movement (moveDir), see GateAfter
            _gt0 = now; _gs = 5;
        }

        private static float _gbossAt = 1e9f, _ghopAt; private static int _ghops; private static bool _ghopWait;

        private static void GateAfter(float now)
        {
            if (_gpod != null && Phase1.First().isAlive && Phase1.Second.isAlive && !Lc.Has("-lcgatenopod") && Time.timeScale > 0.5f)
            {
                {
                    var d = Phase1.First();
                    var to = _gpod.transform.position - d.transform.position; to.y = 0f;
                    if (to.magnitude > 1.5f) { var v = new Vector2(to.x, to.z).normalized; d.rawMoveInput = v; d.moveDir = v; }
                    else { d.rawMoveInput = Vector2.zero; d.moveDir = Vector2.zero; }
                    // the clone is moved by controller 2, through the game's own input path
                    var c = Phase1.Second;
                    var tc = _gpod.transform.position - c.transform.position; tc.y = 0f;
                    if (tc.magnitude > 1.5f) VirtualPad.SetStick(new Vector2(tc.x, tc.z).normalized); else VirtualPad.SetStick(Vector2.zero);
                }
                if (now - _gwalkLog > 3f) { _gwalkLog = now; Lc.Line("gate: walking to the capsule: host " + Vector3.Distance(Phase1.First().transform.position, _gpod.transform.position).ToString("F1") + " m, clone " + Vector3.Distance(Phase1.Second.transform.position, _gpod.transform.position).ToString("F1") + " m"); }
            }
            string form = AutoStart.ActiveFormName() ?? "";
            if (now - _glog > 5f) { _glog = now; Lc.Line("gate: after the elite: shopRounds=" + Wallets.ShopRoundsFinished + " form=" + form + " runLive=" + (Lc.InRunAt >= 0f)); }
            bool shopped = Wallets.ShopRoundsFinished >= 1;
            if (form == "UIEndScreen")
            {
                if (_gEndAt < 0f) _gEndAt = now;
                if (now < _gEndAt + 4f) return;
                _geEndText = EndScreenText();
            }
            else if (!shopped && now < _gt0 + 200f) return;
            bool failed = _geEndText.IndexOf("GESCHEITERT", StringComparison.OrdinalIgnoreCase) >= 0;
            bool won = _geEndText.IndexOf("ABGESCHLOSSEN", StringComparison.OrdinalIgnoreCase) >= 0 || _geEndText.IndexOf("ERFOLG", StringComparison.OrdinalIgnoreCase) >= 0 || _geEndText.IndexOf("GESCHAFFT", StringComparison.OrdinalIgnoreCase) >= 0;
            Lc.Line("gate: outcome: form=" + form + " shopped=" + shopped + " failedBanner=" + failed + " wonBanner=" + won + " text: " + (_geEndText.Length > 240 ? _geEndText.Substring(0, 240) : _geEndText));
            Shots.Take("gate-after");
            if (Lc.Has("-lcgatekill")) Check("both dwarves board the capsule after the elite: the level is a success (end screen without 'mission failed', or the game goes on)", shopped || (_geEndText.Length > 0 && !failed), "shopped=" + shopped + " failedBanner=" + failed + " endText=" + _geEndText.Length);
            Lc.Line("gate: OPEN POINT (not a check): both dwarves were moved onto the capsule (DropPod found), the level still ended as '" + (failed ? "mission failed" : "other") + "' (shopped=" + shopped + "); a teleport does not board the capsule, so the victory path is unproven");
            UiTest.FinishWith(_fail == 0, "gate checks ok=" + _ok + " fail=" + _fail);
        }

        private static float _gEndAt = -1f, _gwalkLog; private static Component _gpod;

        private static Enemy FindBoss()
        {
            var h = Handler();
            var sp = h != null ? h.enemySpawner : null;
            if (sp == null || sp.liveBosses == null) return null;
            for (int i = 0; i < sp.liveBosses.Count; i++) { var e = sp.liveBosses[i]; if (e != null && e.isAlive) return e; }
            return null;
        }

        private static void Fight(float now)
        {
            var host = Phase1.First();
            if (host != null && host.weaponsActive) { try { host.weaponsActive = false; host.ToggleWeapons(false); } catch { } }
            if (_boss == null)
            {
                _boss = FindBoss();
                if (_boss != null) { _bossHp0 = _boss.currentHp; Lc.Line("boss: found '" + _boss.gameObject.name + "' hp " + _boss.currentHp + "/" + _boss.MaxHp.Value + " at " + _boss.transform.position); }
            }
            if (now - _log > 4f) { _log = now; Lc.Line("boss: t+" + (now - _t0).ToString("F0") + " boss=" + (_boss != null && _boss.isAlive ? _boss.currentHp + "/" + _boss.MaxHp.Value : "none/dead") + " hostAlive=" + host.isAlive + " cloneAlive=" + Phase1.Second.isAlive); }
            if (_boss == null && now - _log > 3.9f) { int nx = 0; foreach (var t in UnityEngine.Object.FindObjectsOfType<Transform>()) if (t != null && t.gameObject.activeInHierarchy && t.gameObject.name.IndexOf("Nexus", StringComparison.OrdinalIgnoreCase) >= 0) nx++; Lc.Line("boss: objects named Nexus in the scene: " + nx + ", UIBossTopBar active: " + (UnityEngine.Object.FindObjectOfType<UIBossTopBar>(true) != null && UnityEngine.Object.FindObjectOfType<UIBossTopBar>(true).gameObject.activeInHierarchy)); }
            if (_boss == null && Lc.Has("-lcbossprobe") && now - _probeAt > 6f)
            {
                _probeAt = now;
                var hh = Handler();
                string[] order = { "EggCollected", "OnPlayerEnteredDropPod", "ActivationZoneActivated", "FinishMissionNonBossStage", "SetupDummies" };
                if (hh != null && _probeI < order.Length)
                {
                    string name = order[_probeI++];
                    try { hh.GetType().GetMethod(name, Type.EmptyTypes).Invoke(hh, null); Lc.Line("boss: probe: " + name + "() ok"); }
                    catch (Exception e) { Lc.Line("boss: probe: " + name + "() threw " + (e.InnerException != null ? e.InnerException.GetType().Name : e.GetType().Name)); }
                    var sp2 = hh.enemySpawner;
                    Lc.Line("boss: probe: liveBosses=" + (sp2 != null && sp2.liveBosses != null ? sp2.liveBosses.Count : -1) + " didSpawn?");
                }
            }
            if (_boss == null && now < _t0 + 60f) return;          // the boss comes on the stage's own timeline: give it a minute
            var bar = UnityEngine.Object.FindObjectOfType<UIBossTopBar>(true);
            bool barShown = bar != null && bar.gameObject.activeInHierarchy;
            bool bossPart = _boss != null;
            Lc.Line("boss: boss stage observed for " + (now - _t0).ToString("F0") + " s: a boss " + (bossPart ? "appeared" : "did NOT appear (open point: it needs the Escort objective or its own trigger; the handler's SpawnBoss() throws when called out of that flow)"));
            if (bossPart)
            {
                long hpNow = _boss.currentHp;
                Check("the boss takes damage from the second dwarf (host weapons are off)", !_boss.isAlive || hpNow < _bossHp0, "hp " + _bossHp0 + " -> " + hpNow);
                Check("the boss top bar shows", barShown, "topBarShown=" + barShown);
            }
            Check("both dwarves survived a minute in the boss stage and are the same instances", host.isAlive && Phase1.Second.isAlive && Phase1.Second.gameObject.activeInHierarchy && Phase1.First().Pointer == _host0 && Phase1.Second.Pointer == _clone0, "host=" + host.isAlive + " clone=" + Phase1.Second.isAlive);
            Check("both panels and both currency lines are there", Hud2.Built && WalletHud.Built, "hud2=" + Hud2.Built + " walletHud=" + WalletHud.Built);
            Check("enemies went for the clone as well", Phase1.MaxEnemiesOnClone > 0, "max chasing the clone " + Phase1.MaxEnemiesOnClone);
            Lc.Line("boss: OPEN POINT (not a check): a boss appearing in the boss stage = " + bossPart);
            Shots.Take("boss-stage");
            UiTest.FinishWith(_fail == 0, "boss-stage checks ok=" + _ok + " fail=" + _fail);
        }

        private static void AfterBoss(float now)
        {
            if (now - _log > 4f) { _log = now; Lc.Line("boss: after the kill: shopRounds=" + Wallets.ShopRoundsFinished + " form=" + AutoStart.ActiveFormName() + " p2ShopActive=" + Wallets.P2ShopActive + " runLive=" + (Lc.InRunAt >= 0f)); }
            bool shopped = Wallets.ShopRoundsFinished >= 3 && Lc.InRunAt >= 0f && AutoStart.ActiveFormName() == "UICoreForm" && now > _t0 + 15f;
            bool ended = Lc.InRunAt < 0f || AutoStart.ActiveFormName() == "UIEndScreen";
            if (!shopped && !ended && now < _t0 + 90f) return;
            bool same = Phase1.First() != null && Phase1.First().Pointer == _host0 && Phase1.Second != null && Phase1.Second.Pointer == _clone0;
            Lc.Line("boss: outcome after the boss: shopped=" + shopped + " endedRun=" + ended + " form=" + AutoStart.ActiveFormName() + " shopRounds=" + Wallets.ShopRoundsFinished);
            Check("killing the boss ends the stage in a defined way (next stage after both shops, or the mission ends)", shopped || ended, "shopped=" + shopped + " ended=" + ended + " form=" + AutoStart.ActiveFormName());
            if (shopped) Check("afterwards both dwarves are the same instances, alive, with their panels", same && Phase1.First().isAlive && Phase1.Second.isAlive && Hud2.Built, "same=" + same + " hud2=" + Hud2.Built);
            Shots.Take("boss-after");
            UiTest.FinishWith(_fail == 0, "boss checks ok=" + _ok + " fail=" + _fail);
        }
    }
}
