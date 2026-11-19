using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace DeskPond
{
    /// <summary>
    /// Turns the player window into a transparent, always-on-top, click-through desktop overlay,
    /// and keeps it cheap.
    ///
    /// LAYOUT
    /// The window is a thin band along the bottom of the primary screen, sitting on the pond's
    /// floor: on top of a bottom taskbar, or on the screen edge when the taskbar is at the top,
    /// at a side, or auto-hidden (see <see cref="TaskbarInfo"/>). It only grows to the full work
    /// area while a panel is open. A band a few hundred pixels tall costs a fraction of what a
    /// full-screen transparent window does to render and composite.
    ///
    /// World space: one unit is 16 art pixels, x = 0 is the window's left edge and y = 0 is the
    /// floor (the window's bottom edge). The camera is fitted so one art pixel is exactly
    /// <see cref="Zoom"/> screen pixels.
    ///
    /// COST
    /// - 12 fps while nothing is happening, 30 while the cursor is on the pet, a panel is open or
    ///   something is animating fast (callers set <see cref="WantFast"/> each frame).
    /// - While a fullscreen game or video is in front, the overlay hides itself, stops rendering
    ///   and drops to 2 fps. It would otherwise sit on top of the game.
    ///
    /// Input never comes from Unity here: the overlay never takes focus and is click-through most
    /// of the time, so cursor and button state are read from the OS.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class DesktopWindow : MonoBehaviour
    {
        /// <summary>
        /// Fixed window height in art pixels: room for the deepest pond plus the pet, and for the
        /// panel. Fixed on purpose: resizing a Unity window rebuilds its surface and flashes.
        /// </summary>
        public const int WindowArtH = 212;

        /// <summary>Window width around the pond when it is not full width, in art pixels.</summary>
        const int LocalArtW = 320;

        // Input and logic tick at 30 so hovering and clicking feel immediate, 60 while dragging so
        // the pet keeps up with the cursor, but an idle pet only draws every third tick (10 fps).
        const int TickFps = 30;
        const int DragFps = 60;
        const int IdleRenderInterval = 3;
        const int HiddenFps = 2;

        bool _fullPond;
        float _focusPx = -1f;

        [SerializeField] Camera worldCamera;

        GameConfig _config;
        int _sizeStep;
        int _zoom = 3;

        public static DesktopWindow Instance { get; private set; }

        public event Action DisplayChanged;

        /// <summary>Screen pixels per art pixel.</summary>
        public int Zoom => _zoom;

        public TaskbarInfo Taskbar { get; private set; }

        /// <summary>Set every frame by anything animating fast or under the cursor: draw every tick.</summary>
        public bool WantFast { get; set; }

        /// <summary>Set every frame while something follows the cursor (dragging): tick at 60.</summary>
        public bool WantSmooth { get; set; }

        /// <summary>WantFast with a name, so -perflog can say who keeps the frame rate up.</summary>
        public void Fast(string who)
        {
            WantFast = true;
            if (_perfLog)
                _fastBy = who;
        }

        static readonly bool _perfLog = Array.IndexOf(Environment.GetCommandLineArgs(), "-perflog") >= 0;
        string _fastBy;
        float _perfClock;
        int _perfTicks, _perfRenders;
        readonly System.Collections.Generic.Dictionary<string, int> _perfWho =
            new System.Collections.Generic.Dictionary<string, int>();

        /// <summary>True while a fullscreen app is in front and the pet is tucked away.</summary>
        public bool HiddenForFullscreen { get; private set; }


        /// <summary>Set by the pet each frame: true when the cursor is over something interactive.</summary>
        public bool PointerOverPet { get; set; }

        /// <summary>Hit test against the OS cursor, supplied by GameSession.</summary>
        public Func<Vector2, bool> HitTest { get; set; }

        /// <summary>OS cursor in Unity screen coordinates (origin bottom-left of the window).</summary>
        public Vector2 CursorScreen { get; private set; }
        public bool CursorValid { get; private set; }
        public bool PrimaryDown { get; private set; }
        public bool PrimaryPressedThisFrame { get; private set; }

        public bool IsActive => _active;

        /// <summary>
        /// Width of the whole floor the pet can use, in world units. World x = 0 is the left end
        /// of that floor (the screen edge, or a left taskbar's edge), whatever part of it the
        /// window currently covers.
        /// </summary>
        public float WorldWidth => (_active ? Taskbar.Right - Taskbar.Left : Screen.width) / (Atlas.Ppu * _zoom);

        bool _prevPrimary;
        bool _active;
        bool _suppressOverlay;
        bool _clickThrough;
        IntPtr _hwnd = IntPtr.Zero;
        RectInt _windowRect;
        int _lastScreenW, _lastScreenH;
        float _layoutPoll;
        float _fullscreenPoll;
        int _fullscreenStreak;
        float _applyRetry;
        int _applyAttempts;
        int _repositionFrames;
        int _appliedFps = -1;
        bool _hideWhenFullscreen = true;

        public static DesktopWindow Create()
        {
            if (Instance != null)
                return Instance;
            var existing = FindFirstObjectByType<DesktopWindow>();
            if (existing != null)
                return existing;
            var go = new GameObject("DesktopWindow");
            DontDestroyOnLoad(go);
            return go.AddComponent<DesktopWindow>();
        }

        void Awake()
        {
            Instance = this;
            // One worker is plenty; the default spawns one per core, each with its own allocators.
            Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobWorkerCount = 1;
            QualitySettings.vSyncCount = 0;
            QualitySettings.antiAliasing = 0;
            QualitySettings.shadows = ShadowQuality.Disable;
            ApplyFps(TickFps);
        }

        public void ApplyConfig(GameConfig config) => _config = config;

        /// <summary>Settings: bigger or smaller pet, in whole steps of screen pixels per art pixel.</summary>
        public void SetSizeStep(int step)
        {
            _sizeStep = Mathf.Clamp(step, -2, 3);
            Relayout(true);
        }

        public void SetHideInFullscreen(bool on) => _hideWhenFullscreen = on;

        /// <summary>The deepest pond that still leaves room for the pet in the fixed window.</summary>
        public int MaxPondDepth => Mathf.Clamp(Mathf.Min(WindowArtH - 52,
            Mathf.RoundToInt(Taskbar.Screen.height / (float)Mathf.Max(1, _zoom) * 0.45f)), 20, 160);

        /// <summary>Full-width pond: the window spans the floor. Otherwise it is a box round the pet.</summary>
        public void SetPondMode(bool full)
        {
            if (full == _fullPond)
                return;
            _fullPond = full;
            Relayout(false);
        }

        /// <summary>
        /// Keeps the pet inside the window. Moving a window (no resize) is cheap and does not
        /// flash, so the box simply slides along when the pet nears an edge.
        /// </summary>
        public void Follow(float worldX, bool recentre)
        {
            _focusPx = Taskbar.Left + worldX * Atlas.Ppu * _zoom;
            if (!_active || _fullPond || _windowRect.width <= 0)
                return;
            var margin = _windowRect.width * 0.28f;
            var inside = _focusPx > _windowRect.x + margin && _focusPx < _windowRect.xMax - margin;
            if (inside && !recentre)
                return;
            var x = Mathf.Clamp(Mathf.RoundToInt(_focusPx - _windowRect.width * 0.5f), Taskbar.Left,
                Mathf.Max(Taskbar.Left, Taskbar.Right - _windowRect.width));
            if (x == _windowRect.x)
                return;
            _windowRect.x = x;
            MoveWindow();
            LayoutCamera();
        }

        void Start()
        {
            if (worldCamera == null)
                worldCamera = Camera.main;

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-nooverlay") >= 0)
            {
                _suppressOverlay = true;
                Debug.Log("DeskPond: -nooverlay, running as a normal window.");
            }
            else
            {
                Apply();
            }
#endif
            if (worldCamera != null)
            {
                worldCamera.clearFlags = CameraClearFlags.SolidColor;
                worldCamera.backgroundColor = _active ? new Color(0f, 0f, 0f, 0f) : new Color(0.16f, 0.18f, 0.26f, 1f);
                worldCamera.orthographic = true;
                worldCamera.allowHDR = false;
                worldCamera.allowMSAA = false;
                worldCamera.useOcclusionCulling = false;
            }

            Relayout(true);
        }

        /// <summary>
        /// Auto zoom from the screen height, so the pet is the same physical size on a laptop and
        /// on a 4K monitor: 1080p is 3, 1440p is 4, 2160p is 6.
        /// </summary>
        int AutoZoom(int screenHeight)
        {
            var configured = _config != null ? Mathf.RoundToInt(_config.petZoom) : 0;
            var baseZoom = Mathf.Max(2, Mathf.RoundToInt(screenHeight / 360f));
            if (configured > 0 && Mathf.Abs(screenHeight - 1080) < 1)
                baseZoom = configured;
            return Mathf.Clamp(baseZoom + _sizeStep, 1, 10);
        }

        /// <summary>Reads the taskbar, sizes and places the window, then fits the camera.</summary>
        void Relayout(bool force)
        {
            var info = TaskbarInfo.Read(Screen.currentResolution.width, Screen.currentResolution.height);
            var zoom = AutoZoom(info.Screen.height);
            var changed = force || !info.SameLayout(Taskbar) || zoom != _zoom;
            Taskbar = info;
            _zoom = zoom;

            if (_active)
            {
                var span = info.Right - info.Left;
                var height = Mathf.Min(WindowArtH * _zoom, info.FloorY - Mathf.Max(info.Work.yMin, info.Screen.yMin));
                var width = _fullPond ? span : Mathf.Min(span, LocalArtW * _zoom);
                var focus = _focusPx >= 0f ? _focusPx : info.Left + span * 0.6f;
                var x = _fullPond ? info.Left
                    : Mathf.Clamp(Mathf.RoundToInt(focus - width * 0.5f), info.Left, info.Right - width);
                var rect = new RectInt(x, info.FloorY - height, width, height);
                if (force || !rect.Equals(_windowRect))
                {
                    var resized = force || rect.width != _windowRect.width || rect.height != _windowRect.height;
                    _windowRect = rect;
                    if (resized)
                        PlaceWindow();
                    else
                        MoveWindow();
                    changed = true;
                }
            }

            if (changed)
            {
                Debug.Log($"DeskPond layout: {info} zoom={_zoom} window=({_windowRect.x},{_windowRect.y}," +
                          $"{_windowRect.width}x{_windowRect.height}) fullPond={_fullPond}");
                LayoutCamera();
                DisplayChanged?.Invoke();
            }
        }

        /// <summary>Fits the camera so the window's bottom-left is world (0, 0).</summary>
        void LayoutCamera()
        {
            if (worldCamera == null)
                return;
            // The scene camera once carried a letterboxed viewport from a pixel-perfect component;
            // anything but the full window squashes the art.
            worldCamera.rect = new Rect(0f, 0f, 1f, 1f);
            var upp = 1f / (Atlas.Ppu * _zoom);
            worldCamera.orthographicSize = Screen.height * upp * 0.5f;
            var p = worldCamera.transform.position;
            // The world is anchored to the desktop floor, so moving the window just moves the camera.
            var offset = _active ? (_windowRect.x - Taskbar.Left) * upp : 0f;
            worldCamera.transform.position = new Vector3(offset + Screen.width * upp * 0.5f,
                Screen.height * upp * 0.5f, p.z);
            _lastScreenW = Screen.width;
            _lastScreenH = Screen.height;
        }

        void Update()
        {
            var dt = Time.unscaledDeltaTime;
            TickApplyRetry(dt);

            CursorValid = TryGetCursor(out var cursor);
            if (CursorValid)
                CursorScreen = cursor;

            var down = ReadPrimaryDown();
            // The button is read globally, so a click meant for another app would otherwise start a
            // drag here. Only count it if the overlay was actually taking clicks when it happened
            // (click-through off), and never in snapshot runs.
            PrimaryPressedThisFrame = down && !_prevPrimary && (!_active || !_clickThrough) && !SnapshotMode.Active;
            _prevPrimary = down;
            PrimaryDown = down;

            // Unity resizes its surface a frame or two after the window changes; refit then.
            if (Screen.width != _lastScreenW || Screen.height != _lastScreenH)
            {
                LayoutCamera();
                DisplayChanged?.Invoke();
            }

            if (_repositionFrames > 0)
            {
                _repositionFrames--;
                MoveWindow();
            }

            if (_active)
            {
                var over = PointerOverPet;
                if (CursorValid && HitTest != null && !HiddenForFullscreen)
                    over |= HitTest(CursorScreen);
                SetClickThrough(!over);

                _layoutPoll -= dt;
                if (_layoutPoll <= 0f)
                {
                    // A system call, so polled rather than run every frame.
                    _layoutPoll = 1f;
                    Relayout(false);
                }

                _fullscreenPoll -= dt;
                if (_fullscreenPoll <= 0f)
                {
                    _fullscreenPoll = 1.5f;
                    UpdateFullscreenHide();
                }
            }

            // Logic and input tick at 30 (60 while dragging); drawing drops to every third tick
            // when nothing is happening. Skipped ticks neither render nor present.
            var fast = WantFast || PointerOverPet || WantSmooth;
            if (_perfLog)
                TickPerfLog(fast);
            ApplyFps(HiddenForFullscreen ? HiddenFps : WantSmooth ? DragFps : TickFps);
            var interval = HiddenForFullscreen || fast ? 1 : IdleRenderInterval;
            if (UnityEngine.Rendering.OnDemandRendering.renderFrameInterval != interval)
                UnityEngine.Rendering.OnDemandRendering.renderFrameInterval = interval;

            PointerOverPet = false;
            WantFast = false;
            WantSmooth = false;
        }

        void TickPerfLog(bool fast)
        {
            _perfTicks++;
            if (fast || _perfTicks % IdleRenderInterval == 0)
                _perfRenders++;
            if (fast)
            {
                var who = WantSmooth ? "smooth" : PointerOverPet ? "pointer" : _fastBy ?? "?";
                _perfWho.TryGetValue(who, out var n);
                _perfWho[who] = n + 1;
            }
            _fastBy = null;
            _perfClock += Time.unscaledDeltaTime;
            if (_perfClock < 10f)
                return;
            var parts = new System.Text.StringBuilder();
            foreach (var kv in _perfWho)
                parts.Append(kv.Key).Append('=').Append(kv.Value).Append(' ');
            Debug.Log($"DeskPond perf: {_perfTicks / _perfClock:0.0} ticks/s, ~{_perfRenders / _perfClock:0.0} renders/s, fast by: {parts}");
            _perfClock = 0f;
            _perfTicks = 0;
            _perfRenders = 0;
            _perfWho.Clear();
        }

        void ApplyFps(int fps)
        {
            if (fps == _appliedFps)
                return;
            _appliedFps = fps;
            Application.targetFrameRate = fps;
        }

        // ------------------------------------------------------------------ fullscreen apps

        void UpdateFullscreenHide()
        {
            var hide = _hideWhenFullscreen && !SnapshotMode.Active && ForegroundIsFullscreen();
            // Hiding and re-showing a screen-wide layered window makes the whole desktop hitch, so
            // only hide once a fullscreen window has stayed in front for two polls (~3 s). Brief
            // overlays (a screenshot, alt-tab) then never trigger it. Coming back is immediate.
            if (hide && !HiddenForFullscreen && ++_fullscreenStreak < 2)
                return;
            if (!hide)
                _fullscreenStreak = 0;
            if (hide == HiddenForFullscreen)
                return;

            HiddenForFullscreen = hide;
            if (worldCamera != null)
                worldCamera.enabled = !hide;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (_hwnd != IntPtr.Zero)
            {
                ShowWindow(_hwnd, hide ? SW_HIDE : SW_SHOWNOACTIVATE);
                if (!hide)
                    PushToFront();
            }
#endif
            Debug.Log("DeskPond: " + (hide ? "fullscreen app in front, hiding." : "back on the desktop."));
        }

        bool ForegroundIsFullscreen()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            var fg = GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == _hwnd)
                return false;

            var cls = new System.Text.StringBuilder(64);
            GetClassName(fg, cls, cls.Capacity);
            var name = cls.ToString();
            if (name == "Progman" || name == "WorkerW" || name == "Shell_TrayWnd"
                || name == "Shell_SecondaryTrayWnd" || name == "Windows.UI.Core.CoreWindow")
                return false;

            if (!GetWindowRect(fg, out var r))
                return false;
            var s = Taskbar.Screen;
            if (!(r.Left <= s.xMin && r.Top <= s.yMin && r.Right >= s.xMax && r.Bottom >= s.yMax))
                return false;
            return !IsScreenshotTool(fg);
#else
            return false;
#endif
        }

        /// <summary>
        /// Screenshot tools cover the whole screen with a frozen image while you pick an area.
        /// That is not a fullscreen app, and the pet should stay in the shot.
        /// </summary>
        static bool IsScreenshotTool(IntPtr hwnd)
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0)
                return false;
            try
            {
                using (var p = System.Diagnostics.Process.GetProcessById((int)pid))
                {
                    switch (p.ProcessName.ToLowerInvariant())
                    {
                        case "snippingtool": case "screenclippinghost": case "screensketch":
                        case "sharex": case "snipaste": case "lightshot": case "greenshot":
                        case "picpick": case "flameshot": case "pixpin": case "wechat": case "weixin":
                        case "qq": case "feishu": case "lark": case "dingtalk":
                            return true;
                    }
                }
            }
            catch (Exception)
            {
                // The process may have exited between the two calls.
            }
            return false;
