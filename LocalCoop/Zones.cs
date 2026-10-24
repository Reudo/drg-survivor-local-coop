using System;
using Assets.Scripts.LevelGeneration;
using HarmonyLib;
using UnityEngine;

namespace LocalCoop
{
    /// <summary>
    /// Zones in the world that react to "the" player: the supply-pod / excavation circle (ActivationZone) and the map's buff zones
    /// (MapBuffZone: the healing pillars, the bounce mushroom of the Azure Weald). Each of them holds one injected `player`, the host, and
    /// checks only that dwarf's distance, so the second dwarf could stand in the circle forever without filling it, and the bounce pad
    /// only launched the host. Before such a zone's Update runs, its `player` is set to the dwarf the zone should serve: the host while the
    /// host is inside, otherwise the second dwarf when it is inside (the zone has one timer, so whoever is in it fills it).
    /// Installed with the clone. Twin `-lcnozones`: the zones keep serving only the host.
    /// </summary>
    internal static class Zones
    {
        private static bool _installed;
        internal static int Swaps;

        internal static void Install()
        {
            if (_installed || Lc.Has("-lcnozones")) return;
            _installed = true;
            try
            {
                var h = new Harmony("localcoop.zones");
                foreach (var t in new[] { typeof(ActivationPatch), typeof(BuffPatch), typeof(HolePatch), typeof(DeadPlayerPatch), typeof(BobbyPatch), typeof(HotRockPatch), typeof(PlantPatch) })
                    h.CreateClassProcessor(t).Patch();
                Lc.Line("zones: supply-pod circle and buff zones serve the dwarf that is inside");
            }
            catch (Exception e) { Lc.Line("zones: install failed: " + e.GetType().Name + ": " + e.Message); }
        }

        private static float Flat(Vector3 a, Vector3 b) { float dx = a.x - b.x, dz = a.z - b.z; return dx * dx + dz * dz; }

        /// <summary>The dwarf a zone at `at` with the given squared radius should serve right now.</summary>
        private static Player Pick(Player current, Vector3 at, float sqrRadius)
        {
            try
            {
                var host = Phase1.First();
                var clone = Phase1.Second;
                if (clone == null || host == null) return current;
                if (!clone.isAlive) return current;
                if (!host.isAlive) return clone;
                if (Flat(host.transform.position, at) <= sqrRadius) return host;
                if (Flat(clone.transform.position, at) <= sqrRadius) return clone;
                return host;
            }
            catch { return current; }
        }

        private static void Serve(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase zone, Type declaring, Player current, Vector3 at, float sqrRadius)
        {
            var p = Pick(current, at, sqrRadius);
            if (p == null) return;
            if (current != null && current.Pointer == p.Pointer) return;
            Phase1.SetRef(zone, declaring, "player", p.Pointer);
            Swaps++;
        }

        /// <summary>
        /// The next run's map load calls every subscribed Player.OnBiomeSetup. The destroyed second dwarf of the last run is still subscribed;
        /// its light is gone, the call throws and the load coroutine dies: RETRY stays on the loading screen. A destroyed dwarf is skipped, and
        /// a throwing handler is swallowed so the load goes on. Twin `-lcnodeadplayer`.
        /// </summary>
        [HarmonyPatch(typeof(Player), "OnBiomeSetup")]
        private static class DeadPlayerPatch
        {
            internal static int Skipped, Swallowed;
            private static bool Prefix(Player __instance)
            {
                if (Lc.Has("-lcnodeadplayer")) return true;
                if (__instance == null) { Skipped++; Lc.LineOnce("zones: a destroyed player's OnBiomeSetup skipped"); return false; }
                return true;
            }
            private static Exception Finalizer(Exception __exception)
            {
                if (__exception == null || Lc.Has("-lcnodeadplayer")) return __exception;
                Swallowed++; Lc.LineOnce("zones: Player.OnBiomeSetup threw " + __exception.GetType().Name + ", swallowed so the map load goes on");
                return null;
            }
        }

        private static bool BothInside(Vector3 at, float sqrRadius)
        {
            try
            {
                var host = Phase1.First(); var clone = Phase1.Second;
                return host != null && clone != null && host.isAlive && clone.isAlive && Flat(host.transform.position, at) <= sqrRadius && Flat(clone.transform.position, at) <= sqrRadius;
            }
            catch { return false; }
        }

