using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LocalCoop
{
    /// <summary>
    /// Controller vibration with two pads. The game's Rumble writes the motor speeds to one pad and later zeroes whichever pad is "current",
    /// and it rumbles for every dwarf's damage, so with two controllers a pad could keep running. Now: rumble goes to player 1's pad(s) only,
    /// player 2's damage gives player 2's pad a short pulse of its own, and a watchdog stops every motor that has not been refreshed for a
    /// few seconds. Installed with the clone. Twin `-lcnohaptics`: the game's rumble untouched.
    /// </summary>
    internal static class Haptics
    {
        private static bool _installed;
        private static float _lastOn = -1f;       // the last time a non-zero speed was requested for player 1's pad
        private static float _p2Until = -1f;       // end of player 2's pulse
        private static float _buzzP1 = -1f, _buzzP2 = -1f;     // end of a "your turn" buzz (level-up form)
        private const float Watchdog = 2.5f;
        internal static int Routed, Pulses, Stopped;

        internal static void Install()
        {
            if (_installed || Lc.Has("-lcnohaptics")) return;
            _installed = true;
            try
            {
                var h = new Harmony("localcoop.haptics");
                foreach (var t in new[] { typeof(VibratePatch), typeof(DamagePatch) }) h.CreateClassProcessor(t).Patch();
                Lc.Line("haptics: rumble to player 1's pad, a pulse for player 2's damage, watchdog " + Watchdog + " s");
            }
            catch (Exception e) { Lc.Line("haptics: install failed: " + e.GetType().Name + ": " + e.Message); }
        }

        private static bool IsP2(Gamepad g)
        {
            var p2 = VirtualPad.P2Device();
            return p2 != null && g != null && g.deviceId == p2.deviceId;
        }

        private static void Set(bool p2Pad, float low, float high)
        {
            try
            {
                foreach (var g in Gamepad.all)
                {
                    if (g == null || IsP2(g) != p2Pad) continue;
                    g.SetMotorSpeeds(low, high);
                }
            }
            catch { }
        }

        /// <summary>A short buzz on one player's pad: "it is your turn" (level-up form).</summary>
        internal static void Buzz(bool p2)
        {
            if (Lc.Has("-lcnohaptics")) return;
            float until = Time.realtimeSinceStartup + 0.45f;
            Set(p2, 0.6f, 0.9f);
            if (p2) _buzzP2 = until; else _buzzP1 = until;
        }

        /// <summary>Every motor off (the run ended, the watchdog ran out).</summary>
        internal static void Silence()
        {
            try { foreach (var g in Gamepad.all) if (g != null) g.SetMotorSpeeds(0f, 0f); } catch { }
            _lastOn = -1f; _p2Until = -1f;
        }

        internal static void Tick()
        {
            if (!_installed) return;
            float now = Time.realtimeSinceStartup;
            if (_lastOn >= 0f && now - _lastOn > Watchdog) { Set(false, 0f, 0f); _lastOn = -1f; Stopped++; }
            if (_p2Until >= 0f && now > _p2Until) { Set(true, 0f, 0f); _p2Until = -1f; }
            if (_buzzP1 >= 0f && now > _buzzP1) { Set(false, 0f, 0f); _buzzP1 = -1f; }
            if (_buzzP2 >= 0f && now > _buzzP2) { Set(true, 0f, 0f); _buzzP2 = -1f; }
            if (Phase1.Second == null && (_lastOn >= 0f || _p2Until >= 0f)) Silence();
        }

        /// <summary>The game's Rumble.Vibrate(low, high): player 1's pad only.</summary>
        [HarmonyPatch(typeof(Rumble), "Vibrate")]
        private static class VibratePatch
        {
            private static bool Prefix(float __0, float __1)
            {
                try
                {
                    if (Phase1.Second == null) return true;
                    Routed++;
                    if (__0 > 0f || __1 > 0f) _lastOn = Time.realtimeSinceStartup;
                    Set(false, __0, __1);
                    return false;
                }
                catch { return true; }
            }
        }

        /// <summary>Damage to the second dwarf must not shake player 1's pad; player 2's pad gets a short pulse.</summary>
        [HarmonyPatch(typeof(Rumble), "OnPlayerDamageTaken")]
        private static class DamagePatch
        {
            private static bool Prefix(PlayerDamageTakenArgs __0)
            {
                try
                {
                    if (__0 == null || __0.Player == null || !Share.IsClone(__0.Player)) return true;
                    if (VirtualPad.P2Device() != null) { Set(true, 0.5f, 0.7f); _p2Until = Time.realtimeSinceStartup + 0.25f; Pulses++; }
                    return false;
                }
                catch { return true; }
            }
        }
    }
}
