using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEngine;

namespace LocalCoop
{
    /// <summary>
    /// `-lcperf`: what the plugin itself costs per frame. Every tick body is timed; every 5 s one line says how many milliseconds per frame each
    /// part used (the biggest first), next to the frame time, so a frame-rate drop can be told apart: the plugin, or the game with a second dwarf.
    /// Without the flag nothing is measured.
    /// </summary>
    internal static class Perf
    {
        private static readonly bool On = Lc.Has("-lcperf");
        private static readonly Dictionary<string, long> Ticks = new Dictionary<string, long>();
        private static float _at;
        private static int _frames;
        private static readonly double ToMs = 1000.0 / Stopwatch.Frequency;

        internal static void Run(string name, Action a)
        {
            if (!On) { try { a(); } catch (Exception e) { Fail(name, e); } return; }
            long t0 = Stopwatch.GetTimestamp();
            try { a(); } catch (Exception e) { Fail(name, e); }
            long d = Stopwatch.GetTimestamp() - t0;
            long s; Ticks.TryGetValue(name, out s); Ticks[name] = s + d;
        }

        /// <summary>Measures a patched method or any other section by name (same report).</summary>
        internal static long Begin() { return On ? Stopwatch.GetTimestamp() : 0L; }
        internal static void End(string name, long t0)
        {
            if (!On) return;
            long d = Stopwatch.GetTimestamp() - t0;
            long s; Ticks.TryGetValue(name, out s); Ticks[name] = s + d;
        }

        private static void Fail(string name, Exception e)
        {
            Lc.Errors++;
            Lc.LineOnce("tick error in " + name + ": " + e.GetType().Name + ": " + e.Message);
        }

        internal static void Report()
        {
            if (!On) return;
            _frames++;
            float now = Time.realtimeSinceStartup;
            if (_at <= 0f) { _at = now; return; }
            if (now - _at < 5f) return;
            double frameMs = (now - _at) * 1000.0 / Math.Max(1, _frames);
            var list = new List<KeyValuePair<string, long>>(Ticks);
            list.Sort((a, b) => b.Value.CompareTo(a.Value));
            var sb = new StringBuilder();
            double total = 0;
            foreach (var kv in list) total += kv.Value * ToMs / _frames;
            for (int i = 0; i < list.Count && i < 8; i++) sb.Append(list[i].Key).Append('=').Append((list[i].Value * ToMs / _frames).ToString("F2")).Append(' ');
            int enemies = -1, proj = -1;
            try { enemies = UnityEngine.Object.FindObjectsOfType<Enemy>().Length; } catch { }
            Lc.Line("perf: frame " + frameMs.ToString("F1") + " ms (" + (1000.0 / frameMs).ToString("F0") + " fps), plugin ms/frame total " + total.ToString("F2") + " | " + sb + "| enemies=" + enemies + " second=" + (Phase1.Second != null));
            Ticks.Clear(); _frames = 0; _at = now;
        }
    }
}
