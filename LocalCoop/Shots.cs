using System;
using System.IO;
using UnityEngine;

namespace LocalCoop
{
    /// <summary>
    /// Screenshots from inside the game process (`ScreenCapture.CaptureScreenshot`), so no desktop
    /// capture is needed. Files land in lc-shots/ and are judged by the launcher (not all black, not
    /// all one colour), never by the mod itself.
    /// </summary>
    internal static class Shots
    {
        internal static int Taken;
        private static float _nextAt;
        private static string _dir;

        internal static void Tick()
        {
            if (Lc.ShotEvery <= 0f) return;
            // Menu shots too: the first one proves the process draws at all.
            float now = Time.realtimeSinceStartup;
            if (_nextAt <= 0f) _nextAt = now + 5f;
            if (now < _nextAt) return;
            _nextAt = now + Lc.ShotEvery;
            Take(Lc.InRunAt >= 0f ? "run" : "menu");
        }

        internal static void Take(string label)
        {
            try
            {
                if (_dir == null)
                {
                    _dir = Path.Combine(Lc.Dir, "lc-shots");
                    Directory.CreateDirectory(_dir);
                }
                string name = string.Format("shot-{0:000}-{1}-t{2:000}.png", Taken, label, (int)Time.realtimeSinceStartup);
                ScreenCapture.CaptureScreenshot(Path.Combine(_dir, name));
                Taken++;
                Lc.Line("shot " + name);
            }
            catch (Exception e) { Lc.Line("shot failed: " + e.Message); }
        }
    }
}