#else
            return false;
#endif
        }

        // ------------------------------------------------------------------ input

        static bool ReadPrimaryDown()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            const int VK_LBUTTON = 0x01;
            return (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;
#else
            var mouse = UnityEngine.InputSystem.Mouse.current;
            return mouse != null && mouse.leftButton.isPressed;
#endif
        }

        bool TryGetCursor(out Vector2 screen)
        {
            screen = Vector2.zero;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (!GetCursorPos(out var p))
                return false;

            // Desktop coordinates to the client area, then scaled to Unity's screen space in case
            // the backbuffer has not caught up with a resize yet.
            var local = p;
            if (_hwnd != IntPtr.Zero && ScreenToClient(_hwnd, ref local) && GetClientRect(_hwnd, out var client))
            {
                var w = client.Right - client.Left;
                var h = client.Bottom - client.Top;
                if (w > 0 && h > 0)
                {
                    screen = new Vector2(local.X * (float)Screen.width / w, (h - local.Y) * (float)Screen.height / h);
                    return true;
                }
            }

            screen = new Vector2(p.X, Screen.height - p.Y);
            return true;
#else
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null)
                return false;
            screen = mouse.position.ReadValue();
            return true;
#endif
        }

        // ------------------------------------------------------------------ window

        void TickApplyRetry(float dt)
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (_active || _suppressOverlay || _applyAttempts >= 20)
                return;
            _applyRetry -= dt;
            if (_applyRetry > 0f)
                return;
            _applyRetry = 0.25f;
            _applyAttempts++;
            Apply();
            if (_active)
            {
                Debug.Log($"DeskPond: overlay applied on attempt {_applyAttempts}.");
                if (worldCamera != null)
                    worldCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
                Relayout(true);
            }
