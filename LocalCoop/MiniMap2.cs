using System;
using UnityEngine;
using UnityEngine.UI;

namespace LocalCoop
{
    /// <summary>
    /// The minimap shows one dot, the host's (`UIMinimap.playerDot`, driven by `Minimap.player`). A copy of that dot in player 2's colour follows the
    /// second dwarf. Placement: the game's own dot is the calibration (its position and its normalized map position give the origin, the map image's size
    /// the scale), so the formula does not have to be known. Twin `-lcnominimap2`. Test `-lcminimap2test`.
    /// The fog of war: the game reveals around the host only (`Minimap.ExploreMap`, driven by `Minimap.player`). Here the same circle is painted around the
    /// second dwarf with `Minimap.PaintCircle` (twin `-lcnofogp2`).
    /// </summary>
    internal static class MiniMap2
    {
        private static GameObject _dot;
        internal static Vector2 LastNHost, LastNClone;
        private static float _fogAt;
        internal static int FogPaints, FogRadius;

        // The game's own reveal (`Minimap.ExploreMap`, decompiled): around the host's map cell every pixel inside the sight circle is copied from the finished map
        // (`mapTex`, offset 0x18) into the displayed one (`finalTex`, 0x20), the copied cells are listed in `sightRangeCoords`, then the texture is applied.
        // `PaintCircle` is NOT that (it paints a fixed colour), so the same loop runs here for the second dwarf. The circle (centre offset and radius) is read from
        // the cells the game just revealed for the host, so no constant has to be known.
        private static bool SightCircle(Minimap mm, Player host, out int radius, out int offX, out int offY)
        {
            radius = 0; offX = 0; offY = 0;
            var l = mm.sightRangeCoords;
            if (l == null || l.Count < 12) return false;
            int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
            for (int i = 0; i < l.Count; i++) { var c = l[i]; if (c.X < minX) minX = c.X; if (c.X > maxX) maxX = c.X; if (c.Y < minY) minY = c.Y; if (c.Y > maxY) maxY = c.Y; }
            radius = (Mathf.Max(maxX - minX, maxY - minY) + 1) / 2;
            var h = mm.GetMapCoord(host.transform.position);
            offX = (minX + maxX) / 2 - h.X; offY = (minY + maxY) / 2 - h.Y;
            return radius > 0;
        }

        /// <summary>Reveals the map around the second dwarf the way the game does around the host.</summary>
        private static void PaintFog(Minimap mm, Player host, Player clone)
        {
            if (Lc.Has("-lcnofogp2") || Time.realtimeSinceStartup < _fogAt || !clone.isAlive || host == null) return;
            _fogAt = Time.realtimeSinceStartup + 0.25f;
            int r, ox, oy;
            if (!SightCircle(mm, host, out r, out ox, out oy)) return;
            var src = mm.mapTex; var dst = mm.finalTex;
            if (src == null || dst == null) return;
            if (FogRadius != r) { FogRadius = r; Lc.Line("minimap2: fog circle for the second dwarf: radius " + r + " cells, centre offset (" + ox + "," + oy + ") from the dwarf's cell"); }
            var c = mm.GetMapCoord(clone.transform.position);
            int cx = c.X + ox, cy = c.Y + oy, n = 0;
            for (int x = Mathf.Max(0, cx - r); x < Mathf.Min(dst.width, cx + r + 1); x++)
                for (int y = Mathf.Max(0, cy - r); y < Mathf.Min(dst.height, cy + r + 1); y++)
                {
                    if ((x - cx) * (x - cx) + (y - cy) * (y - cy) > r * r) continue;
                    dst.SetPixel(x, y, src.GetPixel(x, y)); n++;
                }
            if (n > 0) dst.Apply();
            FogPaints++;
        }

