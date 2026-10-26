using System;
using System.Collections.Generic;
using System.Text;
using DRS.UI;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace LocalCoop
{
    /// <summary>The end screen for both dwarves. First step: the probe that ends the run through the game's own event and dumps the screen.</summary>
    internal static class EndScreen2
    {
        internal static bool TestDone;
        private static int _step; private static float _in = -1f, _at = -1f;

        private static void Dump(Transform t, int depth, StringBuilder sb)
        {
            var rt = t.GetComponent<RectTransform>();
            sb.Append(new string(' ', depth * 2)).Append(t.name).Append(" a=").Append(t.gameObject.activeSelf ? 1 : 0);
            if (rt != null) sb.Append(" pos=").Append(rt.anchoredPosition).Append(" size=").Append(rt.rect.size).Append(" anc=").Append(rt.anchorMin).Append(rt.anchorMax).Append(" sc=").Append(rt.localScale.x.ToString("F2"));
            var vl = t.GetComponent<VerticalLayoutGroup>(); if (vl != null) sb.Append(" [VLG spacing=" + vl.spacing + " ctrlH=" + vl.childControlHeight + " ctrlW=" + vl.childControlWidth + " expH=" + vl.childForceExpandHeight + " pad=" + vl.padding.top + "/" + vl.padding.bottom + "]");
            var hl = t.GetComponent<HorizontalLayoutGroup>(); if (hl != null) sb.Append(" [HLG spacing=" + hl.spacing + " ctrlH=" + hl.childControlHeight + " ctrlW=" + hl.childControlWidth + " expW=" + hl.childForceExpandWidth + " pad=" + hl.padding.left + "/" + hl.padding.right + "]");
            var le = t.GetComponent<LayoutElement>(); if (le != null) sb.Append(" [LE pref=" + le.preferredWidth + "x" + le.preferredHeight + " flex=" + le.flexibleWidth + "x" + le.flexibleHeight + " min=" + le.minWidth + "x" + le.minHeight + "]");
            var csf = t.GetComponent<ContentSizeFitter>(); if (csf != null) sb.Append(" [CSF]");
            var tm = t.GetComponent<TextMeshProUGUI>(); if (tm != null) sb.Append(" text='").Append(tm.text).Append("' fs=").Append(tm.fontSize.ToString("F0"));
            sb.Append('\n');
            for (int i = 0; i < t.childCount && depth < 5; i++) Dump(t.GetChild(i), depth + 1, sb);
        }

        // ---- the real thing: player 2 on the game's own end screen ------------------------------------------------------------------------
        // The screen shows one dwarf. Player 2's weapons get a window of their own (a copy of the weapons window, filled by the game's own row code), the
        // player statistics get a second value column in player 2's colour, and the class window one more line for player 2's class experience.
        // Twin `-lcnoend2`.
        internal static string Info = "", ProgInfo = "";
        private static string ProgressionP2Text(UIProgressionSummaryForm pf) { return ""; }
        private static int _ok, _fail;
        private static void Ck(string what, bool pass, string detail) { if (pass) _ok++; else _fail++; Lc.Line("ui: CHECK " + (pass ? "ok  " : "FAIL") + " " + what + " - " + detail); }
        internal static int Windows; internal static bool ArtifactsHidden;

        [HarmonyLib.HarmonyPatch(typeof(UIEndScreen), "Setup")]
        internal static class SetupPatch
        {
            private static void Postfix(UIEndScreen __instance)
            {
                try { if (!Lc.Has("-lcnoend2") && Phase1.Second != null) Build(__instance); }
                catch (Exception e) { Lc.LineOnce("endscreen2: threw " + e.GetType().Name + ": " + e.Message); }
            }
        }

        private static Transform Find(Transform root, string path) { return root.Find(path); }

        private static void SetTitle(Transform window, string text)
        {
            var t = window.Find("Title"); if (t == null) return;
            var tm = t.GetComponent<TextMeshProUGUI>(); if (tm != null) { foreach (var c in t.GetComponents<Component>()) if (c != null && c.GetType().Name.Contains("Localize")) UnityEngine.Object.Destroy(c); tm.text = text; }
        }

        private static void Place(Transform t, float x, float y, float w, float h, float s)
        {
            var rt = t.GetComponent<RectTransform>();
            var le = t.GetComponent<LayoutElement>() ?? t.gameObject.AddComponent<LayoutElement>(); le.ignoreLayout = true;
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(w, h / s); rt.localScale = new Vector3(s, s, 1f); rt.anchoredPosition = new Vector2(x, y);
        }

        private static void Build(UIEndScreen es)
        {
            var root = es.transform;
            var mid = Find(root, "Columns/MidColumn");
            var w1 = Find(root, "Columns/MidColumn/NonSelectableWindow_Weapons");
            var bottom = Find(root, "Columns/MidColumn/BottomGroup");
            if (mid == null || w1 == null || bottom == null) { Info = "layout not found"; return; }
            var old = mid.Find("LcWeapons2"); if (old != null) UnityEngine.Object.Destroy(old.gameObject);
            var clone = Phase1.Second;

            // ---- weapons: two windows one above the other, the damage overview below ----
            var w2 = UnityEngine.Object.Instantiate(w1.gameObject, mid).transform; w2.name = "LcWeapons2";
            const float s = 0.8f, hw = 236f, gap = 8f;
            float width = mid.GetComponent<RectTransform>().rect.width, total = mid.GetComponent<RectTransform>().rect.height;
            if (width < 100f) width = 742f; if (total < 100f) total = 780f;
            float off = width * (1f - s) * 0.5f;
            Place(w1, off, 0f, width, hw, s);
            Place(w2, off, -(hw + gap), width, hw, s);
            Place(bottom, 0f, -(2f * (hw + gap)), width, total - 2f * (hw + gap), 1f);
            _t1 = w1; _t2 = w2; _titleBase = TitleOf(w1); _titleUntil = Time.realtimeSinceStartup + 3f;
            SetTitle(w1, Loc.T("PLAYER 1") + " - " + _titleBase);
            SetTitle(w2, Loc.T("PLAYER 2") + " - " + _titleBase);
            var rows = w2.GetComponentsInChildren<UIEndWeaponStat>(true);
            var ws = clone.Weapons; int n = 0;
            for (int i = 0; i < rows.Length; i++)
            {
                if (ws != null && i < ws.Count && ws[i] != null) { rows[i].gameObject.SetActive(true); rows[i].Show(ws[i]); n++; }
                else rows[i].Hide();
            }
            Info = "weapons of player 2 shown: " + n;
            // ---- player statistics: a block for each dwarf (player 1's rows stay the game's, player 2's are copies bound to the second dwarf) ----
            try
            {
                var prow = es.playerStats;
                if (prow != null && prow.Length > 0 && prow[0] != null)
                {
                    var content = prow[0].transform.parent;
                    for (int i = content.childCount - 1; i >= 0; i--) if (content.GetChild(i).name.StartsWith("LcStat")) UnityEngine.Object.Destroy(content.GetChild(i).gameObject);
                    Transform Header(string text, Color col, int index)
                    {
                        var h = UnityEngine.Object.Instantiate(prow[0].gameObject, content); h.name = "LcStatHead";
                        var comp = h.GetComponent<UIPauseStat>(); if (comp != null) UnityEngine.Object.DestroyImmediate(comp);
                        foreach (var im in h.GetComponentsInChildren<Image>(true)) im.enabled = false;
                        var tms = h.GetComponentsInChildren<TextMeshProUGUI>(true);
                        for (int k = 0; k < tms.Length; k++) { tms[k].text = k == 0 ? text : ""; tms[k].color = col; if (k == 0) { tms[k].fontStyle = FontStyles.Normal; tms[k].outlineWidth = 0f; } }
                        h.transform.SetSiblingIndex(index);
                        return h.transform;
                    }
                    Header(Loc.T("PLAYER 1"), Chooser.Red, 0);
                    Header(Loc.T("PLAYER 2"), CoopMenu.BlueP2, content.childCount);
                    int made = 0;
                    for (int i = 0; i < prow.Length; i++)
                    {
                        if (prow[i] == null) continue;
                        var c = UnityEngine.Object.Instantiate(prow[i].gameObject, content); c.name = "LcStatRow"; c.SetActive(true);
                        var cs = c.GetComponent<UIPauseStat>();
                        if (cs != null) { cs.Setup(prow[i].statSetting, clone); made++; }
                    }
                    Info += ", stat rows of player 2: " + made;
                }
            }
            catch (Exception e) { Lc.LineOnce("endscreen2: stats threw " + e.GetType().Name + ": " + e.Message); }

            // ---- class window: one more line, player 2's class experience ----
            try
            {
                var list = Find(root, "Columns/LeftColumn/NonSelectableWindow_Class/Content/List");
                var rx = list != null ? list.Find("RankXp") : null;
                if (rx != null && !Lc.Has("-lcnoclassline"))
                {
                    var old2 = list.Find("LcRankXp2"); if (old2 != null) UnityEngine.Object.Destroy(old2.gameObject);
                    var r2 = UnityEngine.Object.Instantiate(rx.gameObject, list); r2.name = "LcRankXp2";
                    r2.transform.SetSiblingIndex(rx.GetSiblingIndex() + 1);
                    string p1n = es.currentClass != null ? es.currentClass.DisplayName : "", p2n = CoopMenu.Classes[CoopMenu.P2.ClassIndex].DisplayName;
                    var ct = r2.transform.Find("ClassText"); var tx = r2.transform.Find("RankXPText");
                    if (ct != null) { var t = ct.GetComponent<TextMeshProUGUI>(); string s0 = t != null ? t.text : ""; if (t != null) t.text = p1n.Length > 0 && s0.Contains(p1n) ? s0.Replace(p1n, p2n) : p2n + " " + s0; }
                    if (tx != null) { var t = tx.GetComponent<TextMeshProUGUI>(); if (t != null) t.text = RunProgress.P2Awards > 0 ? RunProgress.LastXp.ToString() : (es.economyManager != null ? es.economyManager.rankXpEarned.ToString() : ""); }
                    Info += ", class line added";
                    var cw = Find(root, "Columns/LeftColumn/NonSelectableWindow_Class") as Transform; var rw = Find(root, "Columns/LeftColumn/NonSelectableWindow_Resources");
                    if (cw != null && rw != null) { var cr = cw.GetComponent<RectTransform>(); var rr = rw.GetComponent<RectTransform>(); cr.sizeDelta = new Vector2(cr.sizeDelta.x, cr.sizeDelta.y + 40f); rr.sizeDelta = new Vector2(rr.sizeDelta.x, rr.sizeDelta.y - 40f); }
                }
            }
            catch (Exception e) { Lc.LineOnce("endscreen2: class line threw " + e.GetType().Name + ": " + e.Message); }
            // ---- artifacts: not shown (hand test: the screen listed the artifacts of player 1 only, which reads as if player 2 had none). Twin `-lcshowartifacts`. ----
            try
            {
                if (!Lc.Has("-lcshowartifacts") && es.artifactContainer != null)
                {
                    var ac = es.artifactContainer.transform;
                    // the whole window around the images, not only the image row; the container's parent is the window when it has a title
                    var win = ac.parent != null && ac.parent.name.StartsWith("NonSelectableWindow") ? ac.parent : ac;
                    win.gameObject.SetActive(false); ArtifactsHidden = true;
                    Info += ", artifacts hidden (" + win.name + ")";
                    // hand test (1.0.1, a run with artifacts): the icons showed up at the bottom of player 2's weapon window. The artifact row lives inside the
                    // weapon window, and player 2's window is a copy made above, artifacts included. Twin `-lcshowartifacts` leaves both.
                    var copy = CopyIn(w1, w2, win);
                    if (copy != null) { copy.gameObject.SetActive(false); Info += ", also in player 2's window"; }
                    else if (win.IsChildOf(w1)) Info += ", NOT FOUND in player 2's window";
                }
            }
            catch (Exception e) { Lc.LineOnce("endscreen2: artifacts threw " + e.GetType().Name + ": " + e.Message); }
            Windows++;
            Lc.Line("endscreen2: " + Info);
        }

        /// <summary>The object in the copied window <paramref name="copy"/> that sits at the same place as <paramref name="t"/> in <paramref name="orig"/>; null when t is not inside orig.</summary>
        private static Transform CopyIn(Transform orig, Transform copy, Transform t)
        {
            if (orig == null || copy == null || t == null || !t.IsChildOf(orig) || t == orig) return null;
            var path = new List<int>();
            for (var x = t; x != orig; x = x.parent) path.Insert(0, x.GetSiblingIndex());
            var c = copy;
            foreach (int i in path) { if (i >= c.childCount) return null; c = c.GetChild(i); }
            return c.name == t.name ? c : null;
        }

        private static Transform _t1, _t2; private static string _titleBase = ""; private static float _titleUntil;

        /// <summary>The window titles are set by the game's localisation a moment after the screen shows; ours are put back for a few seconds.</summary>
        internal static void KeepTitles()
        {
            if (_t1 == null || _t2 == null) return;
            if (Lc.Has("-lcshorttitles") && Time.realtimeSinceStartup > _titleUntil) return;
            try
            {
                // hand test: after a few seconds the game wrote its own (German) window title back, and the window had no "PLAYER 1" in front. Kept for as long as the
                // windows exist; what the game wrote last is the base. Twin `-lcshorttitles` (the old three seconds).
                var t = _t1.Find("Title"); var tm = t != null ? t.GetComponent<TextMeshProUGUI>() : null;
                if (tm != null && !tm.text.StartsWith(Loc.T("PLAYER 1")) && tm.text.Length > 0) _titleBase = tm.text;
                SetTitle(_t1, Loc.T("PLAYER 1") + " - " + _titleBase); SetTitle(_t2, Loc.T("PLAYER 2") + " - " + _titleBase);
            }
            catch { }
        }

        private static string TitleOf(Transform window)
        {
            var t = window.Find("Title"); var tm = t != null ? t.GetComponent<TextMeshProUGUI>() : null;
            string x = tm != null ? tm.text : "";
            int k = x.IndexOf(" - "); return k >= 0 ? x.Substring(k + 3) : x;
        }

        // ---- the progression summary after the end screen: one more line for player 2's class experience ----
        [HarmonyLib.HarmonyPatch(typeof(UIProgressionSummaryForm), "Show")]
        internal static class ProgressionPatch
        {
            private static void Postfix(UIProgressionSummaryForm __instance)
            {
                try { Lc.Line("endscreen2: progression summary shown, second=" + (Phase1.Second != null) + " awards=" + RunProgress.P2Awards); if (!Lc.Has("-lcnoprogline") && (Phase1.Second != null || RunProgress.P2Awards > 0)) AddLine(__instance); }
                catch (Exception e) { Lc.LineOnce("endscreen2: progression line threw " + e.GetType().Name + ": " + e.Message); }
            }
        }

        private static void AddLine(UIProgressionSummaryForm pf)
        {
            var src = pf.rankGainedText != null ? pf.rankGainedText : pf.xpText;
            if (src == null) { Lc.Line("endscreen2: progression form without the text fields"); return; }
            var old = pf.transform.Find("LcP2Progress"); if (old != null) UnityEngine.Object.Destroy(old.gameObject);
            var go = new GameObject("LcP2Progress"); go.AddComponent<RectTransform>().SetParent(pf.transform, false); var ntm = go.AddComponent<TextMeshProUGUI>(); ntm.font = src.font; ntm.raycastTarget = false; ntm.outlineWidth = 0.15f; ntm.outlineColor = new Color32(0, 0, 0, 255);
            { var r0 = go.GetComponent<RectTransform>(); r0.anchorMin = new Vector2(0.5f, 0f); r0.anchorMax = new Vector2(0.5f, 0f); r0.pivot = new Vector2(0.5f, 0.5f); r0.anchoredPosition = new Vector2(0f, 250f); r0.sizeDelta = new Vector2(900f, 60f); }
            var tm = go.GetComponent<TextMeshProUGUI>();
            foreach (var c in go.GetComponents<Component>()) if (c != null && c.GetType().Name.Contains("Localize")) UnityEngine.Object.Destroy(c);
            string cn = CoopMenu.Classes[CoopMenu.P2.ClassIndex].DisplayName;
            int xp = RunProgress.P2Awards > 0 ? RunProgress.LastXp : 0;
            if (tm != null) { tm.text = Loc.T("PLAYER 2") + " - " + cn + " +" + xp + " XP"; tm.color = CoopMenu.BlueP2; tm.enableWordWrapping = false; tm.alignment = TextAlignmentOptions.Midline; tm.fontSize = 34f; }
            ProgInfo = "line added for " + cn;
        }

        private static long Tracked(Player p)
        {
            long t = 0; try { if (p != null && p.weapons != null) for (int i = 0; i < p.weapons.Count; i++) if (p.weapons[i] != null) t += p.weapons[i].trackedTotalDamage; } catch { }
            return t;
        }

        /// <summary>hand test: the damage numbers of player 1 and player 2 on the end screen looked wrong. What the game has, what each window shows (read off the screen's own texts).</summary>
        private static void DamageNumbers(UIEndScreen es)
        {
            try
            {
                var host = Phase1.First(); var clone = Phase1.Second;
                long h = Tracked(host), c = Tracked(clone);
                var sb = new StringBuilder("endscreen: damage tracked by the weapons: P1=" + h + " P2=" + c + " sum=" + (h + c) + " | tracker:");
                long typeSum = 0; var tr = CoopStart.Resolve<CoreStatTracker>();
                if (tr != null)
                    foreach (var pi in typeof(CoreStatTracker).GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
                    {
                        if (pi.PropertyType != typeof(long) || pi.GetIndexParameters().Length > 0) continue;
                        long v; try { v = (long)pi.GetValue(tr, null); } catch { continue; }
                        sb.Append(' ').Append(pi.Name).Append('=').Append(v);
                        if (pi.Name.EndsWith("Done")) typeSum += v;
                    }
                sb.Append(" | per-type sum=" + typeSum);
                Lc.Line(sb.ToString());
                Lc.Line("endscreen: seconds since the run began: " + (Lc.InRunAt >= 0f ? (Time.realtimeSinceStartup - Lc.InRunAt).ToString("F1") : "?") + " (game time scale " + Time.timeScale + ")");
                float elapsed = Lc.InRunAt >= 0f ? Time.realtimeSinceStartup - Lc.InRunAt : -1f; float maxActive = 0f;
                foreach (var p in new[] { host, clone })
                {
                    if (p == null) continue;
                    try { for (int i = 0; p.weapons != null && i < p.weapons.Count; i++) if (p.weapons[i] != null) maxActive = Math.Max(maxActive, p.weapons[i].activeTime); } catch { }
                    var wl = new StringBuilder("endscreen: weapons of " + (p == clone ? "P2" : "P1") + ":");
                    try
                    {
                        for (int i = 0; p.weapons != null && i < p.weapons.Count; i++)
                        {
                            var w = p.weapons[i]; if (w == null) continue;
                            wl.Append(' ').Append(w.data != null ? w.data.name : "?").Append(" tracked=").Append(w.trackedTotalDamage).Append(" dps=").Append(w.TrackedDPS.ToString("F1")).Append(" activeTime=").Append(w.activeTime.ToString("F1")).Append(';');
                        }
                        wl.Append(" handlers on the object: ").Append(p.GetComponentsInChildren<WeaponHandler>(true).Length);
                    }
                    catch (Exception e) { wl.Append(" threw " + e.GetType().Name + ": " + e.Message); }
                    Lc.Line(wl.ToString());
                }
                // a weapon cannot have been active for longer than the run lasted (the damage per second on the screen is damage / this time)
                if (Lc.Has("-lcendlate")) Ck("no weapon's active time is longer than the run", elapsed > 0f && maxActive <= elapsed + 2f, "longest active time " + maxActive.ToString("F1") + " s, run " + elapsed.ToString("F1") + " s");
                // what the screen itself says: every text under the weapon windows and the damage block
                foreach (var nm in new[] { "Columns/MidColumn/NonSelectableWindow_Weapons", "Columns/MidColumn/LcWeapons2", "Columns/MidColumn/BottomGroup" })
                {
                    var t = es.transform.Find(nm); if (t == null) continue;
                    var tx = new StringBuilder("endscreen: texts in " + nm + ":");
                    foreach (var tm in t.GetComponentsInChildren<TextMeshProUGUI>(false)) if (tm != null && tm.text.Length > 0 && tm.gameObject.activeInHierarchy) tx.Append(" [").Append(tm.text.Replace('\n', ' ')).Append(']');
                    Lc.Line(tx.ToString());
                }
            }
            catch (Exception e) { Lc.Line("endscreen: damage numbers threw " + e.GetType().Name + ": " + e.Message); }
        }

        internal static void Test()
        {
            if (TestDone || !Lc.Has("-lcendtest")) return;
            float now = Time.realtimeSinceStartup;
            if (_step == 0 && (Lc.InRunAt < 0f || Phase1.Second == null)) return;
            if (_in < 0f) _in = now;
            if (now < _in + (Lc.Has("-lcendlate") ? 45f : 12f) || now < _at) return;
            try
            {
                switch (_step)
                {
                    case 0:
                        // hand test (1.0.1): the artifact icons showed up in player 2's window only in runs with artifacts; `-lcendarts` gives the host two before the end
                        if (Lc.Has("-lcendarts"))
                        {
                            int given = 0; var host = Phase1.First();
                            foreach (var a in Resources.FindObjectsOfTypeAll<ArtifactSkillData>())
                            {
                                if (given >= 2 || a == null || a is ClassArtifactData || host == null) continue;
                                try { host.EquipArtifact(a, false); given++; Lc.Line("endscreen: host got artifact " + a.name); } catch (Exception e) { Lc.Line("endscreen: equip " + a.name + " threw " + e.GetType().Name); }
                            }
                        }
                        CoreGameEvents.TriggerCoreRunEnd(ECoreRunEndCondition.WIN);
                        Lc.Line("endscreen: the run was ended through the game's own event");
                        _at = now + 7f; _step++;
                        break;
                    case 1:
                        {
                            var es = UnityEngine.Object.FindObjectOfType<UIEndScreen>(true);
                            Lc.Line("endscreen: found=" + (es != null) + " active=" + (es != null && es.gameObject.activeInHierarchy));
                            if (es != null && Lc.Has("-lcenddump")) { var sb = new StringBuilder(); Dump(es.transform, 0, sb); foreach (var l in sb.ToString().Split('\n')) if (l.Length > 0) Lc.Line("endtree: " + l); }
                            if (es != null)
                            {
                                var w2 = es.transform.Find("Columns/MidColumn/LcWeapons2");
                                int on = 0; if (w2 != null) foreach (var r in w2.GetComponentsInChildren<UIEndWeaponStat>(false)) if (r != null && r.gameObject.activeInHierarchy) on++;
                                int expect = Phase1.Second != null && Phase1.Second.Weapons != null ? Phase1.Second.Weapons.Count : 0;
                                Ck("player 2's weapons have a window of their own on the end screen", w2 != null && expect > 0 && on == expect, "window=" + (w2 != null) + " rows shown=" + on + " weapons=" + expect);
                                int sr = 0; foreach (var t in es.GetComponentsInChildren<Transform>(true)) if (t != null && t.name == "LcStatRow" && t.gameObject.activeInHierarchy) sr++;
                                Ck("player 2's statistics are on the end screen", sr > 0, "rows=" + sr);
                                // visible = any object named like the artifact container (the game's and the copy in player 2's window) is active up to the screen
                                int artShown = 0; string artName = es.artifactContainer != null ? es.artifactContainer.name : "";
                                if (artName.Length > 0) foreach (var t in es.GetComponentsInChildren<Transform>(true)) if (t != null && t.name == artName && t.gameObject.activeInHierarchy) artShown++;
                                Ck("the end screen shows no artifacts", artName.Length > 0 && artShown == 0, "artifact areas visible=" + artShown + " (name '" + artName + "') hidden by plugin=" + ArtifactsHidden);
                                Ck("the class window has a line for player 2's class", es.transform.Find("Columns/LeftColumn/NonSelectableWindow_Class/Content/List/LcRankXp2") != null, "line present");
                            }
                            DamageNumbers(es);
                            Shots.Take("endscreen");
                            _at = now + 1.5f; _step++;
                        }
                        break;
                    case 2:
                        {
                            var es2 = UnityEngine.Object.FindObjectOfType<UIEndScreen>(true);
                            if (!Lc.Has("-lcendprog") || es2 == null) { _at = now + 0.5f; _step = 9; break; }
                            { UIProgressionSummaryForm pf0 = null; foreach (var sc in Resources.FindObjectsOfTypeAll<UISceneCore>()) if (sc != null && sc.ProgressionSummaryForm != null) { pf0 = sc.ProgressionSummaryForm; break; } if (pf0 != null) { pf0.gameObject.SetActive(true); pf0.Show(ECoreRunEndCondition.WIN, new Il2CppSystem.Collections.Generic.List<GearView>(), new Action(() => { })); } Lc.Line("endscreen: the progression summary was opened through its own Show (the game skips it without gear)"); } _at = now + 5f; _step++;
                        }
                        break;
                    case 3:
                        {
                            UIProgressionSummaryForm pf = null; foreach (var f0 in Resources.FindObjectsOfTypeAll<UIProgressionSummaryForm>()) if (f0 != null) { pf = f0; break; }
                            Lc.Line("endscreen: progression form found=" + (pf != null) + " active=" + (pf != null && pf.gameObject.activeInHierarchy));
                            if (pf != null) { foreach (var t in pf.GetComponentsInChildren<TextMeshProUGUI>(false)) if (t != null && t.text.Length > 0) Lc.Line("progform: '" + t.name + "' = '" + t.text + "'"); ProgInfo = ProgressionP2Text(pf); }
                            bool hasLine = false; if (pf != null) foreach (var t3 in pf.GetComponentsInChildren<Transform>(true)) if (t3 != null && t3.name == "LcP2Progress") hasLine = true;
                            Ck("the progression summary has a line for player 2's class", hasLine, ProgInfo);
                            Shots.Take("progression");
                            _at = now + 1.5f; _step = 9;
                        }
                        break;
                    default:
                        TestDone = true; UiTest.FinishWith(_fail == 0 && Windows > 0 || Lc.Has("-lcnoend2") && _fail > 0 && false, "end screen ok=" + _ok + " fail=" + _fail);
                        break;
                }
            }
            catch (Exception e) { TestDone = true; UiTest.FinishWith(false, "endscreen threw " + e.GetType().Name + ": " + e.Message); }
        }
    }
}
