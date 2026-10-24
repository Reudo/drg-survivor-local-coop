using System;
using System.Collections.Generic;
using DRS.UI;
using HarmonyLib;
using UnityEngine;

namespace LocalCoop
{
    /// <summary>
    /// What the two dwarves share. XP is one pool: whatever the second dwarf would gain goes to the first one (the owner of the level),
    /// and each pickup flies to the dwarf that touched it (Pickup.player is set when a dwarf's trigger meets it). Every level-up is
    /// answered twice, one after the other: first by the host, then by the second dwarf (LevelRounds).
    /// Patches are installed with the clone, never during title-scene startup.
    /// </summary>
    internal static class Share
    {
        private static bool _installed;
        internal static int XpRedirected, PickupsAssigned, ClaimedByClone, ClaimedByHost, TouchCalls;
        internal static float XpRedirectedAmount;
        private static readonly HashSet<string> Seen = new HashSet<string>();

        internal static void Install()
        {
            if (_installed || Lc.Has("-lcnoshare")) return;     // the flag is the test twin
            _installed = true;
            try
            {
                var h = new Harmony("localcoop.share");
                foreach (var t in new[] { typeof(XpPatch), typeof(TouchPatch), typeof(ClaimPatch), typeof(LevelGainPatch), typeof(CardPatch), typeof(Bonus.ArtifactMirrorPatch), typeof(Bonus.SupportMirrorPatch), typeof(Bonus.SupportTagPatch), typeof(Revive.DeathPatch), typeof(LevelRounds.CratePatch), typeof(Bonus.SlotIndicatorPatch), typeof(Bonus.BookPatch), typeof(RunProgress.EndPatch), typeof(MousePatch.DownPatch), typeof(MousePatch.ClickPatch), typeof(MousePatch.UpPatch), typeof(DrillSound.TickPatch), typeof(EndScreen2.SetupPatch), typeof(EndScreen2.ProgressionPatch), typeof(Arrow2.ShowPatch), typeof(Arrow2.HidePatch), typeof(Arrow2.ColorPatch), typeof(Arrow2.TickPatch), typeof(PauseView.ShowPatch), typeof(PauseView.HidePatch), typeof(PauseView.VisibilityPatch), typeof(LevelBanner.SetupPatch), typeof(Bonus.HoverSelectPatch), typeof(Bonus.HoverDeselectPatch), typeof(Pod.InsidePatch), typeof(Chooser.CrossButtonPatch), typeof(Chooser.CrossSubmitPatch), typeof(Chooser.BackPatch), typeof(Chooser.PausePatch) })
                    h.CreateClassProcessor(t).Patch();
                Wallets.Install(h);
                Buffs2.Install(h);
                Lc.Line("share: xp redirect, pickup owner, level rounds, wallets installed");
            }
            catch (Exception e) { Lc.Line("share: install failed: " + e.GetType().Name + ": " + e.Message); }
        }

        internal static bool IsClone(Player p)
        {
            try { return Phase1.Second != null && p != null && p.Pointer == Phase1.Second.Pointer; } catch { return false; }
        }

        [HarmonyPatch(typeof(Player), "GainXp")]
        private static class XpPatch
        {
            private static bool Prefix(Player __instance, float __0, bool __1)
            {
                if (!IsClone(__instance)) return true;
                var host = Phase1.First();
                if (host == null) return true;
                XpRedirected++; XpRedirectedAmount += __0;
                host.GainXp(__0, __1);
                return false;
            }
        }

        [HarmonyPatch(typeof(Player), "OnTriggerEnter")]
        private static class TouchPatch
        {
            private static void Prefix(Player __instance, Collider __0)
            {
                try
                {
                    TouchCalls++;
                    if (Lc.Has("-lcnopickupowner")) return;     // twin: pickups keep flying to the host
                    if (Phase1.Second == null || __0 == null) return;
                    var pk = __0.GetComponentInParent<Pickup>();
                    if (Seen.Count < 14 && Seen.Add(__0.gameObject.name + (pk != null ? "[pickup]" : ""))) Lc.Line("share: touch " + __0.gameObject.name + " pickup=" + (pk != null) + " by " + (IsClone(__instance) ? "P2" : "P1"));
                    if (pk == null) return;
                    Phase1.SetRef(pk, typeof(Pickup), "player", __instance.Pointer);
                    PickupsAssigned++;
                }
                catch { }
            }
        }

