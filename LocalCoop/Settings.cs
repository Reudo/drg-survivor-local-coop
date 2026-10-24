using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace LocalCoop
{
    /// <summary>
    /// General settings in a plain text file in the BepInEx config folder: `BepInEx\config\localcoop-settings.txt` (key = value, # starts a comment). Created with the defaults on the
    /// first co-op start, read at every start. Unknown keys and unreadable values are ignored (the default stays) and named in lc.log.
    /// </summary>
    internal static class Settings
    {
        internal const string FileName = "localcoop-settings.txt";

        /// <summary>How much health a revived dwarf gets: "percent" of its max HP or a "flat" number of HP.</summary>
        internal static string ReviveMode = "percent";
        internal static float RevivePercent = 20f;
        internal static float ReviveFlat = 20f;
        /// <summary>How long the other dwarf has to stand next to a downed one (seconds, real time).</summary>
        internal static float ReviveSeconds = 3f;
        /// <summary>Experimental: the second dwarf gets a companion drone (Bosco) of its own instead of sharing the host's.</summary>
        internal static bool SecondDrone;
        /// <summary>Debug mode: extra trace lines in lc.log (loose pickups near the second dwarf, level-up rounds, card arrows).</summary>
        internal static bool Debug;
        /// <summary>Only with Debug = on: neither dwarf can die (health is topped up).</summary>
        internal static bool DebugGodMode;

        /// <summary>BepInEx\config\localcoop-settings.txt (older versions kept it in the game folder: moved there at the first start).</summary>
        internal static string PathOf { get { return System.IO.Path.Combine(BepInEx.Paths.ConfigPath, FileName); } }
        private static string OldPath { get { return System.IO.Path.Combine(Lc.Dir ?? ".", FileName); } }

        // lines added to an existing file that was made by an older version and lacks them (the owner's values stay untouched)
        private static readonly string[][] Additions =
        {
            new[] { "ReviveSeconds", "# Revive time: seconds the other dwarf has to stand within 3 m of the downed one.", "ReviveSeconds = 3" },
            new[] { "SecondDrone", "# Companion drone (experimental): on = player 2 gets a drone of its own, off = both share the one drone.", "SecondDrone = off" },
            new[] { "Debug", "# Debug mode (on / off): writes extra trace lines into lc.log (loose pickups near player 2, level-up rounds, card arrows). For bug reports.", "Debug = off" },
            new[] { "DebugGodMode", "# God mode (on / off): only counts while Debug = on. Neither dwarf can die.", "DebugGodMode = off" },
        };

        private const string Template =
            "# Deep Rock Galactic: Survivor - Local Co-op settings\r\n" +
            "# Lines starting with # are comments. Changes apply the next time the game is started.\r\n" +
            "\r\n" +
            "# Revive: how much health a revived dwarf gets.\r\n" +
            "#   ReviveMode = percent   -> ReviveHealthPercent of the dwarf's max HP\r\n" +
            "#   ReviveMode = flat      -> ReviveHealthFlat hit points\r\n" +
            "ReviveMode = percent\r\n" +
            "ReviveHealthPercent = 20\r\n" +
            "ReviveHealthFlat = 20\r\n" +
            "\r\n" +
            "# Revive time: seconds the other dwarf has to stand within 3 m of the downed one.\r\n" +
            "ReviveSeconds = 3\r\n" +
            "\r\n" +
            "# Companion drone (experimental): on = player 2 gets a drone of its own, off = both share the one drone.\r\n" +
            "SecondDrone = off\r\n";

        internal static void Load()
        {
            try
            {
                string path = PathOf;
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                if (!File.Exists(path) && File.Exists(OldPath)) { File.Move(OldPath, path); Lc.Line("settings: " + FileName + " moved from the game folder to " + path); }
                if (!File.Exists(path)) { File.WriteAllText(path, Template, new UTF8Encoding(false)); Lc.Line("settings: " + FileName + " created with the defaults"); }
                var kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var raw in File.ReadAllLines(path))
                {
                    string line = raw; int hash = line.IndexOf('#'); if (hash >= 0) line = line.Substring(0, hash);
                    int eq = line.IndexOf('='); if (eq <= 0) continue;
                    kv[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
                string v;
                var add = new StringBuilder();
                foreach (var a in Additions) if (!kv.ContainsKey(a[0])) { add.Append("\r\n").Append(a[1]).Append("\r\n").Append(a[2]).Append("\r\n"); Lc.Line("settings: " + a[0] + " was missing in the file and was added with its default"); }
                if (add.Length > 0) { File.AppendAllText(path, add.ToString(), new UTF8Encoding(false)); foreach (var a in Additions) if (!kv.ContainsKey(a[0])) kv[a[0]] = a[2].Substring(a[2].IndexOf('=') + 1).Trim(); }
                if (kv.TryGetValue("ReviveMode", out v)) { v = v.ToLowerInvariant(); if (v == "percent" || v == "flat") ReviveMode = v; else Lc.Line("settings: ReviveMode '" + v + "' is not percent or flat, default kept"); }
                ReviveHpPercentFrom(kv); ReviveFlatFrom(kv);
                { float sec; if (Num(kv, "ReviveSeconds", out sec)) ReviveSeconds = Math.Max(0.5f, Math.Min(60f, sec)); }
                { float o = Lc.Num("-lcreviveseconds="); if (o > 0.1f) ReviveSeconds = o; }          // test twin: a different time than the default
                if (kv.TryGetValue("SecondDrone", out v)) { v = v.ToLowerInvariant(); if (v == "on" || v == "true" || v == "1" || v == "yes") SecondDrone = true; else if (v == "off" || v == "false" || v == "0" || v == "no") SecondDrone = false; else Lc.Line("settings: SecondDrone '" + v + "' is not on or off, default kept"); }
                Debug = Flag(kv, "Debug"); DebugGodMode = Flag(kv, "DebugGodMode");
                if (Debug) Lc.Line("settings: DEBUG MODE ON" + (DebugGodMode ? ", GOD MODE ON (neither dwarf can die)" : ""));
                else if (DebugGodMode) Lc.Line("settings: DebugGodMode = on is ignored because Debug is off");
                Lc.Line("settings: revive mode=" + ReviveMode + " percent=" + RevivePercent.ToString("F1", CultureInfo.InvariantCulture) + " flat=" + ReviveFlat.ToString("F0", CultureInfo.InvariantCulture) + " seconds=" + ReviveSeconds.ToString("F1", CultureInfo.InvariantCulture) + " (" + path + ")");
            }
            catch (Exception e) { Lc.Line("settings: could not read " + FileName + ": " + e.GetType().Name + ": " + e.Message + " (defaults)"); }
        }

        private static bool Flag(Dictionary<string, string> kv, string key)
        {
            string v;
            if (!kv.TryGetValue(key, out v)) return false;
            v = v.ToLowerInvariant();
            if (v == "on" || v == "true" || v == "1" || v == "yes") return true;
            if (!(v == "off" || v == "false" || v == "0" || v == "no")) Lc.Line("settings: " + key + " '" + v + "' is not on or off, off kept");
            return false;
        }

        private static bool Num(Dictionary<string, string> kv, string key, out float value)
        {
            value = 0f; string v;
            if (!kv.TryGetValue(key, out v)) return false;
            v = v.Replace("%", "").Replace(',', '.').Trim();
            if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return true;
            Lc.Line("settings: " + key + " '" + v + "' is not a number, default kept");
            return false;
        }

        private static void ReviveHpPercentFrom(Dictionary<string, string> kv) { float f; if (Num(kv, "ReviveHealthPercent", out f)) RevivePercent = Math.Max(1f, Math.Min(100f, f)); }
        private static void ReviveFlatFrom(Dictionary<string, string> kv) { float f; if (Num(kv, "ReviveHealthFlat", out f)) ReviveFlat = Math.Max(1f, f); }

        /// <summary>Health of a revived dwarf with the given max HP.</summary>
        internal static int ReviveHp(double maxHp)
        {
            if (ReviveMode == "flat") return (int)Math.Max(1, Math.Min(Math.Round(ReviveFlat), Math.Max(1, Math.Round(maxHp))));
            return Math.Max(1, (int)Math.Round(maxHp * RevivePercent / 100.0));
        }
    }
}
