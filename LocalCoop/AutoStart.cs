using System;
using System.Collections.Generic;
using System.Text;
using DRS.UI;
using FundayFactory.UI;
using HarmonyLib;
using UnityEngine;

namespace LocalCoop
{
    /// <summary>
    /// `-lcauto`: walk the menu into a run with nobody clicking, and keep the run going (level-up,
    /// gear popup, shop). Ported in a reduced form from the network prototype's CoopAuto; the lessons
    /// stay valid:
    ///  - the boot splash needs a real mouse click (the launcher script does that, advancing its flow
    ///    from here skips the save-slot selector and the campaign then throws);
    ///  - drive by observed UI state, not by timing;
    ///  - the game's buttons are DRS.UI.UIButton, pressed through their protected OnButtonClick;
    ///  - the wizard has no Next button, it advances by selecting content in a fixed order.
    /// </summary>
    internal static class AutoStart
    {
        private static readonly string[] Plan =
        {
            "UIChallengeSetButton_MissionRoad", // run-type landing page (campaign tab)
            "UIMissionRoadButton_",             // mission type (Elimination / Escort)
            "MissionSectorButton_",             // sector
            "MissionNodeButton_",               // node
            "ClassSelectButton_",               // dwarf class
            "UISelectable_Hex_ClassArtifact",   // class artifact
            "HazLevelButton",                   // hazard level
        };

        private static int _planIndex;
        private static float _planPressedAt = -1f;
        private static float _nextStepAt = -1f;
        private static int _steps;
        private static bool _playPressed;
        private static float _unlockAt;
        internal static int UnlocksDismissed;
        private static bool _levelUpAnswered, _gearAnswered, _shopAnswered, _shopP2Answered;
        private static float _levelUpAt;
        private static string _lastSig;

        internal static int LevelUpsAnswered, ShopsClosed;

        internal static void Tick()
        {
            if (!Lc.Auto) return;

            float now = Time.realtimeSinceStartup;
            if (now < _nextStepAt) return;
            _nextStepAt = now + 2f;

            if (Lc.InRunAt >= 0f) { ServeModals(); return; }

            if (_steps++ > 400) { Lc.LineOnce("auto: gave up after 400 steps"); return; }
            Step();
        }

