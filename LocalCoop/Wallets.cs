using System;
using DRS.UI;
using HarmonyLib;
using UnityEngine;

namespace LocalCoop
{
    /// <summary>
    /// Two wallets. The host keeps the game's own Wallet; the second dwarf gets a second one for the run currencies (gold, morkite, nitra).
    /// Every income of the host's wallet is credited to both in full (so before anything is bought they are equal: "shared"), every
    /// spending goes to the wallet of whoever buys: the shop runs twice, first for the host, then for the second dwarf, each with its own
    /// player and wallet, and the level-up reroll of a P2 round is paid from P2's wallet.
    /// Profile currencies (credits, bismor, ...) are saved and stay with the host's wallet alone.
    /// </summary>
    internal static class Wallets
    {
        internal static Wallet W2;
        private static IntPtr _w2For = IntPtr.Zero;
        internal static bool P2ShopActive, InShop;
        internal static int Mirrored, ShopRoundsStarted, ShopRoundsFinished;
        private static int _depth;
        private static bool _installed;

        internal static void Install(Harmony h)
        {
            if (_installed) return;
            _installed = true;
            foreach (var t in new[] { typeof(AddEcPatch), typeof(AddCvPatch), typeof(SpendEcPatch), typeof(SpendCvPatch), typeof(ShopShowPatch), typeof(ShopPlayPatch), typeof(MidRunPatch), typeof(WalletHud.EventPatch) })
                h.CreateClassProcessor(t).Patch();
        }

        internal static Wallet Host() { return CoopStart.Resolve<Wallet>(); }

        /// <summary>A run is over: the second wallet and the shop state belong to it.</summary>
        internal static void ResetRun() { _pinsHost = null; _pinsP2 = null; W2 = null; _w2For = IntPtr.Zero; P2ShopActive = false; InShop = false; _depth = 0; }

        /// <summary>The second wallet of the current clone (created on first use, starting with what the host has).</summary>
        internal static Wallet Ensure()
        {
            var clone = Phase1.Second;
            if (clone == null || Lc.Has("-lcnowallet2")) return null;
            if (W2 != null && _w2For == clone.Pointer) return W2;
            var host = Host();
            if (host == null) return null;
            var w = new Wallet();
            Phase1.SetRef(w, typeof(Wallet), "gold", new CurrencyValue(ECurrency.GOLD, host.Gold).Pointer);
            Phase1.SetRef(w, typeof(Wallet), "morkite", new CurrencyValue(ECurrency.MORKITE, host.Morkite).Pointer);
            Phase1.SetRef(w, typeof(Wallet), "nitra", new CurrencyValue(ECurrency.NITRA, host.Nitra).Pointer);
            W2 = w; _w2For = clone.Pointer;
            Lc.Line("wallets: second wallet created: gold=" + w.Gold + " morkite=" + w.Morkite + " nitra=" + w.Nitra);
            return W2;
        }

        private static bool RunCurrency(ECurrency c) { return c == ECurrency.GOLD || c == ECurrency.MORKITE || c == ECurrency.NITRA; }

        private static void Mirror(Wallet instance, ECurrency c, int amount)
        {
            if (amount == 0 || !RunCurrency(c) || InShop || Phase1.Second == null) return;
            var host = Host();
            if (host == null || instance.Pointer != host.Pointer) return;
            var w = Ensure();
            if (w == null) return;
            try { w.Add(c, amount, false); Mirrored++; } catch (Exception e) { Lc.LineOnce("wallets: mirror threw " + e.GetType().Name + ": " + e.Message); }
        }

        [HarmonyPatch(typeof(Wallet), "Add", new[] { typeof(ECurrency), typeof(int), typeof(bool) })]
        private static class AddEcPatch
        {
            private static void Prefix(Wallet __instance) { _depth++; WalletHud.Current = __instance; }
            private static void Postfix(Wallet __instance, ECurrency __0, int __1) { _depth--; WalletHud.Current = null; if (_depth == 0) Mirror(__instance, __0, __1); }
        }