        internal static int Boosts;

        [HarmonyPatch(typeof(ActivationZone), "Update")]
        private static class ActivationPatch
        {
            private static void Prefix(ActivationZone __instance, out float __state)
            {
                __state = float.NaN;
                try
                {
                    float r = __instance.radius;
                    Serve(__instance, typeof(ActivationZone), __instance.player, __instance.transform.position, r * r);
                    if (!Lc.Has("-lcnozoneboost") && BothInside(__instance.transform.position, r * r)) __state = __instance.timeLeft;
                }
                catch { }
            }

            // both dwarves inside: the circle fills twice as fast (whatever direction the game's timer runs, its step this frame is taken once more)
            private static void Postfix(ActivationZone __instance, float __state)
            {
                try
                {
                    if (float.IsNaN(__state)) return;
                    float after = __instance.timeLeft, d = after - __state;
                    if (Mathf.Abs(d) < 1e-5f || Mathf.Abs(d) > 1f) return;
                    float v = after + d;
                    if (v < 0f) v = 0f;
                    float max = __instance.activationTime;
                    if (max > 0f && v > max) v = max;
                    __instance.timeLeft = v; Boosts++;
                }
                catch { }
            }
        }

        [HarmonyPatch(typeof(MapBuffZone), "Update")]
        private static class BuffPatch
        {
            private static void Prefix(MapBuffZone __instance)
            {
                try
                {
                    float r = __instance.PlayerRadius;
                    Serve(__instance, typeof(MapBuffZone), __instance.player, __instance.transform.position, r * r);
                }
                catch { }
            }
        }

        // the bounce mushroom overrides Update
        [HarmonyPatch(typeof(AzureWealdMagicHole), "Update")]
        private static class HolePatch
        {
            private static void Prefix(AzureWealdMagicHole __instance)
            {
                try
                {
                    float r = __instance.PlayerRadius;
                    Serve(__instance, typeof(MapBuffZone), __instance.player, __instance.transform.position, r * r);
                }
                catch { }
            }
        }

        // hand test: the escort mission's drilldozer ("Bobby") drives while the host stands in its circle; the second dwarf in the circle did nothing.
        // Its Update measures the distance to its one injected `player`. Twin `-lcnobobbyzone`.
        [HarmonyPatch(typeof(Bobby), "Update")]
        private static class BobbyPatch
        {
            private static void Prefix(Bobby __instance)
            {
                try
                {
                    if (Lc.Has("-lcnobobbyzone")) return;
                    float r = (int)__instance.State == 2 ? __instance.ommoranMiningActivationRange : __instance.playerActivationRange;
                    Serve(__instance, typeof(Bobby), __instance.player, __instance.transform.position, r * r);
                }
                catch { }
            }
        }

        // hand test: the second dwarf takes no damage from the magma floor (hot rock). The game's HotRockSystem asks, once a second, which colliders touch
        // each hot rock near the HOST, and hurts what it finds; for the second dwarf that did nothing. After the game's own tick, a dwarf that stands on a
        // hot rock and lost no health gets the hit here. Twin `-lcnohotrockfix`.
        internal static int HotHits;
        private static Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Collider> _rockHits;

        [HarmonyPatch(typeof(HotRockSystem), "Tick")]
        private static class HotRockPatch
        {
            private static void Prefix(out long __state)
            {
                __state = -1;
                try { var c = Phase1.Second; if (c != null && c.isAlive) __state = c.currentHp; } catch { }
            }

