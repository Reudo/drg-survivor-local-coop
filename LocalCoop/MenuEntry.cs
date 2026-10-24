using System;
using DRS.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LocalCoop
{
    /// <summary>Adds a LOCAL CO-OP entry to the main menu (a copy of the play button, below it) and opens the co-op menu from it.</summary>
    internal static class MenuEntry
    {
        private static UIMenuSplashForm _menu;
        private static GameObject _entry;
        internal static UIButton EntryButton;

        internal static void Tick()
        {
            if (Lc.Has("-lcnomenuentry")) return;
            try
            {
                if (_entry != null) { KeepLabel(); HidePlay(); return; }
                var menu = Find.One<UIMenuSplashForm>(2f);
                if (menu == null || !menu.gameObject.activeInHierarchy || menu.playButton == null) return;
                _menu = menu;
                Inject(menu);
            }
            catch (Exception e) { Lc.LineOnce("ui: menu entry failed: " + e.GetType().Name + ": " + e.Message); }
        }

        private static readonly System.Collections.Generic.List<TextMeshProUGUI> _own = new System.Collections.Generic.List<TextMeshProUGUI>();
        private static float _labelAt;
        internal static int LabelFixes;

        /// <summary>The game rewrites the button text when the main menu is shown again (after BACK the entry read "SPIEL"): put it back.
        /// Twin `-lcnolabelkeep`.</summary>
        private static void KeepLabel()
        {
            if (Lc.Has("-lcnolabelkeep") || Time.realtimeSinceStartup < _labelAt) return;
            _labelAt = Time.realtimeSinceStartup + 0.25f;
            foreach (var t in _entry.GetComponentsInChildren<TextMeshProUGUI>(true))
                if (_own.Count > 0 && !_own.Contains(t)) continue; else
                if (t.text != Loc.T("LOCAL CO-OP")) { t.text = Loc.T("LOCAL CO-OP"); LabelFixes++; }
        }

        internal static GameObject EntryObject { get { return _entry; } }

        internal static bool PlayHidden;

        /// <summary>The game's own PLAY button is not needed in co-op: it starts a single-player run. It is hidden once the co-op entry exists; the game
        /// may show it again when the main menu is shown, so this runs every tick. Not during test runs (their drivers use the button), twin `-lckeepplay`.</summary>
        private static void HidePlay()
        {
            if (Lc.Has("-lckeepplay") || ((Lc.Auto || Lc.Test.Length > 0 || Lc.Str("-lcui=").Length > 0) && !Lc.Has("-lchideplay"))) return;
            try
            {
                var p = _menu != null ? _menu.playButton : null;
                if (p == null || !p.gameObject.activeSelf) return;
                p.gameObject.SetActive(false);
                PlayHidden = true;
                var es = UnityEngine.EventSystems.EventSystem.current;
                if (es != null && _entry != null) es.SetSelectedGameObject(_entry);
                Lc.LineOnce("ui: the game's PLAY button is hidden (co-op only)");
            }
            catch (Exception e) { Lc.LineOnce("ui: hiding the play button failed: " + e.GetType().Name + ": " + e.Message); }
        }

        /// <summary>What is on screen on the entry right now: the text of every label that is switched on (test).</summary>
        internal static string VisibleLabel { get { try { var sb = new System.Text.StringBuilder(); foreach (var t in _entry.GetComponentsInChildren<TextMeshProUGUI>(false)) if (t.enabled) sb.Append(t.text); return sb.ToString(); } catch { return "<err>"; } } }

        /// <summary>Test: the game rewrites the text of the entry's own copy of the label, as it does when the main menu is shown again.</summary>
        internal static void SimulateGameRewrite() { foreach (var t in _entry.GetComponentsInChildren<TextMeshProUGUI>(true)) if (!_own.Contains(t)) t.text = "SPIEL"; }

        internal static string Label
        {
            get { try { var t = _own.Count > 0 ? _own[0] : (_entry != null ? _entry.GetComponentInChildren<TextMeshProUGUI>(true) : null); return t != null ? t.text : "<none>"; } catch { return "<err>"; } }
        }

        private static void Inject(UIMenuSplashForm menu)
        {
            var play = menu.playButton;
            var tmp = play.GetComponentInChildren<TextMeshProUGUI>(true);
            if (!CoopMenu.Build(tmp != null ? tmp.font : null)) return;

            var parent = play.transform.parent;
            _entry = UnityEngine.Object.Instantiate(play.gameObject, parent);
            _entry.name = "Button_Cut_LocalCoop";
            // the copy must not keep the game's text binding, or the label is overwritten with "SPIEL"
            foreach (var c in _entry.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                string tn = c.GetType().Name;
                if (tn.Contains("Localize")) UnityEngine.Object.Destroy(c);
            }
            foreach (var t in _entry.GetComponentsInChildren<TextMeshProUGUI>(true)) t.text = Loc.T("LOCAL CO-OP");
            // hand test: the entry flickered (the game's text showed for a moment when the menu came back, until the label fix put ours back). The game's own text is
            // switched off and the entry shows a text of ours that the game does not know about. Twin `-lcentrytextfix`: the old way.
            if (!Lc.Has("-lcentrytextfix"))
                foreach (var t in _entry.GetComponentsInChildren<TextMeshProUGUI>(true))
                {
                    var o = UnityEngine.Object.Instantiate(t.gameObject, t.transform.parent); o.name = "LcLabel";
                    foreach (var c in o.GetComponentsInChildren<Component>(true)) if (c != null && c.GetType().Name.Contains("Localize")) UnityEngine.Object.Destroy(c);
                    var ot = o.GetComponent<TextMeshProUGUI>(); if (ot != null) { ot.text = Loc.T("LOCAL CO-OP"); _own.Add(ot); }
                    t.enabled = false;
                }

            // directly under "play" (the group lays its children out by sibling order)
            _entry.transform.SetSiblingIndex(play.transform.GetSiblingIndex() + 1);
            LayoutRebuilder.ForceRebuildLayoutImmediate(parent.GetComponent<RectTransform>());
            { var sbc = new System.Text.StringBuilder(); foreach (var cc in parent.GetComponents<Component>()) sbc.Append(cc.GetIl2CppType().Name).Append(' '); Lc.Line("ui: menu group components: " + sbc); }
            Lc.Line("ui: menu group layout=" + (parent.GetComponent<UnityEngine.UI.LayoutGroup>() != null ? parent.GetComponent<UnityEngine.UI.LayoutGroup>().GetType().Name : "none"));

            EntryButton = _entry.GetComponent<UIButton>();
            // Drop whatever the copy inherited, then bind the co-op menu.
            try { EntryButton.OnClick.RemoveAllListeners(); } catch { }
            EntryButton.OnClick.AddListener(new Action(() => { if (Flow.OldMenu) CoopMenu.ShowMode("classic"); else Flow.OpenLobby(); }));
            Lc.Line("ui: LOCAL CO-OP entry added to the main menu");
        }
    }
}
