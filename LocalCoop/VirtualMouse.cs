using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace LocalCoop
{
    /// <summary>A mouse that exists only inside the game's Input System: tests click real buttons through the game's own UI input
    /// (pointer move, press, release), the same path a real mouse takes.</summary>
    internal static class VirtualMouse
    {
        private static Mouse _mouse;
        internal static int Clicks;

        private static Mouse Dev
        {
            get
            {
                if (_mouse != null) return _mouse;
                try { _mouse = InputSystem.AddDevice<Mouse>("LcVirtualMouse"); Lc.Line("mouse: virtual mouse added"); }
                catch (Exception e) { Lc.LineOnce("mouse: AddDevice failed: " + e.GetType().Name + ": " + e.Message); }
                return _mouse;
            }
        }

        /// <summary>Step 0 moves, step 1 presses, step 2 releases; the caller spaces the steps over frames.</summary>
        internal static void Step(int step, Vector2 screenPos)
        {
            var m = Dev;
            if (m == null) return;
            var st = new MouseState();
            st.position = screenPos; st.delta = new Vector2(12f, 9f);
            if (step == 1) st.buttons = 1;                 // bit 0 = left button
            InputSystem.QueueStateEvent(m, st);
            if (step == 1) Clicks++;
        }
    }
}