            private static Exception Finalizer(HotRockSystem __instance, long __state, Exception __exception)
            {
                try
                {
                    var c = Phase1.Second;
                    if (Lc.Has("-lcnohotrockfix") || c == null || !c.isAlive || __state < 0 || c.currentHp < __state) return __exception;
                    var rocks = __instance.hotRocks; if (rocks == null) return __exception;
                    var cp = c.transform.position;
                    var cols = _rockHits ?? (_rockHits = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Collider>(16));
                    for (int i = 0; i < rocks.Count; i++)
                    {
                        var r = rocks[i]; float dx = r.x - cp.x, dz = r.z - cp.z;
                        if (dx * dx + dz * dz > 4f) continue;                                            // far from the dwarf: no need to ask physics
                        int n = Physics.OverlapSphereNonAlloc(r, 0.3f, cols, 1 << c.gameObject.layer);
                        bool hit = false;
                        for (int k = 0; k < n && !hit; k++) { var col = cols[k]; if (col == null) continue; var pl = col.GetComponentInParent<Player>(); if (pl != null && pl.Pointer == c.Pointer) hit = true; }
                        if (!hit) continue;
                        c.TakeDamage(Math.Max(1, __instance.damage), false, EDamageType.KINETIC, false, (EDamageSource)6, null, false);
                        HotHits++; Lc.LineOnce("zones: the second dwarf stands on a hot rock and was not hurt by the game's own tick, hurt here");
                        break;
                    }
                }
                catch (Exception e) { Lc.LineOnce("zones: hot rock fix threw " + e.GetType().Name + ": " + e.Message); }
                return __exception;
            }
        }

        // ---- test `-lchotrocktest`: a hot rock is put under each dwarf in turn; both must lose health ---------------------------------------------
        private static int _hs; private static float _hat = -1f; private static long _hp0;
        internal static bool HotDone { get { return _hs >= 4; } }
        private static void HCheck(string name, bool ok, string info) { Lc.Line("zones: CHECK " + (ok ? "OK   " : "FAIL ") + name + " | " + info); }

        internal static void Tick()
        {
            BobbyTest(); PlantTest();
            if (!Lc.Has("-lchotrocktest") || _hs >= 4 || Phase1.Second == null || Lc.InRunAt < 0f) return;
            try
            {
                float now = Time.realtimeSinceStartup;
                if (_hat < 0f) { _hat = now + 9f; return; }
                if (now < _hat) return;
                var sys = CoopStart.Resolve<HotRockSystem>(); var host = Phase1.First(); var c = Phase1.Second;
                if (sys == null || host == null) { HCheck("the hot rock system exists", false, "sys=" + (sys != null)); _hs = 4; return; }
                switch (_hs)
                {
                    case 0: _hp0 = host.currentHp; sys.AddHotRock(host.transform.position); _hs = 1; _hat = now + 3.5f; break;
                    case 1: HCheck("a hot rock under the host hurts the host (the game's own way)", host.currentHp < _hp0, "hp " + _hp0 + " -> " + host.currentHp); _hs = 2; break;
                    case 2: _hp0 = c.currentHp; sys.AddHotRock(c.transform.position); _hs = 3; _hat = now + 3.5f; break;
                    case 3: HCheck("a hot rock under the second dwarf hurts the second dwarf", c.currentHp < _hp0, "hp " + _hp0 + " -> " + c.currentHp + " ourHits=" + HotHits); _hs = 4; Lc.Line("zones: SUMMARY hot rock test done" + (Lc.Has("-lcnohotrockfix") ? " (twin: expected FAIL)" : "")); break;
                }
            }
            catch (Exception e) { Lc.LineOnce("zones: hot rock test threw " + e.GetType().Name + ": " + e.Message); _hs = 4; }
        }

        // ---- test `-lcbobbytest` (with `-lcmission1`, an escort run): the host is sent far away from the drilldozer, the second dwarf stands in its circle ----
        private static int _bs; private static float _bat = -1f; private static Vector3 _bpos0;
        internal static bool BobbyDone { get { return _bs >= 3; } }