        /// <summary>Cells inside a circle around a world position that are still hidden: the displayed map differs from the finished map there.</summary>
        private static int Hidden(Minimap mm, Vector3 world, int r)
        {
            var src = mm.mapTex; var dst = mm.finalTex; if (src == null || dst == null) return -1;
            var c = mm.GetMapCoord(world); int n = 0;
            for (int x = Mathf.Max(0, c.X - r); x < Mathf.Min(dst.width, c.X + r + 1); x++)
                for (int y = Mathf.Max(0, c.Y - r); y < Mathf.Min(dst.height, c.Y + r + 1); y++)
                {
                    if ((x - c.X) * (x - c.X) + (y - c.Y) * (y - c.Y) > r * r) continue;
                    if (src.GetPixel(x, y) != dst.GetPixel(x, y)) n++;
                }
            return n;
        }

        private static int Snapshot(Minimap mm, Vector3 world, out int sum)
        {
            sum = 0; int n = 0;
            var t = mm.MapTexture; if (t == null) return -1;
            var c = mm.GetMapCoord(world);
            for (int dx = -10; dx <= 10; dx += 2) for (int dy = -10; dy <= 10; dy += 2)
            {
                int x = c.X + dx, y = c.Y + dy; if (x < 0 || y < 0 || x >= t.width || y >= t.height) continue;
                var px = t.GetPixel(x, y); sum += (int)(px.r * 255) + 3 * (int)(px.g * 255) + 7 * (int)(px.b * 255) + 13 * (int)(px.a * 255); n++;
            }
            return n;
        }

        private static Vector2 Norm(Minimap mm, Vector3 world)
        {
            var c = mm.GetMapCoord(world);
            return new Vector2((float)c.X / Mathf.Max(1, mm.WIDTH), (float)c.Y / Mathf.Max(1, mm.HEIGHT));
        }

        internal static GameObject Dot { get { return _dot; } }

        internal static void Tick()
        {
            if (Lc.Has("-lcnominimap2")) { if (_dot != null) _dot.SetActive(false); return; }
            try
            {
                var clone = Phase1.Second;
                if (clone == null) { if (_dot != null) UnityEngine.Object.Destroy(_dot); _dot = null; return; }
                var ui = Find.One<UIMinimap>();
                if (ui == null || ui.playerDot == null || ui.miniMap == null || ui.imgRT == null || !ui.miniMap.inited) return;
                if (_dot == null)
                {
                    _dot = UnityEngine.Object.Instantiate(ui.playerDot, ui.playerDot.transform.parent);
                    _dot.name = "LcPlayerDot2";
                    foreach (var g in _dot.GetComponentsInChildren<Graphic>(true)) g.color = CoopMenu.BlueP2;
                    Lc.Line("minimap2: second dot created");
                }
                bool on = ui.playerDot.activeInHierarchy && clone.isAlive;
                if (_dot.activeSelf != on) _dot.SetActive(on);
                if (!on) return;
                var host = Phase1.First();
                var mm = ui.miniMap;
                PaintFog(mm, Phase1.First(), clone);
                LastNHost = mm.GetPlayerNormalizedPosition();
                LastNClone = Norm(mm, clone.transform.position);
                var size = ui.imgRT.rect.size;
                var rt1 = ui.playerDot.GetComponent<RectTransform>(); var rt2 = _dot.GetComponent<RectTransform>();
                var origin = rt1.anchoredPosition - new Vector2(LastNHost.x * size.x, LastNHost.y * size.y);
                rt2.anchoredPosition = origin + new Vector2(LastNClone.x * size.x, LastNClone.y * size.y);
            }
            catch (Exception e) { Lc.LineOnce("minimap2: tick threw " + e.GetType().Name + ": " + e.Message); }
        }

        // ---- test -------------------------------------------------------------------------------------------------------
        private static int _stage, _ok, _bad;
        private static float _at = -1f;
        internal static bool TestDone { get { return _stage >= 4; } }
        private static int _snap0; private static Vector3 _far;

        private static void Check(string name, bool ok, string info) { if (ok) _ok++; else _bad++; Lc.Line("minimap2: CHECK " + (ok ? "OK   " : "FAIL ") + name + " | " + info); }

