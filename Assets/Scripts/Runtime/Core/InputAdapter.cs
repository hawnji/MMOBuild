using UnityEngine;
#if EV_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Embervale.Client
{
    /// <summary>
    /// Thin wrapper so gameplay code works with either the Input System package (preferred, installed by the
    /// project bootstrap) or the legacy Input Manager. EV_INPUT_SYSTEM is defined by the asmdef when the
    /// com.unity.inputsystem package is present; ENABLE_INPUT_SYSTEM by Unity when its backend is active
    /// (Player Settings > Active Input Handling). Otherwise the legacy Input Manager is used.
    /// </summary>
    public static class InputAdapter
    {
        public enum Key { Escape, Enter, Space, Tab, Shift, Ctrl, Q, E, W, A, S, D, M, I, K, B, F1, F2, F3, F4, F5, Up, Down, Left, Right, Backspace }

        public static Vector2 MousePosition
        {
            get
            {
#if EV_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
                return Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#else
                return Input.mousePosition;
#endif
            }
        }

        /// <summary>Scroll in "notches" (positive = away from the user).</summary>
        public static float Scroll
        {
            get
            {
#if EV_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
                if (Mouse.current == null) return 0;
                var v = Mouse.current.scroll.ReadValue().y;
                return Mathf.Abs(v) > 20 ? v / 120f : v;
#else
                return Input.mouseScrollDelta.y;
#endif
            }
        }

        public static bool MouseDown(int button)
        {
#if EV_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            var m = Mouse.current;
            if (m == null) return false;
            return button == 0 ? m.leftButton.wasPressedThisFrame : button == 1 ? m.rightButton.wasPressedThisFrame : m.middleButton.wasPressedThisFrame;
#else
            return Input.GetMouseButtonDown(button);
#endif
        }

        public static bool MouseHeld(int button)
        {
#if EV_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            var m = Mouse.current;
            if (m == null) return false;
            return button == 0 ? m.leftButton.isPressed : button == 1 ? m.rightButton.isPressed : m.middleButton.isPressed;
#else
            return Input.GetMouseButton(button);
#endif
        }

        public static bool MouseUp(int button)
        {
#if EV_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            var m = Mouse.current;
            if (m == null) return false;
            return button == 0 ? m.leftButton.wasReleasedThisFrame : button == 1 ? m.rightButton.wasReleasedThisFrame : m.middleButton.wasReleasedThisFrame;
#else
            return Input.GetMouseButtonUp(button);
#endif
        }

        public static bool Down(Key k)
        {
#if EV_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            var c = ToControl(k);
            return c != null && c.wasPressedThisFrame;
#else
            return Input.GetKeyDown(ToLegacy(k));
#endif
        }

        public static bool Held(Key k)
        {
#if EV_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            var c = ToControl(k);
            return c != null && c.isPressed;
#else
            if (k == Key.Shift) return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (k == Key.Ctrl) return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            return Input.GetKey(ToLegacy(k));
#endif
        }

#if EV_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
        private static UnityEngine.InputSystem.Controls.ButtonControl ToControl(Key k)
        {
            var kb = Keyboard.current;
            if (kb == null) return null;
            switch (k)
            {
                case Key.Escape: return kb.escapeKey;
                case Key.Enter: return kb.enterKey;
                case Key.Space: return kb.spaceKey;
                case Key.Tab: return kb.tabKey;
                case Key.Shift: return kb.shiftKey;
                case Key.Ctrl: return kb.ctrlKey;
                case Key.Q: return kb.qKey;
                case Key.E: return kb.eKey;
                case Key.W: return kb.wKey;
                case Key.A: return kb.aKey;
                case Key.S: return kb.sKey;
                case Key.D: return kb.dKey;
                case Key.M: return kb.mKey;
                case Key.I: return kb.iKey;
                case Key.K: return kb.kKey;
                case Key.B: return kb.bKey;
                case Key.F1: return kb.f1Key;
                case Key.F2: return kb.f2Key;
                case Key.F3: return kb.f3Key;
                case Key.F4: return kb.f4Key;
                case Key.F5: return kb.f5Key;
                case Key.Up: return kb.upArrowKey;
                case Key.Down: return kb.downArrowKey;
                case Key.Left: return kb.leftArrowKey;
                case Key.Right: return kb.rightArrowKey;
                case Key.Backspace: return kb.backspaceKey;
            }
            return null;
        }
#else
        private static KeyCode ToLegacy(Key k)
        {
            switch (k)
            {
                case Key.Escape: return KeyCode.Escape;
                case Key.Enter: return KeyCode.Return;
                case Key.Space: return KeyCode.Space;
                case Key.Tab: return KeyCode.Tab;
                case Key.Shift: return KeyCode.LeftShift;
                case Key.Ctrl: return KeyCode.LeftControl;
                case Key.Q: return KeyCode.Q;
                case Key.E: return KeyCode.E;
                case Key.W: return KeyCode.W;
                case Key.A: return KeyCode.A;
                case Key.S: return KeyCode.S;
                case Key.D: return KeyCode.D;
                case Key.M: return KeyCode.M;
                case Key.I: return KeyCode.I;
                case Key.K: return KeyCode.K;
                case Key.B: return KeyCode.B;
                case Key.F1: return KeyCode.F1;
                case Key.F2: return KeyCode.F2;
                case Key.F3: return KeyCode.F3;
                case Key.F4: return KeyCode.F4;
                case Key.F5: return KeyCode.F5;
                case Key.Up: return KeyCode.UpArrow;
                case Key.Down: return KeyCode.DownArrow;
                case Key.Left: return KeyCode.LeftArrow;
                case Key.Right: return KeyCode.RightArrow;
                case Key.Backspace: return KeyCode.Backspace;
            }
            return KeyCode.None;
        }
#endif
    }
}