        [HarmonyPatch(typeof(Wallet), "Add", new[] { typeof(CurrencyValue) })]
        private static class AddCvPatch
        {
            private static void Prefix(Wallet __instance) { _depth++; WalletHud.Current = __instance; }
            private static void Postfix(Wallet __instance, CurrencyValue __0) { _depth--; WalletHud.Current = null; if (_depth == 0 && __0 != null) Mirror(__instance, __0.Type, __0.Value); }
        }

        // spending only needs to be known to the display filter
        [HarmonyPatch(typeof(Wallet), "Spend", new[] { typeof(ECurrency), typeof(int) })]
        private static class SpendEcPatch
        {
            private static void Prefix(Wallet __instance) { WalletHud.Current = __instance; }
            private static void Postfix() { WalletHud.Current = null; }
        }

        [HarmonyPatch(typeof(Wallet), "Spend", new[] { typeof(CurrencyValue) })]
        private static class SpendCvPatch
        {
            private static void Prefix(Wallet __instance) { WalletHud.Current = __instance; }
            private static void Postfix() { WalletHud.Current = null; }
        }

        // hand test: after quitting a co-op run the game asked "continue the run?" and loaded it without the second dwarf. The game saves the run in the shop between
        // two stages (UIShopScreen.TriggerMidRunSave); in a co-op run it does not, so there is nothing to continue. Saving and loading the second dwarf is a possible later
        // feature (17b). Twin `-lckeepmidrun`.
        internal static int MidRunSkips;

        [HarmonyPatch(typeof(UIShopScreen), "TriggerMidRunSave")]
        private static class MidRunPatch
        {
            private static bool Prefix()
            {
                if (Lc.Has("-lckeepmidrun") || Phase1.Second == null) return true;
                MidRunSkips++; Lc.LineOnce("wallets: the shop does not save the run (co-op run)");
                return false;
            }
        }

        // ---- the shop, twice ----------------------------------------------------------------------------------------------
        [HarmonyPatch(typeof(UIShopScreen), "Show")]
        private static class ShopShowPatch
        {
            private static void Postfix() { if (!P2ShopActive) InShop = true; }
        }

        [HarmonyPatch(typeof(UIShopScreen), "OnPlayButton")]
        private static class ShopPlayPatch
        {
            private static bool Prefix(UIShopScreen __instance)
            {
                try
                {
                    if (P2ShopActive) { EndP2Shop(__instance); return true; }
                    var clone = Phase1.Second;
                    if (clone != null && clone.isAlive && Ensure() != null && !Lc.Has("-lcnoshop2")) { StartP2Shop(__instance, clone); return false; }
                }
                catch (Exception e) { Lc.Line("wallets: shop switch threw " + e.GetType().Name + ": " + e.Message); P2ShopActive = false; }
                InShop = false;
                return true;
            }
        }

        private static void SetShopContext(UIShopScreen screen, Player p, Wallet w)
        {
            Phase1.SetRef(screen, typeof(UIShopScreen), "player", p.Pointer);
            Phase1.SetRef(screen, typeof(UIShopScreen), "wallet", w.Pointer);
            try { screen.skillCollection.SetPlayer(p); } catch (Exception e) { Lc.LineOnce("wallets: SetPlayer threw " + e.Message); }
            foreach (var b in screen.GetComponentsInChildren<UIShopButton>(true))
            {
                Phase1.SetRef(b, typeof(UIShopButton), "player", p.Pointer);
                if (!Lc.Has("-lcshopkeepwallet")) Phase1.SetRef(b, typeof(UIShopButton), "wallet", w.Pointer);   // twin: buttons keep paying from the host's wallet
            }
            foreach (var b in screen.GetComponentsInChildren<UIButtonPrice>(true)) Phase1.SetRef(b, typeof(UIButtonPrice), "wallet", w.Pointer);
            LevelRounds.SwapCardsTo(p);
        }

