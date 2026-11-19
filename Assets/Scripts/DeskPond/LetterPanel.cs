using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DeskPond
{
    /// <summary>
    /// The sheet of paper that unrolls from a mail bottle: the cat's letter, and either a KEEP
    /// button or two short answers to send back. Hit-tested by hand against the OS cursor, like
    /// the other panels, because the overlay never has focus.
    /// </summary>
    public class LetterPanel : MonoBehaviour
    {
        const float W = 156f, H = 104f;

        static readonly Color Paper = new Color32(0xFF, 0xF6, 0xE0, 255);
        static readonly Color PaperEdge = new Color32(0xF2, 0xE2, 0xC0, 255);
        static readonly Color Ink = new Color32(0x4A, 0x3A, 0x52, 255);
        static readonly Color InkSoft = new Color32(0x9A, 0x80, 0x8A, 255);

        class Btn
        {
            public RectTransform Rect;
            public Image Face;
            public Color Color;
            public Action Click;
            public bool Hovered;
        }

        DesktopWindow _window;
        CanvasScaler _scaler;
        RectTransform _panel, _body;
        Font _font;
        readonly List<Btn> _buttons = new List<Btn>();
        Vector2 _anchor;

        public bool IsOpen => _panel != null && _panel.gameObject.activeSelf;

        public static LetterPanel Create()
        {
            var root = new GameObject("LetterPanel");
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 55;
            canvas.pixelPerfect = true;
            var p = root.AddComponent<LetterPanel>();
            p._window = DesktopWindow.Instance;
            p._scaler = root.AddComponent<CanvasScaler>();
            p._scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            p._font = UiKit.PixelFont;

            p._panel = p.Box(root.transform, "Panel", 0f, 0f, W, H);
            p._panel.anchorMin = p._panel.anchorMax = Vector2.zero;
            p._panel.pivot = new Vector2(0.5f, 0f);
            p._panel.gameObject.SetActive(false);
            return p;
        }

        /// <summary>
        /// Opens the letter above a screen point. <paramref name="onReply"/> gets the tag of the
        /// answer that was tapped, or null when the letter had no question and was just kept.
        /// </summary>
        public void Show(string text, (string Label, string Tag)[] replies, Vector2 screenPoint, Action<string> onReply)
        {
            _buttons.Clear();
            for (var i = _panel.childCount - 1; i >= 0; i--)
                Destroy(_panel.GetChild(i).gameObject);

            var shadow = Surface(_panel, 3f, 3f, W, H, UiKit.Outline);
            shadow.color = new Color(1f, 1f, 1f, 0.3f);
            var body = Surface(_panel, 0f, 0f, W, H, Paper);
            _body = body.rectTransform;

            // A torn-off header strip, the letter, and the sign-off.
            Surface(_body, 3f, 3f, W - 6f, 11f, PaperEdge, false);
            Icon(_body, "mail_bottle", 5f, 3f, 9f, 11f);
            Label(_body, "A LETTER FOR YOU", 17f, 3f, W - 40f, 11f, InkSoft, TextAnchor.MiddleLeft);
            Label(_body, text, 8f, 19f, W - 16f, 52f, Ink, TextAnchor.UpperLeft);
            Label(_body, "- YOUR CAT", 8f, H - 31f, W - 30f, 10f, InkSoft, TextAnchor.MiddleRight);
            Icon(_body, "heart", W - 20f, H - 31f, 10f, 10f);

            if (replies != null && replies.Length > 0)
            {
                var bw = (W - 16f - (replies.Length - 1) * 4f) / replies.Length;
                for (var i = 0; i < replies.Length; i++)
                {
                    var tag = replies[i].Tag;
                    Button(replies[i].Label, 8f + i * (bw + 4f), H - 19f, bw, 14f, i == 0 ? UiKit.SellFace : UiKit.ButtonFace,
                        () => { Close(); onReply?.Invoke(tag); });
                }
            }
            else
            {
                Button("KEEP", W - 58f, H - 19f, 50f, 14f, UiKit.SellFace, () => { Close(); onReply?.Invoke(null); });
            }

            _anchor = screenPoint;
            _panel.gameObject.SetActive(true);
            ApplyTransform();
        }

        public void Close()
        {
            if (_panel != null)
                _panel.gameObject.SetActive(false);
        }

        public bool ContainsScreenPoint(Vector2 screen) =>
            IsOpen && _body != null && RectTransformUtility.RectangleContainsScreenPoint(_body, screen, null);

        public void ClampIntoScreen()
        {
            if (IsOpen)
                ApplyTransform();
        }

        void ApplyTransform()
        {
            var scale = _window != null ? _window.Zoom : 3f;
            // Never larger than the window it lives in.
            scale = Mathf.Min(scale, (Screen.height - 4f) / H, (Screen.width - 4f) / W);
            _scaler.scaleFactor = Mathf.Max(1f, scale);
            var half = W * scale * 0.5f;
            var x = Mathf.Clamp(_anchor.x, half + 2f, Mathf.Max(half + 2f, Screen.width - half - 2f));
            var y = Mathf.Clamp(_anchor.y, 2f, Mathf.Max(2f, Screen.height - H * scale - 2f));
            _panel.anchoredPosition = new Vector2(Mathf.Round(x / scale), Mathf.Round(y / scale));
        }

        void Update()
        {
            if (!IsOpen || _window == null)
                return;
            _window.Fast("letter");
            var mouse = _window.CursorScreen;
            if (ContainsScreenPoint(mouse))
                _window.PointerOverPet = true;
            var pressed = _window.PrimaryPressedThisFrame;
            for (var i = 0; i < _buttons.Count; i++)
            {
                var b = _buttons[i];
                if (b.Rect == null)
                    continue;
                var inside = RectTransformUtility.RectangleContainsScreenPoint(b.Rect, mouse, null);
                if (inside != b.Hovered)
                {
                    b.Hovered = inside;
                    b.Face.sprite = UiKit.Raised(inside ? UiKit.Lighter(b.Color, 0.08f) : b.Color);
                }

                if (inside && pressed)
                {
                    b.Click?.Invoke();
                    return;
                }
            }
        }

        // ------------------------------------------------------------------ building blocks

        RectTransform Box(Transform parent, string name, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        Image Surface(Transform parent, float x, float y, float w, float h, Color face, bool raised = true)
        {
            var rt = Box(parent, "Surface", x, y, w, h);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = raised ? UiKit.Raised(face) : UiKit.Sunken(face);
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 1f;
            img.raycastTarget = false;
            return img;
        }

        Text Label(Transform parent, string text, float x, float y, float w, float h, Color color, TextAnchor anchor)
        {
            var rt = Box(parent, "Label", x, y, w, h);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = _font;
            t.fontSize = 8;
            t.color = color;
            t.alignment = anchor;
            t.text = text;
            t.lineSpacing = 1.05f;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            // A long letter steps down a size rather than running over the sign-off.
            while (t.fontSize > 6 && h >= 20f && t.preferredHeight > h)
                t.fontSize--;
            return t;
        }

        void Icon(Transform parent, string atlasName, float x, float y, float w, float h)
        {
            var sprite = Atlas.Ui(atlasName);
            if (sprite == null)
                return;
            var sw = sprite.rect.width;
            var sh = sprite.rect.height;
            var k = Mathf.Min(1f, Mathf.Min(w / sw, h / sh));
            var rt = Box(parent, "Icon", Mathf.Round(x + (w - sw * k) / 2f), Mathf.Round(y + (h - sh * k) / 2f), sw * k, sh * k);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.raycastTarget = false;
        }

        void Button(string text, float x, float y, float w, float h, Color face, Action click)
        {
            var img = Surface(_body, x, y, w, h, face);
            Label(img.transform, text, 0f, 0f, w, h, UiKit.TextDark, TextAnchor.MiddleCenter);
            _buttons.Add(new Btn { Rect = img.rectTransform, Face = img, Color = face, Click = click });
        }
    }
}
