using System;
using DRS.UI;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace LocalCoop
{
    /// <summary>
    /// Currency display with two lines: the game's own counters (gold, nitra) keep showing the host's wallet (line P1); a copy of that row
    /// below shows the second wallet (line P2). Events of the second wallet are kept away from the game's counters (the wallet event is
    /// static and carries no wallet), so P2's spending cannot change the P1 line or pop up as a gain.
    /// </summary>
    internal static class WalletHud
    {
        private static bool _built;
        private static UICoreWallet _cw;
        private static UIAnimatedCounter _g2, _n2;
        private static GameObject _copy;
        private static TextMeshProUGUI _l1, _l2;
        private static UIAnimatedCounter _n1;
        private static RectTransform _origRt, _copyRt;
        private static int _lastLayoutG = int.MinValue, _lastLayoutN = int.MinValue;
        private static int _lastG = int.MinValue, _lastN = int.MinValue;
        private static float _nextAt;
        internal static Wallet Current;          // the wallet whose Add/Spend is running right now

        internal static bool Enabled { get { return !Lc.Has("-lcnowalletui"); } }

        [HarmonyPatch(typeof(UICoreWallet), "OnCurrencyChanged")]
        internal static class EventPatch
        {
            private static bool Prefix()
            {
                if (Lc.Has("-lcnohudfilter")) return true;      // twin: the counters take the second wallet's events
                try { return !(Current != null && Wallets.W2 != null && Current.Pointer == Wallets.W2.Pointer); } catch { return true; }
            }
        }

        internal static string P1GoldText { get { try { return _cw != null ? _cw.goldCounter.GetComponent<TextMeshProUGUI>().text : ""; } catch { return ""; } } }
        internal static string P2GoldText { get { try { return _g2 != null ? _g2.GetComponent<TextMeshProUGUI>().text : ""; } catch { return ""; } } }
        internal static bool Built { get { return _built; } }

        internal static void Reset() { _built = false; _cw = null; _g2 = null; _n2 = null; _copy = null; _l1 = null; _lastG = int.MinValue; _lastN = int.MinValue; }

        internal static void Tick()
        {
            if (!Enabled || !Phase1.Ready || Wallets.W2 == null) return;
            try
            {
                if (!_built) Build();
                if (!_built) return;
                SetShopOnly(Lc.Has("-lcwalletalways") || Find.AnyActive<UIShopScreen>());     // twin: the second line stays visible all the time
                if (!Lc.Has("-lcnowalletlabelfix")) { Follow(_l1, _n1); Follow(_l2, _n2); }          // the counters animate: every frame
                if (Time.realtimeSinceStartup < _nextAt) return;
                _nextAt = Time.realtimeSinceStartup + 0.12f;
                int g = Wallets.W2.Gold, n = Wallets.W2.Nitra;
                if (g != _lastG) { _lastG = g; _g2.Set(g, false); }
                if (n != _lastN) { _lastN = n; _n2.Set(n, false); }
                // hand test: from three digits on the number ran over the label (fixed x). The label follows the end of the nitra number.
                if (!Lc.Has("-lcnowalletlabelfix"))
                {
                    // the copy never gets the game's own layout pass (its events are filtered), so a longer number ran over the gold icon
                    if (_copy != null && (g != _lastLayoutG || n != _lastLayoutN)) { _lastLayoutG = g; _lastLayoutN = n; UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(_copy.GetComponent<RectTransform>()); }
                    Follow(_l1, _n1); Follow(_l2, _n2);
                    // hand test: the pop-up lines of pickups ("+2030 Gold") run over a line below the host's; experiment: -lcwalletside puts player 2's line right of the host's (the line below was right; only the label has to follow the number)
                    if (_copyRt != null && _origRt != null && _l1 != null && Lc.Has("-lcwalletside")) _copyRt.anchoredPosition = _origRt.anchoredPosition + new Vector2(_l1.rectTransform.anchoredPosition.x + 80f, 0f);
                }
            }
            catch (Exception e) { Lc.LineOnce("wallethud: tick threw " + e.GetType().Name + ": " + e.Message); }
        }

        private static void Build()
        {
            _cw = Find.One<UICoreWallet>();
            if (_cw == null) return;
            Transform row = _cw.goldCounter.transform.parent.parent.parent;        // Amount_Gold -> UICollectCounter_GOLD -> Content -> Wallet
            if (row == null || row.parent == null) return;
            var copy = UnityEngine.Object.Instantiate(row.gameObject, row.parent);
            copy.name = "LcWallet2";
            var rt = copy.GetComponent<RectTransform>();
            var orig = row.GetComponent<RectTransform>();
            // directly below the host's line (one row high); the second line only exists while the shop is open (see Tick), so the pop-up
            // lines of pickups no longer have to be dodged
            _copy = copy;
            _origRt = orig; _copyRt = rt;
            rt.anchoredPosition = Lc.Has("-lcwalletside") ? orig.anchoredPosition + new Vector2(260f, 0f) : orig.anchoredPosition + new Vector2(0f, -(Mathf.Max(orig.rect.height, 44f) + 4f));
            _g2 = FindCounter(copy.transform, "Amount_Gold");
            _n2 = FindCounter(copy.transform, "Amount_Nitra");
            if (_g2 == null || _n2 == null) { Lc.Line("wallethud: counters not found in the copy (gold=" + (_g2 != null) + " nitra=" + (_n2 != null) + ")"); return; }
            _l1 = Label(row, "P1", orig.anchoredPosition);
            _l2 = Label(copy.transform, "P2", Vector2.zero);
            _n1 = FindCounter(row, "Amount_Nitra");
            SetShopOnly(false);
            _built = true;
            Lc.Line("wallethud: second currency line built");
        }

        internal static bool P2LineVisible { get { return _copy != null && _copy.activeSelf; } }

        /// <summary>The second wallet line (and the P1 tag next to the host's line) is shown only while the shop is open.</summary>
        private static void SetShopOnly(bool on)
        {
            try
            {
                if (_copy != null && _copy.activeSelf != on) _copy.SetActive(on);
                if (_l1 != null && _l1.gameObject.activeSelf != on) _l1.gameObject.SetActive(on);
            }
            catch { }
        }

        private static void Follow(TextMeshProUGUI label, UIAnimatedCounter nitra)
        {
            try
            {
                if (label == null || nitra == null || !label.gameObject.activeInHierarchy) return;
                var t = nitra.GetComponent<TextMeshProUGUI>(); if (t == null) return;
                t.ForceMeshUpdate();
                var parent = (RectTransform)label.rectTransform.parent;
                float right = parent.InverseTransformPoint(t.transform.TransformPoint(new Vector3(t.textBounds.max.x, 0f, 0f))).x;
                float x = right - parent.rect.xMin + 14f;
                if (x < 170f) x = 170f;
                label.rectTransform.anchoredPosition = new Vector2(x, 0f);
            }
            catch { }
        }

        private static UIAnimatedCounter FindCounter(Transform root, string namePart)
        {
            foreach (var c in root.GetComponentsInChildren<UIAnimatedCounter>(true))
                if (c != null && c.gameObject.name.StartsWith(namePart.Substring(0, 6))) { if (c.gameObject.name == namePart) return c; }
            // the nitra counter's name is not known beforehand: take the second counter of the row
            var all = root.GetComponentsInChildren<UIAnimatedCounter>(true);
            if (namePart == "Amount_Gold") return all.Length > 0 ? all[0] : null;
            return all.Length > 1 ? all[1] : null;
        }

        private static TextMeshProUGUI Label(Transform parent, string text, Vector2 at)
        {
            try
            {
                var src = _cw.goldCounter.GetComponent<TextMeshProUGUI>();
                var go = new GameObject("LcLabel" + text);
                var rt = go.AddComponent<RectTransform>();
                rt.SetParent(parent, false);
                rt.anchorMin = new Vector2(0f, 0.5f); rt.anchorMax = new Vector2(0f, 0.5f); rt.pivot = new Vector2(0f, 0.5f);
                rt.anchoredPosition = new Vector2(170f, 0f);
                rt.sizeDelta = new Vector2(40f, 24f);
                var t = go.AddComponent<TextMeshProUGUI>();
                t.font = src.font; t.fontSize = src.fontSize * 0.8f; t.text = text; t.alignment = TextAlignmentOptions.MidlineLeft;
                t.color = text == "P1" ? CoopMenu.OrangeP1 : CoopMenu.BlueP2; t.raycastTarget = false;
                return t;
            }
            catch (Exception e) { Lc.LineOnce("wallethud: label threw " + e.GetType().Name + ": " + e.Message); return null; }
        }
    }
}
