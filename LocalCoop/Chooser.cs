using System;
using System.Collections.Generic;
using DRS.UI;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace LocalCoop
{
    /// <summary>
    /// Whose turn it is at an upgrade screen. The level-up cards, the weapon upgrade (overclock) screen and the shop are answered by one dwarf
    /// after the other (host first, then player 2). Two things make that unmistakable:
    ///  - a banner at the top and a coloured frame around the screen: "PLAYER 1 ..." in the host's colour, "PLAYER 2 ..." in player 2's;
    ///  - while it is somebody's turn, the other player's gamepad is switched off for the game's input actions (it keeps sending events, the
    ///    plugin still reads it), so a controller cannot pick the other dwarf's upgrade. Keyboard and mouse stay available to the host.
    /// Needs two gamepads for the lock-out (a single pad is both players). Twin `-lcnochooser`: no banner, no lock.
    /// </summary>
    internal static class Chooser
    {
        internal static int Who;                 // 0 = nobody is choosing, 1 = host, 2 = player 2
        internal static string Kind = "";
        internal static int Locked;              // gamepads currently switched off
        private static float _at, _diagAt;

        /// <summary>For the hand test's lc.log: what the game's input thinks while an upgrade screen is open.</summary>
        internal static void Diag()
        {
            try
            {
                var sb = new System.Text.StringBuilder("chooser: diag who=" + Who + " " + Kind + " | ");
                var es = UnityEngine.EventSystems.EventSystem.current;
                sb.Append("module=").Append(es != null && es.currentInputModule != null ? es.currentInputModule.GetType().Name : "<none>").Append(" ");
                sb.Append("selected=").Append(es != null && es.currentSelectedGameObject != null ? es.currentSelectedGameObject.name : "<none>").Append(" navEvents=").Append(es != null && es.sendNavigationEvents);
                var pi = InputHandler.PlayerInput;
                sb.Append(" | scheme=").Append(pi.currentControlScheme).Append(" paired=");
                foreach (var d in pi.devices) sb.Append(d.displayName).Append('#').Append(d.deviceId).Append(' ');
                sb.Append("| inputType=").Append(InputHandler.InputType).Append(" | pads=");
                foreach (var g in Gamepad.all) sb.Append(g.displayName).Append('#').Append(g.deviceId).Append(g.enabled ? "(on) " : "(OFF) ");
                Lc.Line(sb.ToString());
            }
            catch (Exception e) { Lc.LineOnce("chooser: diag threw " + e.GetType().Name + ": " + e.Message); }
        }
        private static readonly List<InputDevice> Off = new List<InputDevice>();
        private static GameObject _root;
        private static TextMeshProUGUI _text;
        private static Image _panel;
        private static readonly Image[] Frame = new Image[4];

        internal static readonly Color Red = new Color(1f, 0.16f, 0.12f, 1f);
        internal static readonly Color Blue = new Color(0.30f, 0.65f, 1f, 1f);

        internal static void Tick()
        {
            if (Phase1.Second == null) { if (Who != 0 || Off.Count > 0 || _saved) Release(); return; }
            float now = Time.realtimeSinceStartup;
            if (now < _at) return;
            _at = now + 0.15f;
            if (Who != 0 && now >= _diagAt) { _diagAt = now + 1.5f; Diag(); }
            try
            {
                string kind; int who = Detect(out kind);
                if (who != Who) { Who = who; Kind = kind; if (who == 0) Unlock(true); else Lock(); Show(); }
                else if (who == 0 && Off.Count == 0) Failsafe();
                else { KeepPaired(); if (Who != 0 && (Kind == "SHOP" || Kind == "WEAPON UPGRADE")) Show(); }
            }
            catch (Exception e) { Lc.LineOnce("chooser: tick threw " + e.GetType().Name + ": " + e.Message); }
        }

        private static int Detect(out string kind)
        {
            kind = "";
            if (Phase1.LoadingActive()) return 0;          // a lock must never reach into a loading screen ("press any key")
            if (PauseView.IsOpen && !Lc.Has("-lcnopauseowner")) { kind = "PAUSE"; return PauseView.Opener; }      // hand test: both players could operate the pause menu
            foreach (var f in Find.Forms<UIOverclockForm>())
                if (f != null && f.gameObject.activeInHierarchy)
                {
                    kind = "WEAPON UPGRADE";
                    try { var w = f.weaponHandler; if (w != null && w.owner != null && Share.IsClone(w.owner)) return 2; } catch { }
                    return LevelRounds.P2Round ? 2 : 1;
                }
            foreach (var f in Find.Forms<UILevelUpForm>())
                if (f != null && f.gameObject.activeInHierarchy)
                {
                    kind = "LEVEL UP";
                    try { string t = f.titleText != null ? f.titleText.text : ""; t = System.Text.RegularExpressions.Regex.Replace(t ?? "", "<[^>]+>", "").Trim(); if (LevelBanner.OrigTitle.Length > 1 && !Lc.Has("-lcnolevelbanner")) t = System.Text.RegularExpressions.Regex.Replace(LevelBanner.OrigTitle, "<[^>]+>", "").Trim(); if (t.Length > 1) kind = t.ToUpperInvariant(); } catch { }
                    return LevelRounds.P2Round ? 2 : 1;
                }
            foreach (var f in Find.Forms<UIShopScreen>())
                if (f != null && f.gameObject.activeInHierarchy) { kind = "SHOP"; return Wallets.P2ShopActive ? 2 : 1; }
            return 0;
        }

        // ---- the lock ----------------------------------------------------------------------------------------------
        // The game's UI actions listen to the devices paired with its PlayerInput. A turn pairs the chooser's own device with it and stops the
        // game from switching to another controller on its own (neverAutoSwitchControlSchemes), so the other player's pad cannot answer the
        // screen. No device is switched off (hand test: with real pads the game's menus stayed dead). Everything the game had is given back
        // when the turns are over. Twin `-lcdisablelock`: the old way, the other pad is switched off as well.
        private static bool _saved, _savedNever;
        private static string _savedScheme = "";
        private static readonly List<InputDevice> SavedDevices = new List<InputDevice>();
        private static InputDevice _chooserDev;
        private static float _repairLogAt;
        internal static int Repairs;

        private static InputDevice ChooserDevice(InputDevice p2)
        {
            if (Who == 2) return p2;
            foreach (var g in Gamepad.all) if (g != null && g.deviceId != p2.deviceId) return g;
            return null;
        }

        internal static int CrossBlocked;
        internal static int BackBlocked, StartBlocked;
        internal static int BackSeen;
        internal static bool InBack;         // while the game's back action runs (B / Esc): what it opens can be told apart from what Start opens

        /// <summary>May this device answer the screen that is up (the chooser's own pad; the keyboard and mouse belong to the host)? Everything is allowed when nobody is choosing.</summary>
        internal static bool DeviceAllowed(InputDevice d)
        {
            try
            {
                if (Who == 0 || d == null || Lc.Has("-lcnocrossblock") || Lc.Has("-lcnochooser")) return true;
                var p2 = VirtualPad.P2Device(); if (p2 == null) return true;
                if (!(d is Gamepad)) return Who == 1;
                var cd = ChooserDevice(p2);
                return cd != null && d.deviceId == cd.deviceId;
            }
            catch { return true; }
        }

        /// <summary>
        /// Hand test: during one player's level-up (or shop) the other player's A still confirmed the cards. Some of the game's own polling (UIButton while it is
        /// selected, the UI module's submit) does not follow the pairing above. While a turn is on and a controller other than the chooser's holds its A button
        /// (and the chooser's own pad does not), the game's confirm is held back at the buttons themselves. Twin `-lcnocrossblock`.
        /// </summary>
        internal static bool OtherPadOnly()
        {
            try
            {
                if (Who == 0 || Lc.Has("-lcnocrossblock") || Lc.Has("-lcnochooser")) return false;
                var p2 = VirtualPad.P2Device(); if (p2 == null) return false;
                var cd = ChooserDevice(p2);
                bool mine = false, other = false;
                foreach (var g in Gamepad.all)
                {
                    if (g == null) continue;
                    bool pressed = g.buttonSouth.isPressed || g.buttonSouth.wasPressedThisFrame || g.buttonSouth.wasReleasedThisFrame;
                    if (!pressed) continue;
                    if (cd != null && g.deviceId == cd.deviceId) mine = true; else other = true;
                }
                return other && !mine;
            }
            catch { return false; }
        }

        private static bool Paired(PlayerInput pi, InputDevice d)
        {
            foreach (var x in pi.devices) if (x != null && d != null && x.deviceId == d.deviceId) return true;
            return false;
        }

        private static void Pair(PlayerInput pi, InputDevice dev)
        {
            if (dev != null && dev is Gamepad)
                pi.SwitchCurrentControlScheme("Gamepad", new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<InputDevice>(new InputDevice[] { dev }));
            else
            {
                var l = new List<InputDevice>();
                if (Keyboard.current != null) l.Add(Keyboard.current);
                if (Mouse.current != null) l.Add(Mouse.current);
                pi.SwitchCurrentControlScheme("Keyboard&Mouse", new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<InputDevice>(l.ToArray()));
            }
        }

        private static void Lock()
        {
            Unlock(false);
            if (Who == 0 || Lc.Has("-lcnochooser")) return;
            var p2 = VirtualPad.P2Device();
            if (p2 == null) return;                       // one pad is both players
            try
            {
                var pi = InputHandler.PlayerInput;
                if (pi == null) return;
                if (!_saved)
                {
                    _saved = true; _savedNever = pi.neverAutoSwitchControlSchemes; _savedScheme = pi.currentControlScheme ?? "";
                    SavedDevices.Clear(); foreach (var d in pi.devices) if (d != null) SavedDevices.Add(d);
                }
                pi.neverAutoSwitchControlSchemes = true;
                _chooserDev = ChooserDevice(p2);
                Pair(pi, _chooserDev);
                Lc.Line("chooser: UI actions paired with " + (_chooserDev != null ? _chooserDev.displayName + "#" + _chooserDev.deviceId : "keyboard and mouse") + ", auto-switch off");
            }
            catch (Exception e) { Lc.LineOnce("chooser: pairing failed " + e.GetType().Name + ": " + e.Message); }
            if (Lc.Has("-lcdisablelock"))
                foreach (var g in Gamepad.all)
                {
                    if (g == null || _chooserDev == null || g.deviceId == _chooserDev.deviceId) continue;
                    try { InputSystem.DisableDevice(g); Off.Add(g); } catch (Exception e) { Lc.LineOnce("chooser: disable failed " + e.Message); }
                }
            int other = 0; foreach (var g in Gamepad.all) if (g != null && (_chooserDev == null || g.deviceId != _chooserDev.deviceId)) other++;
            Locked = other;
            Lc.Line("chooser: " + Kind + " for player " + Who + ", " + Locked + " gamepad(s) kept away from the game's actions");
        }

        /// <summary>The game can still re-pair on its own (a device change); the chooser's device is paired again, quietly.</summary>
        private static void KeepPaired()
        {
            if (Who == 0 || Lc.Has("-lcnochooser") || !_saved) return;
            try
            {
                var pi = InputHandler.PlayerInput;
                if (pi == null || _chooserDev == null) return;
                if (!Paired(pi, _chooserDev) || pi.devices.Count != 1)
                {
                    pi.neverAutoSwitchControlSchemes = true; Pair(pi, _chooserDev); Repairs++;
                    if (Time.realtimeSinceStartup > _repairLogAt) { _repairLogAt = Time.realtimeSinceStartup + 3f; Lc.Line("chooser: the game's pairing drifted, paired " + _chooserDev.displayName + "#" + _chooserDev.deviceId + " again (" + Repairs + ")"); }
                }
            }
            catch { }
        }

        /// <summary>No turn is on, so no gamepad may be off: whatever the plugin switched off and did not get back is switched on here.</summary>
        private static void Failsafe()
        {
            try
            {
                foreach (var g in Gamepad.all)
                    if (g != null && !g.enabled) { InputSystem.EnableDevice(g); Lc.LineOnce("chooser: failsafe switched " + g.displayName + "#" + g.deviceId + " back on"); }
            }
            catch { }
        }

        /// <summary>Gives back what the lock took: devices switched on, and (only when the turns are over) the game's own pairing and auto-switch.</summary>
        private static void Unlock(bool restore)
        {
            foreach (var d in Off) { try { if (d != null) InputSystem.EnableDevice(d); } catch { } }
            Off.Clear(); Locked = 0;
            if (!restore || !_saved) return;
            _saved = false;
            try
            {
                var pi = InputHandler.PlayerInput;
                if (pi == null) return;
                pi.neverAutoSwitchControlSchemes = _savedNever;
                var l = new List<InputDevice>(); foreach (var d in SavedDevices) if (d != null) l.Add(d);
                if (l.Count > 0 && _savedScheme.Length > 0) pi.SwitchCurrentControlScheme(_savedScheme, new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<InputDevice>(l.ToArray()));
                Lc.Line("chooser: pairing given back (" + _savedScheme + ", " + l.Count + " device(s)), auto-switch " + (_savedNever ? "off" : "on"));
            }
            catch (Exception e) { Lc.LineOnce("chooser: restore failed " + e.GetType().Name + ": " + e.Message); }
            SavedDevices.Clear();
        }

        /// <summary>Everything back (the run ended, the clone is gone).</summary>
        internal static void Release()
        {
            Unlock(true);
            if (Who != 0) Lc.Line("chooser: released");
            Who = 0; Kind = "";
            Show();
        }

        // ---- the banner --------------------------------------------------------------------------------------------
        private static Image Bar(Transform parent, string name, Vector2 amin, Vector2 amax, Vector2 pivot, Vector2 size, Color c)
        {
            var go = new GameObject(name);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = amin; rt.anchorMax = amax; rt.pivot = pivot; rt.sizeDelta = size; rt.anchoredPosition = Vector2.zero;
            var im = go.AddComponent<Image>(); im.color = c; im.raycastTarget = false;
            return im;
        }

        private static void Build()
        {
            _root = new GameObject("LcChooser");
            UnityEngine.Object.DontDestroyOnLoad(_root);
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 5000;
            var rt = _root.GetComponent<RectTransform>();
            const float t = 10f;
            Frame[0] = Bar(rt, "Top", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, t), Red);
            Frame[1] = Bar(rt, "Bottom", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, t), Red);
            Frame[2] = Bar(rt, "Left", new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(t, 0), Red);
            Frame[3] = Bar(rt, "Right", new Vector2(1, 0), new Vector2(1, 1), new Vector2(1, 0.5f), new Vector2(t, 0), Red);
            _panel = Bar(rt, "Banner", new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(600, 76), new Color(0.04f, 0.05f, 0.08f, 0.96f));
            _panel.rectTransform.anchoredPosition = new Vector2(-26, -26);      // top right: the top left holds the wallet lines, the middle the screen title
            var go = new GameObject("Text");
            var trt = go.AddComponent<RectTransform>();
            trt.SetParent(_panel.rectTransform, false);
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
            _text = go.AddComponent<TextMeshProUGUI>();
            var font = CoopMenu.FontAsset != null ? CoopMenu.FontAsset : TMP_Settings.defaultFontAsset;
            if (font != null) _text.font = font;
            _text.fontSize = 40; _text.fontStyle = FontStyles.Bold; _text.alignment = TextAlignmentOptions.Midline; _text.raycastTarget = false;
        }

        // ---- the shop's title: "PLAYER n" above it, the title itself in the player's colour (instead of the corner banner) ----------------
        private static TextMeshProUGUI _shopTitle, _shopTag;
        private static Color _shopTitleOrig;
        internal static string ShopInfo = "";

        private static TextMeshProUGUI FindShopTitle(Component f)
        {
            if (_shopTitle != null && _shopTitle.gameObject.activeInHierarchy && _shopTitle.transform.IsChildOf(f.transform)) return _shopTitle;
            TextMeshProUGUI best = null;
            foreach (var t in f.GetComponentsInChildren<TextMeshProUGUI>(false))
            {
                if (t == null || t == _shopTag) continue;
                string s = (t.text ?? "").Trim(); if (s.Length < 2 || s.Length > 20) continue;
                if (t.GetComponentInParent<Button>() != null || t.GetComponentInParent<UIButton>() != null) continue;
                if (best == null || t.fontSize > best.fontSize) best = t;
            }
            return best;
        }

        private static void ShopTitleTag(bool on)
        {
            try
            {
                if (!on || Lc.Has("-lcnoshoptitle"))
                {
                    if (_shopTag != null) _shopTag.gameObject.SetActive(false);
                    if (_shopTitle != null) { _shopTitle.color = _shopTitleOrig; _shopTitle = null; }
                    return;
                }
                Component f = null;
                if (Kind == "SHOP") { foreach (var x in Find.Forms<UIShopScreen>()) if (x != null && x.gameObject.activeInHierarchy) { f = x; break; } }
                else { foreach (var x in Find.Forms<UIOverclockForm>()) if (x != null && x.gameObject.activeInHierarchy) { f = x; break; } }
                if (f == null) return;
                var title = FindShopTitle(f); if (title == null) { ShopInfo = "no title found"; return; }
                if (title != _shopTitle) { if (_shopTitle != null) _shopTitle.color = _shopTitleOrig; _shopTitle = title; _shopTitleOrig = title.color; }
                var col = Who == 1 ? Red : Blue;
                title.color = col;
                if (_shopTag == null)
                {
                    var go = new GameObject("LcShopTag"); go.AddComponent<RectTransform>();
                    _shopTag = go.AddComponent<TextMeshProUGUI>();
                    var font = CoopMenu.FontAsset != null ? CoopMenu.FontAsset : TMP_Settings.defaultFontAsset; if (font != null) _shopTag.font = font;
                    _shopTag.alignment = TextAlignmentOptions.Midline; _shopTag.raycastTarget = false; _shopTag.fontSize = 34f; _shopTag.enableWordWrapping = false;
                    _shopTag.outlineWidth = 0.1f; _shopTag.outlineColor = new Color32(0, 0, 0, 255);
                }
                var trt = title.rectTransform; var brt = _shopTag.rectTransform;
                brt.SetParent(trt.parent, false);
                brt.anchorMin = trt.anchorMin; brt.anchorMax = trt.anchorMax; brt.pivot = new Vector2(0.5f, 0f);
                brt.sizeDelta = new Vector2(Mathf.Max(600f, trt.rect.width), 54f);
                brt.anchoredPosition = trt.anchoredPosition + new Vector2(0f, trt.rect.height * (1f - trt.pivot.y) + 2f);
                _shopTag.color = col; _shopTag.text = Loc.T("PLAYER ") + Who; _shopTag.gameObject.SetActive(true);
                ShopInfo = "title '" + title.text + "' size " + title.fontSize;
                Lc.LineOnce("chooser: shop " + ShopInfo + " tag for player " + Who);
            }
            catch (Exception e) { Lc.LineOnce("chooser: shop title threw " + e.GetType().Name + ": " + e.Message); }
        }

        internal static bool ShopTitleOk(int who)
        {
            try
            {
                var c = who == 1 ? Red : Blue;
                return _shopTitle != null && _shopTag != null && _shopTag.gameObject.activeSelf && _shopTag.text == Loc.T("PLAYER ") + who
                    && Mathf.Abs(_shopTitle.color.r - c.r) < 0.02f && Mathf.Abs(_shopTitle.color.b - c.b) < 0.02f;
            }
            catch { return false; }
        }

        internal static bool PanelShown { get { try { return _root != null && _root.activeSelf && _panel != null && _panel.gameObject.activeSelf; } catch { return false; } } }

        private static void Show()
        {
            try
            {
                if (Who == 0 || Lc.Has("-lcnochooser")) { if (_root != null) _root.SetActive(false); ShopTitleTag(false); return; }
                if (_root == null) Build();
                if (_root == null) return;
                _root.SetActive(true);
                // hand test: the pause menu and the shop show the player's name above their own title, the corner banner would say it twice (twins -lcpausepanel, -lcnoshoptitle)
                bool tagged = Kind == "SHOP" || Kind == "WEAPON UPGRADE";
                ShopTitleTag(tagged);
                bool tagOk = tagged && _shopTag != null && _shopTag.gameObject.activeSelf;
                // the corner banner stays only where nothing else says whose turn it is: with the twins, or for a screen whose title was not found
                bool corner = Lc.Has("-lccornerall") || (Kind == "PAUSE" && Lc.Has("-lcpausepanel")) || (tagged && (Lc.Has("-lcnoshoptitle") || !tagOk)) || (!tagged && Kind != "PAUSE" && Lc.Has("-lcpanellevel"));
                _panel.gameObject.SetActive(corner);
                var c = Who == 1 ? Red : Blue;
                foreach (var f in Frame) f.color = c;
                _text.color = c;
                _text.text = Loc.T("PLAYER ") + Who + "  -  " + Loc.T(Kind);
            }
            catch (Exception e) { Lc.LineOnce("chooser: banner threw " + e.GetType().Name + ": " + e.Message); }
        }

        [HarmonyLib.HarmonyPatch(typeof(UIButton), "OnUpdateSelected")]
        internal static class CrossButtonPatch
        {
            private static bool Prefix() { if (!OtherPadOnly()) return true; CrossBlocked++; return false; }
        }

        [HarmonyLib.HarmonyPatch(typeof(Button), "OnSubmit")]
        internal static class CrossSubmitPatch
        {
            private static bool Prefix() { if (!OtherPadOnly()) return true; CrossBlocked++; return false; }
        }

        // hand test: both controllers could close the pause menu and press B (Details) in the level-up screen. The game's back and pause actions are
        // answered by UIManager whatever pad pressed; while somebody's turn is on, only that pad's presses count.
        [HarmonyLib.HarmonyPatch(typeof(UIManager), "OnBackAction")]
        internal static class BackPatch
        {
            private static bool Prefix(InputAction.CallbackContext obj)
            {
                try { if (Lc.Has("-lcnobackblock")) return true; var d = obj.control != null ? obj.control.device : null; if (!DeviceAllowed(d)) { BackBlocked++; return false; } 
                    // hand test: after the B fix player 1's Start no longer opened the pause menu in the run - the game feeds Start into the back action too. Only the pad's
                    // other buttons (B) count as "back"; Start (and Esc on the keyboard) must still open the menu.
                    var gp = d as UnityEngine.InputSystem.Gamepad; var cn = obj.control != null ? obj.control.name : "";
                    InBack = gp != null && !Lc.Has("-lcstartisback") ? (cn != "start" && cn != "select") : gp != null;
                    if (BackSeen < 6) { BackSeen++; Lc.LineOnce("back action from " + (d != null ? d.name : "?") + "/" + cn + " InBack=" + InBack); }
                    return true; } catch { return true; }
            }

            private static void Finalizer() { InBack = false; }
        }

        [HarmonyLib.HarmonyPatch(typeof(UIManager), "OnPauseMenu")]
        internal static class PausePatch
        {
            private static bool Prefix(InputAction.CallbackContext context)
            {
                try { var d = context.control != null ? context.control.device : null; if (DeviceAllowed(d)) return true; StartBlocked++; return false; } catch { return true; }
            }
        }
    }
}
