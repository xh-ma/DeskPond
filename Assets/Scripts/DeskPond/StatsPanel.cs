using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DeskPond
{
    /// <summary>
    /// The activity panel: where today's hours went, which applications took them, and what the
    /// pacing model has learned about how fast this user works.
    ///
    /// Same chrome as BasketMenu, opened from its "stats" button. Drag the header to move it,
    /// drag the bottom-right grip to resize, and both persist in the save file.
    ///
    /// Window titles are hidden until asked for, even when they are being recorded, so the panel
    /// is safe to have on screen during a screenshot or a screen share by default.
    /// </summary>
    public class StatsPanel : MonoBehaviour
    {
        class PanelButton
        {
            public RectTransform Rect;
            public Image Background;
            public Text Label;
            public Action OnClick;
            public Func<bool> Enabled;
            public Color Face;
        }

        class AppRow
        {
            public Text Name;
            public Text Time;
            public Text Inputs;
            public Image Icon;
            public RectTransform Bar;
            public float BarMax;
        }

        const float MinScale = 1f;
        const float MaxScale = 5f;
        const float DefaultScale = 1.6f;
        const float HeaderH = 30f;
        const float GripSize = 10f;
        const int AppRows = 6;
        const int ChartHours = 24;
        const float ChartH = 46f;
        const float RowH = 26f;
        const float IconSize = 16f;

        /// <summary>Rollups walk every stored slice, so they run on a timer, not every frame.</summary>
        const float RefreshSeconds = 2f;

        int _width = 380;
        float _height = 300f;
        float _scale = 2f;
        Vector2 _screenPos;
        bool _placed;

        GameSession _session;
        CanvasScaler _scaler;
        RectTransform _panel;
        RectTransform _header;
        RectTransform _grip;
        Font _font;
        CanvasGroup _group;

        readonly List<PanelButton> _buttons = new List<PanelButton>();
        readonly List<RectTransform> _hourBars = new List<RectTransform>();
        readonly List<Image> _hourFills = new List<Image>();
        readonly List<AppRow> _appRows = new List<AppRow>();

        Text _summary;
        Text _counts;
        Text _rateLine;
        Text _progressLine;
        Text _lifetimeLine;
        Text _pacing;
        Text _footer;
        PanelButton _titlesButton;
        PanelButton _clearButton;

        bool _clearArmed;
        float _refreshCooldown;

        bool _moving;
        bool _resizing;
        Vector2 _grabScreen;
        Vector2 _grabPanelPos;
        float _grabScale;

        DesktopWindow _window;
        AnimationCurve _openCurve;
        float _openSeconds = 0.18f;
        Coroutine _openRoutine;

        public bool IsOpen => _panel != null && _panel.gameObject.activeSelf;

        public bool IsInteracting => _moving || _resizing;

        public static StatsPanel Create(GameSession session, GameConfig config)
        {
            var root = new GameObject("StatsPanel");
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Above the basket menu, since it is opened from it.
            canvas.sortingOrder = 51;

            var panel = root.AddComponent<StatsPanel>();
            panel._session = session;
            if (config != null)
            {
                // This panel is roughly half again as tall as the shop, so it starts smaller than
                // the configured popup scale or it would not fit on a 1080p screen. Resizing it
                // larger sticks, because the size is saved separately.
                panel._scale = Mathf.Clamp(Mathf.Min(config.menuScale, DefaultScale),
                    MinScale, MaxScale);
                panel._openCurve = config.popupOpen;
                panel._openSeconds = config.popupOpenSeconds;
            }

            var save = session != null ? session.Save : null;
            if (save != null && save.statsScale > 0f)
                panel._scale = Mathf.Clamp(save.statsScale, MinScale, MaxScale);

            panel._scaler = root.AddComponent<CanvasScaler>();
            panel._scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            panel._scaler.scaleFactor = panel._scale;

            panel.Build();

            if (save != null && save.statsScreenX >= 0f)
            {
                panel._screenPos = new Vector2(save.statsScreenX, save.statsScreenY);
                panel._placed = true;
            }

            return panel;
        }

        public void ToggleAt(Vector2 screenPoint)
        {
            if (_panel == null)
                return;

            _panel.gameObject.SetActive(!IsOpen);
            if (!IsOpen)
                return;

            if (!_placed)
                _screenPos = new Vector2(screenPoint.x, screenPoint.y + 24f);

            _clearArmed = false;
            ClampToScreen();
            ApplyTransform();
            Refresh();
            PlayOpen();
        }

        public void Close()
        {
            if (_panel != null)
                _panel.gameObject.SetActive(false);
        }

        public bool ContainsScreenPoint(Vector2 screen)
        {
            return IsOpen && RectTransformUtility.RectangleContainsScreenPoint(_panel, screen, null);
        }

        public bool PointerOverPanel() => ContainsScreenPoint(MousePosition());

        void PlayOpen()
        {
            if (_group == null)
                return;
            if (_openRoutine != null)
                StopCoroutine(_openRoutine);
            _openRoutine = StartCoroutine(OpenRoutine());
        }

        System.Collections.IEnumerator OpenRoutine()
        {
            var duration = Mathf.Max(0.05f, _openSeconds);
            for (var t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                var k = t / duration;
                _group.alpha = Mathf.Clamp01(k * 1.6f);
                var scale = _openCurve != null && _openCurve.length > 0 ? _openCurve.Evaluate(k) : 1f;
                _panel.localScale = Vector3.one * scale;
                yield return null;
            }

            _group.alpha = 1f;
            _panel.localScale = Vector3.one;
            _openRoutine = null;
        }

        void Update()
        {
            if (!IsOpen)
                return;

            _refreshCooldown -= Time.unscaledDeltaTime;
            if (_refreshCooldown <= 0f)
            {
                Refresh();
                _refreshCooldown = RefreshSeconds;
            }

            var mouse = MousePosition();
            var pressed = WasPressed();
            var held = IsHeld();

            if (HandleDrags(mouse, pressed, held))
                return;

            foreach (var button in _buttons)
            {
                var inside = RectTransformUtility.RectangleContainsScreenPoint(button.Rect, mouse, null);
                var usable = button.Enabled == null || button.Enabled();
                button.Background.sprite = !usable ? UiKit.Sunken(UiKit.Dimmer(button.Face))
                    : inside ? UiKit.Raised(UiKit.Lighter(button.Face)) : UiKit.Raised(button.Face);

                if (inside && pressed && usable)
                {
                    button.OnClick?.Invoke();
                    Refresh();
                    return;
                }
            }
        }

        bool HandleDrags(Vector2 mouse, bool pressed, bool held)
        {
            var overGrip = RectTransformUtility.RectangleContainsScreenPoint(_grip, mouse, null);
            SetResizeCursor(overGrip || _resizing);

            if (pressed && !_moving && !_resizing)
            {
                if (overGrip)
                {
                    _resizing = true;
                    _grabScreen = mouse;
                    _grabScale = _scale;
                }
                else if (RectTransformUtility.RectangleContainsScreenPoint(_header, mouse, null))
                {
                    _moving = true;
                    _grabScreen = mouse;
                    _grabPanelPos = _screenPos;
                }
            }

            if (_moving)
            {
                _screenPos = _grabPanelPos + (mouse - _grabScreen);
                ClampToScreen();
                ApplyTransform();
                if (!held)
                {
                    _moving = false;
                    _placed = true;
                    StoreLayout();
                }

                return true;
            }

            if (_resizing)
            {
                // Same trick as the basket panel: solve for the scale that keeps the corner under
                // the cursor, so it reads as a window resize rather than a nudge.
                _scale = Mathf.Clamp((mouse.x - _screenPos.x) * 2f / _width, MinScale, MaxScale);
                ApplyTransform();
                if (!held)
                {
                    _resizing = false;
                    StoreLayout();
                }

                return true;
            }

            return false;
        }

        static bool _cursorIsResize;

        static void SetResizeCursor(bool resize)
        {
            if (resize == _cursorIsResize)
                return;
            _cursorIsResize = resize;
            if (resize)
                Cursor.SetCursor(UiKit.ResizeCursor, new Vector2(8f, 8f), CursorMode.Auto);
            else
                Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        }

        void OnDisable() => SetResizeCursor(false);

        void ApplyTransform()
        {
            if (_scaler != null)
                _scaler.scaleFactor = _scale;
            if (_panel != null)
                _panel.anchoredPosition = _screenPos / _scale;
        }

        void ClampToScreen()
        {
            var halfW = _width * _scale * 0.5f;
            var h = _height * _scale;
            var maxX = Mathf.Max(halfW + 4f, Screen.width - halfW - 4f);
            _screenPos.x = Mathf.Clamp(_screenPos.x, halfW + 4f, maxX);
            _screenPos.y = Mathf.Clamp(_screenPos.y, 4f, Mathf.Max(4f, Screen.height - h - 4f));
        }

        void StoreLayout()
        {
            if (_session == null || _session.Save == null)
                return;

            _session.Save.statsScale = _scale;
            _session.Save.statsScreenX = _screenPos.x;
            _session.Save.statsScreenY = _screenPos.y;
            SaveSystem.Write(_session.Save);
        }

        DesktopWindow Window => _window != null
            ? _window
            : _window = FindFirstObjectByType<DesktopWindow>();

        Vector2 MousePosition()
        {
            var w = Window;
            if (w != null && w.IsActive && w.CursorValid)
                return w.CursorScreen;

            var mouse = UnityEngine.InputSystem.Mouse.current;
            return mouse != null ? mouse.position.ReadValue() : Vector2.zero;
        }

        bool WasPressed()
        {
            var w = Window;
            if (w != null && w.IsActive)
                return w.PrimaryPressedThisFrame;

            var mouse = UnityEngine.InputSystem.Mouse.current;
            return mouse != null && mouse.leftButton.wasPressedThisFrame;
        }

        bool IsHeld()
        {
            var w = Window;
            if (w != null && w.IsActive)
                return w.PrimaryDown;

            var mouse = UnityEngine.InputSystem.Mouse.current;
            return mouse != null && mouse.leftButton.isPressed;
        }

        /// <summary>Whether titles are being recorded at all, which the panel can now change.</summary>
        bool TitlesRecording => _session != null && _session.RecordTitles;

        public void Refresh()
        {
            if (!IsOpen)
                return;

            var log = _session != null ? _session.Activity : null;
            if (log == null)
            {
                _summary.text = "Activity logging is off.";
                _counts.text = "";
                _rateLine.text = "";
                _footer.text = "Enable trackActivity in GameConfig.";
                RefreshProgress();
                foreach (var row in _appRows)
                {
                    row.Name.text = "";
                    row.Time.text = "";
                    row.Bar.sizeDelta = new Vector2(0f, row.Bar.sizeDelta.y);
                }

                foreach (var bar in _hourBars)
                    bar.sizeDelta = new Vector2(bar.sizeDelta.x, 0f);
                RefreshPacing();
                return;
            }

            RefreshChart(log);
            RefreshCounts(log);
            RefreshApps(log);
            RefreshPacing();

            _titlesButton.Label.text = TitlesRecording ? "sites: on" : "sites: off";
            _clearButton.Label.text = _clearArmed ? "sure?" : "clear";
        }

        void RefreshChart(ActivityLog log)
        {
            var today = DateTime.Now;
            var hours = log.InputsByHour(today);

            // The busiest hour has to be known before any bar is coloured. Tracking it in the same
            // pass that paints would highlight every running maximum, not the single peak.
            var peak = 1;
            var total = 0;
            var busiest = 0;
            for (var i = 0; i < ChartHours; i++)
            {
                peak = Mathf.Max(peak, hours[i]);
                total += hours[i];
                if (hours[i] > hours[busiest])
                    busiest = i;
            }

            for (var i = 0; i < ChartHours && i < _hourBars.Count; i++)
            {
                // A busy hour and an untouched hour must look different, so anything above zero
                // gets at least one pixel rather than collapsing into the baseline.
                var fraction = hours[i] / (float)peak;
                var height = hours[i] > 0 ? Mathf.Max(2f, fraction * ChartH) : 0f;
                _hourBars[i].sizeDelta = new Vector2(_hourBars[i].sizeDelta.x, height);
                _hourFills[i].color = i == busiest && hours[i] > 0 ? UiKit.ChartPeak : UiKit.ChartBar;
            }

            var sessions = log.Sessions(today);
            var longest = TimeSpan.Zero;
            var active = TimeSpan.Zero;
            foreach (var s in sessions)
            {
                var span = s.End - s.Start;
                active += span;
                if (span > longest)
                    longest = span;
            }

            _summary.text = total <= 0
                ? "Nothing recorded yet today."
                : $"{Duration(active.TotalSeconds)} active  ·  {sessions.Count} " +
                  $"stretch{(sessions.Count == 1 ? "" : "es")}  ·  longest {Duration(longest.TotalSeconds)}";
        }

        void RefreshApps(ActivityLog log)
        {
            var from = DateTime.Now.Date.ToUniversalTime();
            var totals = log.AppTotals(from, from.AddDays(1), TitlesRecording);

            var peak = 1;
            for (var i = 0; i < totals.Count && i < AppRows; i++)
                peak = Mathf.Max(peak, totals[i].Seconds);

            for (var i = 0; i < _appRows.Count; i++)
            {
                var row = _appRows[i];
                if (i >= totals.Count)
                {
                    row.Name.text = "";
                    row.Time.text = "";
                    row.Inputs.text = "";
                    row.Icon.enabled = false;
                    row.Bar.sizeDelta = new Vector2(0f, row.Bar.sizeDelta.y);
                    continue;
                }

                var entry = totals[i];
                var label = TitlesRecording && !string.IsNullOrEmpty(entry.Title)
                    ? CleanTitle(entry.Title)
                    : Pretty(entry.App);
                row.Name.text = Ellipsis(label, 26);
                row.Time.text = Duration(entry.Seconds);
                row.Inputs.text = entry.Inputs > 0 ? Count(entry.Inputs) : "";

                var icon = AppIcons.Get(entry.App);
                row.Icon.sprite = icon;
                row.Icon.enabled = icon != null;

                row.Bar.sizeDelta = new Vector2(row.BarMax * entry.Seconds / peak, row.Bar.sizeDelta.y);
            }
        }

        /// <summary>The headline number: clicks and keystrokes counted together, as one total.</summary>
        void RefreshCounts(ActivityLog log)
        {
            // The whole of today, not "today up to this second": the totals mean the same thing
            // either way in normal use, and this does not depend on where the clock happens to be.
            var dayStart = DateTime.Now.Date.ToUniversalTime();
            var today = log.InputTotal(dayStart, dayStart.AddDays(1));
            _counts.text = Count(today) + " today";

            var counter = _session != null ? _session.Input : null;
            var rate = counter != null ? counter.Rate : 0f;
            var scope = counter != null && counter.IsGlobal ? "everywhere" : "this window only";
            _rateLine.text = $"{rate:0.0} per second right now  ·  counting {scope}";

            RefreshProgress();

            var save = _session != null ? _session.Save : null;
            if (save != null)
                _lifetimeLine.text = Count(save.lifetimeInputs) + " since you started";
        }

        void RefreshProgress()
        {
            var save = _session != null ? _session.Save : null;
            if (save == null)
            {
                _progressLine.text = "";
                return;
            }

            var pct = Mathf.RoundToInt(_session.CatchProgress * 100f);
            _progressLine.text = $"{Count(save.inputsTowardCatch)} / {Count(save.nextCatchTarget)} " +
                                 $"toward the next fish  ({pct}%)";
        }

        /// <summary>Grouped digits, because 12480 and 1248 look alike at a glance.</summary>
        static string Count(long value) =>
            value.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);

        void RefreshPacing()
        {
            var model = _session != null ? _session.Pacing : null;
            if (model == null)
            {
                _pacing.text = "";
                return;
            }

            var target = _session.Save != null ? _session.Save.nextCatchTarget : 0;
            _pacing.text = model.Ready
                ? $"median {model.MedianRate:0}/min  ·  next fish {target} inputs"
                : $"learning your pace  ·  {model.Samples}/{RateModel.WarmupSamples} active minutes";

            if (_session.Activity != null && _session.Save != null)
                _footer.text = $"{_session.Save.lifetimeFish} fish caught  ·  " +
                               $"{_session.Activity.Buckets.Count} slices kept";
        }

        /// <summary>Seconds to something readable: 2h 05m, 47m, 12s.</summary>
        static string Duration(double seconds)
        {
            if (seconds < 60d)
                return Mathf.RoundToInt((float)seconds) + "s";

            var total = (int)seconds;
            var h = total / 3600;
            var m = total % 3600 / 60;
            return h > 0 ? $"{h}h {m:00}m" : $"{m}m";
        }

        /// <summary>
        /// A browser tab title carries the application on the end: "GitHub - Google Chrome".
        /// The app is already named by the icon and the row it sits in, so that tail is noise.
        /// </summary>
        static string CleanTitle(string title)
        {
            if (string.IsNullOrEmpty(title))
                return "";

            foreach (var tail in Tails)
            {
                var at = title.LastIndexOf(tail, StringComparison.OrdinalIgnoreCase);
                if (at > 0)
                    return title.Substring(0, at).Trim();
            }

            return title.Trim();
        }

        static readonly string[] Tails =
        {
            " - Google Chrome", " - Microsoft Edge", " — Mozilla Firefox", " - Mozilla Firefox",
            " - Visual Studio Code", " - Brave", " - Opera", " and 1 more page", " - Chromium"
        };

        /// <summary>
        /// The executable's own product name where Windows reports one, so "msedge" shows up as
        /// "Microsoft Edge" without this file needing a table of every program in existence. The
        /// short list below only covers ones whose product name is unhelpful or missing.
        /// </summary>
        static string Pretty(string process)
        {
            if (string.IsNullOrEmpty(process))
                return "unknown";

            var friendly = AppIcons.FriendlyName(process);
            if (!string.IsNullOrEmpty(friendly))
                return friendly;

            switch (process.ToLowerInvariant())
            {
                case "code": return "VS Code";
                case "devenv": return "Visual Studio";
                case "unity": return "Unity";
                case "chrome": return "Chrome";
                case "msedge": return "Edge";
                case "firefox": return "Firefox";
                case "explorer": return "File Explorer";
                case "winword": return "Word";
                case "excel": return "Excel";
                case "powerpnt": return "PowerPoint";
                case "wechat": case "wechatapp": return "WeChat";
                case "discord": return "Discord";
                case "slack": return "Slack";
                case "windowsterminal": return "Terminal";
                case "deskpond": return "DeskPond";
                default: return process;
            }
        }

        static string Ellipsis(string text, int max)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= max)
                return text;
            return text.Substring(0, Mathf.Max(1, max - 1)) + "…";
        }

        /// <summary>
        /// One switch with two jobs: start recording titles, and show them. Turning it off stops
        /// recording and discards what was already captured, so "off" means off.
        /// </summary>
        void ToggleTitles() => _session?.SetRecordTitles(!TitlesRecording);

        /// <summary>Two presses to wipe: the first arms it, the second does it.</summary>
        void ClearPressed()
        {
            if (!_clearArmed)
            {
                _clearArmed = true;
                return;
            }

            _clearArmed = false;
            _session?.ClearActivity();
        }

        void Build()
        {
            _font = UiKit.PixelFont;

            var panelGo = new GameObject("Panel");
            panelGo.transform.SetParent(transform, false);
            var fill = panelGo.AddComponent<Image>();
            fill.sprite = UiKit.Raised(UiKit.PanelFace);
            fill.type = Image.Type.Sliced;
            fill.color = Color.white;
            _panel = fill.rectTransform;
            _panel.anchorMin = Vector2.zero;
            _panel.anchorMax = Vector2.zero;
            _panel.pivot = new Vector2(0.5f, 0f);
            _group = panelGo.AddComponent<CanvasGroup>();

            const float pad = 12f;
            var content = _width - pad * 2f;
            var y = 0f;

            var header = AddSurface(_panel, "Header", UiKit.HeaderFace, true);
            _header = header.rectTransform;
            _header.anchorMin = new Vector2(0f, 1f);
            _header.anchorMax = new Vector2(1f, 1f);
            _header.pivot = new Vector2(0.5f, 1f);
            _header.offsetMin = new Vector2(3f, 0f);
            _header.offsetMax = new Vector2(-3f, -3f);
            _header.sizeDelta = new Vector2(-6f, HeaderH);

            MakeLabel(_header, "Title", new Vector2(pad, -7f), 13, TextAnchor.UpperLeft,
                UiKit.TextOnHeader, "Activity", 200f, FontStyle.Bold);
            MakeLabel(_header, "Day", new Vector2(-pad, -7f), 10, TextAnchor.UpperRight,
                UiKit.TextOnHeader, DateTime.Now.ToString("ddd d MMM"), 140f, FontStyle.Normal);

            y -= HeaderH + 10f;

            MakeLabel(_panel, "TodayHeader", new Vector2(pad, y), 10, TextAnchor.UpperLeft,
                UiKit.TextMuted, "TODAY, BY HOUR", 200f, FontStyle.Bold);
            y -= 16f;

            // Sunken track so the chart reads as a recessed well, matching the shop rows.
            var track = AddSurface(_panel, "ChartTrack", UiKit.FieldFace, false);
            Place(track.rectTransform, new Rect(pad, y, content, ChartH + 8f));

            var slot = content / ChartHours;
            for (var i = 0; i < ChartHours; i++)
            {
                var bar = AddImage(_panel, "Hour" + i, UiKit.White, UiKit.ChartBar);
                bar.type = Image.Type.Simple;
                var rt = bar.rectTransform;
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(0f, 1f);
                // Grows upward from the bottom of the track.
                rt.pivot = new Vector2(0.5f, 0f);
                rt.anchoredPosition = new Vector2(pad + slot * (i + 0.5f), y - ChartH - 4f);
                rt.sizeDelta = new Vector2(Mathf.Max(2f, slot - 2f), 0f);
                _hourBars.Add(rt);
                _hourFills.Add(bar);
            }

            y -= ChartH + 10f;

            foreach (var mark in new[] { 0, 6, 12, 18 })
            {
                MakeLabel(_panel, "Tick" + mark, new Vector2(pad + slot * mark, y), 9,
                    TextAnchor.UpperLeft, UiKit.TextMuted, mark.ToString("00"), 24f, FontStyle.Normal);
            }

            y -= 14f;
            _summary = MakeLabel(_panel, "Summary", new Vector2(pad, y), 10, TextAnchor.UpperLeft,
                UiKit.TextDark, "", content, FontStyle.Normal);
            y -= 20f;

            MakeLabel(_panel, "InputHeader", new Vector2(pad, y), 10, TextAnchor.UpperLeft,
                UiKit.TextMuted, "INPUT", 240f, FontStyle.Bold);
            y -= 16f;

            // The day's count is the headline, so it gets the largest type on the panel.
            var well = AddSurface(_panel, "CountWell", UiKit.FieldFace, false);
            Place(well.rectTransform, new Rect(pad, y, content, 54f));

            _counts = MakeLabel(_panel, "Counts", new Vector2(pad + 8f, y - 5f), 14,
                TextAnchor.UpperLeft, UiKit.TextDark, "", content - 16f, FontStyle.Bold);
            _rateLine = MakeLabel(_panel, "Rate", new Vector2(pad + 8f, y - 24f), 9,
                TextAnchor.UpperLeft, UiKit.TextMuted, "", content - 16f, FontStyle.Normal);
            _progressLine = MakeLabel(_panel, "Progress", new Vector2(pad + 8f, y - 37f), 9,
                TextAnchor.UpperLeft, UiKit.TextMuted, "", content - 16f, FontStyle.Normal);
            y -= 62f;

            _lifetimeLine = MakeLabel(_panel, "Lifetime", new Vector2(pad, y), 9,
                TextAnchor.UpperLeft, UiKit.TextMuted, "", content, FontStyle.Normal);
            y -= 20f;

            MakeLabel(_panel, "AppsHeader", new Vector2(pad, y), 10, TextAnchor.UpperLeft,
                UiKit.TextMuted, "WHERE THE TIME WENT", 200f, FontStyle.Bold);
            MakeLabel(_panel, "AppsLegend", new Vector2(-pad, y), 9, TextAnchor.UpperRight,
                UiKit.TextMuted, "inputs      time", 140f, FontStyle.Normal);
            y -= 16f;

            for (var i = 0; i < AppRows; i++)
            {
                var rowBg = AddSurface(_panel, "AppRow" + i, UiKit.FieldFace, false);
                Place(rowBg.rectTransform, new Rect(pad, y, content, RowH));

                // The bar sits inside the row behind everything else, so it reads as a fill level.
                var bar = AddImage(_panel, "AppBar" + i, UiKit.White, UiKit.ChartBar);
                bar.type = Image.Type.Simple;
                var barRt = bar.rectTransform;
                barRt.anchorMin = new Vector2(0f, 1f);
                barRt.anchorMax = new Vector2(0f, 1f);
                barRt.pivot = new Vector2(0f, 1f);
                barRt.anchoredPosition = new Vector2(pad + 2f, y - 2f);
                barRt.sizeDelta = new Vector2(0f, RowH - 4f);
                bar.color = new Color(UiKit.ChartBar.r, UiKit.ChartBar.g, UiKit.ChartBar.b, 0.5f);

                var icon = AddImage(_panel, "AppIcon" + i, null, Color.white);
                icon.type = Image.Type.Simple;
                icon.enabled = false;
                var iconRt = icon.rectTransform;
                iconRt.anchorMin = new Vector2(0f, 1f);
                iconRt.anchorMax = new Vector2(0f, 1f);
                iconRt.pivot = new Vector2(0f, 1f);
                iconRt.anchoredPosition = new Vector2(pad + 6f, y - 5f);
                iconRt.sizeDelta = new Vector2(IconSize, IconSize);

                _appRows.Add(new AppRow
                {
                    Name = MakeLabel(_panel, "AppName" + i, new Vector2(pad + 28f, y - 7f), 10,
                        TextAnchor.UpperLeft, UiKit.TextDark, "", content - 150f, FontStyle.Normal),
                    Inputs = MakeLabel(_panel, "AppInputs" + i, new Vector2(-pad - 62f, y - 7f), 9,
                        TextAnchor.UpperRight, UiKit.TextMuted, "", 78f, FontStyle.Normal),
                    Time = MakeLabel(_panel, "AppTime" + i, new Vector2(-pad - 8f, y - 7f), 10,
                        TextAnchor.UpperRight, UiKit.TextDark, "", 56f, FontStyle.Normal),
                    Icon = icon,
                    Bar = barRt,
                    BarMax = content - 4f
                });

                y -= RowH + 3f;
            }

            y -= 6f;
            MakeLabel(_panel, "PacingHeader", new Vector2(pad, y), 10, TextAnchor.UpperLeft,
                UiKit.TextMuted, "PACING", 200f, FontStyle.Bold);
            y -= 16f;
            _pacing = MakeLabel(_panel, "Pacing", new Vector2(pad, y), 10, TextAnchor.UpperLeft,
                UiKit.TextDark, "", content, FontStyle.Normal);
            y -= 18f;
            _footer = MakeLabel(_panel, "Footer", new Vector2(pad, y), 9, TextAnchor.UpperLeft,
                UiKit.TextMuted, "", content, FontStyle.Normal);
            y -= 22f;

            _titlesButton = AddButton("Titles", new Rect(pad, y, 74f, 18f), "sites: off",
                ToggleTitles, null, UiKit.ButtonFace);
            _clearButton = AddButton("Clear", new Rect(pad + 80f, y, 52f, 18f), "clear",
                ClearPressed, () => _session != null && _session.Activity != null, UiKit.QuitFace);
            AddButton("Close", new Rect(_width - pad - 24f, y, 24f, 18f), "✕",
                Close, null, UiKit.ButtonFace);
            y -= 24f;

            _height = -y + 8f;
            _panel.sizeDelta = new Vector2(_width, _height);

            var hit = AddImage(_panel, "GripHit", UiKit.Sunken(UiKit.FieldFace),
                new Color(1f, 1f, 1f, 0f));
            _grip = hit.rectTransform;
            _grip.anchorMin = new Vector2(1f, 0f);
            _grip.anchorMax = new Vector2(1f, 0f);
            _grip.pivot = new Vector2(1f, 0f);
            _grip.anchoredPosition = Vector2.zero;
            _grip.sizeDelta = new Vector2(GripSize * 2.2f, GripSize * 2.2f);

            var mark2 = AddImage(_grip, "GripMark", UiKit.Sunken(UiKit.FieldFace), Color.white);
            var markRt = mark2.rectTransform;
            markRt.anchorMin = new Vector2(1f, 0f);
            markRt.anchorMax = new Vector2(1f, 0f);
            markRt.pivot = new Vector2(1f, 0f);
            markRt.anchoredPosition = new Vector2(-3f, 3f);
            markRt.sizeDelta = new Vector2(GripSize, GripSize);

            _screenPos = new Vector2(Screen.width * 0.5f, Screen.height * 0.35f);
            ApplyTransform();
            _panel.gameObject.SetActive(false);
        }

        static void Place(RectTransform rt, Rect rect)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(rect.x, rect.y);
            rt.sizeDelta = new Vector2(rect.width, rect.height);
        }

        static Image AddImage(Transform parent, string name, Sprite sprite, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        static Image AddSurface(Transform parent, string name, Color face, bool raised)
        {
            return AddImage(parent, name, raised ? UiKit.Raised(face) : UiKit.Sunken(face), Color.white);
        }

        PanelButton AddButton(string name, Rect rect, string label, Action onClick,
            Func<bool> enabled, Color face)
        {
            var image = AddSurface(_panel, name, face, true);
            Place(image.rectTransform, rect);

            var text = MakeLabel(image.rectTransform, name + "Text", Vector2.zero, 10,
                TextAnchor.MiddleCenter, UiKit.TextDark, label, rect.width, FontStyle.Normal);
            var textRt = text.rectTransform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.pivot = new Vector2(0.5f, 0.5f);
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;

            var button = new PanelButton
            {
                Rect = image.rectTransform,
                Background = image,
                Label = text,
                OnClick = onClick,
                Enabled = enabled,
                Face = face
            };
            _buttons.Add(button);
            return button;
        }

        Text MakeLabel(Transform parent, string name, Vector2 pos, int size, TextAnchor anchor,
            Color color, string content, float width, FontStyle style)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<Text>();
            text.font = _font;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = anchor;
            text.color = color;
            text.text = content;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;

            var rt = text.rectTransform;
            var right = anchor == TextAnchor.UpperRight;
            rt.anchorMin = new Vector2(right ? 1f : 0f, 1f);
            rt.anchorMax = rt.anchorMin;
            rt.pivot = new Vector2(right ? 1f : 0f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(width, size + 8f);
            return text;
        }
    }
}
