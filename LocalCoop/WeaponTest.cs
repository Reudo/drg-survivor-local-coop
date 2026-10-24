using System;
using System.Collections.Generic;
using System.Text;
using Assets.Scripts.Data;
using Assets.Scripts.SkillSystem;
using HarmonyLib;
using UnityEngine;

namespace LocalCoop
{
    /// <summary>
    /// `-lcui=weapons`: the weapon matrix of the second dwarf. A run is started from the co-op menu; then every player weapon of the game
    /// (the starter weapons of all subclasses first, then every other non-enemy WeaponSkillData) is equipped on the clone alone, with the
    /// host's weapons switched off, and must fire and damage enemies within a window. Damage evidence is independent of the weapon's own
    /// bookkeeping: every Enemy.TakeDamage is counted while only the clone can be dealing it.
    /// Flags: `-lcwpmax=N` tests only the first N weapons. Twin: `-lcwpoff` (the clone's weapons are removed again after equipping: every row goes red). `-lcnoweapontick` (no extra tick) stayed green: the game ticks them itself.
    /// </summary>
    internal static class WeaponTest
    {
        private sealed class Row { public string Name; public bool Starter, Equipped, Fired, Damaged, HostSilent = true; public long Hits, Dmg; public int Shots, Near; public float Secs; public string Note = ""; }

        private static int _step;
        private static float _at = -1f, _inRun = -1f, _winStart, _winLen;
        private static List<WeaponSkillData> _list;
        private static HashSet<string> _starters = new HashSet<string>();
        private static readonly List<Row> Rows = new List<Row>();
        private static int _idx;
        private static WeaponHandler _cur;
        private static Row _row;
        private static long _hostDmg0;
        private static int _shots0;
        private static bool _installed;
        internal static long Hits, Dmg;
        internal static int SrcNone, SrcClone, SrcOther, TrackCalls, TrackOther;
        internal static long TrackSum, MineDmg;
        internal static int Mine;
        internal static readonly Dictionary<string, int> BySource = new Dictionary<string, int>();          // Enemy.TakeDamage events while a window runs

        [HarmonyPatch(typeof(Enemy), "TakeDamage")]
        private static class TakeDamagePatch
        {
            private static void Prefix(Enemy __instance, long __0, EDamageSource __4, Entity __5)
            {
                if (!_counting) return;
                Hits++; Dmg += __0;
                string key = __4.ToString(); int cnt; BySource.TryGetValue(key, out cnt); BySource[key] = cnt + 1;
                if (__5 == null) SrcNone++; else if (Phase1.Second != null && __5.Pointer == Phase1.Second.Pointer) SrcClone++; else SrcOther++;
                // attribution by place: the clone stands far from the host, so a victim much nearer to the clone than to the host
                // (and out of the host's reach) was hurt by the clone's weapon, not by the host's
                try
                {
                    var host = Phase1.First(); var clone = Phase1.Second;
                    if (host == null || clone == null || __instance == null) return;
                    var v = __instance.transform.position;
                    float dc = Flat(v - clone.transform.position), dh = Flat(v - host.transform.position);
                    if (dh > 12f && dc < dh - 6f) { Mine++; MineDmg += __0; }
                }
                catch { }
            }

            private static float Flat(Vector3 d) { return Mathf.Sqrt(d.x * d.x + d.z * d.z); }
        }

        [HarmonyPatch(typeof(WeaponHandler), "TrackDamage")]
        private static class TrackPatch
        {
            private static void Prefix(WeaponHandler __instance, long __0)
            {
                if (!_counting || Phase1.Second == null) return;
                try
                {
                    var o = __instance.owner;
                    if (o == null && __instance.weaponHandlerOwner != null) o = __instance.weaponHandlerOwner.owner;
                    if (o != null && o.Pointer == Phase1.Second.Pointer) { TrackCalls++; TrackSum += __0; }
                    else if (o != null) TrackOther++;
                }
                catch { }
            }
        }

