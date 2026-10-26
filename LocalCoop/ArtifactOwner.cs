using System;
using System.Reflection;
using HarmonyLib;

namespace LocalCoop
{
    /// <summary>
    /// hand test: artifacts that react to something that happened to a dwarf (damage taken, a dodge, a mined block) worked for both dwarves, whoever it
    /// happened to. The game raises one static event per kind and every copy of the artifact (one per dwarf) reacts to it; none of them asks whose event it is
    /// (measured: an event naming player 1 gave the dodge buff of "Reflex-Justierer" to both). The handlers are wrapped here: a copy ignores an event whose
    /// player is not its own dwarf. Twin `-lcnoartowner`.
    /// The reroll event names nobody: it goes to the copy of the dwarf who is choosing (Chooser.Who).
    /// </summary>
    internal static class ArtifactOwner
    {
        internal static int Skipped, Passed;
        private static bool _done;

        private static Player ArgPlayer(object[] args)
        {
            if (args == null) return null;
            foreach (var a in args)
            {
                if (a == null) continue;
                var p = a.GetType().GetProperty("Player", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null && p.PropertyType == typeof(Player)) { try { return p.GetValue(a, null) as Player; } catch { } }
            }
            return null;
        }

        private static bool PrefixEvent(object __instance, object[] __args)
        {
            try
            {
                if (Lc.Has("-lcnoartowner") || Phase1.Second == null) return true;
                var me = Buffs2.OwnerOf(__instance); var who = ArgPlayer(__args);
                if (me == null || who == null) return true;                         // unknown: as the game does
                if (me.Pointer == who.Pointer) { Passed++; return true; }
                Skipped++; return false;
            }
            catch { return true; }
        }

        private static bool PrefixReroll(object __instance)
        {
            try
            {
                if (Lc.Has("-lcnoartowner") || Phase1.Second == null || Chooser.Who == 0) return true;
                var me = Buffs2.OwnerOf(__instance); if (me == null) return true;
                bool mine = (Chooser.Who == 2) == Share.IsClone(me);
                if (mine) { Passed++; return true; }
                Skipped++; return false;
            }
            catch { return true; }
        }

        // ---- per-level artifact: reads the level of its own dwarf -----------------------------------------------------------------------------
        // (read off the game's code: `OnPlayerLevelUpStatUpdate` ignores its argument and counts `player.currentLevel - levelAlreadyApplied + 1` levels.) The second dwarf's level is
        // only copied from the host's when its own level-up round is queued, which is after the event: its copy of the artifact always saw the previous level and ran one level behind.
        private static void PrefixLevel(object __instance)
        {
            try
            {
                if (Lc.Has("-lcnoartowner") || Phase1.Second == null) return;
                var me = Buffs2.OwnerOf(__instance); var host = Phase1.First();
                if (me != null && host != null && Share.IsClone(me) && me.currentLevel != host.currentLevel) { me.currentLevel = host.currentLevel; LevelSyncs++; }
            }
            catch { }
        }

        // ---- wallet artifacts: the static wallet event carries the total of whichever wallet changed ---------------------------------------------
        // (read off the game's code: `OnCurrencyChanged(type, total, delta)` sets the stacks to total / count.) With two wallets both copies took the total of the wallet that changed
        // last. Each copy now takes the total of its own dwarf's wallet.
        private static int OwnTotal(Player me, ECurrency c)
        {
            Wallet w = Share.IsClone(me) ? Wallets.Ensure() : Wallets.Host();
            if (w == null) return -1;
            switch (c) { case ECurrency.GOLD: return w.Gold; case ECurrency.MORKITE: return w.Morkite; case ECurrency.NITRA: return w.Nitra; default: return -1; }
        }

        private static void PrefixWallet(object __instance, ECurrency __0, ref int __1)
        {
            try
            {
                if (Lc.Has("-lcnoartowner") || Phase1.Second == null) return;
                var me = Buffs2.OwnerOf(__instance); if (me == null) return;
                int t = OwnTotal(me, __0);
                if (t >= 0 && t != __1) { __1 = t; WalletFixes++; }
            }
            catch { }
        }

        /// <summary>The copy of the second dwarf starts from the host's wallet (the game injects the host's): tell it the total of its own.</summary>
        private static void PostfixWalletSetup(ScaleStatWithWalletArtifact __instance)
        {
            try
            {
                if (Lc.Has("-lcnoartowner") || Phase1.Second == null) return;
                var me = Buffs2.OwnerOf(__instance); if (me == null || !Share.IsClone(me)) return;
                var d = __instance.data; if (d == null) return;
                __instance.OnCurrencyChanged(d.CurrencyType, 0, 0);
            }
            catch { }
        }

        internal static int LevelSyncs, WalletFixes;

        internal static void Install(Harmony h)
        {
            if (_done || Lc.Has("-lcnoartowner")) return;
            _done = true; int n = 0;
            try
            {
                const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                var ev = new HarmonyMethod(typeof(ArtifactOwner), "PrefixEvent"); var rr = new HarmonyMethod(typeof(ArtifactOwner), "PrefixReroll");
                Action<Type, string, HarmonyMethod> hook = (t, name, pre) =>
                {
                    var m = t.GetMethod(name, F);
                    if (m == null) { Lc.Line("artowner: no " + t.Name + "." + name); return; }
                    h.Patch(m, prefix: pre); n++;
                };
                hook(typeof(TimedBuffOnDamageTakenArtifact), "OnDamageTaken", ev);
                hook(typeof(DamageShieldArtifact), "OnPlayerDamageTaken", ev);
                hook(typeof(CurrencyOnDamageTakenArtifact), "OnPlayerDamageTaken", ev);
                hook(typeof(GroundZonesOnDmgTakenArtifact), "OnPlayerDamageTaken", ev);
                hook(typeof(JetBootsArtifact), "OnPlayerDamageTaken", ev);
                hook(typeof(DmgZoneOnDodgeArtifact), "OnPlayerDodge", ev);
                hook(typeof(CurrencyFromBlockArtifact), "OnTrigger", ev);
                hook(typeof(StatusEffectOnDmgDoneArtifact), "OnPlayerDamageDone", ev);
                // not the class artifacts (Juggernaut / Foreman / Scout recon): wrapping their handlers closed the game a few seconds into the run (measured, 3 of 3 runs)
                hook(typeof(LuckRerollArtifact), "OnPreReroll", rr); hook(typeof(Pay2WinArtifact), "OnPreReroll", rr);
                hook(typeof(StatModPrPlayerLevelArtifact), "OnPlayerLevelUpStatUpdate", new HarmonyMethod(typeof(ArtifactOwner), "PrefixLevel"));
                hook(typeof(ScaleStatWithWalletArtifact), "OnCurrencyChanged", new HarmonyMethod(typeof(ArtifactOwner), "PrefixWallet"));
                var setup = typeof(ScaleStatWithWalletArtifact).GetMethod("Setup", F);
                if (setup != null) { h.Patch(setup, postfix: new HarmonyMethod(typeof(ArtifactOwner), "PostfixWalletSetup")); n++; }
                Lc.Line("artowner: " + n + " artifact handlers filter on their own dwarf");
            }
            catch (Exception e) { Lc.Line("artowner: install failed: " + e.GetType().Name + ": " + e.Message); }
        }
    }
}
