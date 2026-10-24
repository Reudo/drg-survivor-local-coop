using System;
using Assets.Scripts.SkillSystem;
using DRS.UI;
using HarmonyLib;
using UnityEngine;

namespace LocalCoop
{
    /// <summary>
    /// `-lcwiz` (with `-lcauto`): feasibility probe for reusing the game's own mission wizard in the co-op menu.
    /// The wizard is walked by AutoStart as before. When the game calls UIManager.OnStartGameButton, this logs what the run
    /// settings hold (mission, sector, node, biome, hazard, class) and, with `-lcwizdefer`, holds the start back for 4 s and then
    /// calls it again itself (what a co-op page for player 2 would do in between). Twin `-lcwizlose`: after the hold, the run
    /// settings are reset first, so the later start must not carry the player's choice.
    /// The patch is installed only once the wizard form is on screen (never at title start).
    /// </summary>
    internal static class WizardProbe
    {
        internal static bool On { get { return Lc.Has("-lcwiz"); } }
        private static bool _installed, _held, _released, _allow;
        private static UIManager _mgr;
        private static float _heldAt;
        internal static string Seen = "";
        private static int _p2Phase; private static float _p2At; private static bool _p2Done;
        private static ClassData _p1Class; private static ClassArtifactData _p1Mod;

        private static void P2Walk(float now)
        {
            if (now < _p2At) return;
            _p2At = now + 2f;
            var f = Find.One<UIPlaySelectForm>();
            var b = AutoStart.ActiveButtons(f);
            Lc.Line("wiz: p2 phase " + _p2Phase + " buttons: " + AutoStart.Describe(b));
            string[] seq = { "UIBackButton", "UIBackButton", "ClassSelectButton_Scout", "UISelectable_Hex_ClassArtifact", "HazLevelButton" };
            if (_p2Phase >= 1 && _p2Phase <= seq.Length)
            {
                var c = AutoStart.Lowest(b, seq[_p2Phase - 1]);
                if (c != null) { AutoStart.Press(c, "p2 step " + _p2Phase + " " + seq[_p2Phase - 1]); _p2Phase++; }
                else Lc.Line("wiz: p2 step " + _p2Phase + ": no button " + seq[_p2Phase - 1]);
            }
        }

        [HarmonyPatch(typeof(UIManager), "OnStartGameButton")]
        private static class StartPatch
        {
            [HarmonyPrefix]
            private static bool Pre(UIManager __instance)
            {
                try
                {
                    Lc.Line("wiz: OnStartGameButton called by the game, allow=" + _allow + " | " + Flow.State());
                    Seen = Flow.State();
                    if (_allow) return true;
                    if (!Lc.Has("-lcwizdefer")) return true;
                    if (Lc.Has("-lcwizp2") && _p2Phase > 0)
                    {
                        _p2Done = true; _heldAt = Time.realtimeSinceStartup; _mgr = __instance;
                        Lc.Line("wiz: second start call (player 2's walk through the wizard) | " + Flow.State());
                        return false;
                    }
                    if (Lc.Has("-lcwizp2")) { _p2Phase = 1; _p2At = Time.realtimeSinceStartup + 1f; _mgr = __instance; _p1Class = CoopStart.Rsm().CurrentClass; _p1Mod = CoopStart.Rsm().CurrentClassMod; Lc.Line("wiz: first start call held; P1 class saved: " + _p1Class.Dwarf + "/" + CoopMenu.SubName(_p1Mod)); return false; }
                    _held = true; _heldAt = Time.realtimeSinceStartup; _mgr = __instance;
                    Lc.Line("wiz: start held back (a co-op page would run now)");
                    return false;
                }
                catch (Exception e) { Lc.Line("wiz: prefix threw " + e.Message); return true; }
            }
        }

        internal static void Tick()
        {
            if (!On) return;
            float now = Time.realtimeSinceStartup;
            if (!_installed)
            {
                if (AutoStart.ActiveFormName() != "UIPlaySelectForm") return;
                _installed = true;
                try { new Harmony("localcoop.wiz").CreateClassProcessor(typeof(StartPatch)).Patch(); Lc.Line("wiz: patch installed on UIManager.OnStartGameButton"); }
                catch (Exception e) { Lc.Line("wiz: patch failed " + e.GetType().Name + ": " + e.Message); }
                return;
            }
            if (_p2Phase >= 1 && _p2Phase < 6 && !_p2Done) P2Walk(now);
            if (_p2Phase == 6 && _p2Done && !_released)
            {
                _released = true;
                var rsm = CoopStart.Rsm();
                string p2 = rsm.CurrentClass.Dwarf + "/" + CoopMenu.SubName(rsm.CurrentClassMod);
                Lc.Line("wiz: P2's pick read from the run settings: " + p2);
                rsm.SetClass(_p1Class); rsm.SetClassMod(_p1Mod);
                Lc.Line("wiz: P1's class restored | " + Flow.State());
                Seen = Flow.State();
                _allow = true;
                try { _mgr.OnStartGameButton(); Lc.Line("wiz: start released"); } catch (Exception e) { Lc.Line("wiz: release threw " + e.Message); }
                _held = true;
            }
            if (_held && !_released && now > _heldAt + 4f)
            {
                _released = true;
                Lc.Line("wiz: form while held: " + AutoStart.ActiveFormName() + " | " + Flow.State());
                if (Lc.Has("-lcwizlose"))
                {
                    try { var rsm = CoopStart.Rsm(); rsm.PrepNewRun(EMissionType.Campaign, null); Lc.Line("wiz: twin: run settings reset | " + Flow.State()); }
                    catch (Exception e) { Lc.Line("wiz: twin reset threw " + e.Message); }
                }
                _allow = true;
                try { _mgr.OnStartGameButton(); Lc.Line("wiz: start released | " + Flow.State()); }
                catch (Exception e) { Lc.Line("wiz: release threw " + e.GetType().Name + ": " + e.Message); }
            }
            if (_released && Lc.InRunAt >= 0f && now > Lc.InRunAt + 6f && !Lc.QuitSent)
            {
                Lc.QuitSent = true;
                Lc.Line("wiz: in run | " + Flow.State());
                bool same = Flow.State() == Seen;
                Lc.Line("RESULT " + Lc.Test + (same ? " PASS" : " FAIL") + " wiz in-run, settings at start " + (same ? "equal to" : "DIFFERENT from") + " the player's choice: " + Seen + " vs " + Flow.State());
                Application.Quit();
            }
            if (_released && Lc.InRunAt < 0f && now > _heldAt + 90f && !Lc.QuitSent)
            {
                Lc.QuitSent = true;
                Lc.Line("RESULT " + Lc.Test + " FAIL wiz no run after release");
                Application.Quit();
            }
        }
    }
}
