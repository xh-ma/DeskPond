using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace DeskPond
{
    /// <summary>
    /// Samples which window is in front and hands the elapsed time to ActivityLog, so the stats
    /// panel can show where the day went.
    ///
    /// Process names are always read. Window titles are read only when told to, because they are a
    /// different class of information: a process name says "a browser was open", a title says which
    /// page, which document, which conversation. See ActivityLog for where that line is drawn.
    ///
    /// Windows only. Elsewhere this reports nothing and the rest of the game is unaffected.
    /// </summary>
    public class ForegroundWatcher
    {
        /// <summary>Titles longer than this are cut; nothing useful lives past it.</summary>
        const int MaxTitleLength = 120;

        readonly Dictionary<uint, string> _processNames = new Dictionary<uint, string>();

        float _pollCooldown;
        string _currentApp;
        string _currentTitle;
        float _heldSeconds;
        bool _warnedResolve;
        bool _loggedFirst;

        /// <summary>True when the platform can actually report a foreground window.</summary>
        public bool IsSupported =>
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            true;
#else
            false;
#endif

        public string CurrentApp => _currentApp;

        public string CurrentTitle => _currentTitle;

        /// <summary>
        /// Polls on an interval and credits the time since the last poll to whatever was in front
        /// during it. Crediting the previous sample rather than the new one is what makes the
        /// totals add up to real elapsed time instead of counting switches.
        /// </summary>
        public void Tick(float dt, ActivityLog log, float pollSeconds, bool recordTitles)
        {
            if (!IsSupported || log == null)
                return;

            _heldSeconds += dt;
            _pollCooldown -= dt;
            if (_pollCooldown > 0f)
                return;

            _pollCooldown = Mathf.Max(1f, pollSeconds);

            if (!string.IsNullOrEmpty(_currentApp))
                log.AddAppTime(_currentApp, recordTitles ? _currentTitle : null, _heldSeconds);
            _heldSeconds = 0f;

            Sample(recordTitles, out _currentApp, out _currentTitle);

            // One line confirming sampling actually works, so a silent failure is visible in the
            // log instead of showing up as an empty stats panel days later.
            if (!_loggedFirst && !string.IsNullOrEmpty(_currentApp))
            {
                _loggedFirst = true;
                Debug.Log($"DeskPond activity: first sample app={_currentApp} " +
                          $"title={(recordTitles ? "\"" + _currentTitle + "\"" : "not recorded")}");
            }
        }

        /// <summary>Flushes the time held since the last poll, for quit.</summary>
        public void Commit(ActivityLog log, bool recordTitles)
        {
            if (log == null || string.IsNullOrEmpty(_currentApp) || _heldSeconds <= 0f)
                return;

            log.AddAppTime(_currentApp, recordTitles ? _currentTitle : null, _heldSeconds);
            _heldSeconds = 0f;
        }

        void Sample(bool recordTitles, out string app, out string title)
        {
            app = null;
            title = null;

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            try
            {
                var window = GetForegroundWindow();
                if (window == IntPtr.Zero)
                    return;

                GetWindowThreadProcessId(window, out var pid);
                if (pid == 0)
                    return;

                app = ResolveProcessName(pid);
                if (recordTitles)
                    title = ReadTitle(window);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("DeskPond: foreground sample failed. " + ex.Message);
            }
#endif
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        /// <summary>
        /// Cached, because resolving a pid to a name costs far more than the two Win32 calls.
        ///
        /// Asks kernel32 for the image path rather than System.Diagnostics.Process: that managed
        /// API can be stripped out of a player build, and it is markedly slower.
        /// </summary>
        string ResolveProcessName(uint pid)
        {
            if (_processNames.TryGetValue(pid, out var cached))
                return cached;

            var name = string.Empty;
            var handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
            if (handle != IntPtr.Zero)
            {
                try
                {
                    var buffer = new StringBuilder(512);
                    var size = (uint)buffer.Capacity;
                    if (QueryFullProcessImageName(handle, 0, buffer, ref size))
                    {
                        var full = buffer.ToString();
                        name = StripPath(full);
                        // Grab the icon and product name once, while the path is still in hand.
                        AppIcons.EnsureCached(name, full);
                    }
                }
                finally
                {
                    CloseHandle(handle);
                }
            }

            if (string.IsNullOrEmpty(name) && !_warnedResolve)
            {
                _warnedResolve = true;
                var err = Marshal.GetLastWin32Error();
                Debug.LogWarning($"DeskPond: could not name pid {pid} (error {err}); " +
                                 "foreground time for it will not be recorded.");
            }

            // Pids are recycled, so the cache is bounded rather than kept forever.
            if (_processNames.Count > 128)
                _processNames.Clear();

            _processNames[pid] = name;
            return name;
        }

        /// <summary>Full image path down to the bare name, so "...\Code.exe" becomes "Code".</summary>
        static string StripPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            var slash = path.LastIndexOfAny(new[] { '\\', '/' });
            var name = slash >= 0 ? path.Substring(slash + 1) : path;
            var dot = name.LastIndexOf('.');
            return dot > 0 ? name.Substring(0, dot) : name;
        }

        static string ReadTitle(IntPtr window)
        {
            var length = GetWindowTextLength(window);
            if (length <= 0)
                return string.Empty;

            var buffer = new StringBuilder(length + 1);
            var written = GetWindowText(window, buffer, buffer.Capacity);
            if (written <= 0)
                return string.Empty;

            var text = buffer.ToString();
            return text.Length > MaxTitleLength ? text.Substring(0, MaxTitleLength) : text;
        }

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        // Limited information is enough for the image name and, unlike full query access, is
        // granted for processes this one does not own.
        const uint ProcessQueryLimitedInformation = 0x1000;

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true,
            EntryPoint = "QueryFullProcessImageNameW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool QueryFullProcessImageName(IntPtr process, uint flags,
            StringBuilder name, ref uint size);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool CloseHandle(IntPtr handle);

        // The wide variants are required, not stylistic: the ANSI ones convert through the system
        // code page, which turns any title outside it into question marks.
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW")]
        static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextLengthW")]
        static extern int GetWindowTextLength(IntPtr hWnd);
#endif
    }
}
