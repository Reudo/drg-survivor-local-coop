using System;
using System.Collections.Generic;
using System.Text;
using DRS.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LocalCoop
{
    /// <summary>
    /// Two bottom panels: left P1 (the game's own), right P2 (a copy fed from the clone). The game's bottom bar
    /// (UICoreFront.actionBarGroup) holds the hp bar, its text and the four weapon slots; it is duplicated once and the
    /// original is moved to the left. The copy is filled from the clone every few frames: hp fill and text, and each
    /// weapon slot through UIActionBarSlot.Setup(data, handler). "P1" / "P2" labels are copies of the hp text object.
    /// Everything is undone when the clone is gone.
    /// </summary>
    internal static class Hud2
    {
        internal static string P1Text = "";
        internal static string P1Count = "";
        internal static bool Enabled = true;               // `-lchudoff`: vanilla single panel (test twin)

        private static UICoreFront _front;
        internal static RectTransform BuffBox2;     // player 2's copy of the buff row (Buffs2)
        internal static UICoreFront Front { get { return _front; } }
        private static GameObject _group2;
        private static RectTransform _rt1, _rt2;      // the two full-width groups (never moved)
        private static RectTransform _bar1, _bar2;      // the visible health bar of each panel
        private static Vector2 _pos1Orig;
        private static Image _hp2;
        private static TextMeshProUGUI _hpText2;
        private static UIActionBarSlot[] _slots1, _slots2;
        private static TextMeshProUGUI _label1, _label2;
        private static float _nextSync;
        private static bool _built, _failed;

        internal static void Init() { Enabled = !Lc.Has("-lchudoff"); }

        internal static bool Built { get { return _built; } }
        internal static float BuiltAt = -1f;

        /// <summary>The game shows its action bar: a run is on screen (not the end screen, not the outro).</summary>
        internal static bool RunUiVisible { get { try { return _front != null && _front.actionBarGroup != null && _front.actionBarGroup.activeInHierarchy; } catch { return false; } } }

        internal static void Tick()
        {
            try
            {
                var second = Phase1.Second;
                if (Lc.Has("-lchplow") && !Enabled) { var h0 = Phase1.First(); if (h0 != null) h0.currentHp = (long)(h0._maxHp_k__BackingField.Value * 0.25f); }
                if (!Enabled || _failed) return;
                if (second == null)
                {
                    // a co-op run is on and the host is in the stage, only the second dwarf is not made yet: the second panel is there from the start
                    // (empty), it fills as soon as the dwarf exists. Twin `-lcnoearlyhud`.
                    bool pending = !Lc.Has("-lcnoearlyhud") && CoopStart.Started && Lc.InRunAt >= 0f && Phase1.First() != null && !Phase1.LoadingActive();
                    if (pending) { if (!_built) Build(null); else Mirror(); return; }
                    Remove(); return;
                }
                if (!_built) { if (Phase1.Ready || Lc.Has("-lcnohide") || !Lc.Has("-lcnoearlyhud")) Build(second); return; }
                Mirror();
                float now = Time.realtimeSinceStartup;
                if (Lc.Has("-lchplow")) { try { second.currentHp = (long)(second._maxHp_k__BackingField.Value * 0.3f); var h = Phase1.First(); if (h != null) h.currentHp = (long)(h._maxHp_k__BackingField.Value * 0.25f); } catch { } }
                if (now < _nextSync) return;
                _nextSync = now + 0.1f;
                Sync(second);
            }
            catch (Exception e) { _failed = true; Lc.Line("hud2: failed: " + e.GetType().Name + ": " + e.Message); }
        }

        /// <summary>The slot of the second panel that belongs to a slot of the game's own bar (same index), or null (not a slot of the first bar).</summary>
        internal static UIActionBarSlot CounterpartOf(UIActionBarSlot s)
        {
            try
            {
                if (s == null || _slots1 == null || _slots2 == null) return null;
                for (int i = 0; i < _slots1.Length && i < _slots2.Length; i++) if (_slots1[i] != null && _slots1[i].Pointer == s.Pointer) return _slots2[i];
            }
            catch { }
            return null;
        }

        internal static bool P2Visible { get { return _group2 != null && _group2.activeInHierarchy && (_cg2 == null || _cg2.alpha > 0.05f); } }
        private static CanvasGroup _cg1, _cg2;

        /// <summary>The copy has to follow what the game does with the original panel (it hides it when the stage ends: end screen, outro),
        /// otherwise the second panel stays on top of the end screen. Twin `-lcnohudmirror`.</summary>
        private static void Mirror()
        {
            if (Lc.Has("-lcnohudmirror") || _front == null || _group2 == null) return;
            var g1 = _front.actionBarGroup;
            if (g1 == null) return;
            bool on = g1.activeInHierarchy;
            if (_group2.activeSelf != on) _group2.SetActive(on);
            if (_cg1 == null) _cg1 = g1.GetComponent<CanvasGroup>();
            if (_cg2 == null) _cg2 = _group2.GetComponent<CanvasGroup>();
            if (_cg1 != null && _cg2 != null && _cg2.alpha != _cg1.alpha) _cg2.alpha = _cg1.alpha;
        }

        private static void Build(Player second)
        {
            _front = Find.One<UICoreFront>();
            if (_front == null || _front.actionBarGroup == null) return;

            var g1 = _front.actionBarGroup;
            _rt1 = g1.GetComponent<RectTransform>();
            _pos1Orig = _rt1.anchoredPosition;

            _group2 = UnityEngine.Object.Instantiate(g1, g1.transform.parent);
            _group2.name = "LcActionBarGroup2";
            _rt2 = _group2.GetComponent<RectTransform>();

            // hand test: the copy also had the first dwarf's buff icons and the drone widget (a circle with "0" in front of player 2's weapons). Both belong to
            // player 1 only, so they are cut out of the copy. Twin `-lcdronehud`.
            if (!Lc.Has("-lcdronehud"))
                foreach (var c in _group2.GetComponentsInChildren<Transform>(true))
                {
                    if (c == null) continue;
                    if (c.name == "StackableBuffContainer" && !Lc.Has("-lcnobuffbox2"))
                    {
                        // player 2's own buffs go here (see Buffs2); the copied icons are player 1's and go
                        BuffBox2 = c.GetComponent<RectTransform>();
                        for (int i = c.childCount - 1; i >= 0; i--) { var k = c.GetChild(i); k.gameObject.SetActive(false); UnityEngine.Object.Destroy(k.gameObject); }
                        continue;
                    }
                    if (c.name == "BoscoEggWidget" || c.name == "StackableBuffContainer") { c.gameObject.SetActive(false); UnityEngine.Object.Destroy(c.gameObject); }
                }

            // The group is a full-width strip; the visible parts are its children (health bar, slot row). Each copy moves its
            // own children to its side, so the two panels stand next to each other in the middle of the screen.
            var bar1 = g1.transform.Find("Bar (Health)");
            float barW = bar1 != null ? bar1.GetComponent<RectTransform>().rect.width : 568f;
            float shift = barW * 0.5f + 40f;
            ShiftChildren(g1.transform, -shift);
            ShiftChildren(_group2.transform, +shift);
            _bar1 = bar1 != null ? bar1.GetComponent<RectTransform>() : null;
            var bar2t = _group2.transform.Find("Bar (Health)"); _bar2 = bar2t != null ? bar2t.GetComponent<RectTransform>() : null;

            // The copy's hp bar and text are found by the same object names as the originals.
            _hp2 = FindByName<Image>(_group2, _front.hpProgressBar != null ? _front.hpProgressBar.gameObject.name : "");
            _hpText2 = FindByName<TextMeshProUGUI>(_group2, _front.hpText != null ? _front.hpText.gameObject.name : "");
            _slots1 = _front.actionBarSlots;
            _slots2 = _group2.GetComponentsInChildren<UIActionBarSlot>(true);

            // Cloned slots need the same UI manager as the originals before Setup works.
            for (int i = 0; i < _slots2.Length && i < _slots1.Length; i++)
            {
                try { _slots2[i].Init(_slots1[i].uiManager); } catch (Exception e) { Lc.LineOnce("hud2: slot init failed: " + e.GetType().Name + ": " + e.Message); }
                try { _slots2[i].ClearWeapon(); } catch { }
            }

            _label1 = PlaceLabel(_bar1, g1.transform, "P1");
            _label2 = PlaceLabel(_bar2, _group2.transform, "P2");

            if (second == null) { try { if (_hp2 != null) _hp2.fillAmount = 0f; if (_hpText2 != null) _hpText2.text = ""; } catch { } }
            if (Lc.Has("-lchuddump")) DumpTree(_group2.transform, 0);
            _built = true; BuiltAt = Time.realtimeSinceStartup;
            Lc.Line("hud2: built at run start +" + (Time.realtimeSinceStartup - Lc.InRunAt).ToString("F1") + " s (second dwarf " + (second != null ? "there" : "not made yet") + "); shift=" + shift.ToString("F0") + " slots=" + _slots1.Length + "/" + _slots2.Length + " hpFill=" + (_hp2 != null) + " hpText=" + (_hpText2 != null));
        }


        private static void DumpTree(Transform t, int depth)
        {
            try
            {
                var rt = t.GetComponent<RectTransform>();
                var sb = new StringBuilder();
                sb.Append("hud2: tree ").Append(new string(' ', depth * 2)).Append(t.name).Append(" active=").Append(t.gameObject.activeSelf);
                if (rt != null) sb.Append(" pos=").Append(rt.anchoredPosition).Append(" size=").Append(rt.rect.size);
                foreach (var c in t.GetComponents<Component>()) if (c != null && !(c is Transform)) sb.Append(" [").Append(c.GetType().Name).Append("]");
                var im = t.GetComponent<Image>(); if (im != null) sb.Append(" sprite=").Append(im.sprite != null ? im.sprite.name : "-").Append(" color=").Append(im.color).Append(" type=").Append(im.type).Append(" fill=").Append(im.fillAmount).Append(" method=").Append(im.fillMethod).Append(" tiled=").Append(im.hasBorder);
                var mk = t.GetComponent<Mask>(); if (mk != null) sb.Append(" MASK showGraphic=").Append(mk.showMaskGraphic);
                var rm = t.GetComponent<RectMask2D>(); if (rm != null) sb.Append(" RECTMASK");
                Lc.Line(sb.ToString());
                for (int i = 0; i < t.childCount && depth < 4; i++) DumpTree(t.GetChild(i), depth + 1);
            }
            catch { }
        }

        private static string PathOf(Transform t, Transform stopAt)
        {
            var sb = new StringBuilder();
            while (t != null && t != stopAt) { sb.Insert(0, "/" + t.name); t = t.parent; }
            return sb.ToString();
        }

        private static void DumpHierarchy(GameObject root)
        {
            try
            {
                var rt = root.GetComponent<RectTransform>();
                Lc.Line("hud2: group '" + root.name + "' size=" + rt.rect.size + " anchors=" + rt.anchorMin + ".." + rt.anchorMax + " pos=" + rt.anchoredPosition);
                if (_front.hpProgressBar != null) Lc.Line("hud2: hpBar path " + PathOf(_front.hpProgressBar.transform, root.transform.parent));
                if (_front.hpText != null) Lc.Line("hud2: hpText path " + PathOf(_front.hpText.transform, root.transform.parent));
                var slots = _front.actionBarSlots;
                for (int i = 0; slots != null && i < slots.Length && i < 7; i++)
                {
                    var srt = slots[i].GetComponent<RectTransform>();
                    Lc.Line("hud2: slot " + i + " path " + PathOf(slots[i].transform, root.transform.parent) + " size=" + srt.rect.size + " active=" + slots[i].gameObject.activeInHierarchy);
                }
                // children of the group, two levels
                for (int i = 0; i < root.transform.childCount; i++)
                {
                    var c = root.transform.GetChild(i);
                    var crt = c.GetComponent<RectTransform>();
                    Lc.Line("hud2: child " + i + " '" + c.name + "' size=" + (crt != null ? crt.rect.size.ToString() : "n/a") + " active=" + c.gameObject.activeSelf + " kids=" + c.childCount);
                }
            }
            catch (Exception e) { Lc.Line("hud2: dump failed " + e.Message); }
        }


        private static readonly System.Collections.Generic.List<KeyValuePair<RectTransform, Vector2>> _moved = new System.Collections.Generic.List<KeyValuePair<RectTransform, Vector2>>();

        private static void ShiftChildren(Transform group, float dx)
        {
            for (int i = 0; i < group.childCount; i++)
            {
                var rt = group.GetChild(i).GetComponent<RectTransform>();
                if (rt == null) continue;
                _moved.Add(new KeyValuePair<RectTransform, Vector2>(rt, rt.anchoredPosition));
                rt.anchoredPosition = rt.anchoredPosition + new Vector2(dx, 0f);
            }
        }

        private static float CenterX(RectTransform rt)
        {
            return rt.TransformPoint(rt.rect.center).x;
        }

        private static T FindByName<T>(GameObject root, string name) where T : Component
        {
            foreach (var c in root.GetComponentsInChildren<T>(true))
                if (c != null && c.gameObject.name == name) return c;
            return null;
        }

        /// <summary>The label is a child of the bar. If the bar clips its children (Mask / RectMask2D) a child above it would be invisible; then the
        /// label hangs on the bar's parent and is put above the bar's top middle in world space.</summary>
        private static TextMeshProUGUI PlaceLabel(RectTransform bar, Transform fallbackParent, string text)
        {
            if (bar == null) return MakeLabel(fallbackParent, text, Vector2.zero);
            bool clips = bar.GetComponent<UnityEngine.UI.Mask>() != null || bar.GetComponent<UnityEngine.UI.RectMask2D>() != null;
            if (!clips) return MakeLabel(bar.transform, text, Vector2.zero);
            Lc.LineOnce("hud2: the health bar clips its children, label " + text + " hangs on the bar's parent");
            var t = MakeLabel(bar.parent, text, Vector2.zero);
            if (t != null)
            {
                var rt = t.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                var c = new Vector3[4]; bar.GetWorldCorners(c);
                rt.position = new Vector3((c[1].x + c[2].x) * 0.5f, c[1].y, c[1].z) + new Vector3(0f, 4f, 0f);
            }
            return t;
        }

        /// <summary>"P1" / "P2" above the middle of the health bar: a label of its own (not a copy of the hp text, which is bold and anchored somewhere else),
        /// plain weight, black outline, in the player's colour.</summary>
        private static TextMeshProUGUI MakeLabel(Transform parent, string text, Vector2 offset)
        {
            try
            {
                var src = _front.hpText;
                if (src == null) return null;
                var go = new GameObject("LcLabel" + text);
                var rt = go.AddComponent<RectTransform>();
                rt.SetParent(parent, false);
                rt.anchorMin = new Vector2(0.5f, 1f); rt.anchorMax = new Vector2(0.5f, 1f); rt.pivot = new Vector2(0.5f, 0f);
                rt.anchoredPosition = new Vector2(0f, 4f) + offset;
                rt.sizeDelta = new Vector2(160f, 50f);
                var t = go.AddComponent<TextMeshProUGUI>();
                t.font = src.font;
                t.fontSize = Mathf.Max(src.fontSize * 1.7f, 38f);
                t.fontStyle = FontStyles.Normal;
                t.alignment = TextAlignmentOptions.Midline;
                t.text = text;
                t.color = text == "P1" ? CoopMenu.OrangeP1 : CoopMenu.BlueP2;
                t.outlineWidth = 0.07f; t.outlineColor = new Color32(0, 0, 0, 255);
                t.raycastTarget = false;
                t.enableWordWrapping = false;
                return t;
            }
            catch (Exception e) { Lc.LineOnce("hud2: label failed: " + e.GetType().Name + ": " + e.Message); return null; }
        }

        private static void Sync(Player second)
        {
            if (!Phase1.Ready && !Lc.Has("-lcnohide"))
            {
                // the dwarf is not complete yet (it is made while the pod is still coming down): the panel stands there, empty
                if (_hp2 != null) _hp2.fillAmount = 0f;
                if (_hpText2 != null) _hpText2.text = "";
                return;
            }
            long max = 1;
            try { var s = second._maxHp_k__BackingField; if (s != null) max = Math.Max(1L, (long)s.Value); } catch { }
            long cur = Math.Max(0L, second.currentHp);
            if (_hp2 != null) _hp2.fillAmount = Mathf.Clamp01((float)cur / max);
            if (_hpText2 != null) _hpText2.text = HpPrefix() + cur + " / " + max;

            for (int i = 0; i < _slots2.Length; i++)
            {
                WeaponHandler w = null;
                try { if (second.weapons != null && i < second.weapons.Count) w = second.weapons[i]; } catch { }
                var slot = _slots2[i];
                bool has = false;
                try { has = slot.HasWeapon; } catch { }
                if (w != null && w.data != null)
                {
                    if (!has || slot.WeaponHandler == null || slot.WeaponHandler.Pointer != w.Pointer) { try { slot.Show(0); slot.Setup(w.data, w); } catch (Exception e) { Lc.LineOnce("hud2: slot setup failed: " + e.GetType().Name + ": " + e.Message); } }
                }
                else if (has) { try { slot.ClearWeapon(); } catch { } }
                // hand test: player 2's weapon showed level 3 until the next level-up. The badge is made to show the weapon's own level every tick.
                if (w != null && has && !Lc.Has("-lcnolvlsync"))
                {
                    try
                    {
                        int wl = w.WeaponLevel; var lt = slot.levelText;
                        if (lt != null)
                        {
                            string digits = ""; foreach (char ch in lt.text) if (char.IsDigit(ch)) digits += ch;
                            if (digits != wl.ToString()) { if (LevelFixes < 8) Lc.Line("hud2: slot " + i + " showed '" + lt.text + "' for a weapon of level " + wl + ", corrected"); LevelFixes++; slot.UpdateWeaponLevel(wl); }
                        }
                    }
                    catch { }
                }
            }
        }

        private static void Remove()
        {
            if (!_built) return;
            try { if (_group2 != null) UnityEngine.Object.Destroy(_group2); } catch { }
            try { if (_label1 != null) UnityEngine.Object.Destroy(_label1.gameObject); } catch { }
            foreach (var kv in _moved) { try { if (kv.Key != null) kv.Key.anchoredPosition = kv.Value; } catch { } }
            _moved.Clear();
            _group2 = null; _label1 = null; _label2 = null; _built = false;
            Lc.Line("hud2: removed");
        }


        /// <summary>The assertions of the HUD acceptance run.</summary>
        internal static bool HudChecks(float wantFill, out string detail)
        {
            bool built = _built;
            bool sides = false, fill = false, p2weapon = false, p1weapon = false, p1text = false;
            try
            {
                if (_bar1 != null && _bar2 != null && _rt1 != null)
                {
                    float x1 = CenterX(_bar1), x2 = CenterX(_bar2), mid = CenterX(_rt1);
                    sides = x1 < mid && x2 > mid && Mathf.Abs(x2 - x1) > 0.001f;
                }
                fill = _hp2 != null && Mathf.Abs(_hp2.fillAmount - wantFill) < 0.02f;
                // the left panel must show the original dwarf (max hp of the original), not the clone's text
                var gc = Find.One<GameController>();
                var first = gc != null ? gc.player : null;
                if (first != null && _front != null && _front.hpText != null)
                {
                    long maxHost = (long)first._maxHp_k__BackingField.Value;
                    string t = _front.hpText.text ?? "";
                    p1text = t.EndsWith("/ " + maxHost) || t.EndsWith("/" + maxHost);
                    P1Text = t;
                }
                foreach (var s in _slots2) { try { if (s.HasWeapon) p2weapon = true; } catch { } }
                int p1count = 0;
                foreach (var s in _slots1) { try { if (s.HasWeapon) p1count++; } catch { } }
                int hostWeapons = first != null && first.weapons != null ? first.weapons.Count : -1;
                p1weapon = p1count == hostWeapons && p1count >= 1;
                P1Count = p1count + "/" + hostWeapons;
            }
            catch { }
            string who = "?";
            try
            {
                var form = Find.One<UICoreForm>();
                var gc2 = Find.One<GameController>();
                if (form != null && form.player != null)
                    who = (Phase1.Second != null && form.player.Pointer == Phase1.Second.Pointer) ? "CLONE"
                        : (gc2 != null && gc2.player != null && form.player.Pointer == gc2.player.Pointer ? "host" : "other");
            }
            catch (Exception e) { who = "err " + e.GetType().Name; }
            detail = "UICoreForm.player=" + who + " p1text('" + P1Text + "')=" + p1text + " built=" + built + " p1Left_p2Right=" + sides + " hp2fill(want " + wantFill.ToString("F2") + ")=" + fill + " p2weapon=" + p2weapon + " p1weapons(shown/host " + P1Count + ")=" + p1weapon;
            bool noDrone = true;
            try { if (_group2 != null) foreach (var c in _group2.GetComponentsInChildren<Transform>(true)) if (c != null && !c.Equals(null) && (c.name == "BoscoEggWidget" || c.name == "StackableBuffContainer") && c.gameObject.activeInHierarchy) noDrone = false; } catch { }
            bool dmgText = HudPatches.ClonePlayerTexts > 0;
            detail += " noDroneIconOnP2=" + noDrone + " damageNumberAtP2=" + dmgText + "(" + HudPatches.ClonePlayerTexts + ")";
            return built && sides && fill && p2weapon && p1weapon && p1text && noDrone && dmgText;
        }

        /// <summary>For the test: what the two panels look like on screen right now.</summary>
        internal static string Describe()
        {
            var sb = new StringBuilder();
            try
            {
                if (_bar1 != null) sb.Append("bar1x=").Append(CenterX(_bar1).ToString("F2"));
                if (_bar2 != null) sb.Append(" bar2x=").Append(CenterX(_bar2).ToString("F2"));
                if (_rt1 != null) sb.Append(" midx=").Append(CenterX(_rt1).ToString("F2"));
                sb.Append(" screenW=").Append(Screen.width);
                sb.Append(" hp2fill=").Append(_hp2 != null ? _hp2.fillAmount.ToString("F2") : "n/a");
                sb.Append(" hp2text='").Append(_hpText2 != null ? _hpText2.text : "n/a").Append("'");
                int active1 = 0, active2 = 0;
                if (_slots1 != null) foreach (var s in _slots1) { try { if (s.HasWeapon) active1++; } catch { } }
                if (_slots2 != null) foreach (var s in _slots2) { try { if (s.HasWeapon) active2++; } catch { } }
                sb.Append(" weaponSlots p1/p2=").Append(active1).Append("/").Append(active2);
            }
            catch { }
            return sb.ToString();
        }

        /// <summary>The word in front of the numbers in the game's own health text ("LP: " in German, "HP: " in English): player 2's text is written the same way.</summary>
        internal static int LevelFixes;
        internal static string P2HpText { get { try { return _hpText2 != null ? _hpText2.text : ""; } catch { return ""; } } }

        private static string HpPrefix()
        {
            try
            {
                string t = _front != null && _front.hpText != null ? _front.hpText.text : null;
                if (!string.IsNullOrEmpty(t)) { int i = 0; while (i < t.Length && !char.IsDigit(t[i])) i++; if (i > 0 && i < t.Length) if (!Lc.Has("-lcnegprefix")) { var pf = t.Substring(0, i).TrimEnd('-'); if (pf.Length > 0) return pf; } else return t.Substring(0, i); }
            }
            catch { }
            return "LP: ";
        }
    }
}
