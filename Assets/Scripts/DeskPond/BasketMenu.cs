using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DeskPond
{
    /// <summary>
    /// The panel that opens when the basket is clicked: sell the catch, buy upgrades, pick skins.
    ///
    /// It can be dragged by its header and resized by the grip in its bottom-right corner. Resizing
    /// scales the whole panel uniformly rather than reflowing the layout. The content is a
    /// fixed list, so uniform scaling stays proportioned at any size. Position and size persist
    /// in the save file.
    ///
    /// Buttons are hit-tested by hand against their RectTransforms, so the scene needs no
    /// EventSystem or input module wiring.
    /// </summary>
    public class BasketMenu : MonoBehaviour
    {
        class MenuButton
        {
            public RectTransform Rect;
            public Image Background;
            public Text Label;
            public Action OnClick;
            public Func<bool> Enabled;
            public Sprite Normal;
            public Sprite Hover;
            public Sprite Disabled;
        }

        const float MinScale = 1f;
        const float MaxScale = 5f;
        const float HeaderH = 30f;
        const float GripSize = 10f;

        int _width = 340;
        float _height = 300f;
        float _scale = 2f;
        Vector2 _screenPos;          // panel bottom-centre, in screen pixels
        bool _placed;

        GameSession _session;
        CanvasScaler _scaler;
        RectTransform _panel;
        RectTransform _header;
        RectTransform _grip;
        Text _coins;
        Text _sellLabel;
        Text _status;
        Font _font;

        readonly List<MenuButton> _buttons = new List<MenuButton>();
        readonly List<Text> _upgradeLabels = new List<Text>();
        readonly List<MenuButton> _upgradeButtons = new List<MenuButton>();
        readonly List<MenuButton> _catSkins = new List<MenuButton>();
        readonly List<MenuButton> _basketSkins = new List<MenuButton>();

        bool _moving;
        bool _resizing;
        Vector2 _grabScreen;
        Vector2 _grabPanelPos;
        float _grabScale;

        DesktopWindow _window;
        CanvasGroup _group;
        Coroutine _openRoutine;
        AnimationCurve _openCurve;
        float _openSeconds = 0.18f;

        /// <summary>Invoked by the stats button; GameSession supplies the handler.</summary>
        public Action OnStatsRequested;

        public bool IsOpen => _panel != null && _panel.gameObject.activeSelf;

        /// <summary>True while a move or resize drag is in progress, so clicks stay captured.</summary>
        public bool IsInteracting => _moving || _resizing;

        public static BasketMenu Create(GameSession session, GameConfig config)
        {
            var root = new GameObject("BasketMenu");
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;

            var menu = root.AddComponent<BasketMenu>();
            menu._session = session;
            if (config != null)
            {
                menu._scale = Mathf.Clamp(config.menuScale, MinScale, MaxScale);
                menu._width = Mathf.Max(240, config.menuWidth);
                menu._openCurve = config.popupOpen;
                menu._openSeconds = config.popupOpenSeconds;
            }

            var save = session != null ? session.Save : null;
            if (save != null && save.menuScale > 0f)
                menu._scale = Mathf.Clamp(save.menuScale, MinScale, MaxScale);

            menu._scaler = root.AddComponent<CanvasScaler>();
            menu._scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            menu._scaler.scaleFactor = menu._scale;

            menu.Build();

            if (save != null && save.menuScreenX >= 0f)
            {
                menu._screenPos = new Vector2(save.menuScreenX, save.menuScreenY);
                menu._placed = true;
            }

            return menu;
        }

        public void Toggle()
        {
            if (_panel == null)
                return;
            _panel.gameObject.SetActive(!_panel.gameObject.activeSelf);
            if (IsOpen)
            {
                Refresh();
                PlayOpen();
            }
        }

        /// <summary>
        /// Fade and pop on open. A Coroutine drives it, an AnimationCurve from GameConfig shapes
        /// it, and a CanvasGroup carries the fade, so the feel is tunable in the Inspector.
        /// </summary>
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
                // An un-authored curve deserialises with zero keys; fall back to no scaling.
                var scale = _openCurve != null && _openCurve.length > 0 ? _openCurve.Evaluate(k) : 1f;
                _panel.localScale = Vector3.one * scale;
                yield return null;
            }

            _group.alpha = 1f;
            _panel.localScale = Vector3.one;
            _openRoutine = null;
        }

        /// <summary>
        /// Opens the panel just above the pet rather than in the middle of the monitor. Once the
        /// player has dragged it somewhere, that position wins instead.
        /// </summary>
        public void ToggleAt(Vector2 screenPoint)
        {
            if (_panel == null)
                return;

            Toggle();
            if (!IsOpen)
                return;

            if (!_placed)
                _screenPos = new Vector2(screenPoint.x, screenPoint.y + 24f);

            ApplyTransform();
        }

        public void Close()
        {
            if (_panel != null)
                _panel.gameObject.SetActive(false);
        }

        void QuitGame()
        {
            StoreLayout();
            Debug.Log("DeskPond: quit from menu.");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        public bool PointerOverMenu() => ContainsScreenPoint(MousePosition());

        /// <summary>Takes any screen point, so callers can pass the OS cursor.</summary>
        public bool ContainsScreenPoint(Vector2 screen)
        {
            return IsOpen && RectTransformUtility.RectangleContainsScreenPoint(_panel, screen, null);
        }

        void Update()
        {
            if (!IsOpen)
                return;

            // Catch progress and the basket count change on their own, so they need repainting
            // every frame. Refresh() only runs on open and on button presses.
            RefreshLive();

            var mouse = MousePosition();
            var pressed = WasPressed();
            var held = IsHeld();

            if (HandleDrags(mouse, pressed, held))
                return;

            foreach (var button in _buttons)
            {
                var inside = RectTransformUtility.RectangleContainsScreenPoint(button.Rect, mouse, null);
                var usable = button.Enabled == null || button.Enabled();
                // Swap a pre-baked sprite instead of tinting: a multiply would darken the white
                // highlight along with the face and flatten the bevel.
                button.Background.sprite = !usable ? button.Disabled
                    : inside ? button.Hover : button.Normal;

                if (inside && pressed && usable)
                {
                    button.OnClick?.Invoke();
                    Refresh();
                    return;
                }
            }
        }

        /// <summary>Returns true when a move/resize drag consumed this frame's input.</summary>
        bool HandleDrags(Vector2 mouse, bool pressed, bool held)
        {
            // The grip is small, so the pointer change is the main cue that it is grabbable.
            var overGrip = RectTransformUtility.RectangleContainsScreenPoint(_grip, mouse, null);
            SetResizeCursor(overGrip || _resizing);

            if (pressed && !_moving && !_resizing)
            {
                if (RectTransformUtility.RectangleContainsScreenPoint(_grip, mouse, null))
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
                // The corner tracks the cursor: the panel's right edge sits at
                // _screenPos.x + _width * _scale / 2, so solving for _scale keeps the grip under
                // the pointer. That reads as a normal window resize rather than a nudge.
                var halfWidths = (mouse.x - _screenPos.x) * 2f / _width;
                _scale = Mathf.Clamp(halfWidths, MinScale, MaxScale);
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

        /// <summary>
        /// The panel's position is kept in screen pixels and divided by the canvas scale on the way
        /// in, so resizing does not drag the panel across the screen.
        /// </summary>
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

            _session.Save.menuScale = _scale;
            _session.Save.menuScreenX = _screenPos.x;
            _session.Save.menuScreenY = _screenPos.y;
            SaveSystem.Write(_session.Save);
        }

        DesktopWindow Window => _window != null
            ? _window
            : _window = FindFirstObjectByType<DesktopWindow>();

        // While the overlay is up the window never takes focus, so Unity's mouse state is unusable;
        // the OS cursor and button state come from DesktopWindow instead.
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

        public void Refresh()
        {
            if (_session == null || _session.Save == null || !IsOpen)
                return;

            var save = _session.Save;
            _coins.text = save.coins.ToString();

            var fish = _session.BasketFish;
            _sellLabel.text = fish > 0
                ? "Sell " + fish + " fish   +" + fish * _session.FishPrice
                : "Basket is empty";

            for (var i = 0; i < _upgradeLabels.Count; i++)
            {
                var kind = (UpgradeKind)i;
                var level = ShopDefs.Level(save, kind);
                var max = ShopDefs.MaxLevel(kind);
                var cfg = _session.Config;
                var adaptiveMinutes = cfg != null && cfg.adaptivePacing ? cfg.minutesPerFish : 0f;
                _upgradeLabels[i].text = ShopDefs.Label(kind) + "  " + level + "/" + max +
                                         "   " + ShopDefs.Effect(kind, level, adaptiveMinutes);

                var cost = ShopDefs.NextCost(kind, level);
                _upgradeButtons[i].Label.text = cost < 0 ? "MAX" : cost + "c";
            }

            for (var i = 0; i < _catSkins.Count; i++)
                _catSkins[i].Label.text = SkinLabel(true, save.catSkin, i);
            for (var i = 0; i < _basketSkins.Count; i++)
                _basketSkins[i].Label.text = SkinLabel(false, save.basketSkin, i);

            RefreshLive();
        }

        /// <summary>The parts that change without the player touching anything.</summary>
        void RefreshLive()
        {
            if (_session == null || _session.Save == null || !IsOpen)
                return;

            _coins.text = _session.Save.coins.ToString();
            _status.text = Mathf.RoundToInt(_session.CatchProgress * 100f) + "% to next bite";

            var fish = _session.BasketFish;
            _sellLabel.text = fish > 0
                ? "Sell " + fish + " fish   +" + fish * _session.FishPrice
                : "Basket is empty";
        }

        string SkinLabel(bool isCat, int equipped, int index)
        {
            if (index == equipped)
                return "✓";
            if (ShopDefs.OwnsSkin(_session.Save, isCat, index))
                return "own";
            var cost = ShopDefs.Skins(isCat)[index].Cost;
            return cost > 0 ? cost.ToString() : "free";
        }

        void Build()
        {
            _font = UiKit.PixelFont;

            // One sliced frame carries the fill, the 1px outline and the bevel. Layering
            // separate fill and border images would double the edge and muddy it.
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

            var pad = 12f;
            var y = 0f;

            // Header doubles as the drag handle.
            var header = AddSurface(_panel, "Header", UiKit.HeaderFace, true);
            _header = header.rectTransform;
            _header.anchorMin = new Vector2(0f, 1f);
            _header.anchorMax = new Vector2(1f, 1f);
            _header.pivot = new Vector2(0.5f, 1f);
            _header.offsetMin = new Vector2(3f, 0f);
            _header.offsetMax = new Vector2(-3f, -3f);
            _header.sizeDelta = new Vector2(-6f, HeaderH);

            MakeLabel(_header, "Title", new Vector2(pad, -7f), 13, TextAnchor.UpperLeft,
                UiKit.TextOnHeader, "Fish Basket", 180f, FontStyle.Bold);
            _coins = MakeLabel(_header, "Coins", new Vector2(-pad - 16f, -7f), 13, TextAnchor.UpperRight,
                UiKit.Accent, "0", 120f, FontStyle.Bold);

            var coinIcon = AddImage(_header, "CoinIcon", UiKit.Coin, Color.white);
            coinIcon.type = Image.Type.Simple;
            var coinRt = coinIcon.rectTransform;
            coinRt.anchorMin = new Vector2(1f, 1f);
            coinRt.anchorMax = new Vector2(1f, 1f);
            coinRt.pivot = new Vector2(1f, 1f);
            coinRt.anchoredPosition = new Vector2(-pad, -7f);
            coinRt.sizeDelta = new Vector2(13f, 13f);

            y -= HeaderH + 10f;

            var sell = AddButton(_panel, "Sell", new Rect(pad, y, _width - pad * 2f, 30f), "Sell",
                () => _session.SellAll(), () => _session.BasketFish > 0, UiKit.SellFace, 12);
            _sellLabel = sell.Label;
            y -= 38f;

            MakeLabel(_panel, "UpgradeHeader", new Vector2(pad, y), 10, TextAnchor.UpperLeft,
                UiKit.TextMuted, "UPGRADES", 200f, FontStyle.Bold);
            y -= 16f;

            for (var i = 0; i < 3; i++)
            {
                var kind = (UpgradeKind)i;
                var row = AddSurface(_panel, kind + "Row", UiKit.FieldFace, false);
                Place(row.rectTransform, new Rect(pad, y, _width - pad * 2f, 24f));

                _upgradeLabels.Add(MakeLabel(_panel, kind + "Label", new Vector2(pad + 8f, y - 5f), 11,
                    TextAnchor.UpperLeft, UiKit.TextDark, "", _width - 100f, FontStyle.Normal));

                var captured = kind;
                _upgradeButtons.Add(AddButton(_panel, kind + "Buy",
                    new Rect(_width - pad - 62f, y - 2f, 60f, 20f), "",
                    () => _session.TryBuyUpgrade(captured),
                    () =>
                    {
                        var lvl = ShopDefs.Level(_session.Save, captured);
                        var cost = ShopDefs.NextCost(captured, lvl);
                        return cost >= 0 && _session.Save.coins >= cost;
                    }, UiKit.ButtonFace, 10));
                y -= 28f;
            }

            y -= 6f;
            MakeLabel(_panel, "SkinHeader", new Vector2(pad, y), 10, TextAnchor.UpperLeft,
                UiKit.TextMuted, "SKINS", 200f, FontStyle.Bold);
            y -= 16f;

            BuildSkinRow("Cat", ref y, pad, true, _catSkins);
            BuildSkinRow("Basket", ref y, pad, false, _basketSkins);

            y -= 4f;
            _status = MakeLabel(_panel, "Status", new Vector2(pad, y - 2f), 10, TextAnchor.UpperLeft,
                UiKit.TextMuted, "", _width - pad * 2f - 136f, FontStyle.Normal);

            var close = AddButton(_panel, "Close", new Rect(_width - pad - 24f, y, 24f, 18f), "✕",
                Close, null, UiKit.ButtonFace, 10);
            var quit = AddButton(_panel, "Quit", new Rect(_width - pad - 74f, y, 46f, 18f), "quit",
                QuitGame, null, UiKit.QuitFace, 10);
            var stats = AddButton(_panel, "Stats", new Rect(_width - pad - 130f, y, 52f, 18f), "stats",
                () => OnStatsRequested?.Invoke(), null, UiKit.ButtonFace, 10);
            _ = close;
            _ = quit;
            _ = stats;
            y -= 24f;

            _height = -y + 8f;
            _panel.sizeDelta = new Vector2(_width, _height);

            // Resize grip. The grab area is larger than the visible mark, which stays small.
            var hit = AddImage(_panel, "GripHit", UiKit.Sunken(UiKit.FieldFace), new Color(1f, 1f, 1f, 0f));
            _grip = hit.rectTransform;
            _grip.anchorMin = new Vector2(1f, 0f);
            _grip.anchorMax = new Vector2(1f, 0f);
            _grip.pivot = new Vector2(1f, 0f);
            _grip.anchoredPosition = Vector2.zero;
            _grip.sizeDelta = new Vector2(GripSize * 2.2f, GripSize * 2.2f);

            var mark = AddImage(_grip, "GripMark", UiKit.Sunken(UiKit.FieldFace), Color.white);
            var markRt = mark.rectTransform;
            markRt.anchorMin = new Vector2(1f, 0f);
            markRt.anchorMax = new Vector2(1f, 0f);
            markRt.pivot = new Vector2(1f, 0f);
            markRt.anchoredPosition = new Vector2(-3f, 3f);
            markRt.sizeDelta = new Vector2(GripSize, GripSize);

            _screenPos = new Vector2(Screen.width * 0.5f, Screen.height * 0.35f);
            ApplyTransform();
            _panel.gameObject.SetActive(false);
        }

        void BuildSkinRow(string name, ref float y, float pad, bool isCat, List<MenuButton> into)
        {
            MakeLabel(_panel, name + "SkinLabel", new Vector2(pad, y - 4f), 10, TextAnchor.UpperLeft,
                UiKit.TextMuted, name, 60f, FontStyle.Normal);

            var skins = ShopDefs.Skins(isCat);
            var swatchBase = isCat ? ShopDefs.CatSwatchBase : ShopDefs.BasketSwatchBase;
            var x = pad + 52f;
            var slot = Mathf.Min(46f, (_width - pad * 2f - 52f) / Mathf.Max(1, skins.Length));

            for (var i = 0; i < skins.Length; i++)
            {
                var index = i;
                // The swatch previews the skin applied to a representative fur tone.
                var face = PaletteSwap.Preview(swatchBase, skins[i]);
                var button = AddButton(_panel, name + "Skin" + i, new Rect(x, y, slot - 5f, 19f), "",
                    () => _session.TryBuySkin(isCat, index),
                    () => ShopDefs.OwnsSkin(_session.Save, isCat, index)
                          || _session.Save.coins >= skins[index].Cost,
                    face, 10);
                into.Add(button);
                x += slot;
            }

            y -= 25f;
        }

        static void Stretch(RectTransform rt, float inset)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
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

        /// <summary>The colour is baked into the sprite, so the Image itself is drawn white.</summary>
        static Image AddSurface(Transform parent, string name, Color face, bool raised)
        {
            return AddImage(parent, name, raised ? UiKit.Raised(face) : UiKit.Sunken(face), Color.white);
        }

        MenuButton AddButton(RectTransform parent, string name, Rect rect, string label,
            Action onClick, Func<bool> enabled, Color face, int fontSize)
        {
            var image = AddSurface(parent, name, face, true);
            Place(image.rectTransform, rect);

            var text = MakeLabel(image.rectTransform, name + "Text", Vector2.zero, fontSize,
                TextAnchor.MiddleCenter, UiKit.TextDark, label, rect.width, FontStyle.Normal);
            var textRt = text.rectTransform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.pivot = new Vector2(0.5f, 0.5f);
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;

            var button = new MenuButton
            {
                Rect = image.rectTransform,
                Background = image,
                Label = text,
                OnClick = onClick,
                Enabled = enabled,
                Normal = UiKit.Raised(face),
                Hover = UiKit.Raised(UiKit.Lighter(face)),
                Disabled = UiKit.Sunken(UiKit.Dimmer(face))
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
