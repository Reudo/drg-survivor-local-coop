using System;
using System.Globalization;
using System.IO;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using UnityEngine;

namespace LocalCoop
{
    /// <summary>
    /// Local co-op for Deep Rock Galactic: Survivor: a second real dwarf in the same game instance.
    /// Phase 0 is only the floor everything else stands on: a log, a state dump, screenshots and a
    /// way to get into a run without a human. No gameplay change is made here.
    ///
    /// Rules carried over from the network prototype (see its notes):
    ///  - Never Harmony-patch a method the game calls while the title scene starts (process dies ~5 s
    ///    after launch, coreclr c0000005). Phase 0 installs no patch at all.
    ///  - Every tick body is wrapped, a bug here must never take the game down.
    /// </summary>
    [BepInPlugin("localcoop.drgsurvivor", "DRG Survivor local co-op", "1.0.2")]
    public class Plugin : BasePlugin
    {
        public override void Load()
        {
            Lc.Init(Log);
            AddComponent<Tick>();
            Lc.Line("plugin loaded v1.0.2 args=" + string.Join(" ", Environment.GetCommandLineArgs()));
        }
    }

    public class Tick : MonoBehaviour
    {
        public Tick(IntPtr ptr) : base(ptr) { }

        private void Update()
        {
            try
            {
                Lc.Frame();
                try { var kb = UnityEngine.InputSystem.Keyboard.current; if (kb != null && kb.f9Key.wasPressedThisFrame) { Chooser.Diag(); Lc.Line("f9: input state written (form=" + AutoStart.ActiveFormName() + ")"); } } catch { }
                MenuPad.NavWatch();
                Lc.LogPads();
                Perf.Run("SaveSlot", SaveSlot.Tick);
                Perf.Run("UnlockProbe", UnlockProbe.Tick);
                Perf.Run("AutoStart", AutoStart.Tick);
                Perf.Run("StateDump", StateDump.Tick);
                Perf.Run("Shots", Shots.Tick);
                Perf.Run("Phase1", Phase1.Tick);
                Perf.Run("MoveInput", MoveInput.Tick);
                Perf.Run("BulletHits", BulletHits.Tick);
                Perf.Run("UiProbe", UiProbe.Tick);
                Perf.Run("WizardProbe", WizardProbe.Tick);
                Perf.Run("Flow", Flow.Tick);
                Perf.Run("MenuEntry", MenuEntry.Tick);
                Perf.Run("MenuPad", MenuPad.Tick);
                Perf.Run("LevelRounds", LevelRounds.Tick);
                Perf.Run("LevelBanner", LevelBanner.Tick);
                Perf.Run("WalletHud", WalletHud.Tick);
                Perf.Run("UiTest", UiTest.Tick);
                Perf.Run("Cam2", Cam2.Tick);
                Perf.Run("Hud2", Hud2.Tick);
                Perf.Run("Chooser", Chooser.Tick);
                Perf.Run("Loc", Loc.Tick);
                Perf.Run("PickupWatch", Bonus.PickupWatch);
                Perf.Run("XpTest", Bonus.XpTest);
                Perf.Run("PickupOwner", Bonus.OwnerTick);
                Perf.Run("XpCubes", Bonus.CubeTick);
                Perf.Run("CubeTest", Bonus.CubeTest);
                Perf.Run("Revive", Revive.Tick);
                Perf.Run("MeleeTest", Melee.Test);
                Perf.Run("TurretTest", TurretTest.Tick);
                Perf.Run("SoundTest", SoundTest.Tick);
                Perf.Run("CrateTest", Bonus.CrateTest);
                Perf.Run("SlotArrowTest", Bonus.SlotArrowTest);
                Perf.Run("ArrowProbe", Bonus.ArrowProbe);
                Perf.Run("Pod", Pod.Tick);
                Perf.Run("Arrow2Test", Arrow2.Test);
                Perf.Run("AltF4Probe", AltF4Probe.Tick);
                Perf.Run("Arrow2Real", Arrow2.RealShot);
                Perf.Run("Arrow2Keep", Arrow2.Keep);
                Perf.Run("DrillTest", DrillSound.Test);
                Perf.Run("EndScreen2Test", EndScreen2.Test);
                Perf.Run("EndScreen2Titles", EndScreen2.KeepTitles);
                Perf.Run("RunProgress", RunProgress.Tick);
                Perf.Run("ProgressTest", RunProgress.Test);
                Perf.Run("MilestoneRepair", RunProgress.Repair);
                Perf.Run("SupplyTest", LevelBanner.Test);
                Perf.Run("Buffs2", Buffs2.Tick);
                Perf.Run("Buffs2Test", Buffs2.Test);
                Perf.Run("MoveTest", Buffs2.MoveTest);
                Perf.Run("SlotSwap", Bonus.SlotSwapTick);
                Perf.Run("Drone2", Drone2.Tick);
                Perf.Run("MiniMap2", MiniMap2.Tick);
                Perf.Run("MiniMap2Test", MiniMap2.Test);
                Perf.Run("PauseView", PauseView.Tick);
                Perf.Run("Zones", Zones.Tick);
                Perf.Run("WeaponLog", WeaponLog.Tick);
                Perf.Run("StatLog", StatLog.Tick);
                Perf.Run("Tags", Tags.Tick);
                Perf.Run("Haptics", Haptics.Tick);
                Perf.Run("VirtualPad", VirtualPad.Tick);
                Lc.TickQuit();
                Perf.Report();
            }
            catch (Exception e)
            {
                Lc.Errors++;
                Lc.Line("tick error: " + e.GetType().Name + ": " + e.Message);
            }
        }
    }

