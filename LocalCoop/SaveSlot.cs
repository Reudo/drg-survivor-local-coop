using System;
using HarmonyLib;
using UnityEngine;

namespace LocalCoop
{
    /// <summary>
    /// Tests play in one fixed save slot (owner, 2026-10-26: always slot 3; slots 1 and 2 are the owner's). The game asks for the slot on its own page after the
    /// splash; the harness clicks the window centre to get past the splash, and on that page the centre is slot 2, which is where every test ran so far.
    /// `-lcslot=N` (lc-run.ps1 passes 3): as soon as the page shows, slot N is chosen, and any click on another slot is turned into slot N. Back in the menu the
    /// slot the game has loaded is checked; a wrong slot ends the test at once (RESULT FAIL, before a run can write anything).
    /// </summary>
    internal static class SaveSlot
    {
        private static int _want = -1;          // 1..3, 0 = off
        private static bool _patched, _picked, _checked;
        private static float _at;
        private static ISaveFileManager _sfm;
        internal static int Redirects;

        private static ESaveSlot Wanted { get { return (ESaveSlot)(_want - 1); } }

        internal static void Tick()
        {
            if (_want < 0) { _want = (int)Lc.Num("-lcslot="); if (_want < 1 || _want > 3) _want = 0; else Lc.Line("slot: tests use save slot " + _want); }
            if (_want == 0 || _checked) return;
            float now = Time.realtimeSinceStartup;
            if (now < _at) return;
            _at = now + 0.1f;
            try
            {
                if (!_picked)
                {
                    var sel = UnityEngine.Object.FindObjectOfType<UISaveSlotSelector>();
                    if (sel == null || !sel.gameObject.activeInHierarchy) return;
                    Patch();
                    _sfm = sel.saveFileManager;
                    _picked = true;
                    Lc.Line("slot: save slot page shown, choosing slot " + _want);
                    sel.OnSaveSlotSelected(Wanted);
                    return;
                }
                if (MenuEntry.EntryButton == null) return;          // the main menu is there: the slot is loaded
                _checked = true;
                ESaveSlot got = Wanted; bool known = false;
                try { if (_sfm != null) { var pc = _sfm.TryCast<PCSaveFileManager>(); got = pc != null ? pc.Slot : _sfm.Slot; known = true; } } catch (Exception e) { Lc.Line("slot: reading the loaded slot threw " + e.GetType().Name + ": " + e.Message); }
                Lc.Line("slot: loaded " + (known ? got.ToString() : "unknown") + ", wanted " + Wanted + " (clicks on other slots redirected: " + Redirects + ")");
                if (!known || got != Wanted)
                {
                    Lc.QuitSent = true;
                    Lc.Line("RESULT " + (Lc.Test.Length > 0 ? Lc.Test : "boot") + " FAIL wrong save slot: " + (known ? got.ToString() : "unknown") + " instead of " + Wanted);
                    Application.Quit();
                }
            }
            catch (Exception e) { Lc.LineOnce("slot: threw " + e.GetType().Name + ": " + e.Message); }
        }

        /// <summary>Installed only once the page is on screen (never while the title scene starts, see Plugin).</summary>
        private static void Patch()
        {
            if (_patched) return;
            _patched = true;
            try
            {
                var orig = AccessTools.Method(typeof(UISaveSlotSelector), "OnSaveSlotSelected");
                var pre = new HarmonyMethod(typeof(SaveSlot).GetMethod("Prefix", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic));
                new Harmony("localcoop.saveslot").Patch(orig, pre);
                Lc.Line("slot: save slot choice patched");
            }
            catch (Exception e) { Lc.Line("slot: patch failed " + e.GetType().Name + ": " + e.Message); }
        }

        private static void Prefix(ref ESaveSlot __0)
        {
            if (_want == 0 || __0 == Wanted) return;
            Redirects++;
            Lc.Line("slot: click on " + __0 + " turned into " + Wanted);
            __0 = Wanted;
        }
    }
}