        private static readonly Vector3[] Dirs = { new Vector3(20f, 0f, 0f), new Vector3(-20f, 0f, 0f), new Vector3(0f, 0f, 20f), new Vector3(0f, 0f, -20f) };
        private static Vector3 Spot(int i) { return Dirs[((i % 4) + 4) % 4]; }
        private static int _spot;
        private static float _emptySince = -1f;

        private static string Stats(WeaponHandler w)
        {
            try { return "damage=" + w.damage + " attackSpeed=" + w.attackSpeed + " range=" + w.range + " crit=" + w.critChance + "/" + w.critDamage + " pierce=" + w.pierce + " reloadSpeed=" + w.reloadSpeed + " level=" + w.weaponLevel; }
            catch (Exception e) { return "err " + e.GetType().Name; }
        }

        private static string PlayerStats(Player p)
        {
            try
            {
                string hp = p.maxHp != null ? p.maxHp.Value.ToString() : "null";
                var sb = new StringBuilder("maxHp=" + hp);
                foreach (var pi in p.GetType().GetProperties())
                {
                    string n = pi.Name.ToLower();
                    if (!(n.Contains("damage") || n.Contains("crit") || n.Contains("area") || n.Contains("attackspeed") || n.Contains("might") || n.Contains("luck") || n.Contains("cooldown"))) continue;
                    if (pi.GetIndexParameters().Length > 0) continue;
                    try { var v = pi.GetValue(p, null); if (v == null) continue; var vp = v.GetType().GetProperty("Value"); sb.Append(' ').Append(pi.Name).Append('=').Append(vp != null ? vp.GetValue(v, null) : v); } catch { }
                }
                return sb.ToString();
            }
            catch (Exception e) { return "err " + e.GetType().Name; }
        }

        private static long BaseHits, BaseDmg; private static int _bh0, _bhf0; private static readonly HashSet<string> _projTypes = new HashSet<string>();
        private static int _killLogs;

        /// <summary>RemoveEquippedWeapon only takes the handler out of the player's list; its component keeps running. Remove it and destroy the component.</summary>
        private static void Kill(Player owner, WeaponHandler h)
        {
            if (h == null) return;
            string name = "?"; bool own = false;
            try { name = h.gameObject.name; own = h.gameObject == owner.gameObject; } catch { }
            try { owner.RemoveEquippedWeapon(h); } catch { }
            try
            {
                UnityEngine.Object.Destroy(h);          // the component only: the object "WeaponHandlers" is shared by all weapons of the player
                if (_killLogs++ < 4) Lc.Line("weapons: destroyed handler on '" + name + "' (on the player object: " + own + ")");
            }
            catch (Exception e) { Lc.Line("weapons: destroy threw " + e.GetType().Name); }
        }

        private static long HostDamage()
        {
            long t = 0;
            try { var h = Phase1.First(); if (h != null && h.weapons != null) for (int i = 0; i < h.weapons.Count; i++) if (h.weapons[i] != null) t += h.weapons[i].trackedTotalDamage; } catch { }
            return t;
        }

        private static void Silence()
        {
            try
            {
                var h = Phase1.First();
                if (h == null || h.weapons == null) return;
                if (h.weaponsActive) { h.weaponsActive = false; h.ToggleWeapons(false); }
                var gone = new List<WeaponHandler>();
                for (int i = Lc.Has("-lckeephost") ? 1 : 0; i < h.weapons.Count; i++) if (h.weapons[i] != null) gone.Add(h.weapons[i]);   // short single runs have no level-up yet, so the host can go without any weapon (`-lckeephost` keeps its starter for long runs)
                foreach (var w in gone) Kill(h, w);
                if (!Lc.Has("-lckeephost")) KillAllOf(h, null);
                if (!Lc.Has("-lckeephost") && h.boscoWeapons != null)           // Bosco, the host's companion, shoots on its own and far
                {
                    var bw = new List<WeaponHandler>();
                    for (int i = 0; i < h.boscoWeapons.Count; i++) if (h.boscoWeapons[i] != null) bw.Add(h.boscoWeapons[i]);
                    foreach (var w in bw) Kill(h, w);
                }
      // level-ups hand the host further weapons: none may deal damage during a window
            }
            catch { }
        }