        [HarmonyPatch(typeof(Player), "OnPickupClaimed")]
        private static class ClaimPatch
        {
            private static void Prefix(Player __instance) { if (IsClone(__instance)) ClaimedByClone++; else ClaimedByHost++; }
        }

        // ---- level rounds -------------------------------------------------------------------------------------------
        [HarmonyPatch(typeof(LevelUpManager), "OnPlayerLevelGain")]
        private static class LevelGainPatch
        {
            private static void Postfix(PlayerLevelGainArgs __0)
            {
                try { if (__0 != null && __0.Player != null && !IsClone(__0.Player)) LevelRounds.OnHostLevel(__0.Level); } catch { }
            }
        }

        /// <summary>Which dwarf each chosen card was applied to (the game's own UISkillButton reads its player field when clicked).</summary>
        [HarmonyPatch(typeof(UISkillButton), "OnButtonClick")]
        private static class CardPatch
        {
            private static void Prefix(UISkillButton __instance)
            {
                try
                {
                    var who = __instance.player;
                    string name = __instance.skillData != null ? __instance.skillData.name : "?";
                    LevelRounds.Picks.Add((IsClone(who) ? "P2" : "P1") + ":" + name);
                    if (!IsClone(who)) LevelRounds.OnHostPick();
                }
                catch (Exception e) { LevelRounds.Picks.Add("?:" + e.Message); }
            }
        }
    }

    /// <summary>
    /// Every level the host gains is owed once to the second dwarf. When the game's own level-up flow has ended for the host, the same
    /// flow is run once more with the second dwarf as its player (LevelUpManager, SkillCollectionManager, the form, every card button), and
    /// everything is handed back afterwards. XP pauses the game meanwhile, so nothing else can level.
    /// </summary>
    internal static class LevelRounds
    {
        internal static readonly List<string> Picks = new List<string>();
        internal static int Owed, RoundsStarted, RoundsFinished;
        internal static bool FormPaidByP2;       // seen: the form's wallet was P2's wallet while P2 chose
        private static bool _p2Context;
        private static float _startedAt = -1f;
        private static bool _sawActive;
        private static LevelUpManager _mgr;
        private static int _hostLevel;

        /// <summary>True while the second dwarf's round of a level-up is running (the form belongs to player 2).</summary>
        internal static bool P2Round { get { return _p2Context || TestForceP2; } }
        internal static bool TestForceP2;      // test only (`-lcslotarrows`)

        internal static void Reset() { _stash.Clear(); _hostNext = false; _p2Context = false; _crateRound = false; OwedCrates.Clear(); Owed = 0; _sawActive = false; _startedAt = -1f; _mgr = null; }

        internal static string State { get { return (_p2Context ? "P2 round" : "host") + " owed=" + Owed + " started=" + RoundsStarted + " finished=" + RoundsFinished; } }

        // ---- loot crates (the one after the first boss): every crate the host opens is owed to the second dwarf as well -------------------
        internal static readonly List<LootCrateArgs> OwedCrates = new List<LootCrateArgs>();
        internal static int CrateRoundsStarted, CrateRoundsFinished;
        private static bool _crateRound;

        internal static void OnHostCrate(LootCrateArgs a)
        {
            if (Lc.Has("-lcnocrateshare") || _p2Context || Phase1.Second == null || a == null) return;      // twin: the crate is for the host alone
            try { OwedCrates.Add(new LootCrateArgs(a.Rarity, a.LootCrate, a.Source)); Lc.Line("levelrounds: the host opened a " + a.Rarity + " loot crate, owed to P2: " + OwedCrates.Count); }
            catch (Exception e) { Lc.LineOnce("levelrounds: crate owed threw " + e.GetType().Name + ": " + e.Message); }
        }

        [HarmonyPatch(typeof(LevelUpManager), "OnLootCrateAccessed")]
        internal static class CratePatch
        {
            private static void Postfix(LootCrateArgs __0) { LevelRounds.OnHostCrate(__0); }
        }

