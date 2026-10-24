using System;
using TMPro;
using UnityEngine;

namespace LocalCoop
{
    /// <summary>
    /// "P1" (red) and "P2" (blue) above the dwarves' heads. With the zoomed-out camera the two dwarves look alike; the tags follow each dwarf on
    /// screen and are hidden whenever the game is not showing a run (end screen, menus, level-up and shop screens, pause).
    /// Twin `-lcnotags`.
    /// </summary>
    internal static class Tags
    {
        private static GameObject _root;
        private static TextMeshProUGUI _t1, _t2;
        internal static bool Visible;
        internal static float ErrSum, ErrMax; internal static int ErrN;      // test: pixels between the tag and where its dwarf's head projects now

        private static TextMeshProUGUI Make(string text, Color c)
        {
            var go = new GameObject("Tag" + text);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(_root.transform, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.zero; rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(120f, 60f);
            var t = go.AddComponent<TextMeshProUGUI>();
            var font = CoopMenu.FontAsset != null ? CoopMenu.FontAsset : TMP_Settings.defaultFontAsset;
            if (font != null) t.font = font;
            t.text = text; t.color = c; t.fontStyle = FontStyles.Bold; t.alignment = TextAlignmentOptions.Midline; t.raycastTarget = false;
            t.enableWordWrapping = false;
            try { t.outlineWidth = 0.28f; t.outlineColor = new Color32(0, 0, 0, 255); } catch { }
            return t;
        }

        private static void Build()
        {
            _root = new GameObject("LcTags");
            UnityEngine.Object.DontDestroyOnLoad(_root);
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 800;
            _t1 = Make("P1", Chooser.Red);
            _t2 = Make("P2", Chooser.Blue);
        }

        private static bool Place(TextMeshProUGUI t, Player p, Camera cam)
        {
            if (t == null) return false;
            if (p == null || !p.isAlive) { t.gameObject.SetActive(false); return false; }
            var sp = cam.WorldToScreenPoint(p.transform.position + new Vector3(0f, 2.3f, 0f));
            if (sp.z <= 0f) { t.gameObject.SetActive(false); return false; }
            t.gameObject.SetActive(true);
            t.fontSize = 58f * Screen.height / 1080f;
            t.rectTransform.anchoredPosition = new Vector2(sp.x, sp.y);
            return true;
        }

        private static bool _hooked, _shown;

        /// <summary>Positions are set right before the canvases are drawn, after the camera has moved this frame; set earlier they trail the dwarf by a frame and float.</summary>
        private static void OnRender()
        {
            try
            {
                if (!_shown || _root == null) return;
                var cam = Cam2.Cam;
                if (cam == null) return;
                Place(_t1, Phase1.First(), cam);
                Place(_t2, Phase1.Second, cam);
            }
            catch { }
        }

        private static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            try { Canvas.willRenderCanvases += Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Canvas.WillRenderCanvases>(new Action(OnRender)); }
            catch (Exception e) { Lc.Line("tags: render hook failed " + e.GetType().Name + ": " + e.Message); }
        }

        // hand test: the tags stayed on while the screen already faded to black for the stage change - the pod leaving is the signal
        private static bool _sawExit;
        private static bool PodLeaving()
        {
            if (Lc.Has("-lcnotagfade")) return false;
            try
            {
                var pod = Pod.Find1(); if (pod == null) return false;
                var st = pod.state;
                // hand test: at the mission start the pod also flies away (ANIMATING_OUT) after the dwarves walked out (it passed WAITING_FOR_PLAYER_EXIT), and the tags vanished for three
                // seconds. Only the pod that takes the dwarves out of the stage counts. Twin `-lctagsatstart`.
                if (st == DropPod.EState.WAITING_FOR_PLAYER_EXIT) _sawExit = true;
                else if (st == DropPod.EState.ANIMATING_IN || st == DropPod.EState.WAITING_FOR_PLAYER) _sawExit = false;
                if (st != DropPod.EState.ANIMATING_OUT) return false;
                return Lc.Has("-lctagsatstart") || !_sawExit;
            }
            catch { return false; }
        }

        internal static void Tick()
        {
            try
            {
                var second = Phase1.Second;
                bool show = second != null && Phase1.Ready && !Lc.Has("-lcnotags") && Chooser.Who == 0 && Time.timeScale > 0.01f && Hud2.RunUiVisible && !PodLeaving();
                if (!show)
                {
                    if (_root != null && _root.activeSelf) _root.SetActive(false);
                    Visible = false; _shown = false;
                    return;
                }
                var cam = Cam2.Cam;
                if (cam == null) return;
                if (_root == null) Build();
                Hook(); _shown = true;
                if (!_root.activeSelf) _root.SetActive(true);
                if (_t2 != null && _t2.gameObject.activeSelf && second.isAlive)
                {
                    var want = cam.WorldToScreenPoint(second.transform.position + new Vector3(0f, 2.3f, 0f));
                    var have = _t2.rectTransform.anchoredPosition;
                    float err = new Vector2(want.x - have.x, want.y - have.y).magnitude;
                    ErrSum += err; ErrN++; if (err > ErrMax) ErrMax = err;
                }
                bool a = Place(_t1, Phase1.First(), cam);
                bool b = Place(_t2, second, cam);
                Visible = a || b;
            }
            catch (Exception e) { Lc.LineOnce("tags: tick threw " + e.GetType().Name + ": " + e.Message); }
        }
    }
}
