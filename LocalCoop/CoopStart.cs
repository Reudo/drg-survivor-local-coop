using System;
using System.Reflection;
using Assets.Scripts.Data;
using DRS.UI;
using HarmonyLib;
using UnityEngine;
using Zenject;

namespace LocalCoop
{
    /// <summary>
    /// "START RUN": sets the game's run settings from the co-op menu and starts the run through the game's own entry,
    /// UIManager.OnStartGameButton (decompile: FUNCTION-MAP section 12). Order as the campaign wizard does it:
    /// PrepNewRun, main mission type, sector, node of (biome, sector) which sets biome and objective, hazard level, class, class mod.
    /// P1's class goes into RunSettingsManager; P2's stays in CoopMenu.P2 for the second dwarf.
    /// </summary>
    internal static class CoopStart
    {
        internal static bool Started;        // a run was started from the co-op menu
        internal static bool UseGate;        // test: start the sector's gate node (the boss level at the end of a sector) instead of the biome node
        internal static bool Dry;            // -lcuidry: set everything, read it back, do not start (tests)
        internal static string Readback = "";
        private static float _beganAt = -100f;

        internal static RunSettingsManager Rsm()
        {
            try { var c = ProjectContext.Instance.Container; var r = c.Resolve<RunSettingsManager>(); if (r != null) return r; } catch (Exception e) { Lc.LineOnce("ui: project container resolve failed: " + e.Message); }
            try { var s = Find.One<SceneContext>(); if (s != null) return s.Container.Resolve<RunSettingsManager>(); } catch (Exception e) { Lc.LineOnce("ui: scene container resolve failed: " + e.Message); }
            return null;
        }

        internal static T Resolve<T>()
        {
            try { return ProjectContext.Instance.Container.Resolve<T>(); } catch { }
            try { var s = Find.One<SceneContext>(); if (s != null) return s.Container.Resolve<T>(); } catch { }
            return default(T);
        }

        private static void Call(object target, string name, params object[] args)
        {
            var types = new Type[args.Length];
            for (int i = 0; i < args.Length; i++) types[i] = args[i] != null ? args[i].GetType() : null;
            MethodInfo m = null;
            foreach (var mi in target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (mi.Name != name) continue;
                var ps = mi.GetParameters();
                if (ps.Length < args.Length) continue;
                bool ok = true;
                for (int i = 0; i < args.Length; i++)
                    if (args[i] != null && !ps[i].ParameterType.IsAssignableFrom(args[i].GetType())) { ok = false; break; }
                if (ok) { m = mi; break; }
            }
            if (m == null) throw new MissingMethodException(target.GetType().Name + "." + name);
            var full = new object[m.GetParameters().Length];
            for (int i = 0; i < full.Length; i++) full[i] = i < args.Length ? args[i] : Type.Missing;
            m.Invoke(target, full);
        }

        internal static bool Begin()
        {
            try
            {
                // one press starts one run; after a finished or abandoned run the menu clears Started (CoopMenu.ShowMode), a double click is caught here
                if (!Dry && Started && (Lc.InRunAt >= 0f || Time.realtimeSinceStartup - _beganAt < 8f)) return false;
                _beganAt = Time.realtimeSinceStartup;
                bool useGate = UseGate || Lc.Has("-lcgate") || CoopMenu.GateSel;        // the menu's GATE choice (the flag and UseGate stay for tests)        // Steam launch option for the hand test of the gate level
                var rsm = Rsm();
                var road = Resolve<MissionRoadManager>();
                if (rsm == null || road == null) { Lc.Line("ui: start failed: rsm=" + (rsm != null) + " roadManager=" + (road != null)); return false; }

                var biome = CoopMenu.Biomes[CoopMenu.BiomeIndex];
                var mission = (EMainMissionType)CoopMenu.MissionType;
                var state = road.GetMissionRoadState(mission);
                if (state == null || state.SectorStates == null || CoopMenu.Sector >= state.SectorStates.Count) { Lc.Line("ui: start failed: no sector state"); return false; }
                var sector = state.SectorStates[CoopMenu.Sector];
                MissionNodeState node;
                if (!state.TryGetNodeState(biome.BiomeType, CoopMenu.Sector, out node) || node == null) { Lc.Line("ui: start failed: no node for " + biome.BiomeType + " in sector " + (CoopMenu.Sector + 1)); return false; }

                if (useGate)
                {
                    if (sector.GateState == null) { Lc.Line("ui: start failed: this sector has no gate node"); return false; }
                    node = sector.GateState;
                    foreach (var bl in CoopMenu.Biomes) if (bl.BiomeType == node.Data.Biome) { biome = bl; break; }
                    Lc.Line("ui: starting the GATE node of sector " + (CoopMenu.Sector + 1) + ": type=" + node.Data.Type + " biome=" + node.Data.Biome + " locked=" + node.IsLocked);
                }
                Lc.Line("ui: start: " + CoopMenu.Summary());
                Call(rsm, "PrepNewRun", EMissionType.Campaign, null);
                Call(rsm, "SetMainMissionType", mission);
                Call(rsm, "SetMissonSector", sector);
                Call(rsm, "SetupMissionRoadNode", node, useGate);
                Call(rsm, "SetBiomeLevelData", biome);
                Call(rsm, "SetHazardLevel", CoopMenu.Danger);
                var p1c = CoopMenu.Classes[CoopMenu.P1.ClassIndex];
                if (!Lc.Has("-lcuiskipclass")) Call(rsm, "SetClass", p1c);   // twin: without the class the read-back must differ
                if (CoopMenu.P1.Sub != null) Call(rsm, "SetClassMod", CoopMenu.P1.Sub);

                Readback = "mission=" + rsm.MainMissionType + " biome=" + (rsm.BiomeLevelData != null ? CoopMenu.BiomeName(rsm.BiomeLevelData) : "null")
                    + " danger=" + (rsm.HazardLevelIndex + 1) + " class=" + (rsm.CurrentClass != null ? rsm.CurrentClass.Dwarf.ToString() : "null")
                    + " mod=" + (rsm.CurrentClassMod != null ? CoopMenu.SubName(rsm.CurrentClassMod) : "null")
                    + " sectorIdx=" + (rsm.MissionSectorState != null ? (rsm.MissionSectorState.SectorIndex + 1).ToString() : "null")
                    + " node=" + (rsm.MissionNodeState != null ? rsm.MissionNodeState.Data.Type.ToString() : "null");
                Lc.Line("ui: settings read back: " + Readback);

                if (Dry) return true;

                Started = true;
                CoopMenu.Hide();
                var ui = Find.One<UIManager>();
                if (ui == null) { Lc.Line("ui: start failed: no UIManager"); Started = false; CoopMenu.Show(); return false; }
                ui.OnStartGameButton();
                Lc.Line("ui: run start requested");
                return true;
            }
            catch (Exception e)
            {
                Lc.Line("ui: start threw " + e.GetType().Name + ": " + (e.InnerException != null ? e.InnerException.Message : e.Message));
                Started = false;
                return false;
            }
        }
    }
}