        // ---- pinned cards: each dwarf keeps its own ---------------------------------------------------------------------------
        // hand test: P2 pinned a card and it showed up in P1's next shop. Both shops share one screen and its pin toggles
        // (UIShopButton.pinnedToggle, kept over SetupSkillButtons), so the pins are swapped with the dwarf: saved when a shop
        // ends, put back (card and toggle) before the next one of that dwarf shows.
        private struct Pin { public bool On; public SkillRaritySet Set; }
        private static Pin[] _pinsHost, _pinsP2;
        internal static int PinsRestored;

        private static Pin[] TakePins(UIShopScreen screen)
        {
            var bs = screen.shopButtons;
            var r = new Pin[bs.Length];
            for (int i = 0; i < bs.Length; i++)
            {
                var b = bs[i];
                if (b == null || b.pinnedToggle == null) continue;
                r[i].On = b.pinnedToggle.IsToggled;
                r[i].Set = b.skillRaritySet;
            }
            return r;
        }

        private static void PutPins(UIShopScreen screen, Pin[] pins)
        {
            var bs = screen.shopButtons;
            for (int i = 0; i < bs.Length; i++)
            {
                var b = bs[i];
                if (b == null || b.pinnedToggle == null) continue;
                bool on = pins != null && i < pins.Length && pins[i].On && pins[i].Set != null;
                try
                {
                    if (on) { b.Setup(pins[i].Set, b.onPurchaseCallback, b.onSelectCallback, b.price); PinsRestored++; }
                    b.pinnedToggle.ForceValue(on);
                }
                catch (Exception e) { Lc.LineOnce("wallets: pin swap threw " + e.GetType().Name + ": " + e.Message); }
            }
        }

        /// <summary>Test hook: is card i of the open shop pinned.</summary>
        internal static bool CardPinned(UIShopScreen screen, int i) { var b = screen.shopButtons[i]; return b != null && b.pinnedToggle != null && b.pinnedToggle.IsToggled; }

        private static void StartP2Shop(UIShopScreen screen, Player clone)
        {
            var w = Ensure();
            P2ShopActive = true;
            ShopRoundsStarted++;
            if (!Lc.Has("-lcnopinsplit")) { _pinsHost = TakePins(screen); PutPins(screen, _pinsP2); }   // twin: both dwarves share the pins
            SetShopContext(screen, clone, w);
            Bonus.ClearArrows();
            screen.Show(screen.skillCollection, screen.skillTypes, false);
            Lc.Line("wallets: shop for P2 opened (wallet gold=" + w.Gold + ")");
        }

        private static void EndP2Shop(UIShopScreen screen)
        {
            var host = Phase1.First();
            if (!Lc.Has("-lcnopinsplit")) { _pinsP2 = TakePins(screen); PutPins(screen, _pinsHost); }
            SetShopContext(screen, host, Host());
            Bonus.ClearArrows();
            P2ShopActive = false;
            InShop = false;
            ShopRoundsFinished++;
            Lc.Line("wallets: P2 shop left, context back to the host (host gold=" + Host().Gold + " P2 gold=" + (W2 != null ? W2.Gold : -1) + ")");
        }

        /// <summary>The level-up form's reroll is paid from the wallet of whoever is choosing.</summary>
        internal static void SetFormWallet(Wallet w)
        {
            if (w == null) return;
            foreach (var f in UnityEngine.Object.FindObjectsOfType<UILevelUpForm>(true))
            {
                Phase1.SetRef(f, typeof(UILevelUpForm), "wallet", w.Pointer);
                foreach (var b in f.GetComponentsInChildren<UIButtonPrice>(true)) Phase1.SetRef(b, typeof(UIButtonPrice), "wallet", w.Pointer);
            }
        }
    }
}