        internal static void Test()
        {
            if (!Lc.Has("-lcminimap2test") || Phase1.Second == null || _stage >= 4 || Lc.InRunAt < 0f) return;
            float now = Time.realtimeSinceStartup;
            try
            {
                if (_at < 0f) { _at = now + 8f; return; }
                if (now < _at) return;
                var host = Phase1.First(); var clone = Phase1.Second;
                var ui = Find.One<UIMinimap>();
                if (host == null || ui == null || ui.miniMap == null) { Check("the minimap exists", false, "ui=" + (ui != null)); _stage = 4; return; }
                var rt1 = ui.playerDot.GetComponent<RectTransform>();
                if (_stage == 0)
                {
                    var nh = Norm(ui.miniMap, host.transform.position);
                    Check("the normalized position computed here equals the game's for the host", (nh - ui.miniMap.GetPlayerNormalizedPosition()).magnitude < 0.01f, "mine=" + nh + " game=" + ui.miniMap.GetPlayerNormalizedPosition());
                    clone.transform.position = host.transform.position;
                    _stage = 1; _at = now + 1.5f; return;
                }
                if (_stage == 1)
                {
                    var d2 = Dot != null ? Dot.GetComponent<RectTransform>() : null;
                    Check("the second dot exists and stands on the first when both dwarves are in the same place", d2 != null && Dot.activeSelf && (d2.anchoredPosition - rt1.anchoredPosition).magnitude < 2f,
                        "dot2=" + (d2 != null ? d2.anchoredPosition.ToString() : "none") + " dot1=" + rt1.anchoredPosition);
                    clone.transform.position = host.transform.position + new Vector3(12f, 0f, 0f);
                    _stage = 2; _at = now + 1.5f; return;
                }
                if (_stage == 2)
                {
                    var d2 = Dot != null ? Dot.GetComponent<RectTransform>() : null;
                    float dist = d2 != null ? (d2.anchoredPosition - rt1.anchoredPosition).magnitude : -1f;
                    Check("12 m east of the host the second dot has moved away from the first", dist > 2f, "distance between the dots=" + dist.ToString("F1") + " px");
                    // the most hidden spot within reach of a teleport: scan a ring of candidates around the host
                    Vector3 best = host.transform.position; int bestN = -1;
                    int rr = Mathf.Max(FogRadius, 14);
                    for (int dx = -150; dx <= 150; dx += 15) for (int dz = -150; dz <= 150; dz += 15)
                    {
                        if (Mathf.Abs(dx) < 40 && Mathf.Abs(dz) < 40) continue;
                        var w = host.transform.position + new Vector3(dx, 0f, dz); int h = Hidden(ui.miniMap, w, rr);
                        if (h > bestN) { bestN = h; best = w; }
                    }
                    _far = best; _snap0 = bestN;
                    clone.transform.position = _far;
                    Lc.Line("minimap2: hidden spot chosen " + (best - host.transform.position) + " from the host with " + bestN + " hidden cells in the sight circle");
                    _stage = 3; _at = now + 3f; return;
                }
                if (_stage == 3)
                {
                    int rr = Mathf.Max(FogRadius, 14);
                    int left = Hidden(ui.miniMap, clone.transform.position, rr);
                    Check("the fog around the second dwarf is lifted far away from the host", _snap0 > 20 && left >= 0 && left < _snap0 / 4 && FogPaints > 0, "hidden cells before=" + _snap0 + " after=" + left + " paints=" + FogPaints + " radius=" + FogRadius);
                    Lc.Line("minimap2: SUMMARY ok=" + _ok + " fail=" + _bad + (Lc.Has("-lcnominimap2") ? " (twin: no second dot, FAIL expected)" : "") + (Lc.Has("-lcnofogp2") ? " (twin: no fog for the second dwarf, FAIL expected)" : ""));
                    _stage = 4;
                }
            }
            catch (Exception e) { Lc.LineOnce("minimap2: test threw " + e.GetType().Name + ": " + e.Message); _stage = 4; }
        }
    }
}
