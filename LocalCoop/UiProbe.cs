using System;
using System.Text;
using Assets.Scripts.Data;
using Assets.Scripts.SkillSystem;
using Assets.Scripts.Milestones;
using DRS.SaveSystem;
using DRS.UI;
using UnityEngine;

namespace LocalCoop
{
    /// <summary>`-lcui=probe`: on the main menu, log what the co-op menu will read (classes, subclasses, biomes, hazard levels,
    /// the main-menu button layout) and take one screenshot. Read-only.</summary>
    internal static class UiProbe
    {
        private static float _at = -1f;
        private static bool _done;

        internal static void Tick()
        {
            if (_done || Lc.Str("-lcui=") != "probe" && Lc.Str("-lcui=") != "wprobe" && Lc.Str("-lcui=") != "gprobe" && Lc.Str("-lcui=") != "unlockprobe" && Lc.Str("-lcui=") != "projprobe" && Lc.Str("-lcui=") != "clsprobe" && Lc.Str("-lcui=") != "statprobe" && Lc.Str("-lcui=") != "rankprobe" && Lc.Str("-lcui=") != "subprobe" && Lc.Str("-lcui=") != "bioprobe" && Lc.Str("-lcui=") != "gateprobe" && Lc.Str("-lcui=") != "gateprobe2") return;
            float now = Time.realtimeSinceStartup;
            if (_at < 0f)
            {
                var menu = Find.One<UIMenuSplashForm>();
                if (menu != null && menu.gameObject.activeInHierarchy) _at = now + 4f;
                return;
            }
            if (now < _at) return;
            _done = true;
            try { if (Lc.Str("-lcui=") == "wprobe") WeaponProbe(); else if (Lc.Str("-lcui=") == "gprobe") StageProbe(); else if (Lc.Str("-lcui=") == "unlockprobe") UnlockProbe(); else if (Lc.Str("-lcui=") == "projprobe") ProjProbe(); else if (Lc.Str("-lcui=") == "clsprobe") ClsProbe(); else if (Lc.Str("-lcui=") == "statprobe") StatProbe(); else if (Lc.Str("-lcui=") == "rankprobe") RankProbe(); else if (Lc.Str("-lcui=") == "subprobe") SubProbe(); else if (Lc.Str("-lcui=") == "bioprobe") BioProbe(); else if (Lc.Str("-lcui=") == "gateprobe") GateProbe(); else if (Lc.Str("-lcui=") == "gateprobe2") GateProbe2(); else Run(); } catch (Exception e) { Lc.Line("ui: probe threw " + e.GetType().Name + ": " + e.Message); }
            Shots.Take("menu-probe");
            Lc.QuitSent = true;
            Lc.Line("RESULT " + Lc.Test + " PASS ui-probe");
            Application.Quit();
        }

