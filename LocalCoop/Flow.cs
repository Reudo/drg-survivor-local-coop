using System;
using UnityEngine;

namespace LocalCoop
{
    /// <summary>
    /// The co-op start as the player sees it: LOCAL CO-OP opens a lobby, OPEN MISSION MENU opens the large mission page (the host chooses mission
    /// type, sector or gate, danger and biome and sees the goals), CONFIRM MISSION opens the split-screen pick (player 1 left, player 2 right, both at
    /// once: class page, subclass page, CONFIRM), then the final page (overview, player 2 READY, the host starts). All of it is CoopMenu modes
    /// ("lobby", "mission", "pick", "final"); starting is CoopStart.Begin. The old all-in-one menu ("classic") stays for `-lcoldmenu` and for every
    /// `-lcui=` test other than `flow*`, which drive its buttons.
    /// </summary>
    internal static class Flow
    {
        internal static bool OldMenu
        {
            get
            {
                if (Lc.Has("-lcoldmenu")) return true;
                string ui = Lc.Str("-lcui=");
                return ui.Length > 0 && !ui.StartsWith("flow", StringComparison.OrdinalIgnoreCase);
            }
        }

        internal static void OpenLobby() { CoopMenu.ShowMode("lobby"); }

        internal static string State()
        {
            try
            {
                var rsm = CoopStart.Rsm();
                if (rsm == null) return "rsm=null";
                return "mission=" + rsm.MainMissionType + " biome=" + (rsm.BiomeLevelData != null ? CoopMenu.BiomeName(rsm.BiomeLevelData) : "null")
                    + " danger=" + (rsm.HazardLevelIndex + 1) + " class=" + (rsm.CurrentClass != null ? rsm.CurrentClass.Dwarf.ToString() : "null")
                    + " mod=" + (rsm.CurrentClassMod != null ? CoopMenu.SubName(rsm.CurrentClassMod) : "null")
                    + " sectorIdx=" + (rsm.MissionSectorState != null ? (rsm.MissionSectorState.SectorIndex + 1).ToString() : "null")
                    + " node=" + (rsm.MissionNodeState != null ? rsm.MissionNodeState.Data.Type.ToString() : "null");
            }
            catch (Exception e) { return "state threw " + e.GetType().Name + ": " + e.Message; }
        }

        internal static void Tick() { }
    }
}