    /// <summary>Shared helpers: flags, log file, frame statistics, the end of a test run.</summary>
    internal static class Lc
    {
        internal static int Errors;
        internal static int Frames;
        internal static float Fps;
        internal static string Dir;

        private static BepInEx.Logging.ManualLogSource _log;
        private static StreamWriter _w;
        private static readonly object Gate = new object();
        private static float _fpsAt;
        private static int _fpsFrames;

        // Flags (all optional, a normal launch sets none of them).
        internal static bool Auto;          // -lcauto   : walk the menu into a run
        internal static float QuitAfter;    // -lcquit=s : seconds after "in run" until RESULT + quit
        internal static float ShotEvery;    // -lcshots=s: screenshot interval while playing (0 = off)
        internal static string Test = "";   // -lctest=name

        internal static float InRunAt = -1f;
        internal static bool QuitSent { get { return _quitSent; } set { _quitSent = value; } }
        private static bool _quitSent;
        /// <summary>Seconds after launch without a run before the test gives up by itself.</summary>
        internal const float NoRunTimeout = 120f;

        internal static void Init(BepInEx.Logging.ManualLogSource log)
        {
            _log = log;
            Dir = Paths.GameRootPath;
            Auto = Has("-lcauto");
            QuitAfter = Num("-lcquit=");
            ShotEvery = Num("-lcshots=");
            Test = Str("-lctest=");
            Phase1.Init();
            _godFlag = Has("-lcgod");
            Cam2.Init();
            Hud2.Init();
            try
            {
                _w = new StreamWriter(Path.Combine(Dir, "lc.log"), false) { AutoFlush = true };
                _w.WriteLine("# local co-op log " + DateTime.Now.ToString("s"));
            }
            catch { }
            Settings.Load();
        }

        internal static void Line(string s)
        {
            string t = Time.realtimeSinceStartup.ToString("F1", CultureInfo.InvariantCulture);
            try { lock (Gate) { if (_w != null) _w.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + " [" + t + "] " + s); } } catch { }
        }

        private static readonly System.Collections.Generic.HashSet<string> Once = new System.Collections.Generic.HashSet<string>();
        internal static void LineOnce(string s) { if (Once.Add(s)) Line(s); }

        internal static void Frame()
        {
            if (Frames == 0) AllowBackgroundInput();
            Frames++;
            _fpsFrames++;
            float now = Time.realtimeSinceStartup;
            if (_fpsAt <= 0f) _fpsAt = now;
            if (now - _fpsAt >= 1f)
            {
                Fps = _fpsFrames / (now - _fpsAt);
                _fpsFrames = 0;
                _fpsAt = now;
            }
        }

        internal static bool EverInRun;
        private static bool _godFlag;
        /// <summary>Test flag -lcgod, or the setting DebugGodMode while Debug is on: neither dwarf can die.</summary>
        internal static bool God { get { return _godFlag || Settings.Debug && Settings.DebugGodMode; } }


