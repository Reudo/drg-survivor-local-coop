using System;
using System.Linq;
using DRS.UI;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace LocalCoop
{
    /// <summary>
    /// Makes it obvious whose level-up it is: the title above the cards ("LEVEL UP!") becomes "PLAYER 1 LEVEL UP!" in player 1's orange or player 2's
    /// blue, much larger, and the pad of that player buzzes once when a level-up opens. Twin `-lcnolevelbanner`.
    /// A level-up is announced by the form's own Setup call (once per level), not by the form becoming visible: between two rounds the form stays on.
    /// </summary>
    internal static class LevelBanner
    {
        internal static int Shown, Buzzes;
        internal static string LastTitle = "", OrigTitle = "";
        internal static Color LastColorP1, LastColorP2;
        private static TextMeshProUGUI _tt;
        private static float _baseSize; private static Color _baseColor;
        private static float _shotAt = -1f; private static string _shotName = "";
        private static bool _p2, _supply;
        internal static bool SupplyTitleShown;

        private static string Clean(string s) { return System.Text.RegularExpressions.Regex.Replace(s ?? "", "<[^>]+>", "").Trim(); }

        /// <summary>Is this the supply capsule's form (the game's `LevelUpStringData.SupplyPodTitle`)?</summary>
        private static bool IsSupplyTitle(string orig)
        {
            try
            {
                string o = Clean(orig); if (o.Length == 0) return false;
                foreach (var d in Resources.FindObjectsOfTypeAll<LevelUpStringData>()) if (d != null && d.SupplyPodTitle != null && Clean(d.SupplyPodTitle.GetLocalizedString()) == o) return true;
            }
            catch { }
            return false;
        }

        /// <summary>Each level-up (and each loot crate round) opens with a Setup of the form.</summary>
        [HarmonyPatch]
        internal static class SetupPatch
        {
            private static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
            {
                return AccessTools.GetDeclaredMethods(typeof(UILevelUpForm)).Where(m => m.Name == "Setup").Cast<System.Reflection.MethodBase>();
            }

            private static void Postfix(UILevelUpForm __instance)
            {
                try
                {
                    if (Lc.Has("-lcnolevelbanner") || Phase1.Second == null) return;
                    _p2 = LevelRounds.P2Round;
                    Shown++; Haptics.Buzz(_p2); Buzzes++;
                    _shotAt = Time.realtimeSinceStartup + 1.3f; _shotName = "levelup-form-p" + (_p2 ? "2" : "1");
                    var tt = __instance.titleText;
                    if (tt != null && tt != _tt) { _tt = tt; _baseSize = tt.fontSize; _baseColor = tt.color; }
                    if (tt != null)
                    {
                        string t = tt.text ?? "";
                        if (!t.StartsWith(Loc.T("PLAYER 1")) && !t.StartsWith(Loc.T("PLAYER 2"))) { OrigTitle = t; _supply = !Lc.Has("-lcnosupplytitle") && IsSupplyTitle(t); }
                    }
                }
                catch (Exception e) { Lc.LineOnce("levelbanner: setup postfix threw " + e.GetType().Name + ": " + e.Message); }
            }
        }

        // ---- test (`-lcsupplytest`): the supply capsule's form is announced as player 1's choice for both ------------------------------
        internal static bool TestDone;
        private static bool _tOk; private static int _tStep; private static float _tIn = -1f, _tAt = -1f;

        internal static void Test()
        {
            if (TestDone || !Lc.Has("-lcsupplytest")) return;
            float now = Time.realtimeSinceStartup;
            if (Lc.InRunAt < 0f || Phase1.Second == null) return;
            if (_tIn < 0f) _tIn = now;
            if (now < _tIn + 12f || now < _tAt) return;
            _tAt = now + 2.5f;
            try
            {
                int st = _tStep++; if (st == 0) { CoreGameEvents.TriggerSupplyPodAccessed(); return; }
                if (st == 2) { TestDone = true; UiTest.FinishWith(_tOk, "supply title '" + (LastTitle ?? "") + "'"); return; }
                string t = LastTitle ?? "";
                bool ok = SupplyTitleShown && t.Contains(Loc.T(" chooses for both: ").Trim());
                Lc.Line("ui: CHECK " + (ok ? "ok  " : "FAIL") + " the supply capsule form says player 1 chooses for both - title '" + t + "'");
                Shots.Take("supply-title"); _tOk = ok;

            }
            catch (Exception e) { TestDone = true; UiTest.FinishWith(false, "supply test threw " + e.GetType().Name + ": " + e.Message); }
        }

        private static UILevelUpForm OpenForm()
        {
            foreach (var f in Find.Forms<UILevelUpForm>()) if (f != null && f.gameObject.activeInHierarchy) return f;
            return null;
        }

        internal static void Tick()
        {
            if (Lc.Has("-lcnolevelbanner") || Phase1.Second == null) return;
            try
            {
                var f = OpenForm();
                if (f != null && f.titleText != null && _tt != null && f.titleText == _tt)
                {
                    var tt = f.titleText;
                    bool p2 = LevelRounds.P2Round;
                    // hand test: "Player 1 supply capsule" read as if only player 1 got it. The supply capsule is answered once, by player 1, for both (twin `-lcnosupplytitle`).
                    string want = _supply && !p2 ? Loc.T("PLAYER 1") + Loc.T(" chooses for both: ") + OrigTitle : Loc.T(p2 ? "PLAYER 2" : "PLAYER 1") + " " + OrigTitle;
                    SupplyTitleShown = _supply && !p2;
                    if (tt.text != want) tt.text = want;
                    tt.color = p2 ? CoopMenu.BlueP2 : (Lc.Has("-lcorangep1") ? CoopMenu.OrangeP1 : Chooser.Red);      // player 1 is red everywhere (hand test: it was orange-yellow)
                    if (p2) LastColorP2 = tt.color; else LastColorP1 = tt.color;
                    tt.fontSize = Mathf.Max(_baseSize * 1.3f, 60f) * (SupplyTitleShown ? 0.62f : 1f);       // the longer supply capsule title must not run under the minimap
                    tt.enableWordWrapping = false; tt.overflowMode = TextOverflowModes.Overflow;
                    tt.outlineWidth = 0.18f; tt.outlineColor = new Color32(0, 0, 0, 255);
                    LastTitle = tt.text;
                }
                if (_shotAt > 0f && Time.realtimeSinceStartup > _shotAt) { _shotAt = -1f; if (Lc.Has("-lcbannershot")) Shots.Take(_shotName); }
            }
            catch (Exception e) { Lc.LineOnce("levelbanner: threw " + e.GetType().Name + ": " + e.Message); }
        }
    }
}