        private static void Members(Type t)
        {
            foreach (var pi in t.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
                Lc.Line("wp:   " + t.Name + "." + pi.Name + " : " + pi.PropertyType.Name);
        }

        private static void WeaponProbe()
        {
            var all = Resources.FindObjectsOfTypeAll<WeaponSkillData>();
            Lc.Line("wp: WeaponSkillData x" + all.Length);
            Members(typeof(WeaponSkillData));
            foreach (var w in all)
                Lc.Line("wp: weapon '" + w.name + "' type=" + w.GetType().Name);
            foreach (var t in new[] { typeof(Entity), typeof(Enemy) })
                foreach (var m in t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
                    if (m.Name.IndexOf("Damage", StringComparison.OrdinalIgnoreCase) >= 0 || m.Name.IndexOf("Die", StringComparison.OrdinalIgnoreCase) >= 0 || m.Name.IndexOf("Hit", StringComparison.OrdinalIgnoreCase) >= 0)
                        Lc.Line("wp: " + t.Name + "." + m.Name + "(" + string.Join(",", Array.ConvertAll(m.GetParameters(), q => q.ParameterType.Name)) + ")");
            var arts = Resources.FindObjectsOfTypeAll<ClassArtifactData>();
            foreach (var a in arts) Lc.Line("wp: sub '" + a.name + "' starter=" + (a.StarterWeapon != null ? a.StarterWeapon.name : "null"));
        }

        private static void StageProbe()
        {
            foreach (var t in typeof(GameController).Assembly.GetTypes())
            {
                string n = t.Name;
                if (!(n == "MissionSectorState" || n == "MissionNodeState" || n == "MissionRoadState" || n == "MissionMapConfig" || n == "MissionNodeData" || n == "MissionSectorData" || n == "BossMissionPool" || n == "MissionRoadManager" || n == "EMissionType" || n == "EMainMissionType")) continue;
                var sb = new StringBuilder();
                if (t.IsEnum) { foreach (var nm in Enum.GetNames(t)) sb.Append(nm).Append(' '); Lc.Line("gp3: enum " + t.FullName + ": " + sb); continue; }
                foreach (var pi in t.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)) sb.Append(pi.Name).Append(':').Append(pi.PropertyType.Name).Append(' ');
                foreach (var m in t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)) if (!m.Name.StartsWith("get_") && !m.Name.StartsWith("set_")) sb.Append(m.Name).Append("() ");
                Lc.Line("gp3: " + t.FullName + ": " + sb);
            }

            var tn = new StringBuilder();
            foreach (var t in typeof(GameController).Assembly.GetTypes())
                if (t.Name.IndexOf("Boss", StringComparison.OrdinalIgnoreCase) >= 0 || t.Name.IndexOf("Stage", StringComparison.OrdinalIgnoreCase) >= 0 || t.Name.IndexOf("PowerCore", StringComparison.OrdinalIgnoreCase) >= 0) tn.Append(t.FullName).Append(' ');
            Lc.Line("gp: types: " + tn);
            foreach (var t in typeof(GameController).Assembly.GetTypes())
            {
                if (t.Name.StartsWith("<")) continue;
                foreach (var m in t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly))
                {
                    bool takes = false;
                    foreach (var q in m.GetParameters()) if (q.ParameterType.Name == "StageBossSpawnArgs") takes = true;
                    bool elite = (t.Name == "EnemySpawner" || t.Name == "LevelObjectiveTracker" || t.Name == "GameController") && (m.Name.IndexOf("Elite", StringComparison.OrdinalIgnoreCase) >= 0 || m.Name.IndexOf("Boss", StringComparison.OrdinalIgnoreCase) >= 0);
                    if ((takes || elite) && !m.Name.StartsWith("add_") && !m.Name.StartsWith("remove_")) Lc.Line("gp2: " + t.FullName + "." + m.Name + "(" + string.Join(",", Array.ConvertAll(m.GetParameters(), q => q.ParameterType.Name)) + ")");
                }
                foreach (var f in t.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly))
                    if (f.FieldType.Name.Contains("StageBossSpawnArgs")) Lc.Line("gp2: event/field " + t.FullName + "." + f.Name + " : " + f.FieldType.Name);
            }
            foreach (var t in typeof(GameController).Assembly.GetTypes())
            {
                if (t.Name.StartsWith("<")) continue;
                foreach (var m in t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
                {
                    if (m.Name.IndexOf("Boss", StringComparison.OrdinalIgnoreCase) < 0 || m.Name.StartsWith("get_") || m.Name.StartsWith("set_") || m.Name.StartsWith("add_") || m.Name.StartsWith("remove_")) continue;
                    Lc.Line("gp: " + t.FullName + "." + m.Name + "(" + string.Join(",", Array.ConvertAll(m.GetParameters(), q => q.ParameterType.Name)) + ")");
                }
            }

            string[] keys = { "boss", "stage", "wave", "elite", "mission", "sector", "end", "victory", "complete", "next" };
            foreach (var t in new[] { typeof(GameController), typeof(RunSettingsManager) })
            {
                foreach (var m in t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
                {
                    if (m.Name.StartsWith("get_") || m.Name.StartsWith("set_") || m.Name.StartsWith("add_") || m.Name.StartsWith("remove_")) continue;
                    foreach (var k in keys) if (m.Name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) { Lc.Line("gp: " + t.Name + "." + m.Name + "(" + string.Join(",", Array.ConvertAll(m.GetParameters(), q => q.ParameterType.Name)) + ")"); break; }
                }
                foreach (var pi in t.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
                    foreach (var k in keys) if (pi.Name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) { Lc.Line("gp: " + t.Name + " prop " + pi.Name + " : " + pi.PropertyType.Name); break; }
            }
        }

        private static void UnlockProbe()
        {
            var road = CoopStart.Resolve<MissionRoadManager>();
            var sb0 = new StringBuilder();
            foreach (var m in new[] { EMainMissionType.Elimination, EMainMissionType.Escort })
            {
                var st = road.GetMissionRoadState(m);
                Lc.Line("up: mission " + m + " unlocked=" + st.IsUnlocked + " goalPoints=" + st.MissionGoalPoints + " totalPoints(road)=" + road.GetTotalMissionPoints() + " pointsOf(m)=" + road.GetMissionPoints(m));
                for (int i = 0; i < st.SectorStates.Count; i++)
                {
                    var sec = st.SectorStates[i];
                    var ua = new StringBuilder(); try { foreach (var u in sec.Data.UnlocksAt) ua.Append(u).Append(','); } catch { }
                    string gate = "none";
                    try { var g = sec.GateState; if (g != null) gate = "locked=" + g.IsLocked + " unlocksAt=" + g.UnlocksAt + " minHaz=" + g.MinHazIndex + " type=" + g.Data.Type + " biome=" + g.Data.Biome + " sectorGateUnlocksAt=" + sec.Data.GateUnlocksAt; } catch (Exception e) { gate = "err " + e.Message; }
                    var nodes = new StringBuilder();
                    foreach (var n in sec.Nodes) nodes.Append(n.Data.Biome).Append("[locked=").Append(n.IsLocked).Append(" at=").Append(n.UnlocksAt).Append(" minHaz=").Append(n.MinHazIndex).Append("] ");
                    Lc.Line("up:  sector " + (i + 1) + " locked=" + sec.IsLocked + " dataUnlocksAt=[" + ua + "] minHaz=" + sec.Data.MinHazIndex + " gate: " + gate + " | nodes: " + nodes);
                }
            }
        }

        private static void Dump(string tag, object o)
        {
            var t = o.GetType();
            foreach (var pi in t.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                if (pi.GetIndexParameters().Length > 0) continue;
                string v;
                try { var x = pi.GetValue(o, null); v = x == null ? "null" : (x is string || x.GetType().IsPrimitive || x.GetType().IsEnum ? x.ToString() : "<" + x.GetType().Name + ">"); } catch (Exception e) { v = "err " + e.GetType().Name; }
                if (v.Length > 120) v = v.Substring(0, 120);
                Lc.Line("cp: " + tag + "." + pi.Name + " : " + pi.PropertyType.Name + " = " + v);
            }
            foreach (var m in t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
                if (!m.Name.StartsWith("get_") && !m.Name.StartsWith("set_") && m.GetParameters().Length == 0 && (m.ReturnType == typeof(string) || m.Name.IndexOf("Desc", StringComparison.OrdinalIgnoreCase) >= 0 || m.Name.IndexOf("Title", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    string v; try { var x = m.Invoke(o, null); v = x == null ? "null" : x.ToString(); } catch (Exception e) { v = "err " + (e.InnerException != null ? e.InnerException.Message : e.Message); }
                    if (v.Length > 160) v = v.Substring(0, 160);
                    Lc.Line("cp: " + tag + "." + m.Name + "() : " + m.ReturnType.Name + " = " + v);
                }
        }

        private static void DumpArr(string tag, object arr, int max)
        {
            var e = arr as System.Collections.IEnumerable; if (e == null) { Lc.Line("sp: " + tag + " not enumerable"); return; }
            int n = 0;
            foreach (var o in e) { if (o == null) continue; if (n++ >= max) break; Lc.Line("sp: ---- " + tag + "[" + (n - 1) + "] " + o.GetType().Name); DumpShort(tag + "[" + (n - 1) + "]", o); }
        }

        private static void DumpShort(string tag, object o)
        {
            foreach (var pi in o.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                if (pi.GetIndexParameters().Length > 0 || pi.Name == "Pointer" || pi.Name == "ObjectClass" || pi.Name == "m_CachedPtr" || pi.Name == "hideFlags") continue;
                string v; try { var x = pi.GetValue(o, null); v = x == null ? "null" : (x is string || x.GetType().IsPrimitive || x.GetType().IsEnum ? x.ToString() : "<" + x.GetType().Name + ">"); } catch (Exception ex) { v = "err " + ex.GetType().Name; }
                if (v.Length > 100) v = v.Substring(0, 100);
                Lc.Line("sp: " + tag + "." + pi.Name + " = " + v);
            }
        }

        private static void RankProbe()
        {
            var rx = new System.Text.RegularExpressions.Regex("Rank|ClassLevel|TotalClassLevels|UnlockAt", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly;
            var holders = new System.Collections.Generic.List<Type>();
            foreach (var t in typeof(ClassCollection).Assembly.GetTypes())
            {
                if (t.Name.StartsWith("<")) continue;
                bool any = false;
                foreach (var m in t.GetMembers(flags))
                {
                    if (!rx.IsMatch(m.Name) || m.Name.StartsWith("add_") || m.Name.StartsWith("remove_")) continue;
                    if (m is System.Reflection.MethodInfo mi && (mi.Name.StartsWith("get_") || mi.Name.StartsWith("set_"))) { }
                    string sig = m is System.Reflection.MethodInfo mm ? "(" + string.Join(",", Array.ConvertAll(mm.GetParameters(), q => q.ParameterType.Name)) + ") : " + mm.ReturnType.Name : (m is System.Reflection.PropertyInfo pp ? " : " + pp.PropertyType.Name : "");
                    Lc.Line("rp: " + t.FullName + "." + m.Name + sig);
                    any = true;
                    if (m.Name == "GetRankForClass" || m.Name == "GetProgressTowardsNextClassRank") holders.Add(t);
                }
            }
            foreach (var t in holders)
            {
                object inst = null;
                try { var gm = typeof(CoopStart).GetMethod("Resolve", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public).MakeGenericMethod(t); inst = gm.Invoke(null, null); }
                catch (Exception e) { Lc.Line("rp: resolve " + t.Name + " threw " + (e.InnerException != null ? e.InnerException.Message : e.Message)); }
                Lc.Line("rp: holder " + t.FullName + " instance=" + (inst != null));
                if (inst == null) continue;
                foreach (var c in Resources.FindObjectsOfTypeAll<ClassCollection>()[0].GetClasses())
                    foreach (var m in t.GetMethods(flags))
                    {
                        if (m.Name != "GetRankForClass" && m.Name != "GetProgressTowardsNextClassRank") continue;
                        var ps = m.GetParameters();
                        if (ps.Length != 1) continue;
                        try
                        {
                            object arg = ps[0].ParameterType.IsEnum ? (object)c.Dwarf : (ps[0].ParameterType.IsAssignableFrom(c.GetType()) ? c : null);
                            if (arg == null) { Lc.Line("rp: " + m.Name + " wants " + ps[0].ParameterType.Name); continue; }
                            Lc.Line("rp: " + t.Name + "." + m.Name + "(" + c.Dwarf + ") = " + m.Invoke(inst, new[] { arg }));
                        }
                        catch (Exception e) { Lc.Line("rp: call threw " + (e.InnerException != null ? e.InnerException.Message : e.Message)); }
                    }
            }
            // the unlock requirement on the data objects
            foreach (var c in Resources.FindObjectsOfTypeAll<ClassCollection>()[0].GetClasses()) DumpRank("class " + c.Dwarf, c);
            foreach (var a in Resources.FindObjectsOfTypeAll<ClassArtifactData>()) if (a.name.Contains("Scout") || a.name.Contains("Foreman") || a.name.Contains("Engineer") || a.name.Contains("Gunner")) DumpRank("sub " + a.name, a);
        }

        private static void DumpList(string tag, object list, int max)
        {
            if (list == null) { Lc.Line("gq: " + tag + " null"); return; }
            var t = list.GetType();
            int count = 0; try { count = (int)t.GetProperty("Count").GetValue(list, null); } catch { }
            Lc.Line("gq: " + tag + " count=" + count);
            var item = t.GetMethod("get_Item");
            for (int i = 0; i < count && i < max; i++)
            {
                object o = null; try { o = item.Invoke(list, new object[] { i }); } catch { }
                if (o == null) continue;
                Lc.Line("gq: ---- " + tag + "[" + i + "] " + o.GetType().Name);
                DumpShort(tag + "[" + i + "]", o);
                foreach (var mi in o.GetType().GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
                    if (mi.GetParameters().Length == 0 && !mi.Name.StartsWith("get_") && !mi.Name.StartsWith("set_") && (mi.ReturnType == typeof(string) || mi.ReturnType == typeof(bool) || mi.ReturnType == typeof(int)))
                    { try { Lc.Line("gq: " + tag + "[" + i + "]." + mi.Name + "() = " + mi.Invoke(o, null)); } catch { } }
            }
        }

        private static void GateProbe2()
        {
            var road = CoopStart.Resolve<MissionRoadManager>();
            var st = road.GetMissionRoadState(EMainMissionType.Elimination);
            foreach (int si in new[] { 1, 2 })
            {
                var sec = st.SectorStates[si];
                var g = sec.GateState;
                Lc.Line("gq: ==== gate of sector " + (si + 1));
                DumpList("gate" + si + ".Mutators", g.Data.Mutators, 6);
                DumpList("gate" + si + ".Goals", g.Data.MissionGoals, 4);
                try { DumpList("gate" + si + ".StarterArtifacts", g.Data.FixedGateData.StarterArtifacts, 4); } catch (Exception e) { Lc.Line("gq: starter threw " + e.Message); }
                try { DumpList("gate" + si + ".StartingMutators", g.Data.FixedGateData.StartingMutators, 4); } catch { }
                foreach (var n in sec.Nodes) { DumpList("sector" + si + " node " + n.Data.Biome + ".Mutators", n.Data.Mutators, 4); }
            }
            // texts of the game's own node page, if it exists in the scene
            foreach (var mb in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
            {
                if (mb == null) continue;
                string tn = mb.GetType().Name;
                if (tn.IndexOf("MissionNodePage", StringComparison.OrdinalIgnoreCase) < 0 && tn.IndexOf("BiomeSelectPage", StringComparison.OrdinalIgnoreCase) < 0 && tn.IndexOf("GatePage", StringComparison.OrdinalIgnoreCase) < 0) continue;
                foreach (var pi in mb.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
                {
                    if (pi.PropertyType.Name != "LocalizedString") continue;
                    try
                    {
                        var ls = pi.GetValue(mb, null);
                        var gm = ls.GetType().GetMethod("GetLocalizedString", System.Type.EmptyTypes);
                        Lc.Line("gq: page " + tn + "." + pi.Name + " = " + (gm != null ? gm.Invoke(ls, null) : "<no GetLocalizedString>"));
                    }
                    catch (Exception e) { Lc.Line("gq: page " + tn + "." + pi.Name + " threw " + (e.InnerException != null ? e.InnerException.Message : e.Message)); }
                }
            }
        }

        private static void GateProbe()
        {
            var road = CoopStart.Resolve<MissionRoadManager>();
            var st = road.GetMissionRoadState(EMainMissionType.Elimination);
            for (int si = 0; si < st.SectorStates.Count; si++)
            {
                var sec = st.SectorStates[si];
                Lc.Line("gp: ==== sector " + (si + 1));
                DumpShort("sector" + si, sec);
                if (sec.Data != null) DumpShort("sectordata" + si, sec.Data);
                var g = sec.GateState;
                if (g != null)
                {
                    DumpShort("gate" + si, g);
                    if (g.Data != null) DumpShort("gatedata" + si, g.Data);
                    try { if (g.Data.FixedGateData != null) DumpShort("fixedgate" + si, g.Data.FixedGateData); } catch (Exception e) { Lc.Line("gp: fixed threw " + e.Message); }
                    try { DumpArr("gate" + si + ".mutators", g.Data.Mutators, 6); } catch (Exception e) { Lc.Line("gp: mutators threw " + e.Message); }
                    try { foreach (var m in g.Data.Mutators) { foreach (var mi in m.GetType().GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)) if (mi.GetParameters().Length == 0 && !mi.Name.StartsWith("get_") && !mi.Name.StartsWith("set_")) { try { Lc.Line("gp: mut." + mi.Name + "() = " + mi.Invoke(m, null)); } catch { } } break; } } catch { }
                }
                if (si == 1 || si == 2)
                {
                    foreach (var n in sec.Nodes)
                    {
                        Lc.Line("gp: node " + n.Data.Biome + " minHaz=" + n.MinHazIndex);
                        try { DumpArr("node" + si + "." + n.Data.Biome + ".mutators", n.Data.Mutators, 6); } catch { }
                        break;
                    }
                }
            }
        }

        private static void BioProbe()
        {
            var bl = Resources.FindObjectsOfTypeAll<BiomeLevelDataCollection>()[0];
            var b = bl.Data[0];
            Lc.Line("bp: biome " + b.name);
            DumpShort("BiomeLevelData", b);
            if (b.BiomeData != null) { Lc.Line("bp: BiomeData"); DumpShort("BiomeData", b.BiomeData); }
            var road = CoopStart.Resolve<MissionRoadManager>();
            var st = road.GetMissionRoadState(EMainMissionType.Elimination);
            MissionNodeState n;
            foreach (var bi in bl.Data)
                if (st.TryGetNodeState(bi.BiomeType, 0, out n) && n != null)
                {
                    Lc.Line("bp: node " + bi.name);
                    DumpShort("node " + bi.name, n);
                    if (n.Data != null) DumpShort("nodedata " + bi.name, n.Data);
                    break;
                }
            var rx = new System.Text.RegularExpressions.Regex("nitra|morkite|gold|zucker|sugar|bismor|croppa|enor|jadiz|umanite|aquarq|fossil|pearl|currency|crafting|resource", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var sb = new StringBuilder(); int k = 0;
            foreach (var sp in Resources.FindObjectsOfTypeAll<Sprite>()) if (rx.IsMatch(sp.name) && k++ < 80) sb.Append(sp.name).Append("; ");
            Lc.Line("bp: currency sprites: " + sb);
            foreach (var m in typeof(SaveDataManager).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
                if (System.Text.RegularExpressions.Regex.IsMatch(m.Name, "Score|High|Best", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) Lc.Line("bp: SaveDataManager." + m.Name + "(" + string.Join(",", Array.ConvertAll(m.GetParameters(), q => q.ParameterType.Name)) + ") : " + m.ReturnType.Name);
            foreach (var t in new[] { typeof(MissionNodeState), typeof(MissionSectorState), typeof(MissionRoadManager) })
                foreach (var m in t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
                    if (System.Text.RegularExpressions.Regex.IsMatch(m.Name, "Score|High|Best", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) Lc.Line("bp: " + t.Name + "." + m.Name + "(" + string.Join(",", Array.ConvertAll(m.GetParameters(), q => q.ParameterType.Name)) + ") : " + m.ReturnType.Name);
            foreach (var t in typeof(MissionNodeState).Assembly.GetTypes())
                foreach (var m in t.GetMembers(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
                    if (System.Text.RegularExpressions.Regex.IsMatch(m.Name, "HighScore|Highscore|BestScore|MaxScore", System.Text.RegularExpressions.RegexOptions.IgnoreCase) && !m.Name.StartsWith("set_")) Lc.Line("bp: member " + t.FullName + "." + m.Name);
        }

        private static void SubProbe()
        {
            foreach (var a in Resources.FindObjectsOfTypeAll<ClassArtifactData>())
            {
                var pc = new StringBuilder(); try { foreach (var d in a.PossibleClasses) pc.Append(d).Append(','); } catch { }
                Lc.Line("sb: sub " + a.name + " title='" + a.Title + "' default=" + a.DefaultClass + " possible=" + pc + " rarity=" + a.rarity + " type=" + a.Type);
            }
            foreach (var mb in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
            {
                if (mb == null) continue;
                string tn = mb.GetType().Name;
                if (tn.IndexOf("ClassArtifact", StringComparison.OrdinalIgnoreCase) < 0 && tn.IndexOf("ClassMod", StringComparison.OrdinalIgnoreCase) < 0) continue;
                var sbp = new StringBuilder();
                foreach (var pi in mb.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
                {
                    if (pi.GetIndexParameters().Length > 0) continue;
                    var t = pi.PropertyType;
                    if (!(t == typeof(int) || t == typeof(float) || t == typeof(bool) || t == typeof(string) || t == typeof(ClassArtifactData) || t.Name == "EDwarf" || t.Name == "ClassData")) continue;
                    object v; try { v = pi.GetValue(mb, null); } catch { continue; }
                    if (v is ClassArtifactData cad) v = cad.name;
                    sbp.Append(pi.Name).Append('=').Append(v).Append(' ');
                }
                Lc.Line("sb: ui " + tn + " '" + mb.gameObject.name + "' idx=" + mb.transform.GetSiblingIndex() + " parent=" + (mb.transform.parent != null ? mb.transform.parent.name : "-") + " active=" + mb.gameObject.activeSelf + " | " + sbp);
            }
        }

        private static void DumpRank(string tag, object o)
        {
            foreach (var pi in o.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                if (pi.GetIndexParameters().Length > 0 || !System.Text.RegularExpressions.Regex.IsMatch(pi.Name, "Rank|Level|Unlock", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) continue;
                string v; try { var x = pi.GetValue(o, null); v = x == null ? "null" : x.ToString(); } catch (Exception e) { v = "err " + e.GetType().Name; }
                Lc.Line("rp: " + tag + "." + pi.Name + " = " + v);
            }
        }

        private static void StatProbe()
        {
            var cc = Resources.FindObjectsOfTypeAll<ClassCollection>();
            foreach (var c in cc[0].GetClasses())
            {
                if (c.Dwarf.ToString() != "SCOUT") continue;
                Lc.Line("sp: class " + c.DisplayName);
                DumpArr("BaseStats.mods", c.BaseStats.mods, 14);
            }
            foreach (var a in Resources.FindObjectsOfTypeAll<ClassArtifactData>())
            {
                if (!(a.name.Contains("Scout") || a.name.Contains("Foreman"))) continue;
                Lc.Line("sp: sub " + a.name + " title=" + a.Title);
                DumpArr(a.name + ".StatMods", a.StatMods, 6);
                DumpArr(a.name + ".TagStatMods", a.TagStatMods, 6);
            }
            var rx = new System.Text.RegularExpressions.Regex("hex|gate|sector|lock|biome|mission|elim|escort|node|crystal|cavern", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var sb = new System.Text.StringBuilder(); int k = 0;
            foreach (var sp in Resources.FindObjectsOfTypeAll<Sprite>()) if (rx.IsMatch(sp.name)) { if (k++ < 150) sb.Append(sp.name).Append(" ").Append((int)sp.rect.width).Append("x").Append((int)sp.rect.height).Append("; "); }
            Lc.Line("sp: sprites (" + k + "): " + sb);
            foreach (var key in new[] { "UIMissionRoadButton_Elim", "MissionSectorButton_1", "MissionGateButton_1", "MissionNodeButton_01", "MissionSectorButton_2" })
                foreach (var b in Resources.FindObjectsOfTypeAll<UIButton>())
                {
                    if (b.gameObject.name != key) continue;
                    var imgs = b.GetComponentsInChildren<UnityEngine.UI.Image>(true);
                    var sb2 = new System.Text.StringBuilder();
                    foreach (var im in imgs) sb2.Append(im.gameObject.name).Append("=").Append(im.sprite != null ? im.sprite.name : "null").Append(" ");
                    Lc.Line("sp: button " + key + " images: " + sb2);
                    break;
                }
        }

        private static void ClsProbe()
        {
            var cc = Resources.FindObjectsOfTypeAll<ClassCollection>();
            var c = cc[0].GetClasses()[0];
            Dump("ClassData", c);
            try { Dump("BaseStats", c.BaseStats); } catch (Exception e) { Lc.Line("cp: basestats " + e.Message); }
            ClassArtifactData a = null;
            foreach (var x in Resources.FindObjectsOfTypeAll<ClassArtifactData>()) if (x.name.Contains("Foreman") || a == null) a = x;
            Dump("ClassArtifactData", a);
            if (a.StarterWeapon != null) Dump("StarterWeapon", a.StarterWeapon);
        }

        private static void ProjProbe()
        {
            foreach (var t in typeof(GameController).Assembly.GetTypes())
            {
                if (!(t.Name == "Projectile" || t.Name == "DamageProjectile" || t.Name == "BaseProjectile" || t.Name == "ProjectileDamageHandler" || t.Name == "ProjectileHit" || t.Name.StartsWith("Projectile") && t.Name.Contains("Handler"))) continue;
                var sb = new StringBuilder();
                foreach (var m in t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
                    if (!m.Name.StartsWith("get_") && !m.Name.StartsWith("set_") && !m.Name.StartsWith("add_") && !m.Name.StartsWith("remove_")) sb.Append(m.Name).Append('(').Append(string.Join(",", Array.ConvertAll(m.GetParameters(), q => q.ParameterType.Name))).Append(") ");
                var pb = new StringBuilder();
                foreach (var pi in t.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)) pb.Append(pi.Name).Append(':').Append(pi.PropertyType.Name).Append(' ');
                Lc.Line("pp: " + t.FullName + " base=" + (t.BaseType != null ? t.BaseType.Name : "-") + " methods: " + sb);
                Lc.Line("pp:   props: " + pb);
            }
        }

        private static void Run()
        {
            var cc = Resources.FindObjectsOfTypeAll<ClassCollection>();
            Lc.Line("ui: ClassCollection x" + cc.Length);
            if (cc.Length > 0)
                foreach (var c in cc[0].GetClasses())
                    Lc.Line("ui: class " + c.Dwarf + " name='" + c.DisplayName + "' default=" + (c.DefaultClassArtifact != null ? c.DefaultClassArtifact.name : "null")
                        + " illu=" + (c.Illustration != null) + " vert=" + (c.IllustrationVertical != null));

            var arts = Resources.FindObjectsOfTypeAll<ClassArtifactData>();
            Lc.Line("ui: ClassArtifactData x" + arts.Length);
            foreach (var a in arts)
            {
                var sb = new StringBuilder();
                try { foreach (var d in a.PossibleClasses) sb.Append(d).Append(','); } catch { }
                Lc.Line("ui: subclass '" + a.name + "' default=" + a.DefaultClass + " possible=" + sb + " icon=" + (a.Icon != null));
            }

            var bl = Resources.FindObjectsOfTypeAll<BiomeLevelDataCollection>();
            Lc.Line("ui: BiomeLevelDataCollection x" + bl.Length);
            if (bl.Length > 0)
                foreach (var b in bl[0].Data)
                    Lc.Line("ui: biome '" + b.name + "' type=" + b.BiomeType + " hazards=" + (b.HazardLevels != null ? b.HazardLevels.Length : -1)
                        + " art=" + (b.BiomeData != null && b.BiomeData.IllustrationBar != null) + " primary=" + Join(b.BiomeData != null ? b.BiomeData.PrimaryCraftingMaterials : null) + " secondary=" + Join(b.BiomeData != null ? b.BiomeData.SecondaryCraftingMaterials : null));

            var menu = Find.One<UIMenuSplashForm>();
            var pb = menu.playButton;
            if (pb != null)
            {
                var t = pb.transform;
                Lc.Line("ui: playButton path=" + Path(t));
                var parent = t.parent;
                for (int i = 0; i < parent.childCount; i++)
                {
                    var ch = parent.GetChild(i);
                    var rt = ch.GetComponent<RectTransform>();
                    Lc.Line("ui:   sibling '" + ch.name + "' active=" + ch.gameObject.activeSelf + (rt != null ? " pos=" + rt.anchoredPosition + " size=" + rt.sizeDelta : ""));
                }
            }
            try { Locks(); } catch (Exception e) { Lc.Line("ui: locks probe threw " + e.GetType().Name + ": " + e.Message); }
            Lc.Line("ui: screen " + Screen.width + "x" + Screen.height);
        }

        private static void Locks()
        {
            var save = CoopStart.Resolve<SaveDataManager>();
            var road = CoopStart.Resolve<MissionRoadManager>();
            Lc.Line("ui: save=" + (save != null) + " road=" + (road != null) + (save != null ? " maxHaz=" + save.SaveData.MaxHazIndex + " biomeGoalPoints=" + save.SaveData.BiomeGoalPoints : ""));
            if (save != null)
            {
                var sb = new StringBuilder();
                for (int i = 0; i < 5; i++) sb.Append(i + 1).Append(save.IsHazIndexUnlocked(i) ? "+ " : "- ");
                Lc.Line("ui: hazIndexUnlocked " + sb);
                foreach (var c in Resources.FindObjectsOfTypeAll<ClassCollection>()[0].GetClasses()) Lc.Line("ui: classUnlocked " + c.Dwarf + "=" + save.IsClassUnlocked(c.Dwarf));
                foreach (var a in Resources.FindObjectsOfTypeAll<ClassArtifactData>()) Lc.Line("ui: artifactUnlocked " + a.name + "=" + save.IsUnlocked(a));
            }
            if (road == null) return;
            var tracker = CoopStart.Resolve<MilestoneTracker>();
            for (int m = 0; m < 2; m++)
            {
                var st = road.GetMissionRoadState((EMainMissionType)m);
                if (st == null) continue;
                Lc.Line("ui: road " + (EMainMissionType)m + " unlocked=" + st.IsUnlocked + " sectors=" + st.SectorStates.Count);
                for (int sct = 0; sct < st.SectorStates.Count; sct++)
                {
                    var ss = st.SectorStates[sct];
                    Lc.Line("ui:  sector " + (sct + 1) + " locked=" + ss.IsLocked + " minHaz=" + ss.Data.MinHazIndex);
                    foreach (var b in Resources.FindObjectsOfTypeAll<BiomeLevelDataCollection>()[0].Data)
                    {
                        MissionNodeState n;
                        if (!st.TryGetNodeState(b.BiomeType, sct, out n) || n == null) { Lc.Line("ui:    " + b.BiomeType + " no node"); continue; }
                        var goals = new StringBuilder();
                        foreach (var g in n.GetMissionGoalStates()) goals.Append(g.IsComplete ? "[x] " : "[ ] ").Append(g.Data.GetFormattedDescriptionText()).Append(" | ");
                        var mut = new StringBuilder();
                        if (n.Data.Mutators != null) foreach (var mu in n.Data.Mutators) mut.Append(mu.GetTitle()).Append(" | ");
                        Lc.Line("ui:    " + b.BiomeType + " locked=" + n.IsLocked + " minHaz=" + n.MinHazIndex + " unlocksAt=" + n.UnlocksAt + " goals: " + goals + " mutators: " + mut);
                        if (sct == 0 && m == 0)
                            for (int h = 0; h < b.HazardLevels.Length; h++)
                            {
                                var hl = b.HazardLevels[h];
                                var bg = new StringBuilder();
                                foreach (var g in hl.Goals) bg.Append(g.GetFormattedDescriptionText()).Append(" | ");
                                Lc.Line("ui:      haz " + (h + 1) + " req=" + hl.BiomeGoalRequirement + " goals: " + bg);
                            }
                    }
                }
            }
        }

        private static string Join(ECurrency[] a)
        {
            var sb = new StringBuilder();
            if (a != null) foreach (var c in a) sb.Append(c).Append(',');
            return sb.ToString();
        }

        private static string Path(Transform t)
        {
            var s = t.name;
            for (int i = 0; i < 5 && t.parent != null; i++) { t = t.parent; s = t.name + "/" + s; }
            return s;
        }
    }
}