        /// <summary>
        /// Test runs happen while the Windows session may be locked or another window has the focus. Unity ignores
        /// input without focus by default, which is why splash clicks only worked about every other run.
        /// Only when a test flag is set; a normal launch keeps the game's own behaviour.
        /// </summary>
        private static string _padSig = "?"; private static float _padAt;

        /// <summary>Writes the connected gamepads (and all input devices) to lc.log whenever they change: with Steam Input a game started through another app can see none.</summary>
        internal static void LogPads()
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now < _padAt) return;
            _padAt = now + 2f;
            try
            {
                var sb = new System.Text.StringBuilder();
                foreach (var g in UnityEngine.InputSystem.Gamepad.all) sb.Append(g.displayName).Append('#').Append(g.deviceId).Append(' ');
                var all = new System.Text.StringBuilder();
                foreach (var d in UnityEngine.InputSystem.InputSystem.devices) all.Append(d.layout).Append(' ');
                string sig = sb + "|" + all;
                if (sig == _padSig) return;
                _padSig = sig;
                Line("input: gamepads now: " + (sb.Length > 0 ? sb.ToString() : "none") + "| all devices: " + all);
            }
            catch { }
        }

        private static void AllowBackgroundInput()
        {
            if (!Auto && !Has("-lcremote")) return;     // Remote Play: the guest's controller must work although the host's window is not in front
            try
            {
                var st = UnityEngine.InputSystem.InputSystem.settings;
                st.backgroundBehavior = UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus;
                Line("input: backgroundBehavior=IgnoreFocus");
            }
            catch (Exception e) { Line("input: backgroundBehavior failed " + e.GetType().Name + ": " + e.Message); }
        }

        internal static void MarkInRun()
        {
            if (InRunAt >= 0f) return;
            EverInRun = true;
            InRunAt = Time.realtimeSinceStartup;
            Line("IN RUN");
        }

        internal static void MarkLeftRun() { InRunAt = -1f; }

        /// <summary>`-lcquit`: after that many seconds in a run write RESULT and close the game by itself.</summary>
        internal static void TickQuit()
        {
            if (_quitSent || QuitAfter <= 0f) return;
            // Acceptance runs write their own RESULT; the generic boot verdict must not speak for them.
            if (Str("-lcui=").Length > 0) return;     // the co-op menu tests write their own RESULT
            if ((Phase1.AcceptKind.Length > 0 || (Phase1.Mode == "clone-cam" || Phase1.Mode == "clone-hud" || Phase1.Mode == "clone-leash")) && Phase1.Second != null) return;
            float now = Time.realtimeSinceStartup;
            bool neverStarted = !EverInRun && now > NoRunTimeout;
            if (InRunAt >= 0f && now - InRunAt < QuitAfter) return;
            if (!EverInRun && !neverStarted) return;
            _quitSent = true;

            // A run needs a living dwarf and enemies; reaching a menu or a dummy player is not a run.
            bool pass = !neverStarted && StateDump.Samples >= 5 && StateDump.InRunSamples >= 3
                && StateDump.EnemyMax > 0 && Errors == 0;
            Line("RESULT " + (Test.Length > 0 ? Test : "boot") + " " + (pass ? "PASS" : "FAIL")
                + " samples=" + StateDump.Samples + (neverStarted ? " NEVER-REACHED-A-RUN" : "") + " enemyMax=" + StateDump.EnemyMax + " inRunSamples=" + StateDump.InRunSamples
                + " shots=" + Shots.Taken + " errors=" + Errors + " fpsAvg=" + StateDump.FpsAvg.ToString("F1", CultureInfo.InvariantCulture));
            Application.Quit();
        }

        internal static bool Has(string flag)
        {
            foreach (var a in Environment.GetCommandLineArgs())
                if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase)) return true;
            if (Settings.Debug && (flag == "-lcpickupwatch" || flag == "-lclvltrace" || flag == "-lcarrowlog")) return true;       // debug mode = the log-only traces
            return false;
        }

        internal static string Str(string prefix)
        {
            foreach (var a in Environment.GetCommandLineArgs())
                if (a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return a.Substring(prefix.Length);
            return "";
        }

        internal static float Num(string prefix)
        {
            float v;
            return float.TryParse(Str(prefix), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0f;
        }
    }
}