#endif
        }

        /// <summary>
        /// Resizes Unity's surface first, then moves the OS window over it. SetResolution may
        /// re-centre the window, so the move is repeated for a few frames until it sticks.
        /// </summary>
        void PlaceWindow()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (_hwnd == IntPtr.Zero || _windowRect.width <= 0 || _windowRect.height <= 0)
                return;
            if (Screen.width != _windowRect.width || Screen.height != _windowRect.height)
                Screen.SetResolution(_windowRect.width, _windowRect.height, FullScreenMode.Windowed);
            MoveWindow();
            _repositionFrames = 4;
#endif
        }

        void MoveWindow()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (_hwnd == IntPtr.Zero)
                return;
            // Screen.SetResolution can put Unity's own window style back; re-assert ours.
            SetWindowLong(_hwnd, GWL_STYLE, WS_POPUP | WS_VISIBLE);
            SetWindowLong(_hwnd, GWL_EXSTYLE, (uint)(_clickThrough ? BaseExStyle | WS_EX_TRANSPARENT : BaseExStyle));
            SetWindowPos(_hwnd, HWND_TOPMOST, _windowRect.x, _windowRect.y, _windowRect.width,
                _windowRect.height, SWP_NOACTIVATE | (HiddenForFullscreen ? 0u : SWP_SHOWWINDOW));