        private static void StartCrateRound(LevelUpManager mgr, Player clone)
        {
            var args = OwedCrates[0]; OwedCrates.RemoveAt(0);
            CrateRoundsStarted++; RoundsStarted++;
            _p2Context = true; _crateRound = true; _sawActive = false; _startedAt = Time.realtimeSinceStartup;
            SetContext(mgr, clone);
            try { mgr.OnLootCrateAccessed(args); if (!mgr.flowActive) mgr.StartFlow(); }
            catch (Exception e) { Lc.Line("levelrounds: crate round start threw " + e.GetType().Name + ": " + e.Message); }
            Lc.Line("levelrounds: crate round for P2 started (" + args.Rarity + ", " + State + ")");
        }

        internal static void OnHostLevel(int level)
        {
            if (Lc.Has("-lcnolevelshare")) return;      // twin: no second round
            if (_p2Context) return;
            Owed++;
            _hostLevel = level;
            Lc.Line("levelrounds: host reached level " + level + ", owed to P2: " + Owed);
        }

        private static LevelUpManager Mgr()
        {
            if (_mgr == null) _mgr = CoopStart.Resolve<LevelUpManager>();
            return _mgr;
        }

        private static bool FormActive()
        {
            try
            {
                var forms = Find.Forms<UILevelUpForm>();
                foreach (var f in forms) if (f != null && f.gameObject.activeInHierarchy) return true;
            }
            catch { }
            return false;
        }

        private static string _traceLast = "";

        // ---- alternate the rounds: P1, P2, P1, P2 (not P1, P1, P2, P2) --------------------------------------------------------
        // The game answers all queued host levels in one flow. When the host picks a card while more host levels wait in the queue and the
        // second dwarf is owed a round, the waiting levels are put aside, the flow ends, P2 answers, then one put-aside level is handed back.
        private static readonly List<PlayerLevelGainArgs> _stash = new List<PlayerLevelGainArgs>();
        private static bool _hostNext;
        internal static int Alternations;

        internal static void OnHostPick()
        {
            if (Lc.Has("-lcnoalternate") || _p2Context || Phase1.Second == null || Owed <= 0) return;
            try
            {
                var mgr = Mgr();
                if (mgr == null || mgr.levelUps == null || mgr.levelUps.Count == 0) return;
                for (int i = 0; i < mgr.levelUps.Count; i++) _stash.Add(mgr.levelUps[i]);
                mgr.levelUps.Clear();
                Alternations++;
                Lc.Line("levelrounds: " + _stash.Count + " more host level(s) put aside so that P2 answers in between");
            }
            catch (Exception e) { Lc.LineOnce("levelrounds: stash threw " + e.GetType().Name + ": " + e.Message); }
        }

        private static void ReleaseStash(LevelUpManager mgr)
        {
            for (int i = 0; i < _stash.Count; i++) mgr.levelUps.Add(_stash[i]);
            _stash.Clear(); _hostNext = false;
        }