        private static void BobbyTest()
        {
            if (!Lc.Has("-lcbobbytest") || _bs >= 3 || Phase1.Second == null || Lc.InRunAt < 0f) return;
            try
            {
                float now = Time.realtimeSinceStartup;
                if (_bat < 0f) { _bat = now + 10f; return; }
                if (now < _bat) return;
                var b = Find.One<Bobby>(); var host = Phase1.First(); var c = Phase1.Second;
                if (b == null) { if (now > _bat + 40f) { HCheck("the drilldozer exists in this run", false, "no Bobby (is it an escort run? -lcmission1)"); _bs = 3; } return; }
                if (_bs == 0)
                {
                    Lc.Line("zones: bobby state=" + b.State + " range=" + b.playerActivationRange + " fuel=" + b.fuel + " pos=" + b.transform.position + " host=" + host.transform.position);
                    var bp = b.transform.position; _bpos0 = bp;
                    host.transform.position = bp + new Vector3(60f, 0f, 60f);                 // far outside the circle
                    c.transform.position = bp + new Vector3(1f, 0f, 1f);                       // inside it
                    _bs = 1; _bat = now + 4f; return;
                }
                if (_bs == 1)
                {
                    float moved = Vector3.Distance(b.transform.position, _bpos0);
                    bool inRange = false; try { inRange = b.playerInRange; } catch { }
                    Lc.Line("zones: bobby after 4 s state=" + b.State + " playerInRange=" + inRange + " moved=" + moved.ToString("F2") + " swaps=" + Swaps);
                    HCheck("the drilldozer notices the second dwarf alone in its circle", inRange, "playerInRange=" + inRange + " state=" + b.State + " moved=" + moved.ToString("F2"));
                    _bs = 3; Lc.Line("zones: SUMMARY bobby test done" + (Lc.Has("-lcnobobbyzone") ? " (twin: expected FAIL)" : ""));
                }
            }
            catch (Exception e) { Lc.LineOnce("zones: bobby test threw " + e.GetType().Name + ": " + e.Message); _bs = 3; }
        }

        // hand test: the second dwarf takes no damage from explosions (the exploding plants of the magma biome). A plant holds one injected `player`, the host, and
        // only that dwarf is hurt. After the plant's own explosion, a second dwarf inside its radius that lost no health gets the plant's player damage. Twin `-lcnoplantfix`.
        internal static int PlantHits;

        [HarmonyPatch(typeof(ExplosivePlant), "DoExplode")]
        private static class PlantPatch
        {
            private static void Prefix(out long __state)
            {
                __state = -1;
                try { var c = Phase1.Second; if (c != null && c.isAlive) __state = c.currentHp; } catch { }
            }

            private static Exception Finalizer(ExplosivePlant __instance, long __state, Exception __exception)
            {
                try
                {
                    var c = Phase1.Second;
                    if (Lc.Has("-lcnoplantfix") || c == null || !c.isAlive || __state < 0 || c.currentHp < __state) return __exception;
                    float r = __instance.radius;
                    if (Vector3.Distance(c.transform.position, __instance.transform.position) > r) return __exception;
                    c.TakeDamage((long)Math.Max(1f, __instance.playerDamage), false, EDamageType.KINETIC, true, default(EDamageSource), null, false);
                    PlantHits++; Lc.LineOnce("zones: an explosion reached the second dwarf, hurt here (the plant only knows the host)");
                }
                catch (Exception e) { Lc.LineOnce("zones: plant fix threw " + e.GetType().Name + ": " + e.Message); }
                return __exception;
            }
        }

        // ---- test `-lcplanttest`: an explosive plant is made under the second dwarf and goes off ---------------------------------------------------
        private static int _ps; private static float _pat = -1f; private static long _php;
        internal static bool PlantDone { get { return _ps >= 2; } }

        private static void PlantTest()
        {
            if (!Lc.Has("-lcplanttest") || _ps >= 2 || Phase1.Second == null || Lc.InRunAt < 0f) return;
            try
            {
                float now = Time.realtimeSinceStartup;
                if (_pat < 0f) { _pat = now + 9f; return; }
                if (now < _pat) return;
                var c = Phase1.Second;
                if (_ps == 0)
                {
                    _php = c.currentHp;
                    var go = new GameObject("LcTestPlant"); go.transform.position = c.transform.position;
                    var pl = go.AddComponent<ExplosivePlant>(); pl.radius = 4f; pl.playerDamage = 10f;
                    try { pl.DoExplode(); } catch (Exception e) { Lc.Line("zones: the test plant threw in its own code (" + e.GetType().Name + "), the fix looks at the result anyway"); }
                    _ps = 1; _pat = now + 0.5f; return;
                }
                HCheck("an explosion on the second dwarf hurts it", c.currentHp < _php, "hp " + _php + " -> " + c.currentHp + " ourHits=" + PlantHits);
                _ps = 2; Lc.Line("zones: SUMMARY plant test done" + (Lc.Has("-lcnoplantfix") ? " (twin: expected FAIL)" : ""));
            }
            catch (Exception e) { Lc.LineOnce("zones: plant test threw " + e.GetType().Name + ": " + e.Message); _ps = 2; }
        }
    }
}