#endif
        }

        public void PushToFront()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (_hwnd != IntPtr.Zero)
                SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
#endif
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        const int GWL_STYLE = -16;
        const int GWL_EXSTYLE = -20;
        const uint WS_POPUP = 0x80000000;
        const uint WS_VISIBLE = 0x10000000;
        const int WS_EX_LAYERED = 0x00080000;
        const int WS_EX_TRANSPARENT = 0x00000020;
        const int WS_EX_TOPMOST = 0x00000008;
        // A borderless popup gets no taskbar button without this, which once left no way to quit.
        const int WS_EX_APPWINDOW = 0x00040000;
        const int BaseExStyle = WS_EX_LAYERED | WS_EX_APPWINDOW | WS_EX_TOPMOST;
        const uint SWP_NOSIZE = 0x0001;
        const uint SWP_NOMOVE = 0x0002;
        const uint SWP_NOACTIVATE = 0x0010;
        const uint SWP_SHOWWINDOW = 0x0040;
        const int SW_HIDE = 0;
        const int SW_SHOWNOACTIVATE = 4;
        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        void Apply()
        {
            try
            {
                _hwnd = GetActiveWindow();
                if (_hwnd == IntPtr.Zero)
                    return;

                var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                SetWindowLong(_hwnd, GWL_STYLE, WS_POPUP | WS_VISIBLE);
                SetWindowLong(_hwnd, GWL_EXSTYLE, BaseExStyle);
                DwmExtendFrameIntoClientArea(_hwnd, ref margins);
                _active = true;
                _clickThrough = false;
            }
            catch (Exception ex)
            {
                _active = false;
                Debug.LogWarning("DeskPond: desktop overlay unavailable. " + ex.Message);
            }
        }

        void SetClickThrough(bool value)
        {
            if (value == _clickThrough || _hwnd == IntPtr.Zero)
                return;
            _clickThrough = value;
            var ex = BaseExStyle;
            if (value)
                ex |= WS_EX_TRANSPARENT;
            SetWindowLong(_hwnd, GWL_EXSTYLE, (uint)ex);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct Margins { public int Left, Right, Top, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        struct PointNative { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetCursorPos(out PointNative point);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")] static extern IntPtr GetActiveWindow();
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder name, int max);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool ScreenToClient(IntPtr hwnd, ref PointNative point);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int index, uint newStyle);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int cmd);
        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("Dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
#else
        void SetClickThrough(bool value) => _clickThrough = value;
#endif
    }
}
