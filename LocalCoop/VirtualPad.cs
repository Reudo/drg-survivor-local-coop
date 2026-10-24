using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace LocalCoop
{
    /// <summary>
    /// A gamepad that exists only inside the game's Input System. It needs no driver, no focus and no unlocked
    /// desktop, and it reaches the game through the same path as a real controller. First use: the title
    /// screen's "press any key". Later: the second player's controller in tests.
    /// </summary>
    internal static class VirtualPad
    {
        private static Gamepad _pad, _pad1;

        /// <summary>True while no virtual pad exists, i.e. a normal game: player 2's device is a real second gamepad.</summary>
        internal static bool Remote { get { return Lc.Has("-lcremote"); } }
        internal static bool IsReal { get { return _pad == null; } }

        /// <summary>Tests with two virtual pads: a second pad that stands for the first player's controller.</summary>
        internal static void Pad1Touch()
        {
            if (_pad1 != null) return;
            try { var p2 = Pad; _pad1 = InputSystem.AddDevice<Gamepad>("LcVirtualPad1"); Lc.Line("pad: second virtual gamepad added (player 1's controller in tests)"); }
            catch (Exception e) { Lc.LineOnce("pad: AddDevice 1 failed: " + e.GetType().Name + ": " + e.Message); }
        }

        /// <summary>Press one button on the second virtual pad (controller 1 in tests with two pads).</summary>
        internal static void Press1(GamepadButton b, float hold = 0.15f)
        {
            try
            {
                Pad1Touch();
                if (_pad1 == null) return;
                var st = new GamepadState();
                st.buttons = 1u << (int)b;
                InputSystem.QueueStateEvent(_pad1, st);
                _releaseAt1 = Time.realtimeSinceStartup + hold;
            }
            catch (Exception e) { Lc.LineOnce("pad: Press1 failed: " + e.GetType().Name + ": " + e.Message); }
        }

        internal static void SetStick1(Vector2 v)
        {
            try
            {
                if (_pad1 == null) return;
                var st = new GamepadState(); st.leftStick = v;
                InputSystem.QueueStateEvent(_pad1, st);
            }
            catch (Exception e) { Lc.LineOnce("pad: SetStick1 failed: " + e.GetType().Name + ": " + e.Message); }
        }
        private static float _releaseAt = -1f, _releaseAt1 = -1f;
        internal static int Presses;

        internal static Gamepad Pad
        {
            get
            {
                if (_pad != null) return _pad;
                try
                {
                    _pad = InputSystem.AddDevice<Gamepad>("LcVirtualPad");
                    Lc.Line("pad: virtual gamepad added: " + (_pad != null ? _pad.displayName : "null"));
                }
                catch (Exception e) { Lc.LineOnce("pad: AddDevice failed: " + e.GetType().Name + ": " + e.Message); }
                return _pad;
            }
        }


        /// <summary>
        /// Player 2's pad must not drive dwarf 1: the game binds its actions to every gamepad. A device that is disabled
        /// but keeps sending events is ignored by actions while its state still updates, which this plugin reads itself.
        /// Called once the clone exists (the title screen still needs the pad).
        /// </summary>
        internal static bool Isolated;
        internal static void Isolate()
        {
            if (Isolated || !Lc.Has("-lcisolate")) return;
            try
            {
                var p = Pad;
                if (p == null) return;
                InputSystem.DisableDevice(p, true);
                Isolated = true;
                Lc.Line("pad: device disabled for actions (keepSendingEvents); enabled=" + p.enabled);
            }
            catch (Exception e) { Lc.Line("pad: DisableDevice failed: " + e.GetType().Name + ": " + e.Message); }
        }

        /// <summary>The device that belongs to player 2: the virtual pad when it exists (tests), otherwise the second real gamepad.</summary>
        internal static InputDevice P2Device()
        {
            try
            {
                if (_pad != null) return _pad;
                var all = Gamepad.all;
                // Steam Remote Play Together: the guest's controller shows up as one more gamepad on this PC, the host plays on keyboard and
                // mouse (or a pad of their own). `-lcremote`: player 2 is the most recently connected gamepad, even if it is the only one.
                if (Remote && all != null && all.Count >= 1) return all[all.Count - 1];
                if (all != null && all.Count >= 2) return all[1];
            }
            catch { }
            return null;
        }

        /// <summary>Press the south button (A) for a moment; Tick() releases it.</summary>
        internal static void PressSouth()
        {
            if (Lc.Has("-lcnopad")) return;   // test twin: the start must then fail
            try
            {
                var p = Pad;
                if (p == null) return;
                var st = new GamepadState();
                st.buttons = 1u << (int)GamepadButton.South;
                InputSystem.QueueStateEvent(p, st);
                _releaseAt = Time.realtimeSinceStartup + 0.15f;
                Presses++;
                Lc.Line("pad: pressed south (" + Presses + ")");
            }
            catch (Exception e) { Lc.LineOnce("pad: press failed: " + e.GetType().Name + ": " + e.Message); }
        }

        /// <summary>Press one button (d-pad, A, B, Start ...) for a moment; Tick() releases it.</summary>
        internal static void Press(GamepadButton b, float hold = 0.15f)
        {
            try
            {
                var p = Pad;
                if (p == null) return;
                var st = new GamepadState();
                st.buttons = 1u << (int)b;
                InputSystem.QueueStateEvent(p, st);
                _releaseAt = Time.realtimeSinceStartup + hold;
            }
            catch (Exception e) { Lc.LineOnce("pad: Press failed: " + e.GetType().Name + ": " + e.Message); }
        }

        /// <summary>Test input: hold the left stick of the virtual pad at a value (written into the device state like a real controller would).</summary>
        internal static void SetStick(Vector2 v)
        {
            try
            {
                var p = Pad;
                if (p == null) return;
                var st = new GamepadState();
                st.leftStick = v;
                InputSystem.QueueStateEvent(p, st);
            }
            catch (Exception e) { Lc.LineOnce("pad: SetStick failed: " + e.GetType().Name + ": " + e.Message); }
        }

        /// <summary>What a controller 2 reader sees: the pad's left stick.</summary>
        internal static Vector2 ReadStick()
        {
            try { var p = Pad; return p != null ? p.leftStick.ReadValue() : Vector2.zero; }
            catch { return Vector2.zero; }
        }

        internal static void Tick()
        {
            if (_releaseAt1 >= 0f && Time.realtimeSinceStartup >= _releaseAt1)
            {
                _releaseAt1 = -1f;
                try { if (_pad1 != null) InputSystem.QueueStateEvent(_pad1, new GamepadState()); } catch { }
            }
            if (_releaseAt < 0f || Time.realtimeSinceStartup < _releaseAt) return;
            _releaseAt = -1f;
            try { InputSystem.QueueStateEvent(_pad, new GamepadState()); } catch { }
        }
    }
}
