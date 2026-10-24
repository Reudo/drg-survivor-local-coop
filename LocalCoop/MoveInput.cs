using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LocalCoop
{
    /// <summary>
    /// Movement of the two dwarves from their own devices. Both dwarves hang on the same global input action, and an action reports
    /// ONE value (the strongest control of all devices) and a "move ended" only when every device is at rest. The device routing of
    /// the callbacks alone therefore breaks: when dwarf 1 lets go while controller 2 drives the action, dwarf 1 never gets the end
    /// (its callback is blocked because it came from the other device) and runs on until the player steers against it; the same
    /// the other way round. So every dwarf's `rawMoveInput`/`moveDir` is set here from its own devices, every frame and right
    /// after each of the game's three move callbacks: dwarf 2 reads controller 2's left stick, dwarf 1 reads every other
    /// device (first other gamepad, keyboard WASD/arrows). The callbacks still run (they drive the dwarf's animator).
    /// Active with a real second gamepad, or with `-lcpoll` (tests with two virtual pads). Twin `-lcnopoll`: the old behaviour.
    /// </summary>
    internal static class MoveInput
    {
        internal static bool Active
        {
            get
            {
                if (Lc.Has("-lcnopoll") || Phase1.Second == null) return false;
                if (Lc.Has("-lcpoll")) return true;
                try { return VirtualPad.IsReal && Gamepad.all != null && (Gamepad.all.Count >= 2 || (VirtualPad.Remote && Gamepad.all.Count >= 1)); } catch { return false; }
            }
        }

        private static Vector2 Dead(Vector2 v) { return v.magnitude < 0.2f ? Vector2.zero : (v.magnitude > 1f ? v.normalized : v); }

        internal static Vector2 P2Vector()
        {
            try { var g = VirtualPad.P2Device() as Gamepad; return g != null ? Dead(g.leftStick.ReadValue()) : Vector2.zero; }
            catch { return Vector2.zero; }
        }

        internal static Vector2 P1Vector()
        {
            Vector2 best = Vector2.zero;
            try
            {
                var p2 = VirtualPad.P2Device();
                foreach (var g in Gamepad.all)
                {
                    if (g == null || (p2 != null && g.deviceId == p2.deviceId)) continue;
                    var v = Dead(g.leftStick.ReadValue());
                    if (v.sqrMagnitude > best.sqrMagnitude) best = v;
                }
                var k = Keyboard.current;
                if (k != null)
                {
                    var kv = Vector2.zero;
                    if (k.wKey.isPressed || k.upArrowKey.isPressed) kv.y += 1f;
                    if (k.sKey.isPressed || k.downArrowKey.isPressed) kv.y -= 1f;
                    if (k.dKey.isPressed || k.rightArrowKey.isPressed) kv.x += 1f;
                    if (k.aKey.isPressed || k.leftArrowKey.isPressed) kv.x -= 1f;
                    if (kv.sqrMagnitude > best.sqrMagnitude) best = kv.normalized;
                }
            }
            catch { }
            return best;
        }

        /// <summary>Writes the movement input of one dwarf from its own devices (only while the player has full control).</summary>
        internal static void Apply(Player p)
        {
            if (p == null || !Active) return;
            try
            {
                if (p._moveControlType != Player.EMoveControlType.FULL_CONTROL || (Pod.Chasing && Phase1.Second != null && p.Pointer == Phase1.Second.Pointer)) return;
                bool clone = Phase1.Second != null && p.Pointer == Phase1.Second.Pointer;
                var v = clone ? P2Vector() : P1Vector();
                p.rawMoveInput = v;
                p.moveDir = v;
            }
            catch { }
        }

        internal static void Tick()
        {
            if (!Active) return;
            Apply(Phase1.First());
            Apply(Phase1.Second);
        }
    }
}