        internal static void Tick()
        {
            if (Phase1.Second == null) return;
            try
            {
                var mgr = Mgr();
                if (mgr == null) return;
                var host = Phase1.First();
                var clone = Phase1.Second;
                if (host == null) return;

                if (Lc.Has("-lclvltrace"))
                {
                    string s = (_p2Context ? "P2" : "P1") + " flow=" + mgr.flowActive + " queue=" + mgr.levelUps.Count + " crates=" + mgr.lootCrates.Count + " form=" + FormActive() + " owed=" + Owed + " ts=" + Time.timeScale.ToString("F1");
                    if (s != _traceLast) { _traceLast = s; Lc.Line("lvltrace: " + s); }
                }

                if (_p2Context)
                {
                    SwapCards(clone);
                    try { foreach (var f in UnityEngine.Object.FindObjectsOfType<UILevelUpForm>(false)) if (f.gameObject.activeInHierarchy && f.wallet != null && Wallets.W2 != null && f.wallet.Pointer == Wallets.W2.Pointer) FormPaidByP2 = true; } catch { }
                    if (mgr.flowActive) _sawActive = true;
                    bool over = _sawActive && !mgr.flowActive && mgr.levelUps.Count == 0 && mgr.lootCrates.Count == 0 && !FormActive();
                    if (over || Time.realtimeSinceStartup - _startedAt > 120f) EndRound(mgr, host, clone);
                    return;
                }

                if (!clone.isAlive) { OwedCrates.Clear(); }
                if (_stash.Count > 0 && (!clone.isAlive || Owed == 0) && !mgr.flowActive && !FormActive()) { ReleaseStash(mgr); mgr.StartFlow(); return; }
                if (_stash.Count > 0 && _hostNext && !mgr.flowActive && mgr.levelUps.Count == 0 && !FormActive())
                {
                    _hostNext = false; mgr.levelUps.Add(_stash[0]); _stash.RemoveAt(0); mgr.StartFlow();
                    Lc.Line("levelrounds: the host answers its next level (" + _stash.Count + " left aside, owed to P2: " + Owed + ")");
                    return;
                }
                if (Owed > 0 && !clone.isAlive) { Owed = 0; return; }
                if (Owed == 0 && OwedCrates.Count > 0 && !mgr.flowActive && mgr.levelUps.Count == 0 && mgr.lootCrates.Count == 0 && !FormActive() && Time.timeScale > 0.5f) { StartCrateRound(mgr, clone); return; }
                if (Owed > 0 && !mgr.flowActive && mgr.levelUps.Count == 0 && !FormActive()) StartRound(mgr, host, clone);
            }
            catch (Exception e) { Lc.LineOnce("levelrounds: tick threw " + e.GetType().Name + ": " + e.Message); }
        }

        private static void StartRound(LevelUpManager mgr, Player host, Player clone)
        {
            Owed--;
            RoundsStarted++;
            _p2Context = true; _sawActive = false; _startedAt = Time.realtimeSinceStartup;
            SetContext(mgr, clone);
            try { clone.currentLevel = host.currentLevel; } catch { }
            mgr.levelUps.Add(new PlayerLevelGainArgs(clone, _hostLevel));
            mgr.StartFlow();
            try
            {
                Lc.Line("levelrounds: P2 level " + clone.currentLevel + " host level " + host.currentLevel + " weapons P2=" + (clone.weapons != null ? clone.weapons.Count : -1) + " host=" + (host.weapons != null ? host.weapons.Count : -1));
            }
            catch (Exception e) { Lc.LineOnce("levelrounds: diag threw " + e.GetType().Name); }
            Lc.Line("levelrounds: round " + RoundsStarted + " for P2 started (" + State + ")");
        }

        private static void EndRound(LevelUpManager mgr, Player host, Player clone)
        {
            SetContext(mgr, host);
            SwapCardsTo(host);
            _p2Context = false;
            if (_stash.Count > 0) _hostNext = true;
            RoundsFinished++;
            if (_crateRound) { CrateRoundsFinished++; _crateRound = false; }
            Lc.Line("levelrounds: round finished, context back to the host (" + State + ")");
        }

        /// <summary>Whose skills the level-up flow offers and applies to: the manager, the skill collection, the form.</summary>
        private static void SetContext(LevelUpManager mgr, Player p)
        {
            Phase1.SetRef(mgr, typeof(LevelUpManager), "player", p.Pointer);
            if (!Lc.Has("-lcnoformwallet")) Wallets.SetFormWallet(Equals(p.Pointer, Phase1.Second != null ? Phase1.Second.Pointer : IntPtr.Zero) ? Wallets.Ensure() : Wallets.Host());
            try { mgr.skillCollectionManager.SetPlayer(p); } catch (Exception e) { Lc.LineOnce("levelrounds: SetPlayer threw " + e.Message); }
            foreach (var f in UnityEngine.Object.FindObjectsOfType<UILevelUpForm>(true)) Phase1.SetRef(f, typeof(UILevelUpForm), "player", p.Pointer);
        }

        private static void SwapCards(Player clone) { if (!Lc.Has("-lcnobuttonswap")) SwapCardsTo(clone); }

        internal static void SwapCardsTo(Player p)
        {
            foreach (var b in Resources.FindObjectsOfTypeAll<UISkillButton>())
            {
                if (b == null) continue;
                try { if (b.player == null || b.player.Pointer != p.Pointer) Phase1.SetRef(b, typeof(UISkillButton), "player", p.Pointer); } catch { }
            }
        }
    }
}
