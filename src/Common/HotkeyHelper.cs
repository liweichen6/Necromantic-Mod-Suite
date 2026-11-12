using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using BepInEx.Logging;
using UnityEngine;

namespace NecromanticCommon
{
    /// <summary>
    /// Hotkey utility for the suite. Reads physical key state via Win32 GetAsyncKeyState
    /// (only while the game window is focused) with UnityEngine.Input as a fallback, so
    /// hotkeys work even if Rewired swallows Unity's legacy Input. Edge-triggered and
    /// debounced so one press fires exactly once.
    ///
    /// Note: this only works if the calling plugin's Update() actually runs, see ModHost.
    /// </summary>
    public static class HotkeyHelper
    {
        // =========================================================================
        // Win32 P/Invoke Declarations
        // =========================================================================

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        // =========================================================================
        // State Tracking
        // =========================================================================

        private static uint _currentProcessId = 0;
        private static readonly HashSet<int> _keysHeld = new HashSet<int>();
        private static readonly Dictionary<KeyCode, float> _lastTriggerTime = new Dictionary<KeyCode, float>();
        private const float DebounceSeconds = 0.2f;
        private static float _suppressUntil;

        /// <summary>
        /// Blocks every hotkey for the next <paramref name="seconds"/> (real time). Call it each
        /// frame while waiting for a key to rebind, so the pressed key doesn't also trigger.
        /// </summary>
        public static void SuppressFor(float seconds)
        {
            _suppressUntil = Mathf.Max(_suppressUntil, Time.realtimeSinceStartup + seconds);
        }

        /// <summary>
        /// Checks whether the game's window is currently active and focused in Windows.
        /// Prevents mod hotkeys from triggering while the player is alt-tabbed.
        /// </summary>
        public static bool IsGameWindowFocused()
        {
            try
            {
                if (_currentProcessId == 0)
                {
                    _currentProcessId = (uint)Process.GetCurrentProcess().Id;
                }

                IntPtr fgWnd = GetForegroundWindow();
                if (fgWnd == IntPtr.Zero) return false;

                uint fgPid;
                GetWindowThreadProcessId(fgWnd, out fgPid);
                return fgPid == _currentProcessId;
            }
            catch
            {
                // Fallback to true if P/Invoke is restricted or fails
                return true;
            }
        }

        // =========================================================================
        // Key Mapping
        // =========================================================================

        /// <summary>
        /// Maps common Unity KeyCode enum values to Windows Virtual Key (VK) codes.
        /// </summary>
        public static int KeyCodeToVirtualKey(KeyCode key)
        {
            // Function Keys F1 - F12 (VK_F1 = 0x70 ... VK_F12 = 0x7B)
            if (key >= KeyCode.F1 && key <= KeyCode.F12)
            {
                return 0x70 + (key - KeyCode.F1);
            }

            // Number keys 0 - 9 (0x30 ... 0x39)
            if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9)
            {
                return 0x30 + (key - KeyCode.Alpha0);
            }

            // Keypad numbers 0 - 9 (VK_NUMPAD0 = 0x60 ... VK_NUMPAD9 = 0x69)
            if (key >= KeyCode.Keypad0 && key <= KeyCode.Keypad9)
            {
                return 0x60 + (key - KeyCode.Keypad0);
            }

            // Letters A - Z (0x41 ... 0x5A)
            if (key >= KeyCode.A && key <= KeyCode.Z)
            {
                return 0x41 + (key - KeyCode.A);
            }

            // Special and Navigation Keys
            switch (key)
            {
                case KeyCode.Insert: return 0x2D;      // VK_INSERT
                case KeyCode.Delete: return 0x2E;      // VK_DELETE
                case KeyCode.Home: return 0x24;        // VK_HOME
                case KeyCode.End: return 0x23;         // VK_END
                case KeyCode.PageUp: return 0x21;      // VK_PRIOR
                case KeyCode.PageDown: return 0x22;    // VK_NEXT
                case KeyCode.Tab: return 0x09;         // VK_TAB
                case KeyCode.Space: return 0x20;       // VK_SPACE
                case KeyCode.Return: return 0x0D;      // VK_RETURN
                case KeyCode.Escape: return 0x1B;      // VK_ESCAPE
                case KeyCode.BackQuote: return 0xC0;   // VK_OEM_3 (`~)
                case KeyCode.Minus: return 0xBD;       // VK_OEM_MINUS
                case KeyCode.Equals: return 0xBB;      // VK_OEM_PLUS
                case KeyCode.LeftBracket: return 0xDB; // VK_OEM_4
                case KeyCode.RightBracket: return 0xDD;// VK_OEM_6
                case KeyCode.Backslash: return 0xDC;   // VK_OEM_5
                case KeyCode.Semicolon: return 0xBA;   // VK_OEM_1
                case KeyCode.Quote: return 0xDE;       // VK_OEM_7
                case KeyCode.Comma: return 0xBC;       // VK_OEM_COMMA
                case KeyCode.Period: return 0xBE;      // VK_OEM_PERIOD
                case KeyCode.Slash: return 0xBF;       // VK_OEM_2
                case KeyCode.Backspace: return 0x08;   // VK_BACK
                case KeyCode.Pause: return 0x13;       // VK_PAUSE
                case KeyCode.ScrollLock: return 0x91;  // VK_SCROLL
                case KeyCode.LeftArrow: return 0x25;   // VK_LEFT
                case KeyCode.UpArrow: return 0x26;     // VK_UP
                case KeyCode.RightArrow: return 0x27;  // VK_RIGHT
                case KeyCode.DownArrow: return 0x28;   // VK_DOWN
                case KeyCode.KeypadMultiply: return 0x6A;
                case KeyCode.KeypadPlus: return 0x6B;
                case KeyCode.KeypadMinus: return 0x6D;
                case KeyCode.KeypadPeriod: return 0x6E;
                case KeyCode.KeypadDivide: return 0x6F;

                // Modifiers
                case KeyCode.LeftControl:
                case KeyCode.RightControl: return 0x11; // VK_CONTROL
                case KeyCode.LeftShift:
                case KeyCode.RightShift: return 0x10;   // VK_SHIFT
                case KeyCode.LeftAlt:
                case KeyCode.RightAlt: return 0x12;     // VK_MENU (Alt)

                default: return 0;
            }
        }

