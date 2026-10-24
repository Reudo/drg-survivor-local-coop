using System;
using HarmonyLib;
using UnityEngine.InputSystem;

namespace LocalCoop
{
    /// <summary>
    /// Routes movement input by device. The game's Player subscribes to the global input actions (Player.Start), so a cloned
    /// dwarf listens to the same devices as the first one. These prefixes decide per callback: input from player 2's device
    /// moves only the clone, input from every other device moves only the original. The patched methods run only when an
    /// input event arrives, never during the title-scene startup, so they are installed together with the clone.
    /// </summary>
    internal static class InputRouting
    {
        private static bool _installed;

        internal static void Install()
        {
            if (_installed) return;
            _installed = true;
            try
            {
                var h = new Harmony("localcoop.input");
                foreach (var t in new[] { typeof(OnMoveInputPatch), typeof(OnMoveStartedPatch), typeof(OnMoveEndedPatch) })
                    h.CreateClassProcessor(t).Patch();
                Lc.Line("input routing: patched OnMoveInput / OnMoveStarted / OnMoveEnded");
            }
            catch (Exception e) { Lc.Line("input routing: install failed: " + e.GetType().Name + ": " + e.Message); }
        }

        internal static int Blocked, Passed;

        /// <summary>True when this callback may run on this player.</summary>
        internal static bool Allow(Player instance, InputAction.CallbackContext ctx)
        {
            try
            {
                if (Phase1.Second == null) return true;
                bool isClone = instance != null && instance.Pointer == Phase1.Second.Pointer;
                InputDevice dev = null;
                try { var c = ctx.control; if (c != null) dev = c.device; } catch { }
                var p2 = VirtualPad.P2Device();
                bool fromP2 = dev != null && p2 != null && dev.deviceId == p2.deviceId;
                bool ok = isClone == fromP2;
                if (ok) Passed++; else Blocked++;
                return ok;
            }
            catch { return true; }
        }

        [HarmonyPatch(typeof(Player), "OnMoveInput")]
        private static class OnMoveInputPatch
        {
            private static bool Prefix(Player __instance, InputAction.CallbackContext ctx) { return Allow(__instance, ctx); }
            private static void Postfix(Player __instance) { MoveInput.Apply(__instance); }
        }

        [HarmonyPatch(typeof(Player), "OnMoveStarted")]
        private static class OnMoveStartedPatch
        {
            private static bool Prefix(Player __instance, InputAction.CallbackContext ctx) { return Allow(__instance, ctx); }
            private static void Postfix(Player __instance) { MoveInput.Apply(__instance); }
        }

        [HarmonyPatch(typeof(Player), "OnMoveEnded")]
        private static class OnMoveEndedPatch
        {
            private static bool Prefix(Player __instance, InputAction.CallbackContext ctx) { return Allow(__instance, ctx); }
            private static void Postfix(Player __instance) { MoveInput.Apply(__instance); }
        }
    }
}