        private static int NearEnemies(Player p, float r)
        {
            int n = 0;
            try { foreach (var e in UnityEngine.Object.FindObjectsOfType<Enemy>()) if (e != null && e.isAlive && (e.transform.position - p.transform.position).sqrMagnitude < r * r) n++; } catch { }
            return n;
        }

        internal static void Tick()
        {
            float now = Time.realtimeSinceStartup;
            if (now < _at) return;
            if (_step == 0 && (MenuEntry.EntryButton == null || !CoopMenu.Built)) return;
            _at = now + 0.25f;
            try
            {
                switch (_step)
                {
                    case 0: MenuEntry.EntryButton.OnClick.Invoke(); _step++; _at = now + 0.6f; break;
                    case 1: UiTest.Click("P1class3"); UiTest.Click("P2class" + (Lc.Num("-lcwpclass=") >= 0f && Lc.Has("-lcwpclass=") ? (int)Lc.Num("-lcwpclass=") : 1)); UiTest.Click("sector0"); UiTest.Click("danger1"); UiTest.Click("biome3"); _step++; _at = now + 0.6f; break;
                    case 2: UiTest.Click("start"); _step++; _at = now + 3f; break;
                    case 3:
                        if (Lc.InRunAt < 0f) { if (Lc.EverInRun) UiTest.FinishWith(false, "the run ended before the matrix"); else if (now > _at + 60f) UiTest.FinishWith(false, "the run did not start"); return; }
                        if (_inRun < 0f) _inRun = now;
                        if (now < _inRun + 14f || Phase1.Second == null) return;
                        Setup(); _step++;
                        break;
                    case 4: Next(now); break;
                    case 5: Window(now); break;
                }
            }
            catch (Exception e) { Lc.Line("weapons: tick threw " + e.GetType().Name + ": " + e.Message + " at " + (_row != null ? _row.Name : "-")); UiTest.FinishWith(false, "threw " + e.GetType().Name + ": " + e.Message); }
        }


        // ---- `-lcui=target`: in the shipped play path enemies go for the nearer dwarf (twin `-lcnoretarget`) -----------------------
        private static int _tStep; private static float _tAt = -1f, _tIn = -1f;

        internal static void TargetTick()
        {
            float now = Time.realtimeSinceStartup;
            if (now < _tAt) return;
            if (_tStep == 0 && (MenuEntry.EntryButton == null || !CoopMenu.Built)) return;
            _tAt = now + 0.5f;
            try
            {
                switch (_tStep)
                {
                    case 0: MenuEntry.EntryButton.OnClick.Invoke(); _tStep++; break;
                    case 1: UiTest.Click("P1class3"); UiTest.Click("P2class1"); UiTest.Click("sector0"); UiTest.Click("danger1"); UiTest.Click("biome3"); _tStep++; break;
                    case 2: UiTest.Click("start"); _tStep++; _tAt = now + 3f; break;
                    default:
                        if (Lc.InRunAt < 0f) { if (Lc.EverInRun) UiTest.FinishWith(false, "the run ended early"); else if (now > _tAt + 60f) UiTest.FinishWith(false, "the run did not start"); return; }
                        if (_tIn < 0f) _tIn = now;
                        if (now < _tIn + 6f || Phase1.Second == null) return;
                        // put the clone 20 m from the host, so "nearer" differs between the two
                        if (_tStep == 3) { _tStep++; Phase1.Second.transform.position = Phase1.First().transform.position + new Vector3(20f, 0f, 0f); _tIn = now; return; }
                        if (now < _tIn + 25f) return;
                        int n = Phase1.MaxEnemiesOnClone;
                        Lc.Line("target: most enemies chasing the clone at once: " + n);
                        UiTest.FinishWith(n > 0, "enemies chasing the clone (max at once) = " + n);
                        break;
                }
            }
            catch (Exception e) { UiTest.FinishWith(false, "threw " + e.GetType().Name + ": " + e.Message); }
        }