        // =========================================================================
        // Edge-Triggered Key Detection
        // =========================================================================

        /// <summary>
        /// Checks if a physical Windows Virtual Key was just pressed down this frame (edge-triggered).
        /// Returns true only on the initial transition from unpressed to pressed.
        /// </summary>
        public static bool IsVirtualKeyJustPressed(int vKey)
        {
            if (vKey == 0) return false;

            if (!IsGameWindowFocused())
            {
                _keysHeld.Remove(vKey);
                return false;
            }

            bool isPhysDown = (GetAsyncKeyState(vKey) & 0x8000) != 0;
            if (isPhysDown)
            {
                if (!_keysHeld.Contains(vKey))
                {
                    _keysHeld.Add(vKey);
                    return true;
                }
            }
            else
            {
                _keysHeld.Remove(vKey);
            }

            return false;
        }

        /// <summary>
        /// Checks whether a key is currently held down (continuous state).
        /// </summary>
        public static bool IsKeyHeld(KeyCode key)
        {
            if (!IsGameWindowFocused()) return false;

            int vKey = KeyCodeToVirtualKey(key);
            if (vKey != 0 && (GetAsyncKeyState(vKey) & 0x8000) != 0)
            {
                return true;
            }

            try
            {
                return Input.GetKey(key);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Checks whether a modifier key (Control, Shift, Alt) is currently held down.
        /// </summary>
        public static bool IsModifierHeld(KeyCode modifier)
        {
            return IsKeyHeld(modifier);
        }

        /// <summary>
        /// Primary edge-triggered hotkey check.
        /// Checks Win32 hardware state first, falling back to UnityEngine.Input.GetKeyDown.
        /// Emits a structured BepInEx log line whenever a hotkey triggers.
        /// </summary>
        public static bool IsKeyDown(KeyCode key, ManualLogSource log = null, string actionName = null)
        {
            int vKey = KeyCodeToVirtualKey(key);
            bool win32Down = false;

            if (vKey != 0)
            {
                win32Down = IsVirtualKeyJustPressed(vKey);
            }

            bool unityDown = false;
            try
            {
                unityDown = Input.GetKeyDown(key);
            }
            catch
            {
                // Ignore Unity input exceptions if Legacy Input is turned off
            }

            bool triggered = win32Down || unityDown;
            if (!triggered) return false;

            // While a menu is capturing a key for rebinding, swallow all hotkeys. The edge
            // state above is still updated, so the captured key won't fire once released.
            if (Time.realtimeSinceStartup < _suppressUntil) return false;

            // Win32 state is real-time while Unity's Input is sampled once per frame, so a
            // single press can be reported by Win32 in frame N and by Unity in frame N+1.
            // Without this debounce, toggles (open/close menu) would fire twice and cancel out.
            float now = Time.realtimeSinceStartup;
            float last;
            if (_lastTriggerTime.TryGetValue(key, out last) && (now - last) < DebounceSeconds)
            {
                return false;
            }
            _lastTriggerTime[key] = now;

            if (log != null)
            {
                string desc = string.IsNullOrEmpty(actionName) ? key.ToString() : actionName + " [" + key + "]";
                log.LogInfo("[HotkeyHelper] Hotkey pressed: " + desc + " (Win32=" + win32Down + ", Unity=" + unityDown + ")");
            }

            return triggered;
        }

        /// <summary>
        /// Edge-triggered check for key combos requiring a modifier (e.g. Ctrl + R).
        /// </summary>
        public static bool IsComboDown(KeyCode key, KeyCode modifier, ManualLogSource log = null, string actionName = null)
        {
            if (!IsModifierHeld(modifier)) return false;
            return IsKeyDown(key, log, actionName);
        }
    }
}