        private static void Step()
        {
            string form = ActiveFormName();
            if (form == null) { Lc.Line("auto: no active form, waiting"); return; }

            if (form == "UISplashForm")
            {
                Lc.LineOnce("auto: on the splash, pressing a virtual gamepad button");
                VirtualPad.PressSouth();
                return;
            }
            if (form == "UILoadingForm") { Lc.Line("auto: loading"); return; }

            if (form == "UIMenuSplashForm")
            {
                var menu = FindFirst<UIMenuSplashForm>();
                if (menu == null || menu.playButton == null || _playPressed || Lc.Str("-lcui=").Length > 0) return;   // the co-op menu tests open the menu themselves
                _playPressed = true;
                Lc.Line("phase: menu");
                Press(menu.playButton, "playButton");
                return;
            }

            if (form == "UIPlaySelectForm") { Wizard(); return; }

            if (form.IndexOf("Popup", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                PressIn(FindFirst<UIForm>(), new[] { "ok", "yes", "continue", "resume", "confirm", "close", "accept" }, "popup " + form);
                return;
            }

            Lc.Line("auto: waiting, active form: " + form);
        }

        private static void Wizard()
        {
            var f = FindFirst<UIPlaySelectForm>();
            if (f == null) return;
            var buttons = ActiveButtons(f);
            if (buttons.Length == 0) { Lc.Line("auto: wizard has no active buttons"); return; }

            string sig = Describe(buttons);
            if (sig != _lastSig) { _lastSig = sig; Lc.Line("auto: wizard page: " + sig); }

            if (_planIndex >= Plan.Length) { Lc.LineOnce("auto: plan complete, waiting for the run to load"); return; }
            if (_planPressedAt > 0f && Time.realtimeSinceStartup - _planPressedAt < 1.5f) return;

            var chosen = Lowest(buttons, Plan[_planIndex]);
            if (chosen == null)
            {
                // The game can skip a page on its own (a save remembers the last node).
                for (int i = _planIndex + 1; i < Plan.Length; i++)
                {
                    if (Lowest(buttons, Plan[i]) == null) continue;
                    Lc.Line("auto: skipped plan[" + _planIndex + "] -> plan[" + i + "]");
                    _planIndex = i;
                    return;
                }
                Lc.LineOnce("auto: waiting on plan[" + _planIndex + "] '" + Plan[_planIndex] + "', page has: " + sig);
                return;
            }

            Press(chosen, "plan[" + _planIndex + "] " + Plan[_planIndex]);
            _planIndex++;
            _planPressedAt = Time.realtimeSinceStartup;
        }

        /// <summary>The modal screens that pause a run. Pressing only what a player must press.</summary>
        internal static bool NoServe;        // a test wants to look at the level-up screen first: do not answer it

        private static void ServeModals()
        {
            var lu = NoServe ? null : FindFirst<UILevelUpForm>();
            if (lu != null)
            {
                if (_levelUpAnswered && Time.realtimeSinceStartup - _levelUpAt > 2.5f) _levelUpAnswered = false; // chained level-up
                if (!_levelUpAnswered)
                {
                    _levelUpAnswered = true;
                    _levelUpAt = Time.realtimeSinceStartup;
                    PickCard(lu);
                }
            }
            else _levelUpAnswered = false;

            if ((ActiveFormName() == "UIUnlockForm" || ActiveFormName() == "UIOverclockForm") && Time.realtimeSinceStartup > _unlockAt)          // "something was unlocked" pauses the run until it is dismissed
            {
                _unlockAt = Time.realtimeSinceStartup + 2f;
                var uf = FindFirst<UIForm>();
                PressIn(uf, new[] { "skillselect", "continue", "ok", "close", "claim", "collect", "next", "confirm", "accept", "button" }, "unlock popup");
                UnlocksDismissed++;
            }

            var gear = FindFirst<UIGearFoundForm>();
            if (gear != null)
            {
                if (!_gearAnswered)
                {
                    _gearAnswered = true;
                    try { gear.OnContinueButton(); Lc.Line("auto: dismissed gear-found popup"); }
                    catch (Exception e) { Lc.Line("auto: gear popup failed: " + e.Message); }
                }
            }
            else _gearAnswered = false;

            var shop = FindFirst<UIShopScreen>();
            if (shop != null && Lc.Str("-lcui=") != "wallet")
            {
                if (!_shopAnswered)
                {
                    _shopAnswered = true;
                    try { shop.OnPlayButton(); ShopsClosed++; Lc.Line("auto: shop continue pressed"); }
                    catch (Exception e) { Lc.Line("auto: shop continue failed: " + e.Message); }
                }
                else if (Wallets.P2ShopActive && !_shopP2Answered)       // the shop runs a second time, for the second dwarf
                {
                    _shopP2Answered = true;
                    try { shop.OnPlayButton(); Lc.Line("auto: P2 shop continue pressed"); }
                    catch (Exception e) { Lc.Line("auto: P2 shop continue failed: " + e.Message); }
                }
            }
            else { _shopAnswered = false; _shopP2Answered = false; }
        }

        private static void PickCard(UILevelUpForm form)
        {
            try
            {
                var cards = form.GetComponentsInChildren<UISkillButton>(true);
                var offered = new List<UISkillButton>();
                for (int i = 0; cards != null && i < cards.Length; i++)
                {
                    var c = cards[i];
                    if (c == null || c.gameObject == null || !c.gameObject.activeInHierarchy) continue;
                    try { if (!c.IsAvailable) continue; } catch { }
                    offered.Add(c);
                }
                if (offered.Count == 0) { Lc.LineOnce("auto: level-up form has no available card"); return; }
                var card = offered[0];
                Press(card, "level-up card");          // the player's click path: UISkillButton applies the skill to its player, then calls the form back
                LevelUpsAnswered++;
                Lc.Line("auto: level-up card picked (offered " + offered.Count + ")");
            }
            catch (Exception e) { Lc.Line("auto: level-up pick failed: " + e.Message); }
        }

        // ---- UI helpers -------------------------------------------------------------------

        internal static void Press(UIButton b, string what)
        {
            try
            {
                bool avail = true;
                try { avail = b.IsAvailable; } catch { }
                if (!avail) { Lc.LineOnce("auto: " + what + " not available"); return; }
                var click = AccessTools.Method(b.GetType(), "OnButtonClick") ?? AccessTools.Method(typeof(UIButton), "OnButtonClick");
                Lc.Line("auto: pressing " + what + " '" + b.gameObject.name + "'");
                if (click != null) click.Invoke(b, null); else b.OnClick.Invoke();
            }
            catch (Exception e) { Lc.Line("auto: press failed on " + what + ": " + e.Message); }
        }

        private static void PressIn(MonoBehaviour root, string[] keywords, string what)
        {
            if (root == null) return;
            var buttons = ActiveButtons(root);
            foreach (var k in keywords)
                foreach (var b in buttons)
                {
                    if (b == null || b.gameObject == null) continue;
                    string n = b.gameObject.name.ToLowerInvariant();
                    if (n.Contains("crumb") || n.Contains("back")) continue;
                    if (n.Contains(k)) { Press(b, what); return; }
                }
            Lc.LineOnce("auto: no button for " + what + " among " + Describe(buttons));
        }

        internal static UIButton[] ActiveButtons(Component root)
        {
            try
            {
                var all = root.GetComponentsInChildren<UIButton>(true);
                var list = new List<UIButton>();
                for (int i = 0; all != null && i < all.Length; i++)
                {
                    var b = all[i];
                    if (b != null && b.gameObject != null && b.gameObject.activeInHierarchy) list.Add(b);
                }
                return list.ToArray();
            }
            catch { return new UIButton[0]; }
        }

        internal static UIButton Lowest(UIButton[] buttons, string prefix)
        {
            UIButton best = null;
            string bestName = null;
            foreach (var b in buttons)
            {
                if (b == null || b.gameObject == null) continue;
                string name = BaseName(b.gameObject.name);
                if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                bool avail = true;
                try { avail = b.IsAvailable; } catch { }
                if (!avail) continue;
                if (best == null || string.CompareOrdinal(name, bestName) < 0) { best = b; bestName = name; }
            }
            return best;
        }

        private static string BaseName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            int i = name.LastIndexOf(" (", StringComparison.Ordinal);
            return (i > 0 && name.EndsWith(")", StringComparison.Ordinal)) ? name.Substring(0, i) : name;
        }

        internal static string Describe(UIButton[] buttons)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < buttons.Length && i < 25; i++)
            {
                var b = buttons[i];
                if (b == null || b.gameObject == null) continue;
                bool avail = true;
                try { avail = b.IsAvailable; } catch { }
                sb.Append(b.gameObject.name).Append(avail ? "" : "[unavailable]").Append("; ");
            }
            return sb.ToString();
        }

        internal static string ActiveFormName()
        {
            try
            {
                var forms = UnityEngine.Object.FindObjectsOfType<UIForm>(true);
                for (int i = 0; forms != null && i < forms.Length; i++)
                {
                    var f = forms[i];
                    if (f != null && f.gameObject != null && f.gameObject.activeInHierarchy) return f.gameObject.name;
                }
            }
            catch { }
            return null;
        }

        private static T FindFirst<T>() where T : MonoBehaviour
        {
            try
            {
                var all = UnityEngine.Object.FindObjectsOfType<T>(true);
                for (int i = 0; all != null && i < all.Length; i++)
                {
                    var c = all[i];
                    if (c != null && c.gameObject != null && c.gameObject.activeInHierarchy) return c;
                }
            }
            catch { }
            return null;
        }
    }
}