        private static void Setup()
        {
            if (!_installed) { _installed = true; new Harmony("localcoop.weapons").CreateClassProcessor(typeof(TakeDamagePatch)).Patch(); new Harmony("localcoop.weapons2").CreateClassProcessor(typeof(TrackPatch)).Patch(); /* PatchActivations(); patching ActivateWeapon kills the game, even on the base type only */ }
            var all = new List<WeaponSkillData>();
            foreach (var w in Resources.FindObjectsOfTypeAll<WeaponSkillData>())
            {
                if (w == null || w.name.StartsWith("Enemy") || w.name.StartsWith("Exploder")) continue;
                all.Add(w);
            }
            foreach (var a in Resources.FindObjectsOfTypeAll<ClassArtifactData>()) if (a.StarterWeapon != null) _starters.Add(a.StarterWeapon.name);
            all.Sort((x, y) =>
            {
                int sx = _starters.Contains(x.name) ? 0 : 1, sy = _starters.Contains(y.name) ? 0 : 1;
                return sx != sy ? sx.CompareTo(sy) : string.CompareOrdinal(x.name, y.name);
            });
            string only = Lc.Str("-lcwponly=");
            if (!string.IsNullOrEmpty(only)) { var keep = new HashSet<string>(only.Split(',')); all = all.FindAll(x => keep.Contains(x.name)); }
            int from = (int)Lc.Num("-lcwpfrom=");
            if (from > 0 && from < all.Count) all = all.GetRange(from, all.Count - from);
            int max = (int)Lc.Num("-lcwpmax=");
            if (max > 0 && all.Count > max) all = all.GetRange(0, max);
            all.Insert(0, null);          // row 0: no weapon at all, the clone's own baseline
            _list = all;
            try
            {
                var host = Phase1.First(); var clone = Phase1.Second;
                var to = host.transform.position + Spot(0);
                clone.transform.position = to;
                Lc.Line("weapons: clone moved to " + to + " (host at " + host.transform.position + ")");
            }
            catch (Exception e) { Lc.Line("weapons: moving the clone threw " + e.GetType().Name + ": " + e.Message); }
            try
            {
                var arts = Phase1.Second.equippedArtifacts;
                var names = new StringBuilder();
                var list = new List<ArtifactStateHandler>();
                for (int i = 0; arts != null && i < arts.Count; i++) { var a = arts[i]; if (a == null) continue; list.Add(a); names.Append(a.GetType().Name).Append(' '); }
                Lc.Line("weapons: clone artifacts before: [" + names + "]");
                if (!Lc.Has("-lckeepartifacts"))
                    foreach (var a in list) { try { Phase1.Second.RemoveEquippedArtifact(a); } catch (Exception e) { Lc.Line("weapons: RemoveEquippedArtifact threw " + e.GetType().Name); } }
                Lc.Line("weapons: clone artifacts after: " + (Phase1.Second.equippedArtifacts != null ? Phase1.Second.equippedArtifacts.Count : -1));
            }
            catch (Exception e) { Lc.Line("weapons: artifact listing threw " + e.GetType().Name + ": " + e.Message); }
            try
            {
                var h = Phase1.First();
                for (int i = 0; i < h.weapons.Count; i++) { var w = h.weapons[i]; Lc.Line("weapons: HOST weapon " + w.data.name + " " + Stats(w)); }
                var c0 = Phase1.Second;
                for (int i = 0; i < c0.weapons.Count; i++) { var w = c0.weapons[i]; Lc.Line("weapons: CLONE weapon (its own starter) " + w.data.name + " " + Stats(w)); }
                Lc.Line("weapons: HOST player stats: " + PlayerStats(h) + " | CLONE player stats: " + PlayerStats(c0));
            }
            catch (Exception e) { Lc.Line("weapons: stat dump threw " + e.GetType().Name + ": " + e.Message); }
            // body contact ("bonk", 10 damage every 0.3 s) is not weapon damage: it made the first matrix look fine without any bullet ever hitting
            if (!Lc.Has("-lckeepbonk")) { try { Phase1.Second.bonkDamage = 0; Lc.Line("weapons: clone bonk damage switched off (was counted as weapon damage before)"); } catch (Exception e) { Lc.Line("weapons: bonk off threw " + e.GetType().Name); } }
            Phase1.ForceAll = true;      // test aid: every enemy chases the clone, so the weapon under test always has targets
            Lc.Line("weapons: matrix of " + _list.Count + " weapons (" + _starters.Count + " starters)");
        }

