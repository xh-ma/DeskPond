using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace DeskPond
{
    /// <summary>
    /// Turns the player window into a fullscreen, always-on-top, transparent desktop overlay.
    ///
    /// The window covers the whole screen but is click-through everywhere except over the pet,
    /// so the desktop underneath stays usable. Dragging moves the pet, never the window itself,
    /// which avoids the jitter of moving an OS window every frame.
    ///
    /// Windows player only. In the editor this does nothing, so transparency and click-through
    /// can only be tested in a build.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class DesktopWindow : MonoBehaviour
    {
        const int GWL_STYLE = -16;
        const int GWL_EXSTYLE = -20;

        const uint WS_POPUP = 0x80000000;
        const uint WS_VISIBLE = 0x10000000;

        const int WS_EX_LAYERED = 0x00080000;
        const int WS_EX_TRANSPARENT = 0x00000020;
        const int WS_EX_TOPMOST = 0x00000008;

        // WS_EX_APPWINDOW forces a taskbar button: a borderless WS_POPUP window gets none by
        // default, which previously left no way to close the pet short of Task Manager.
        // Right-clicking that button and choosing Close sends WM_CLOSE, so Unity shuts down
        // cleanly and the save is written.
        const int WS_EX_APPWINDOW = 0x00040000;

        const int BaseExStyle = WS_EX_LAYERED | WS_EX_APPWINDOW | WS_EX_TOPMOST;

        const uint SWP_NOSIZE = 0x0001;
        const uint SWP_NOMOVE = 0x0002;
        const uint SWP_NOACTIVATE = 0x0010;
        const uint SWP_SHOWWINDOW = 0x0040;

        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        [SerializeField] Camera worldCamera;

        [Header("Layout")]
        [Tooltip("Sprite pixels per world unit. Must match the sprites' import setting.")]
        [SerializeField] float pixelsPerUnit = 16f;

        [Tooltip("On-screen magnification. 2 = each sprite pixel covers 2x2 screen pixels.")]
        [SerializeField] float zoom = 3f;

        [Tooltip("World Y of the water surface the pet sits on.")]
        [SerializeField] float waterlineWorldY = -5.04f;

        [Tooltip("Used when the real taskbar height cannot be read from the OS.")]
        [SerializeField] int fallbackTaskbarPx = 48;

        [Header("Water (pushed from GameConfig)")]
        [SerializeField] Color waterColor = new Color(0.45f, 0.72f, 0.95f);
        [SerializeField] float waterRimThickness = 0.08f;

        /// <summary>Applied by GameSession before this component's Start runs.</summary>
        public void ApplyConfig(GameConfig config)
        {
            if (config == null)
                return;
            zoom = config.petZoom;
            waterColor = config.waterColor;
            waterRimThickness = config.waterRimThickness;

            // A desktop pet sits on screen all day, so it has no business rendering flat out.
            // QualitySettings ships with vSync off, which leaves the frame rate uncapped unless
            // something sets this.
            Application.targetFrameRate = Mathf.Clamp(config.targetFrameRate, 10, 144);
        }

        /// <summary>
        /// Raised after the overlay has re-laid itself out for a new screen, so anything holding
        /// screen-derived positions can fix them up.
        /// </summary>
        public event System.Action DisplayChanged;

        // Everything here is derived from the screen, so a change to any of it invalidates the lot.
        int _lastScreenW;
        int _lastScreenH;
        int _lastTaskbarPx;
        float _displayPoll;

        // Apply() needs a window handle, and GetActiveWindow returns nothing until the window is
        // actually up. Launched without focus it can miss, which silently leaves the game as an
        // ordinary decorated window instead of the overlay, so it is retried for a short while.
        float _applyRetry;
        int _applyAttempts;

        bool _suppressOverlay;
        IntPtr _hwnd = IntPtr.Zero;
        bool _clickThrough;
        bool _active;

        public int TaskbarHeightPx { get; private set; }

        /// <summary>Set by the pet each frame: true when the cursor is over something interactive.</summary>
        public bool PointerOverPet { get; set; }

        /// <summary>
        /// Returns true when the given screen point should be interactive. Supplied by
        /// GameSession and evaluated against the OS cursor, so it works while the window is
        /// click-through or unfocused. Unity's own mouse state does not.
        /// </summary>
        public System.Func<Vector2, bool> HitTest { get; set; }

        /// <summary>OS cursor position in Unity screen coordinates (origin bottom-left).</summary>
        public Vector2 CursorScreen { get; private set; }

        public bool CursorValid { get; private set; }

        /// <summary>
        /// Physical left-button state read from the OS. The overlay never takes focus, and Unity
        /// does not deliver mouse input to an unfocused window, so button state cannot come from
        /// the Input System here.
        /// </summary>
        public bool PrimaryDown { get; private set; }

        public bool PrimaryPressedThisFrame { get; private set; }

        bool _prevPrimary;

        public bool IsActive => _active;

        public static DesktopWindow Create()
        {
            var existing = FindFirstObjectByType<DesktopWindow>();
            if (existing != null)
                return existing;

            var go = new GameObject("DesktopWindow");
            DontDestroyOnLoad(go);
            return go.AddComponent<DesktopWindow>();
        }

        void Start()
        {
            if (worldCamera == null)
                worldCamera = Camera.main;

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            // -nooverlay runs as a normal opaque window, to separate rendering problems from
            // transparency problems when diagnosing.
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-nooverlay") >= 0)
            {
                _active = false;
                _suppressOverlay = true;
                Debug.Log("DeskPond: -nooverlay, running as a normal window.");
                LayoutCamera();
            }
            else
            {
                Apply();
            }
#else
            _active = false;
#endif
            if (worldCamera != null)
            {
                worldCamera.clearFlags = CameraClearFlags.SolidColor;
                var bg = worldCamera.backgroundColor;
                // Fully transparent black; the compositor shows the desktop through it.
                worldCamera.backgroundColor = _active ? new Color(0f, 0f, 0f, 0f) : bg;
            }

            if (_active)
                LayoutCamera();
        }

        /// <summary>
        /// The water is a single stripe spanning the screen and resting on the taskbar, not a
        /// wash of colour over it. The scene's full-screen "Pond" backdrop is hidden in overlay
        /// mode; it would paint an opaque rectangle across the desktop.
        /// </summary>
        void LayoutWater()
        {
            if (worldCamera == null)
                return;

            var pond = GameObject.Find("Pond");
            if (pond != null)
            {
                var backdrop = pond.GetComponent<SpriteRenderer>();
                if (backdrop != null)
                    backdrop.enabled = false;
            }

            var halfHeight = worldCamera.orthographicSize;
            // A little overscan so no seam shows at the screen edges.
            var viewWidth = halfHeight * worldCamera.aspect * 2f * 1.04f;
            var camX = worldCamera.transform.position.x;

            // Sits just above the waterline so the stripe rests on the taskbar's top edge.
            Fit("TaskbarRim", viewWidth, waterRimThickness, camX,
                waterlineWorldY + waterRimThickness * 0.5f, waterColor);
        }

        /// <summary>
        /// Scales a quad to an exact world size, whatever its pixels-per-unit.
        /// </summary>
        static void Fit(string objectName, float worldWidth, float worldHeight, float x, float y, Color? tint)
        {
            var go = GameObject.Find(objectName);
            if (go == null)
                return;

            var sr = go.GetComponent<SpriteRenderer>();
            if (sr == null || sr.sprite == null)
                return;

            sr.enabled = true;
            if (tint.HasValue)
                sr.color = tint.Value;

            var unit = sr.sprite.bounds.size;
            if (unit.x <= 0f || unit.y <= 0f)
                return;

            go.transform.localScale = new Vector3(worldWidth / unit.x, worldHeight / unit.y, 1f);
            go.transform.position = new Vector3(x, y, go.transform.position.z);
        }

        /// <summary>
        /// Scales and positions the camera so one sprite pixel covers <see cref="zoom"/> screen
        /// pixels, and the waterline lands exactly on the top edge of the real taskbar.
        /// </summary>
        void LayoutCamera()
        {
            if (worldCamera == null || !worldCamera.orthographic)
                return;

            // PixelPerfectCamera enforces its own reference resolution and would fight this.
            var ppc = worldCamera.GetComponent<UnityEngine.Rendering.Universal.PixelPerfectCamera>();
            if (ppc != null)
                ppc.enabled = false;

            var unitsPerPixel = 1f / (pixelsPerUnit * Mathf.Max(0.01f, zoom));
            worldCamera.orthographicSize = Screen.height * unitsPerPixel * 0.5f;

            TaskbarHeightPx = ReadTaskbarHeight();
            var taskbarWorld = TaskbarHeightPx * unitsPerPixel;

            var worldYAtScreenBottom = waterlineWorldY - taskbarWorld;
            var pos = worldCamera.transform.position;
            pos.y = worldYAtScreenBottom + worldCamera.orthographicSize;
            worldCamera.transform.position = pos;

            Debug.Log($"DeskPond layout: screen={Screen.width}x{Screen.height} taskbar={TaskbarHeightPx}px " +
                      $"ortho={worldCamera.orthographicSize:0.00} camY={pos.y:0.00} zoom={zoom}");

            LayoutWater();
            LogSpriteDiagnostics();
        }

        /// <summary>
        /// Reports where each sprite lands on screen, to tell layout bugs from render bugs.
        /// </summary>
        void LogSpriteDiagnostics()
        {
            foreach (var sr in FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
            {
                var screen = worldCamera.WorldToScreenPoint(sr.transform.position);
                var spriteName = sr.sprite != null ? sr.sprite.name : "NULL";
                Debug.Log($"DeskPond sprite: {sr.gameObject.name} enabled={sr.enabled} " +
                          $"world={sr.transform.position} screen=({screen.x:0},{screen.y:0}) " +
                          $"order={sr.sortingOrder} colorA={sr.color.a:0.00} sprite={spriteName}");
            }
        }

        int ReadTaskbarHeight()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            try
            {
                // Work area excludes the taskbar, so the difference is its height.
                if (SystemParametersInfo(SPI_GETWORKAREA, 0, out var work, 0))
                {
                    var height = Screen.currentResolution.height - (work.Bottom - work.Top);
                    if (height > 0 && height < 200)
                        return height;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("DeskPond: could not read work area. " + ex.Message);
            }
#endif
            return fallbackTaskbarPx;
        }

        void Update()
        {
            // The hover test uses the OS cursor, not Unity's mouse. While the window is
            // click-through, mouse messages go to whatever is underneath, so Unity's pointer state
            // freezes. Driving click-through from it deadlocks: the window stays transparent, so
            // Unity never sees the cursor arrive, so it never becomes clickable.
            TickApplyRetry(Time.unscaledDeltaTime);

            CursorValid = TryGetCursor(out var cursor);
            if (CursorValid)
                CursorScreen = cursor;

            var down = ReadPrimaryDown();
            PrimaryPressedThisFrame = down && !_prevPrimary;
            _prevPrimary = down;
            PrimaryDown = down;

            if (!_active)
            {
                PointerOverPet = false;
                return;
            }

            var over = PointerOverPet;
            if (CursorValid && HitTest != null)
                over |= HitTest(CursorScreen);

            SetClickThrough(!over);
            PointerOverPet = false;

            TickDisplayWatch(Time.unscaledDeltaTime);
        }

        /// <summary>
        /// The screen can change under a running overlay: a monitor is plugged in or unplugged,
        /// the resolution or scaling changes, or the taskbar is resized or moved to another edge.
        /// The camera, the waterline and the window size are all measured from the screen, so they
        /// are recomputed rather than trusted from startup.
        ///
        /// Polled on a timer instead of every frame because reading the work area is a system call.
        /// </summary>
        void TickDisplayWatch(float dt)
        {
            _displayPoll -= dt;
            if (_displayPoll > 0f)
                return;
            _displayPoll = 0.5f;

            var taskbar = ReadTaskbarHeight();
            if (Screen.width == _lastScreenW && Screen.height == _lastScreenH
                && taskbar == _lastTaskbarPx)
                return;

            var first = _lastScreenW == 0;
            _lastScreenW = Screen.width;
            _lastScreenH = Screen.height;
            _lastTaskbarPx = taskbar;

            // Start() already sized and laid out; only react to later changes.
            if (first)
                return;

            Debug.Log($"DeskPond display changed: {Screen.width}x{Screen.height} taskbar={taskbar}px");
            ResizeToScreen();
            LayoutCamera();
            DisplayChanged?.Invoke();
        }

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

            // GetCursorPos is in desktop coordinates; Unity's screen space is the window's client
            // area, bottom-left origin. Converting through the client rect rather than assuming the
            // two are the same rectangle is what keeps clicks landing on what they look like they
            // are landing on: the moment the window is inset, decorated, or sized to anything other
            // than the whole desktop, the difference becomes a constant offset on every hit test.
            var local = p;
            if (_hwnd != IntPtr.Zero && ScreenToClient(_hwnd, ref local)
                && GetClientRect(_hwnd, out var client))
            {
                var w = client.Right - client.Left;
                var h = client.Bottom - client.Top;
                if (w > 0 && h > 0)
                {
                    // Scale rather than assume the two rectangles are the same size. They usually
                    // are, and then this is the identity; when they are not, a fixed subtraction
                    // would leave clicks landing a little off wherever the pointer is.
                    screen = new Vector2(local.X * (float)Screen.width / w,
                        (h - local.Y) * (float)Screen.height / h);
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

        /// <summary>
        /// Apply needs a window handle, and GetActiveWindow has nothing to give until the window
        /// is really up. Launched without focus it can miss, which would silently leave the game
        /// as an ordinary decorated window instead of the overlay, so it is retried briefly.
        /// </summary>
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
                LayoutCamera();
            }
#endif
        }

        /// <summary>Stretches the overlay across the whole primary display.</summary>
        void ResizeToScreen()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (_hwnd == IntPtr.Zero)
                return;
            var res = Screen.currentResolution;
            SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, res.width, res.height,
                SWP_NOACTIVATE | SWP_SHOWWINDOW);
#endif
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
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
                ResizeToScreen();

                // Unity sized its surface for the decorated window it created, then the lines
                // above stripped the decoration and stretched it, leaving Screen reporting the
                // old smaller rectangle. Asking it to resync does not always take, so this is best
                // effort: TryGetCursor scales between the two spaces either way.
                if (GetClientRect(_hwnd, out var client))
                {
                    var w = client.Right - client.Left;
                    var h = client.Bottom - client.Top;
                    if (w > 0 && h > 0 && (w != Screen.width || h != Screen.height))
                        Screen.SetResolution(w, h, FullScreenMode.Windowed);
                }

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

        /// <summary>Re-asserts topmost; some shells demote overlays after a fullscreen app appears.</summary>
        public void PushToFront()
        {
            if (_hwnd != IntPtr.Zero)
                SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct Margins
        {
            public int Left;
            public int Right;
            public int Top;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct RectNative
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct PointNative
        {
            public int X;
            public int Y;
        }

        const uint SPI_GETWORKAREA = 0x0030;

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool SystemParametersInfo(uint action, uint param, out RectNative result, uint winIni);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetCursorPos(out PointNative point);

        [DllImport("user32.dll")]
        static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        static extern IntPtr GetActiveWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetClientRect(IntPtr hwnd, out RECT rect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool ScreenToClient(IntPtr hwnd, ref PointNative point);

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")]
        static extern int SetWindowLong(IntPtr hwnd, int index, uint newStyle);

        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hwnd, IntPtr hwndAfter,
            int x, int y, int cx, int cy, uint flags);

        [DllImport("Dwmapi.dll")]
        static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
#else
        void SetClickThrough(bool value) => _clickThrough = value;

        public void PushToFront()
        {
        }
#endif
    }
}
