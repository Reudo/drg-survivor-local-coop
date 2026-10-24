using System;
using DRS.UI;
using HarmonyLib;

namespace LocalCoop
{
    /// <summary>
    /// The game's own HUD (UICoreForm) listens to global player events and writes them into the one bottom panel. Events of the
    /// clone (damage, heal, weapon equipped/removed) therefore landed in the left panel: it showed the clone's hp and a second
    /// pistol. These prefixes drop those events when they belong to the clone; the right panel is fed by Hud2 instead.
    /// Installed together with the clone, never during title-scene startup.
    /// </summary>
    internal static class HudPatches
    {
        private static bool _installed;
        internal static int Dropped;
        internal static int ClonePlayerTexts;

        internal static void Install()
        {
            if (_installed || Lc.Has("-lcnohudpatch")) return;   // the flag is the test twin
            _installed = true;
            try
            {
                var h = new Harmony("localcoop.hud");
                foreach (var t in new[] { typeof(DamagePatch), typeof(HealPatch), typeof(EquipPatch), typeof(RemovePatch) })
                    h.CreateClassProcessor(t).Patch();
                Lc.Line("hud patches: UICoreForm damage/heal/weapon events of the clone are dropped");
            }
            catch (Exception e) { Lc.Line("hud patches: install failed: " + e.GetType().Name + ": " + e.Message); }
        }

        private static bool IsClone(Player p)
        {
            try { return Phase1.Second != null && p != null && p.Pointer == Phase1.Second.Pointer; } catch { return false; }
        }

        [HarmonyPatch(typeof(UICoreForm), "OnPlayerDamageTaken")]
        private static class DamagePatch
        {
            // hand test: no damage numbers at player 2. The game writes them inside this handler (ShowPlayerDamageText at the dwarf), which is dropped for the clone so the
            // left panel does not show its health; the number alone is shown here. Twin `-lcnoclonedmgtext`.
            private static bool Prefix(UICoreForm __instance, PlayerDamageTakenArgs __0)
            {
                if (__0 == null || !IsClone(__0.Player)) return true;
                Dropped++;
                try { if (!Lc.Has("-lcnoclonedmgtext")) { __instance.ShowPlayerDamageText(-__0.Damage, __0.Player.transform.position, 1.2f); ClonePlayerTexts++; } }
                catch (Exception e) { Lc.LineOnce("hud patches: damage number for the clone threw " + e.GetType().Name + ": " + e.Message); }
                return false;
            }
        }

        [HarmonyPatch(typeof(UICoreForm), "OnPlayerHeal")]
        private static class HealPatch
        {
            private static bool Prefix(PlayerHealArgs __0) { if (__0 != null && IsClone(__0.Player)) { Dropped++; return false; } return true; }
        }

        [HarmonyPatch(typeof(UICoreForm), "OnPlayerWeaponEquipped")]
        private static class EquipPatch
        {
            private static bool Prefix(PlayerWeaponEquippedArgs __0) { if (__0 != null && IsClone(__0.Player)) { Dropped++; return false; } return true; }
        }

        [HarmonyPatch(typeof(UICoreForm), "OnPlayerWeaponRemoved")]
        private static class RemovePatch
        {
            private static bool Prefix(PlayerWeaponRemovedArgs __0)
            {
                try { if (__0 != null && __0.WeaponHandler != null && IsClone(__0.WeaponHandler.owner)) { Dropped++; return false; } } catch { }
                return true;
            }
        }
    }
}