        private static void Next(float now)
        {
            if (_idx >= _list.Count) { Report(); return; }
            var clone = Phase1.Second;
            var data = _list[_idx];
            _row = new Row { Name = data != null ? data.name : "(no weapon)", Starter = data != null && _starters.Contains(data.name) };
            Silence();
            try
            {
                var old = new List<WeaponHandler>();
                for (int i = 0; i < clone.weapons.Count; i++) old.Add(clone.weapons[i]);
                foreach (var h in old) Kill(clone, h);
                _token = data != null ? TokenOf(data.name) : ""; _objs0 = CountNamed(_token);
                _cur = data != null && !Lc.Has("-lcwpnoequip") ? clone.EquipNewWeapon(data, false) : null;
                clone.weaponsActive = true;
                clone.ToggleWeapons(true);
                if (Lc.Has("-lcwpoff") && _cur != null) { Kill(clone, _cur); }      // twin: the equipped weapon is removed again, every row must go red
                _row.Equipped = data == null || Lc.Has("-lcwpnoequip") || (_cur != null && _cur.data != null && _cur.data.name == data.name);
            }
            catch (Exception e) { _row.Note += "equip threw " + e.GetType().Name + ": " + e.Message + "; "; _cur = null; }
            if (!_row.Equipped) { _row.Note += "not equipped; "; Finish(_row); _idx++; return; }
            if (_cur != null) Lc.Line("weapons: CLONE equipped " + _cur.data.name + " " + Stats(_cur));
            _winStart = now; _last = now; _counting = false; _gStart = _g; _extraKilled = 0;
            _step = 5;
        }

        private static float _last, _g, _gStart, _hb;
        private static long _tracked0;
        private static bool _counting;

        private static int _boscoOff;

        /// <summary>Bosco (the host's companion drone) shoots far and on its own; switch its objects off so its shots cannot be mistaken for the clone's.</summary>
        private static void BoscoOff()
        {
            if (Lc.Has("-lckeephost")) return;
            try
            {
                foreach (var t in UnityEngine.Object.FindObjectsOfType<Transform>())
                {
                    if (t == null || !t.gameObject.activeSelf) continue;
                    string n = t.gameObject.name;
                    if (n.IndexOf("Bosco", StringComparison.OrdinalIgnoreCase) < 0 || n.StartsWith("Bosco_") && false) continue;
                    if (t.parent != null && t.parent.gameObject.name.IndexOf("Bosco", StringComparison.OrdinalIgnoreCase) >= 0) continue;     // only the top object
                    t.gameObject.SetActive(false);
                    if (_boscoOff++ < 3) Lc.Line("weapons: switched off '" + n + "'");
                }
            }
            catch { }
        }

        // signals of the weapon handler itself: they cannot come from anything else on the field
        private static float _act0, _relMin, _relMax, _hbAt;
        private static int _targetTicks, _ticks;

