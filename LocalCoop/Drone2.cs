using System;
using Assets.Scripts;
using UnityEngine;
using Zenject;

namespace LocalCoop
{
    /// <summary>
    /// Experiment `-lcdrone2`: the host and the second dwarf share ONE companion drone (Bosco: `Player.bosco` is the same object, the second dwarf has no
    /// Bosco weapon). Here the second dwarf gets a drone of its own: the host's Bosco is copied, injected, owned by the second dwarf, and the
    /// second dwarf equips the Bosco starter weapon. Off by default; the log lines `drone2:` say what happened.
    /// </summary>
    internal static class Drone2
    {
        private static int _stage;
        private static float _at = -1f;
        private static Bosco _own;
        internal static string Summary = "";

        private static string V(Vector3 v) { return "(" + v.x.ToString("F1") + "," + v.y.ToString("F1") + "," + v.z.ToString("F1") + ")"; }

        private static string src0State(Player h) { try { return h.bosco.state.ToString(); } catch { return "?"; } }

        internal static void Tick()
        {
            if (!(Lc.Has("-lcdrone2") || Settings.SecondDrone) || Phase1.Second == null || !Phase1.Ready || _stage >= 4) return;
            float now = Time.realtimeSinceStartup;
            try
            {
                var h = Phase1.First(); var c = Phase1.Second;
                if (h == null || c == null || h.bosco == null) return;
                if (_at < 0f) { _at = now + 4f; return; }
                if (now < _at) return;
                switch (_stage)
                {
                    case 0:
                    {
                        var src = h.bosco;
                        var go = UnityEngine.Object.Instantiate(src.gameObject, src.transform.position + new Vector3(2f, 0f, 0f), src.transform.rotation);
                        go.name = "Bosco2";
                        var ctx = Find.One<SceneContext>();
                        if (ctx != null && ctx.Container != null) ctx.Container.InjectGameObject(go);
                        _own = go.GetComponent<Bosco>();
                        Lc.Line("drone2: copy made, bosco component=" + (_own != null) + " injected=" + (ctx != null));
                        if (_own == null) { _stage = 9; return; }
                        Phase1.SetRef(_own, typeof(Bosco), "player", c.Pointer);
                        Phase1.SetRef(c, typeof(Player), "bosco", _own.Pointer);
                        try { _own.OnStageBegin(); } catch (Exception e) { Lc.Line("drone2: OnStageBegin threw " + e.GetType().Name + ": " + e.Message); }
                        try { _own.SetState(EBoscoState.FOLLOW_PLAYER); } catch (Exception e) { Lc.Line("drone2: SetState threw " + e.GetType().Name + ": " + e.Message); }
                        try { c.EquipBoscoStarterWeapon(); } catch (Exception e) { Lc.Line("drone2: EquipBoscoStarterWeapon threw " + e.GetType().Name + ": " + e.Message); }
                        _stage = 1; _at = now + 3f; break;
                    }
                    case 1:
                    case 2:
                    case 3:
                    {
                        int n = UnityEngine.Object.FindObjectsOfType<Bosco>().Length;
                        float dc = _own != null ? Vector3.Distance(_own.transform.position, c.transform.position) : -1f;
                        float dh = _own != null ? Vector3.Distance(_own.transform.position, h.transform.position) : -1f;
                        int wh = h.boscoWeapons != null ? h.boscoWeapons.Count : -1, wc = c.boscoWeapons != null ? c.boscoWeapons.Count : -1;
                        bool same = h.bosco != null && c.bosco != null && h.bosco.Pointer == c.bosco.Pointer;
                        Summary = "states host/own=" + src0State(h) + "/" + (_own != null ? _own.state.ToString() : "-") + " active host/own=" + (h.bosco != null && h.bosco.isActive) + "/" + (_own != null && _own.isActive) + " boscos=" + n + " ownDistToClone=" + dc.ToString("F1") + " ownDistToHost=" + dh.ToString("F1") + " hostWeapons=" + wh + " cloneWeapons=" + wc + " sameObject=" + same + " ownPos=" + (_own != null ? V(_own.transform.position) : "-");
                        Lc.Line("drone2: t+" + (_stage * 3) + " " + Summary);
                        if (_stage == 3) Shots.Take("drone2");
                        _stage++; _at = now + 3f; break;
                    }
                }
            }
            catch (Exception e) { Lc.LineOnce("drone2: threw " + e.GetType().Name + ": " + e.Message); _stage = 9; }
        }
    }
}
