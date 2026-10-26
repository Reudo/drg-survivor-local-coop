using System;
using DRS.UI;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace LocalCoop
{
    /// <summary>
    /// Revive. In the vanilla game any death is game over. With two dwarves, one death is only "down" while the other one lives: the game's
    /// GameController.OnPlayerDeath is skipped, the dwarf stays down, and when the other dwarf stands within 3 m for 5 s the downed one gets up with
    /// the HP of the settings file. Both down: the game's own game-over runs as always. Ported from the earlier network mod (CoopDowned / CoopCorpse): same radius,
    /// time, percentage and the animator reset that stops a revived dwarf from walking around in its death pose.
    /// Switch: the REVIVE button on the last page before the mission (default on), `-lcnorevive` is the test twin.
    /// </summary>
    internal static class Revive
    {
        internal static bool On = true;
        internal static bool Enabled { get { return On && !Lc.Has("-lcnorevive"); } }

        internal const float Radius = 3f;
        internal static float Seconds { get { return Settings.ReviveSeconds; } }          // from localcoop-settings.txt, default 3 s
        internal static Player Downed;
        internal static int Downs, Revives, BothDown;
        private static float _prog;        // seconds the other dwarf has stood in the circle
        private static GameObject _root;
        private static TextMeshProUGUI _text;

        private static string Who(Player p) { return Share.IsClone(p) ? "P2" : "P1"; }

        internal static void Reset() { Downed = null; _prog = 0f; _testStage = 0; if (_root != null) _root.SetActive(false); Ring(null, 0f); }

        [HarmonyPatch(typeof(GameController), "OnPlayerDeath")]
        internal static class DeathPatch
        {
            private static bool Prefix()
            {
                try
                {
                    if (!Enabled || Phase1.Second == null) return true;
                    var h = Phase1.First(); var c = Phase1.Second;
                    if (h == null || c == null) return true;
                    bool hDead = h.currentHp <= 0, cDead = c.currentHp <= 0;
                    if (hDead && cDead) { BothDown++; Lc.Line("revive: both dwarves are down: the game over runs"); Downed = null; return true; }
                    if (!hDead && !cDead) return true;
                    Downed = hDead ? h : c; _prog = 0f; Downs++;
                    Remember(Downed);
                    Lc.Line("revive: " + Who(Downed) + " is down (game over skipped); the other dwarf can revive within " + Radius + " m in " + Seconds + " s");
                    return false;
                }
                catch (Exception e) { Lc.LineOnce("revive: death patch threw " + e.GetType().Name + ": " + e.Message); return true; }
            }
        }

        internal static bool IsDying(Player p)
        {
            try
            {
                var an = p.PlayerAnimator; var anim = an != null ? an.animator : null;
                if (anim == null) return false;
                var clips = anim.GetCurrentAnimatorClipInfo(0);
                return clips != null && clips.Length > 0 && clips[0].clip != null && clips[0].clip.name.StartsWith("Dying");
            }
            catch { return false; }
        }

        private static void Stand(Player p)
        {
            double max = 100; try { max = p._maxHp_k__BackingField.Value; } catch { }
            p.currentHp = Settings.ReviveHp(max); _standHp = p.currentHp;      // test: the value at the moment of standing up (a hit or a level-up heal a moment later must not decide the check)
            Downed = null; _prog = 0f; Revives++;
            Release(p);
            // the hp is back, the death pose is not: reset the animator (the game's own idle call as the fallback)
            try
            {
                var an = p.PlayerAnimator; var anim = an != null ? an.animator : null;
                if (anim != null) { anim.enabled = false; anim.enabled = true; anim.Rebind(); anim.Update(0f); }
                if (IsDying(p) && an != null) { an.SetIdle(false); anim.Update(0f); }
            }
            catch (Exception e) { Lc.LineOnce("revive: animator reset threw " + e.GetType().Name + ": " + e.Message); }
            Lc.Line("revive: " + Who(p) + " is up again, hp " + p.currentHp + " dying=" + IsDying(p) + " " + PoseInfo(p));
        }

        // ---- a downed dwarf lies still ----------------------------------------------------------------------------------------------------
        // Hand test: the downed dwarves could walk on, their weapons (the pickaxe) kept hitting, the enemies stuck at the place of the fall, and after the
        // revive the dwarf stayed lying. The game ends the run when a dwarf dies, so nothing in it keeps a dead dwarf still. While a dwarf is down: no steering,
        // no weapons, no velocity. The pose of its graphics (and the animator) is put back when it gets up.
        private static Vector3 _gfxPos; private static Quaternion _gfxRot; private static Transform _gfx; private static bool _frozen;
        internal static int FrozenFrames, FrozenAnims; private static float _poseLogAt;

        private static void Remember(Player p)
        {
            try { _gfx = p.gfxContainer; if (_gfx != null) { _gfxPos = _gfx.localPosition; _gfxRot = _gfx.localRotation; } } catch { _gfx = null; }
            _frozen = false;
        }

        private static string PoseInfo(Player p)
        {
            try
            {
                var an = p.PlayerAnimator; var anim = an != null ? an.animator : null; string clip = "-";
                if (anim != null) { var c = anim.GetCurrentAnimatorClipInfo(0); if (c != null && c.Length > 0 && c[0].clip != null) clip = c[0].clip.name; }
                var g = p.gfxContainer;
                return "pose: clip=" + clip + " gfxRot=" + (g != null ? g.localRotation.eulerAngles.ToString() : "-") + " gfxPos=" + (g != null ? g.localPosition.ToString() : "-") + " ctl=" + p._moveControlType + " weapons=" + p.weaponsActive;
            }
            catch { return "pose: ?"; }
        }

        /// <summary>
        /// Diagnosis (hand test: the dwarf that is still up takes damage when monsters walk over the body of the downed one): every hit on the living dwarf while the other lies down
        /// is logged with its source and the distances of that source to both dwarves (at most one line per 0.4 s). Twin `-lcnohitdiag`.
        /// </summary>
        internal static int HitsWhileDown;
        [HarmonyLib.HarmonyPatch(typeof(Player), "TakeDamage")]
        internal static class HitDiag
        {
            private static float _at;
            private static void Prefix(Player __instance, long __0, Entity __5, EDamageSource __4)
            {
                try
                {
                    if (Lc.Has("-lcnohitdiag") || Downed == null || __instance == null || __instance.Pointer == Downed.Pointer) return;
                    HitsWhileDown++;
                    float now = Time.realtimeSinceStartup; if (now < _at) return; _at = now + 0.4f;
                    string src = "-"; float dSelf = -1f, dDown = -1f;
                    if (__5 != null) { src = __5.GetType().Name + "/" + __5.name; var sp = __5.transform.position; dSelf = Vector3.Distance(sp, __instance.transform.position); dDown = Vector3.Distance(sp, Downed.transform.position); }
                    else
                    {
                        // melee hits carry no source: the nearest enemy tells whether one stood at the hit dwarf at all
                        float best = 1e9f; string nm = "-";
                        foreach (var e in UnityEngine.Object.FindObjectsOfType<Enemy>()) { if (e == null || !e.isAlive) continue; float d = Vector3.Distance(e.transform.position, __instance.transform.position); if (d < best) { best = d; nm = e.name; } }
                        src = "- nearest enemy " + nm + " " + best.ToString("F1") + " m";
                    }
                    Lc.Line("revive: hit on " + Who(__instance) + " while " + Who(Downed) + " is down: dmg " + __0 + " source " + __4 + " " + src + " distance to the hit dwarf " + dSelf.ToString("F1") + " m, to the downed one " + dDown.ToString("F1") + " m, dwarves " + Vector3.Distance(__instance.transform.position, Downed.transform.position).ToString("F1") + " m apart");
                }
                catch { }
            }
        }

        internal static int Refreezes;

        /// <summary>Hand test: a level-up that came during a revive switched the downed dwarf's weapons on again (it hit the ground with its pickaxe). Every tick the weapons are checked and switched off again. Twin `-lcnorefreeze`.</summary>
        internal static bool AllPaused(Player p)
        {
            try { for (int i = 0; p.weapons != null && i < p.weapons.Count; i++) { var w = p.weapons[i]; if (w != null && !w.paused) return false; } } catch { }
            return true;
        }

        private static void Freeze(Player p)
        {
            if (Lc.Has("-lcnofreeze")) return;
            try
            {
                if (!_frozen) { _frozen = true; Lc.Line("revive: " + Who(p) + " lies still (no steering, no weapons) " + PoseInfo(p)); try { p.ToggleWeapons(false); } catch { } }
                p._moveControlType = Player.EMoveControlType.DISABLED;
                p.moveDir = Vector2.zero; p.rawMoveInput = Vector2.zero;
                if (p.weaponsActive) p.weaponsActive = false;
                if (!Lc.Has("-lcnorefreeze") && !AllPaused(p)) { try { p.ToggleWeapons(false); Refreezes++; } catch { } }
                var rb = p.rb; if (rb != null) rb.linearVelocity = Vector3.zero;
                // hand test: enemies walking over the body made the downed dwarf swing its pickaxe again (animation); the animator stands still while the dwarf is down. Twin `-lcnofreezeanim`.
                if (!Lc.Has("-lcnofreezeanim")) { var an = p.PlayerAnimator; var anim = an != null ? an.animator : null; if (anim != null && anim.enabled && IsDying(p) && anim.GetCurrentAnimatorStateInfo(0).normalizedTime >= 0.97f) { anim.enabled = false; FrozenAnims++; } }      // only once the dying clip has run to its last frame (hand test: stopped at its first frame the dwarf stood there)
                FrozenFrames++;
                if (Time.realtimeSinceStartup > _poseLogAt) { _poseLogAt = Time.realtimeSinceStartup + 1f; try { var an2 = p.PlayerAnimator; var a2 = an2 != null ? an2.animator : null; Lc.Line("revive: down pose " + PoseInfo(p) + " animator on=" + (a2 != null && a2.enabled) + " t=" + (a2 != null ? a2.GetCurrentAnimatorStateInfo(0).normalizedTime.ToString("F2") : "-")); } catch { } }
            }
            catch { }
        }

        private static void Release(Player p)
        {
            try
            {
                _frozen = false;
                if (p._moveControlType == Player.EMoveControlType.DISABLED) p._moveControlType = Player.EMoveControlType.FULL_CONTROL;
                p.weaponsActive = true; try { p.ToggleWeapons(true); } catch { }
                var g = p.gfxContainer;
                if (g != null && _gfx != null && g.Pointer == _gfx.Pointer) { g.localPosition = _gfxPos; g.localRotation = _gfxRot; }
            }
            catch (Exception e) { Lc.LineOnce("revive: release threw " + e.GetType().Name + ": " + e.Message); }
        }

        // ---- the ground ring: the circle the other dwarf has to stand in, filling up while it does ---------------------------------------
        private static GameObject _ring, _fill;
        private static SpriteRenderer _ringSr, _fillSr;
        private static Sprite _ringSprite, _discSprite;
        internal static bool RingOn { get { try { return _ring != null && _ring.activeSelf; } catch { return false; } } }
        internal static float RingFill { get { try { return _fill != null ? _fill.transform.localScale.x : 0f; } catch { return 0f; } } }

        private static Sprite MakeSprite(bool ring)
        {
            const int n = 256;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp; tex.filterMode = FilterMode.Bilinear;
            var px = new Color32[n * n];
            float c = (n - 1) * 0.5f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float r = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;       // 0 centre .. 1 edge
                    float a;
                    if (ring) a = Mathf.Clamp01(1f - Mathf.Abs(r - 0.955f) / 0.04f);         // a thin ring at the edge
                    else a = r <= 0.93f ? 0.55f : 0f;                                         // the filling disc
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px); tex.Apply(false, false);
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n / (2f * Radius));      // one unit = one metre, the diameter is the revive circle
        }

        private static void Ring(Player downed, float progress)
        {
            if (Lc.Has("-lcnorevivering")) return;
            try
            {
                if (downed == null) { if (_ring != null && _ring.activeSelf) _ring.SetActive(false); return; }
                if (_ring == null)
                {
                    _ringSprite = MakeSprite(true); _discSprite = MakeSprite(false);
                    _ring = new GameObject("LcReviveRing");
                    UnityEngine.Object.DontDestroyOnLoad(_ring);
                    _ringSr = _ring.AddComponent<SpriteRenderer>(); _ringSr.sprite = _ringSprite; _ringSr.sortingOrder = 5;
                    _fill = new GameObject("Fill"); _fill.transform.SetParent(_ring.transform, false);
                    _fillSr = _fill.AddComponent<SpriteRenderer>(); _fillSr.sprite = _discSprite; _fillSr.sortingOrder = 4;
                }
                _ring.SetActive(true);
                var p = downed.transform.position;
                _ring.transform.position = new Vector3(p.x, p.y + 0.12f, p.z);
                _ring.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                var col = Share.IsClone(downed) ? CoopMenu.BlueP2 : CoopMenu.OrangeP1;
                _ringSr.color = new Color(col.r, col.g, col.b, 0.95f);
                _fillSr.color = new Color(col.r, col.g, col.b, 0.9f);
                float s = Mathf.Max(0.02f, Mathf.Clamp01(progress));
                _fill.transform.localScale = new Vector3(s, s, 1f);
            }
            catch (Exception e) { Lc.LineOnce("revive: ring threw " + e.GetType().Name + ": " + e.Message); }
        }

        private static void Label(string text, Player at)
        {
            try
            {
                if (_root == null)
                {
                    _root = new GameObject("LcRevive");
                    UnityEngine.Object.DontDestroyOnLoad(_root);
                    var canvas = _root.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 810;
                    var go = new GameObject("Text"); var rt = go.AddComponent<RectTransform>(); rt.SetParent(_root.transform, false);
                    rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.zero; rt.pivot = new Vector2(0.5f, 0f); rt.sizeDelta = new Vector2(420f, 60f);
                    _text = go.AddComponent<TextMeshProUGUI>();
                    var font = CoopMenu.FontAsset != null ? CoopMenu.FontAsset : TMP_Settings.defaultFontAsset; if (font != null) _text.font = font;
                    _text.alignment = TextAlignmentOptions.Midline; _text.raycastTarget = false; _text.enableWordWrapping = false; _text.fontStyle = FontStyles.Bold;
                    _text.outlineWidth = 0.28f; _text.outlineColor = new Color32(0, 0, 0, 255);
                }
                var cam = Camera.main;
                if (text == null || at == null || cam == null) { _root.SetActive(false); return; }
                var sp = cam.WorldToScreenPoint(at.transform.position + new Vector3(0f, 2.6f, 0f));
                if (sp.z <= 0f) { _root.SetActive(false); return; }
                _root.SetActive(true);
                _text.text = text; _text.color = Share.IsClone(at) ? CoopMenu.BlueP2 : CoopMenu.OrangeP1; _text.fontSize = 40f * Screen.height / 1080f;
                _text.rectTransform.anchoredPosition = new Vector2(sp.x, sp.y);
            }
            catch (Exception e) { Lc.LineOnce("revive: label threw " + e.GetType().Name + ": " + e.Message); }
        }

        /// <summary>Real seconds, not game time: the game slows time down when a dwarf dies, and the revive must not take three times as long. Paused while the game is paused (level-up, shop).</summary>
        internal static float Progress { get { return Mathf.Clamp01(_prog / Seconds); } }

        internal static void Tick()
        {
            try
            {
                Test();
                if (Downed == null || Phase1.Second == null) { if (_root != null && _root.activeSelf) _root.SetActive(false); Ring(null, 0f); return; }
                if (Downed.currentHp > 0) { Downed = null; _prog = 0f; Ring(null, 0f); return; }       // stood up some other way (healing, a new stage)
                Ring(Downed, Progress);
                Freeze(Downed);
                var other = Share.IsClone(Downed) ? Phase1.First() : Phase1.Second;
                if (other == null || !other.isAlive) { _prog = 0f; Label(Loc.T("DOWN"), Downed); return; }
                float d = Vector3.Distance(Downed.transform.position, other.transform.position);
                if (d > Radius) { _prog = 0f; Label(Loc.T("DOWN"), Downed); return; }
                if (_prog <= 0f) Lc.LineOnce("revive: " + Who(other) + " started reviving " + Who(Downed));
                if (Time.timeScale > 0.01f) _prog += Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
                if (Progress >= 1f) { Stand(Downed); Label(null, null); Ring(null, 0f); return; }
                Label(Loc.T("REVIVE") + " " + Mathf.RoundToInt(Progress * 100f) + " %", Downed);
            }
            catch (Exception e) { Lc.LineOnce("revive: tick threw " + e.GetType().Name + ": " + e.Message); }
        }

        // ---- test (`-lcrevivetest`, twin `-lcnorevive`) ------------------------------------------------------------------------
        private static int _testStage, _ok, _bad;
        internal static bool TestDone { get { return _testStage >= 9; } }
        private static float _testAt = -1f;
        private static float _hpAfter; private static long _standHp = -1;
        internal static bool Passed { get { return _bad == 0 && _ok > 0; } }
        internal static string Summary { get { return "ok=" + _ok + " fail=" + _bad; } }

        private static void Check(string name, bool ok, string info)
        {
            if (ok) _ok++; else _bad++;
            Lc.Line("revive: CHECK " + (ok ? "OK   " : "FAIL ") + name + " | " + info);
        }

        private static void Kill(Player p, Player by)
        {
            try { p.GOD_MODE = false; } catch { }
            try { p.damageImmuneUntil = 0f; } catch { }
            try { p.TakeDamage(99999, false, EDamageType.KINETIC, false, EDamageSource.PROJECTILE, by, false); }
            catch (Exception e) { Lc.Line("revive: kill threw " + e.GetType().Name + ": " + e.Message); }
        }

        private static bool RunOver() { return Lc.InRunAt < 0f || Find.AnyActive<UIEndScreen>(); }

        private static void Test()
        {
            if (!Lc.Has("-lcrevivetest") || Phase1.Second == null || _testStage >= 9) return;
            var h0 = Phase1.First(); var c0 = Phase1.Second; var h = h0; var c = c0;
            if (h == null || c == null || Lc.InRunAt < 0f) return;
            if (Lc.Has("-lcrevivehost")) { h = c0; c = h0; }          // the host dies and the clone revives it (hand test: P1 down made P2's hp go far below zero); here c = the dying one, h = the other
            float now = Time.realtimeSinceStartup;
            if (_testAt < 0f) { _testAt = now + 10f; return; }
            if (now < _testAt) return;
            switch (_testStage)
            {
                case 0:
                    h.transform.position = c.transform.position + new Vector3(14f, 0f, 0f);       // the host is far away when the clone dies
                    Kill(c, h); _testStage = 1; _testAt = now + 3f; break;
                case 1:
                    Check("the revive time is 3 s unless the settings file says otherwise", Mathf.Abs(Seconds - 3f) < 0.01f, "seconds=" + Seconds.ToString("F1"));
                    Check("one dwarf died: it is down, not game over", c.currentHp <= 0 && Downed != null && (Downed != null && Downed.Pointer == c.Pointer) && !RunOver(), "clone hp=" + c.currentHp + " downed=" + (Downed != null ? Who(Downed) : "-") + " runOver=" + RunOver());
                    if (RunOver()) { Lc.Line("revive: SUMMARY ok=" + _ok + " fail=" + _bad + " (run ended, nothing more to test)"); _testStage = 9; break; }
                    Shots.Take("down-" + (Lc.Has("-lcrevivehost") ? "p1" : "p2")); if (Lc.Has("-lcrevivehost")) Check("player 2's health text is not mangled while player 1 is down (hp " + h.currentHp + ")", !Hud2.P2HpText.Contains("-") && Hud2.P2HpText.Contains("LP"), "text='" + Hud2.P2HpText + "'"); Check("a downed dwarf lies still: no steering, no weapons", c._moveControlType == Player.EMoveControlType.DISABLED && !c.weaponsActive, "ctl=" + c._moveControlType + " weapons=" + c.weaponsActive);
                    { int stuck = 0, total = 0; foreach (var en in UnityEngine.Object.FindObjectsOfType<Enemy>()) { if (en == null || !en.isAlive || !en.gameObject.activeInHierarchy) continue; total++; if (en.player != null && en.player.Pointer == c.Pointer) stuck++; }
                      Check("no enemy keeps going for the downed dwarf", total == 0 || stuck == 0, "enemies=" + total + " on the downed=" + stuck); }
                    Check("with the other dwarf far away the revive circle stays empty", Progress <= 0.001f && c.currentHp <= 0, "progress=" + Progress.ToString("F2") + " hp=" + c.currentHp);
                    Check("the downed dwarf lies in a circle on the ground (empty)", RingOn && RingFill < 0.05f, "ring=" + RingOn + " fill=" + RingFill.ToString("F2"));
                    Check("the downed dwarf's weapons are paused", AllPaused(c), "paused=" + AllPaused(c));
                    try { c.ToggleWeapons(true); c.weaponsActive = true; } catch { }          // what a level-up during the revive did
                    h.transform.position = c.transform.position + new Vector3(1.5f, 0f, 0f);
                    _testStage = 2; _testAt = now + 2.5f; break;
                case 2:
                    Check("weapons stay off after the game switched them on again (level-up during the revive)", AllPaused(c) && !c.weaponsActive, "paused=" + AllPaused(c) + " active=" + c.weaponsActive + " refreezes=" + Refreezes);
                    Check("standing next to the downed dwarf fills the revive circle", Progress > 0.2f && Progress < 1f && Downed != null && c.currentHp <= 0, "progress=" + Progress.ToString("F2") + " hp=" + c.currentHp);
                    Shots.Take("revive-ring");
                    Check("the ring on the ground fills with it", RingOn && Mathf.Abs(RingFill - Mathf.Max(0.02f, Progress)) < 0.15f && RingFill > 0.15f, "fill=" + RingFill.ToString("F2") + " progress=" + Progress.ToString("F2"));
                    _testStage = 3; _testAt = now + 4f; break;
                case 3:
                    double max = 0; try { max = c._maxHp_k__BackingField.Value; } catch { }
                    Check("the ring is gone again once it is up", !RingOn, "ring=" + RingOn);
                    Check("after the revive time (" + Seconds.ToString("F1") + " s) the downed dwarf is up with the HP of the settings file", c.currentHp > 0 && Downed == null && _standHp == Settings.ReviveHp(max), "hp at standing up=" + _standHp + " now=" + c.currentHp + " expected=" + Settings.ReviveHp(max) + " (" + Settings.ReviveMode + " " + (Settings.ReviveMode == "flat" ? Settings.ReviveFlat : Settings.RevivePercent) + ")" + " maxHp=" + max.ToString("F0") + " downed=" + (Downed != null));
                    _testStage = 4; _testAt = now + 1.5f; break;
                case 4:
                    Shots.Take("revived-" + (Lc.Has("-lcrevivehost") ? "p1" : "p2")); Check("the revived dwarf is not in its death pose", !IsDying(c) && c.currentHp > 0, "dying=" + IsDying(c) + " hp=" + c.currentHp + " " + PoseInfo(c));
                    Check("the revived dwarf can steer and shoot again", c._moveControlType == Player.EMoveControlType.FULL_CONTROL && c.weaponsActive, "ctl=" + c._moveControlType + " weapons=" + c.weaponsActive);
                    Kill(c, h); _testStage = 5; _testAt = now + 1.5f; break;
                case 5:
                    // second death of the clone, then the host at 0 hp too, then the game's death event (the damage path of the host is not reliable in the harness)
                    c.currentHp = 0; h.currentHp = 0;
                    try { var gc = Find.One<GameController>(); if (gc != null) gc.OnPlayerDeath(); } catch (Exception e) { Lc.Line("revive: OnPlayerDeath threw " + e.GetType().Name + ": " + e.Message); }
                    _testStage = 6; _testAt = now + 6f; break;
                case 6:
                    Check("both dwarves down: the run ends (the game's own game over)", RunOver() || BothDown > 0, "runOver=" + RunOver() + " bothDown=" + BothDown + " hostHp=" + h.currentHp + " cloneHp=" + c.currentHp);
                    Lc.Line("revive: SUMMARY ok=" + _ok + " fail=" + _bad + (Lc.Has("-lcnorevive") ? " (twin: revive off, FAIL expected)" : ""));
                    _testStage = 9; break;            }
        }
    }
}
