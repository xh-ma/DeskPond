using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace DeskPond
{
    public enum TaskbarEdge
    {
        Left = 0,
        Top = 1,
        Right = 2,
        Bottom = 3,
        None = 4
    }

    /// <summary>
    /// Where the Windows taskbar is and how the pond should sit around it, in desktop pixels with
    /// a top-left origin (the OS convention).
    ///
    /// The pond is always a horizontal strip along the bottom of the primary screen:
    /// - taskbar at the bottom: the pond rests on top of it;
    /// - taskbar at the top, left or right, or auto-hidden: the pond sits on the very bottom
    ///   edge of the screen, and a side taskbar is left uncovered.
    /// </summary>
    public struct TaskbarInfo
    {
        public TaskbarEdge Edge;
        public bool AutoHide;
        public RectInt Screen;     // primary monitor
        public RectInt Taskbar;    // empty when unknown
        public RectInt Work;       // screen minus the taskbar

        /// <summary>Desktop Y of the line the pond rests on (pixels from the top).</summary>
        public int FloorY;

        /// <summary>Horizontal span the pond may use.</summary>
        public int Left, Right;

        public override string ToString() =>
            $"edge={Edge} autohide={AutoHide} screen={Screen.width}x{Screen.height} " +
            $"taskbar=({Taskbar.x},{Taskbar.y},{Taskbar.width}x{Taskbar.height}) floor={FloorY} span={Left}..{Right}";

        public bool SameLayout(TaskbarInfo o) =>
            Edge == o.Edge && AutoHide == o.AutoHide && Screen.Equals(o.Screen) && FloorY == o.FloorY
            && Left == o.Left && Right == o.Right && Work.Equals(o.Work);

        public static TaskbarInfo Read(int fallbackW, int fallbackH)
        {
            var info = new TaskbarInfo
            {
                Edge = TaskbarEdge.None,
                Screen = new RectInt(0, 0, fallbackW, fallbackH),
            };

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            try
            {
                var w = GetSystemMetrics(SM_CXSCREEN);
                var h = GetSystemMetrics(SM_CYSCREEN);
                if (w > 0 && h > 0)
                    info.Screen = new RectInt(0, 0, w, h);

                var data = new APPBARDATA { cbSize = Marshal.SizeOf(typeof(APPBARDATA)) };
                if (SHAppBarMessage(ABM_GETTASKBARPOS, ref data) != IntPtr.Zero)
                {
                    info.Edge = (TaskbarEdge)Mathf.Clamp((int)data.uEdge, 0, 3);
                    info.Taskbar = new RectInt(data.rc.Left, data.rc.Top,
                        data.rc.Right - data.rc.Left, data.rc.Bottom - data.rc.Top);
                }

                var state = new APPBARDATA { cbSize = Marshal.SizeOf(typeof(APPBARDATA)) };
                info.AutoHide = ((long)SHAppBarMessage(ABM_GETSTATE, ref state) & ABS_AUTOHIDE) != 0;

                if (SystemParametersInfo(SPI_GETWORKAREA, 0, out var work, 0))
                    info.Work = new RectInt(work.Left, work.Top, work.Right - work.Left, work.Bottom - work.Top);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("DeskPond: could not read the taskbar. " + ex.Message);
            }
#endif
            if (info.Work.width <= 0 || info.Work.height <= 0)
                info.Work = info.Screen;

            info.Resolve();
            return info;
        }

        /// <summary>Works out the floor line and the usable span from the edge.</summary>
        void Resolve()
        {
            Left = Screen.xMin;
            Right = Screen.xMax;
            FloorY = Screen.yMax;

            // A taskbar on another monitor, or one that is auto-hidden, does not touch the pond.
            var onThisScreen = Taskbar.width > 0 && Taskbar.height > 0 && Screen.Overlaps(Taskbar);
            if (AutoHide || !onThisScreen)
                return;

            switch (Edge)
            {
                case TaskbarEdge.Bottom:
                    FloorY = Mathf.Clamp(Taskbar.yMin, Screen.yMin + Screen.height / 2, Screen.yMax);
                    break;
                case TaskbarEdge.Left:
                    Left = Mathf.Clamp(Taskbar.xMax, Screen.xMin, Screen.xMax - 200);
                    break;
                case TaskbarEdge.Right:
                    Right = Mathf.Clamp(Taskbar.xMin, Screen.xMin + 200, Screen.xMax);
                    break;
                // Top: the pond keeps the whole bottom edge.
            }
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        const uint ABM_GETSTATE = 0x4;
        const uint ABM_GETTASKBARPOS = 0x5;
        const long ABS_AUTOHIDE = 0x1;
        const int SM_CXSCREEN = 0;
        const int SM_CYSCREEN = 1;
        const uint SPI_GETWORKAREA = 0x0030;

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        struct APPBARDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public uint uCallbackMessage;
            public uint uEdge;
            public RECT rc;
            public IntPtr lParam;
        }

        [DllImport("shell32.dll")]
        static extern IntPtr SHAppBarMessage(uint msg, ref APPBARDATA data);

        [DllImport("user32.dll")]
        static extern int GetSystemMetrics(int index);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool SystemParametersInfo(uint action, uint param, out RECT result, uint winIni);
#endif
    }
}
