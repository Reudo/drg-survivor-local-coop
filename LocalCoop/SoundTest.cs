using System;
using System.Text;
using Assets.Scripts.SkillSystem;
using DRS.UI;
using UnityEngine;

namespace LocalCoop
{
    /// <summary>
    /// `-lcsoundtest` (with -lcui=start -lcgod): hand test, a weapon sound (or something else) kept playing in the shop and while clicking through the unlocks before the end screen.
    /// Both dwarves get `-lcsoundweapon=` (default the flamethrower, a looping sound); after a while the shop opens the way the game opens it between stages, and the playing audio
    /// sources and each dwarf's weapons' pause state are logged while it is open. Diagnosis first: what is still playing.
    /// </summary>
    internal static class SoundTest
    {
        internal static bool TestDone;
        private static int _step; private static float _at = -1f;

        private static string Playing()
        {
            var sb = new StringBuilder(); int n = 0;
            try
            {
                foreach (var s in UnityEngine.Object.FindObjectsOfType<AudioSource>())
                {
                    if (s == null || !s.isPlaying) continue;
                    n++;
                    string owner = s.transform.IsChildOf(Phase1.Second.transform) ? "P2" : s.transform.IsChildOf(Phase1.First().transform) ? "P1" : "-";
                    sb.Append(" [").Append(s.gameObject.name).Append(" clip=").Append(s.clip != null ? s.clip.name : "-").Append(" loop=").Append(s.loop).Append(" owner=").Append(owner).Append(" vol=").Append(s.volume.ToString("F2")).Append(']');
                }
            }
            catch (Exception e) { sb.Append(" threw " + e.GetType().Name); }
            return n + sb.ToString();
        }

        private static string Paused(Player p)
        {
            var sb = new StringBuilder();
            try { for (int i = 0; p.weapons != null && i < p.weapons.Count; i++) { var w = p.weapons[i]; if (w != null) sb.Append(w.data.name).Append(" paused=").Append(w.paused).Append("; "); } } catch { }
            return sb.ToString() + " weaponsActive=" + p.weaponsActive;
        }

        // ---- always on, log only: when the game stops (time scale 0) or another form comes up during a run, which looping sounds still play and whether the
        // dwarves' weapons are paused (hand test: a weapon sound kept playing in the shop and while clicking through the unlocks; the real shop could not be reached in a test).
        private static bool _wasStopped; private static string _lastForm = ""; private static float _snapAt;

        internal static void Watch()
        {
            if (Phase1.Second == null || Phase1.First() == null) return;
            bool stopped = Time.timeScale < 0.01f; string form = AutoStart.ActiveFormName() ?? "";
            bool changed = (stopped && !_wasStopped) || (form != _lastForm && form != "UICoreForm" && form.Length > 0);
            _wasStopped = stopped; _lastForm = form;
            if (!changed || Time.realtimeSinceStartup < _snapAt) return;
            _snapAt = Time.realtimeSinceStartup + 1f;
            var sb = new StringBuilder(); int n = 0;
            try { foreach (var s in UnityEngine.Object.FindObjectsOfType<AudioSource>()) if (s != null && s.isPlaying && s.loop && s.volume > 0.01f) { n++; sb.Append(s.clip != null ? s.clip.name : "-").Append(' '); } } catch { }
            Lc.Line("sound: game stopped=" + stopped + " form=" + form + " looping sounds " + n + ": " + sb + "| P1 " + Paused(Phase1.First()) + " pod=" + Phase1.First().isInDropPod + " | P2 " + Paused(Phase1.Second) + " pod=" + Phase1.Second.isInDropPod);
        }

        internal static void Tick()
        {
            try { Watch(); } catch { }
            if (TestDone || !Lc.Has("-lcsoundtest")) return;
            var h = Phase1.First(); var c = Phase1.Second;
            if (h == null || c == null || Lc.InRunAt < 0f) return;
            float now = Time.realtimeSinceStartup;
            if (_at < 0f) { _at = now + 10f; return; }
            if (now < _at) return;
            try
            {
                switch (_step)
                {
                    case 0:
                        string want = Lc.Str("-lcsoundweapon="); if (want.Length == 0) want = "BeamWeaponSkill_RotatingBeam_Flamethrower";
                        foreach (var w in Resources.FindObjectsOfTypeAll<WeaponSkillData>()) if (w != null && w.name == want) { if (!Lc.Has("-lcsoundp1off")) h.EquipNewWeapon(w, false); c.EquipNewWeapon(w, false); Lc.Line("sound: both got " + w.name); break; }
                        _step = 1; _at = now + 6f; break;
                    case 1:
                        Lc.Line("sound: in the run, playing: " + Playing());
                        Lc.Line("sound: P1 " + Paused(h) + " | P2 " + Paused(c));
                        var ui = Find.One<UIManager>();
                        var types = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<ESkillType>(3);
                        types[0] = ESkillType.SUPPORT; types[1] = ESkillType.WEAPON_UPGRADE; types[2] = ESkillType.ARTIFACT;
                        ui.ShopScreen.Show(CoopStart.Resolve<SkillCollectionManager>(), types, true);
                        _step = 2; _at = now + 3f; break;
                    case 2:
                    case 3:
                    case 4:
                        Lc.Line("sound: shop open (" + AutoStart.ActiveFormName() + ", timeScale " + Time.timeScale + "), playing: " + Playing());
                        Lc.Line("sound: P1 " + Paused(h) + " | P2 " + Paused(c));
                        _step++; _at = now + 2f;
                        if (_step == 5) { Shots.Take("sound-shop"); TestDone = true; UiTest.FinishWith(true, "sound diagnosis written"); }
                        break;
                }
            }
            catch (Exception e) { TestDone = true; UiTest.FinishWith(false, "sound test threw " + e.GetType().Name + ": " + e.Message); }
        }
    }
}
