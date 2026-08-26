using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace DeskPond
{
    /// <summary>
    /// Counts how busy the user is at their desk: every mouse click and key press bumps a counter,
    /// which is what drives fish toward the hook.
    ///
    /// DELIBERATE DESIGN CONSTRAINT. This is a counter, not a key logger:
    /// the low-level keyboard hook reads only <c>wParam</c> (the message id, i.e. "a key went down"),
    /// and never dereferences <c>lParam</c>, which is where the KBDLLHOOKSTRUCT with the actual
    /// virtual key code lives. No key identity is read, buffered, stored or transmitted; it is
    /// never available to this process. Keep it that way if you extend this file.
    ///
    /// System-wide hooks only run in a Windows player build. In the editor (and on other platforms)
    /// this falls back to Unity's own input, which only counts while the game window has focus.
    /// </summary>
    public class GlobalInputCounter : MonoBehaviour
    {
        const int WH_KEYBOARD_LL = 13;
        const int WH_MOUSE_LL = 14;

        const int WM_KEYDOWN = 0x0100;
        const int WM_SYSKEYDOWN = 0x0104;
        const int WM_LBUTTONDOWN = 0x0201;
        const int WM_RBUTTONDOWN = 0x0204;
        const int WM_MBUTTONDOWN = 0x0207;

        // Ignores events closer together than this, so holding a key down cannot farm progress.
        const int MinEventIntervalMs = 30;

        delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        static GlobalInputCounter _instance;

        // Held in static fields so the GC cannot collect the delegates while Windows holds pointers.
        static HookProc _keyboardProc;
        static HookProc _mouseProc;
        static IntPtr _keyboardHook = IntPtr.Zero;
        static IntPtr _mouseHook = IntPtr.Zero;

        static int _pending;
        static int _lastKeyTick;
        static int _lastMouseTick;

        long _total;
        float _rate;
        int _framePending;

        public long Total => _total;

        /// <summary>Smoothed events per second: how busy the user is right now.</summary>
        public float Rate => _rate;

        /// <summary>
        /// True when system-wide hooks are active, false when on the focused-window fallback.
        /// </summary>
        public bool IsGlobal { get; private set; }

        public static GlobalInputCounter Create()
        {
            if (_instance != null)
                return _instance;

            var go = new GameObject("GlobalInputCounter");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<GlobalInputCounter>();
            return _instance;
        }

        void Awake()
        {
            _instance = this;
            TryInstallHooks();
        }

        void Update()
        {
            // Exponential smoothing so the HUD rate does not flicker.
            var instant = Time.deltaTime > 0f ? _framePending / Time.deltaTime : 0f;
            _rate = Mathf.Lerp(_rate, instant, 1f - Mathf.Exp(-Time.deltaTime * 3f));
            _framePending = 0;
        }

        /// <summary>
        /// Events counted since the last call. GameSession calls this once per frame to fold new
        /// events into catch progress; it is the only consumer of the pending counter.
        /// </summary>
        public int TakeFrameEvents()
        {
            if (!IsGlobal)
                PollLocalInput();

            var taken = System.Threading.Interlocked.Exchange(ref _pending, 0);
            _total += taken;
            _framePending += taken;
            return taken;
        }

        void PollLocalInput()
        {
            var hit = 0;
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null)
            {
                if (mouse.leftButton.wasPressedThisFrame) hit++;
                if (mouse.rightButton.wasPressedThisFrame) hit++;
                if (mouse.middleButton.wasPressedThisFrame) hit++;
            }

            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.anyKey.wasPressedThisFrame)
                hit++;

            if (hit > 0)
                System.Threading.Interlocked.Add(ref _pending, hit);
        }

        void TryInstallHooks()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            try
            {
                _keyboardProc = KeyboardCallback;
                _mouseProc = MouseCallback;
                var module = GetModuleHandle(null);
                _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, module, 0);
                _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, module, 0);
                IsGlobal = _keyboardHook != IntPtr.Zero && _mouseHook != IntPtr.Zero;
                if (!IsGlobal)
                    Debug.LogWarning("DeskPond: could not install global input hooks; " +
                                     "counting focused-window input only.");
            }
            catch (Exception ex)
            {
                IsGlobal = false;
                Debug.LogWarning("DeskPond: global input hooks unavailable. " + ex.Message);
            }
#else
            IsGlobal = false;
#endif
        }

        static IntPtr KeyboardCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            // lParam is never dereferenced; see the class comment. Only the message id is read.
            if (nCode >= 0)
            {
                var msg = wParam.ToInt32();
                if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                    Bump(ref _lastKeyTick);
            }

            return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        }

        static IntPtr MouseCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var msg = wParam.ToInt32();
                if (msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN || msg == WM_MBUTTONDOWN)
                    Bump(ref _lastMouseTick);
            }

            return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        static void Bump(ref int lastTick)
        {
            var now = Environment.TickCount;
            if (now - lastTick < MinEventIntervalMs)
                return;
            lastTick = now;
            System.Threading.Interlocked.Increment(ref _pending);
        }

        void OnDestroy() => RemoveHooks();

        void OnApplicationQuit() => RemoveHooks();

        static void RemoveHooks()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (_keyboardHook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_keyboardHook);
                _keyboardHook = IntPtr.Zero;
            }

            if (_mouseHook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_mouseHook);
                _mouseHook = IntPtr.Zero;
            }
#endif
            _keyboardProc = null;
            _mouseProc = null;
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern IntPtr GetModuleHandle(string lpModuleName);
#else
        static IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam) => IntPtr.Zero;
#endif
    }
}