        private static void SampleHandler()
        {
            try
            {
                foreach (var pr in UnityEngine.Object.FindObjectsOfType<Projectile>())
                    if (pr != null && !pr.isInPool && Phase1.Second != null && BulletHits.BelongsToClone(pr, Phase1.Second)) _projTypes.Add(pr.gameObject.name + (pr.owner != null && pr.owner.Pointer == Phase1.Second.Pointer ? "(owner clone)" : "(via handler chain)"));
            }
            catch { }
            if (_cur == null) return;
            try
            {
                _ticks++;
                if (_cur.target != null) _targetTicks++;
                float r = _cur.reloadTimeLeft;
                if (r < _relMin) _relMin = r;
                if (r > _relMax) _relMax = r;
            }
            catch { }
        }

        // summoned things (drones, turrets): objects in the scene that carry the weapon's name appear after it was equipped
        private static string _token = "";
        private static int _objs0;

        private static int CountNamed(string token)
        {
            if (token.Length == 0) return 0;
            int n = 0;
            try { foreach (var t in UnityEngine.Object.FindObjectsOfType<Transform>()) if (t != null && t.gameObject.activeInHierarchy && t.gameObject.name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0) n++; } catch { }
            return n;
        }

        private static string TokenOf(string weaponName)
        {
            int i = weaponName.LastIndexOf('_');
            string seg = i >= 0 ? weaponName.Substring(i + 1) : weaponName;
            return seg.Length > 6 ? seg.Substring(0, 6) : seg;
        }

        // every trigger pull of a weapon: ActivateWeapon of WeaponHandler and of each handler type that declares its own
        internal static int Activations;

        private static void ActPrefix(WeaponHandler __instance)
        {
            if (!_counting || Phase1.Second == null) return;
            try
            {
                var o = __instance.owner;
                if (o == null && __instance.weaponHandlerOwner != null) o = __instance.weaponHandlerOwner.owner;
                if (o != null && o.Pointer == Phase1.Second.Pointer) Activations++;
            }
            catch { }
        }

        private static void PatchActivations()
        {
            var h = new Harmony("localcoop.weapons3");
            var pre = new HarmonyMethod(typeof(WeaponTest).GetMethod("ActPrefix", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic));
            int n = 0;
            var baseT = typeof(WeaponHandler);
            foreach (var t in baseT.Assembly.GetTypes())
            {
                if (t != baseT) continue;      // patching all 21 declaring types killed the game
                var m = t.GetMethod("ActivateWeapon", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly);
                if (m == null || m.GetParameters().Length != 0) continue;
                try { h.Patch(m, prefix: pre); n++; } catch (Exception e) { Lc.Line("weapons: patching " + t.Name + ".ActivateWeapon threw " + e.GetType().Name); }
            }
            Lc.Line("weapons: ActivateWeapon patched on " + n + " handler types");
        }

        private static int _scanKilled;

        /// <summary>The list `Player.weapons` is not everything that shoots: scan the scene for WeaponHandler components owned by the player (directly or through
        /// a parent handler) and destroy every one except `keep`.</summary>
        private static void KillAllOf(Player owner, WeaponHandler keep)
        {
            try
            {
                foreach (var h in UnityEngine.Object.FindObjectsOfType<WeaponHandler>())
                {
                    if (h == null) continue;
                    if (keep != null && h.Pointer == keep.Pointer) continue;
                    var o = h.owner;
                    if (o == null && h.weaponHandlerOwner != null) o = h.weaponHandlerOwner.owner;
                    if (o == null || o.Pointer != owner.Pointer) continue;
                    UnityEngine.Object.Destroy(h);
                    _scanKilled++;
                }
            }
            catch (Exception e) { Lc.LineOnce("weapons: scan kill threw " + e.GetType().Name); }
        }

        private static int _extraKilled;

        /// <summary>Level-ups give the clone weapons of their own; only the weapon under test may be on the clone during a window.</summary>
        private static void OnlyUnderTest()
        {
            try
            {
                var c = Phase1.Second;
                if (c == null || c.weapons == null) return;
                var extra = new List<WeaponHandler>();
                for (int i = 0; i < c.weapons.Count; i++) { var w = c.weapons[i]; if (w != null && (_cur == null || w.Pointer != _cur.Pointer)) extra.Add(w); }
                foreach (var w in extra) { Kill(c, w); _extraKilled++; }
            }
            catch { }
        }

