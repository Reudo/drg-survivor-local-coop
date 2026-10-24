using System;
using Assets.Scripts.Audio;
using HarmonyLib;
using UnityEngine;

namespace LocalCoop
{
    /// <summary>
    /// The drill sound of the Driller. hand test: no drill sound for the Driller as player 2. The game has one handler (`DrillerAudioHandler`) with one loop:
    /// it follows the host only and is switched on only when the host's class is the Driller (audio name and flag are read once from the host's class). Here the handler
    /// is pointed at player 2 for a tick when player 2 is a Driller and the host is not, or when both are Drillers and only player 2 is mining. Twin `-lcnodrillsound`.
    /// </summary>
    internal static class DrillSound
    {
        internal static int Swaps; internal static bool ForceMining;
        internal static DrillerAudioHandler Handler;
        private static Player _hostPlayer; private static bool _swapped, _wasDriller; private static string _wasLoop;

        private static bool IsDriller(Player p)
        {
            try { return p != null && p.CurrentClass != null && p.CurrentClass.Dwarf.ToString() == "DRILLER"; } catch { return false; }
        }

        [HarmonyPatch(typeof(DrillerAudioHandler), "Tick")]
        internal static class TickPatch
        {
            private static void Prefix(DrillerAudioHandler __instance)
            {
                _swapped = false;
                try
                {
                    Handler = __instance; if (ForceMining && Phase1.Second != null) Phase1.Second.isMining = true;
                    var c = Phase1.Second;
                    if (Lc.Has("-lcnodrillsound") || c == null || !c.isAlive || !IsDriller(c)) return;
                    var h = __instance.player;
                    if (h == null) return;
                    bool hostDrill = __instance.isDriller;
                    bool useClone = !hostDrill || (!h.isMining && c.isMining);
                    if (!useClone) return;
                    _hostPlayer = h; _wasDriller = hostDrill; _wasLoop = __instance.audioLoop;
                    __instance.player = c; __instance.isDriller = true;
                    if (!hostDrill && c.CurrentClass != null) __instance.audioLoop = c.CurrentClass.DrillLoopAudio;
                    _swapped = true; Swaps++;
                }
                catch (Exception e) { Lc.LineOnce("drillsound: prefix threw " + e.GetType().Name + ": " + e.Message); }
            }

            private static void Finalizer(DrillerAudioHandler __instance)
            {
                if (!_swapped) return;
                try { __instance.player = _hostPlayer; __instance.isDriller = _wasDriller; __instance.audioLoop = _wasLoop; } catch { }
                _swapped = false;
            }
        }

        // ---- test (`-lcdrilltest`, host not a Driller, player 2 a Driller): player 2 mines, the loop must come up ---------------------------
        internal static bool TestDone;
        private static int _step; private static float _in = -1f, _at = -1f;

        internal static void Test()
        {
            if (TestDone || !Lc.Has("-lcdrilltest")) return;
            float now = Time.realtimeSinceStartup;
            if (Lc.InRunAt < 0f || Phase1.Second == null) return;
            if (_in < 0f) _in = now;
            if (now < _in + 12f || now < _at) return;
            try
            {
                var c = Phase1.Second;
                if (_step == 0) { ForceMining = true; _at = now + 1.5f; _step++; return; }
                bool loop = false; try { var pi = Handler != null ? Handler.GetType().GetProperty("srcLoop", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic) : null; loop = pi != null && pi.GetValue(Handler, null) != null; Lc.Line("drillsound: srcLoop property found=" + (pi != null)); } catch { }
                bool ok = Swaps > 0 && loop;
                ForceMining = false; c.isMining = false;
                Lc.Line("ui: CHECK " + (ok ? "ok  " : "FAIL") + " the drill loop plays for the Driller as player 2 - swaps=" + Swaps + " loop source=" + loop);
                TestDone = true; UiTest.FinishWith(ok, "drill sound swaps=" + Swaps + " loop=" + loop);
            }
            catch (Exception e) { TestDone = true; UiTest.FinishWith(false, "drill test threw " + e.GetType().Name + ": " + e.Message); }
        }
    }
}
