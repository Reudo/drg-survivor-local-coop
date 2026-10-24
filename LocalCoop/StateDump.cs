using System;
using System.Globalization;
using System.IO;
using System.Text;
using DRS.UI;
using FundayFactory.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LocalCoop
{
    /// <summary>
    /// One JSON line per second in lc-state.jsonl. Tests read only this file, never the screen.
    /// Written fields stay stable so later phases (second dwarf, targets, camera) only add keys.
    /// </summary>
    internal static class StateDump
    {
        internal static int Samples;
        internal static int InRunSamples;
        internal static int EnemyMax;
        internal static double FpsAvg;
        private static double _fpsSum;
        private static int _fpsN;

        private static StreamWriter _w;
        private static float _nextAt;

        internal static void Tick()
        {
            float now = Time.realtimeSinceStartup;
            if (now < _nextAt) return;
            _nextAt = now + 1f;

            if (_w == null)
                _w = new StreamWriter(Path.Combine(Lc.Dir, "lc-state.jsonl"), false) { AutoFlush = true };

            var sb = new StringBuilder(512);
            var inv = CultureInfo.InvariantCulture;
            sb.Append("{\"t\":").Append(now.ToString("F1", inv));
            sb.Append(",\"frame\":").Append(Lc.Frames);
            sb.Append(",\"fps\":").Append(Lc.Fps.ToString("F1", inv));
            sb.Append(",\"scene\":\"").Append(Esc(SceneManager.GetActiveScene().name)).Append('"');
            sb.Append(",\"form\":\"").Append(Esc(AutoStart.ActiveFormName() ?? "")).Append('"');
            sb.Append(",\"timeScale\":").Append(Time.timeScale.ToString("F2", inv));

            Player main = null;
            GameController gc = null;
            try
            {
                gc = Find.One<GameController>();
                if (gc != null) main = gc.player;
            }
            catch { }

            // The title scene already has a Player object (hp 0, not alive); only a living dwarf is a run.
            // hand test: with player 1 down (the other dwarf still plays) the run counted as over and every system gated by it (HUD, tags, pause, pod...) switched off. A downed dwarf that can be revived is still a run.
            bool inRun = gc != null && main != null && (main.isAlive || (!Lc.Has("-lcnodownedrun") && Revive.Downed != null));
            sb.Append(",\"inRun\":").Append(inRun ? "true" : "false");

            if (inRun)
            {
                Lc.MarkInRun();
                try
                {
                    var p = main.position;
                    sb.Append(",\"player\":{\"x\":").Append(p.x.ToString("F2", inv))
                      .Append(",\"y\":").Append(p.y.ToString("F2", inv))
                      .Append(",\"z\":").Append(p.z.ToString("F2", inv))
                      .Append(",\"hp\":").Append(main.currentHp)
                      .Append(",\"alive\":").Append(main.isAlive ? "true" : "false").Append('}');
                }
                catch { }

                if (Lc.Auto) { try { sb.Append(",\"playerList\":").Append(Phase1.Describe()); } catch { } }
                if (Lc.Auto) try { int ne = UnityEngine.Object.FindObjectsOfType<Enemy>().Length; if (ne > EnemyMax) EnemyMax = ne; sb.Append(",\"enemies\":").Append(ne); } catch { }
                InRunSamples++;
            }
            else
            {
                Lc.MarkLeftRun();
            }

            // Cameras: only every 10th sample, it is a list walk over inactive objects too.
            if (Lc.Auto && Samples % 10 == 0)
            {
                try
                {
                    var cams = Resources.FindObjectsOfTypeAll<Camera>();
                    sb.Append(",\"cams\":[");
                    bool first = true;
                    for (int i = 0; cams != null && i < cams.Length; i++)
                    {
                        var c = cams[i];
                        if (c == null) continue;
                        if (!first) sb.Append(',');
                        first = false;
                        var tp = c.transform.position;
                        sb.Append("{\"n\":\"").Append(Esc(c.name)).Append("\",\"en\":").Append(c.enabled ? 1 : 0)
                          .Append(",\"rt\":").Append(c.targetTexture != null ? 1 : 0)
                          .Append(",\"os\":").Append(c.orthographic ? c.orthographicSize.ToString("F2", inv) : "0")
                          .Append(",\"x\":").Append(tp.x.ToString("F1", inv))
                          .Append(",\"z\":").Append(tp.z.ToString("F1", inv)).Append('}');
                    }
                    sb.Append(']');
                }
                catch { }
            }

            sb.Append('}');
            _w.WriteLine(sb.ToString());

            Samples++;
            if (Lc.Fps > 0) { _fpsSum += Lc.Fps; _fpsN++; FpsAvg = _fpsSum / _fpsN; }
        }

        private static string Esc(string s)
        {
            return (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