        private static void Window(float now)
        {
            OnlyUnderTest();
            KillAllOf(Phase1.Second, _cur);
            BoscoOff();
            if (now > _hb) { _hb = now + 10f; Lc.Line("weapons: heartbeat step=" + _step + " idx=" + _idx + " counting=" + _counting + " g=" + _g.ToString("F1") + " timeScale=" + Time.timeScale + " form=" + AutoStart.ActiveFormName() + " near=" + NearEnemies(Phase1.Second, 10f)); }
            Silence();
            float dt = Mathf.Clamp(now - _last, 0f, 1f); _last = now;
            bool live = Time.timeScale > 0.5f && AutoStart.ActiveFormName() == "UICoreForm";
            if (live) _g += dt;
            if (!_counting)
            {
                // the window starts when the game runs and enemies are around (a level-up form or an empty field would test nothing)
                if (live && NearEnemies(Phase1.Second, 10f) < 3)
                {
                    if (_emptySince < 0f) _emptySince = _g;
                    if (_g > _emptySince + 12f)          // no enemies reach this spot: try the next side of the host
                    {
                        _spot++; _emptySince = _g;
                        var hp = Phase1.First().transform.position; Phase1.Second.transform.position = hp + Spot(_spot);
                        Lc.Line("weapons: no enemies reach the clone here, moved to side " + (_spot % 4) + " at " + Phase1.Second.transform.position);
                    }
                }
                else _emptySince = -1f;
                if (live && (NearEnemies(Phase1.Second, 10f) >= 3 || _g > _gStart + 40f)) { _counting = true; Hits = 0; Dmg = 0; _act0 = _cur != null ? _cur.activeTime : 0f; Activations = 0; _bh0 = BulletHits.Calls; _bhf0 = BulletHits.Failures; _projTypes.Clear(); _relMin = 1e9f; _relMax = -1e9f; _targetTicks = 0; _ticks = 0; SrcNone = SrcClone = SrcOther = TrackCalls = TrackOther = 0; TrackSum = 0; Mine = 0; MineDmg = 0; BySource.Clear(); _tracked0 = _cur != null ? _cur.trackedTotalDamage : 0; _hostDmg0 = HostDamage(); _shots0 = Phase1.ShotsSecond; _gStart = _g; _winLen = 6f; }
                else if (now > _winStart + 150f) { _row.Note += "the game was not running with enemies around for 150 s; "; Finish(_row); _idx++; _step = 4; _counting = false; }
                return;
            }
            SampleHandler();
            if (_g < _gStart + _winLen) return;
            if (Mine == 0 && _winLen < 24f) { _winLen += 6f; return; }          // no enemy came in reach yet: give it more time, up to 24 s
            { var sb = new StringBuilder(); foreach (var kv in BySource) sb.Append(kv.Key).Append('=').Append(kv.Value).Append(' '); _row.Note += "bySource[" + sb + "] "; }
            try
            {
                var cw = new StringBuilder(); var c = Phase1.Second; var h = Phase1.First();
                for (int i = 0; c.weapons != null && i < c.weapons.Count; i++) cw.Append(c.weapons[i] != null ? c.weapons[i].data.name : "null").Append(',');
                var hw = new StringBuilder();
                for (int i = 0; h.weapons != null && i < h.weapons.Count; i++) hw.Append(h.weapons[i] != null ? h.weapons[i].data.name : "null").Append(',');
                int bosco = c.boscoWeapons != null ? c.boscoWeapons.Count : -1, hbosco = h.boscoWeapons != null ? h.boscoWeapons.Count : -1;
                _row.Note += "cloneWeapons[" + cw + "] hostWeapons[" + hw + "] bosco clone/host=" + bosco + "/" + hbosco + "; ";
            }
            catch { }
            _row.Note += "scan-killed handlers=" + _scanKilled + "; extra weapons removed=" + _extraKilled + "; src none/clone/other=" + SrcNone + "/" + SrcClone + "/" + SrcOther + " track calls/sum/other=" + TrackCalls + "/" + TrackSum + "/" + TrackOther + " tracked=" + ((_cur != null ? _cur.trackedTotalDamage : 0) - _tracked0) + "; ";
            _row.Hits = Mine; _row.Dmg = MineDmg; _row.Shots = Phase1.ShotsSecond - _shots0;
            _row.Near = NearEnemies(Phase1.Second, 10f); _row.Secs = _g - _gStart;
            _row.HostSilent = HostDamage() == _hostDmg0;
            if (_row.Name == "(no weapon)") { BaseHits = _row.Hits; BaseDmg = _row.Dmg; }
            bool baseline = _row.Name == "(no weapon)";
            float act = _cur != null ? _cur.activeTime - _act0 : 0f;
            long trk = _cur != null ? _cur.trackedTotalDamage - _tracked0 : 0;
            bool reloads = _relMax - _relMin > 0.05f;
            // the handler runs (active time passes), and it did something of its own: shots of the clone, damage it tracked itself, a target it took, or a reload cycle
            int objs = CountNamed(_token) - _objs0;
            bool own = _row.Shots >= 3 || trk > 0 || _targetTicks > 0 || reloads || objs > 0 || Activations >= 3;
            // Fired = the weapon did something of its own. Damaged = damage was actually dealt near the clone, clearly more than with no weapon
            // (the clone's body contact is switched off; "hits" with 0 damage and shots do NOT count: bullets used to fly through enemies).
            bool dealt = _row.Dmg >= 20 && _row.Dmg > 3 * BaseDmg;
            _row.Note += "bulletHitCalls " + (BulletHits.Calls - _bh0) + " (failures " + (BulletHits.Failures - _bhf0) + "); clone-owned flying things: [" + string.Join(",", new List<string>(_projTypes).ToArray()) + "]; ";
            _row.Note += "damage dealt " + _row.Dmg + " (no-weapon baseline " + BaseDmg + "); ";
            _row.Fired = baseline || own || dealt;
            _row.Damaged = baseline ? true : (_cur != null && _row.Equipped && _row.Fired && dealt && _row.HostSilent);
            if (!_row.HostSilent) _row.Note += "host weapon dealt damage during the window; ";
            Finish(_row);
            _winStart = -1f; _idx++; _step = 4; _counting = false;
        }

        private static void Finish(Row r)
        {
            Rows.Add(r);
            Lc.Line("weapons: ROW " + (r.Damaged ? "ok  " : "FAIL") + " " + r.Name + (r.Starter ? " [starter]" : "") + " equipped=" + r.Equipped + " fired=" + r.Fired + " damaged=" + r.Damaged
                + " hitsNearClone=" + r.Hits + " dmg=" + r.Dmg + " shots=" + r.Shots + " near10m=" + r.Near + " secs=" + r.Secs.ToString("F0") + " " + r.Note);
        }

        private static void Report()
        {
            int ok = 0, st = 0, stOk = 0;
            var bad = new StringBuilder();
            foreach (var r in Rows)
            {
                if (r.Damaged) ok++; else bad.Append(r.Name).Append(' ');
                if (r.Starter) { st++; if (r.Damaged) stOk++; }
            }
            Shots.Take("weapons-end");
            Lc.Line("weapons: " + ok + "/" + Rows.Count + " weapons fire and damage for the second dwarf; starters " + stOk + "/" + st + "; failing: " + bad);
            UiTest.FinishWith(stOk == st && Rows.Count == _list.Count && (st > 0 || ok == Rows.Count), "weapons ok=" + ok + "/" + Rows.Count + " starters=" + stOk + "/" + st + " failing=" + bad);
        }
    }
}
