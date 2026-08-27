using System.Collections.Generic;
using UnityEngine;

namespace DeskPond
{
    /// <summary>
    /// Procedural sprites, fonts and the palette for DeskPond's UI.
    ///
    /// The look is a classic pixel-editor chrome (Aseprite's light theme): a warm grey face, a
    /// hard 1px dark outline, and a bevel made of a white highlight on the top-left with a dark
    /// shadow on the bottom-right. Raised = button, inverted bevel = sunken field.
    ///
    /// Colours are baked into each sprite rather than applied through Image.color, because
    /// Image.color multiplies: a white highlight tinted with a beige face would come out beige.
    /// Sprites are cached per face colour, and hover and disabled states are separate pre-baked
    /// sprites rather than a tint.
    /// </summary>
    public static class UiKit
    {
        public static readonly Color PanelFace = Hex(0xD4D0C4);
        public static readonly Color HeaderFace = Hex(0xA8A49A);
        public static readonly Color FieldFace = Hex(0xC4C0B2);
        public static readonly Color Outline = Hex(0x2A2A28);
        public static readonly Color BevelLight = Hex(0xFFFFFF);
        public static readonly Color BevelDark = Hex(0x6E6A62);

        public static readonly Color ButtonFace = Hex(0xD4D0C4);
        public static readonly Color SellFace = Hex(0x93B383);
        public static readonly Color QuitFace = Hex(0xC79191);

        public static readonly Color ChartBar = Hex(0x7A9CC6);
        public static readonly Color ChartPeak = Hex(0x40699E);
        public static readonly Color ChartTrack = Hex(0xBDB9AC);

        public static readonly Color TextDark = Hex(0x1A1A18);
        public static readonly Color TextMuted = Hex(0x55524A);
        public static readonly Color TextOnHeader = Hex(0x14140F);
        public static readonly Color Accent = Hex(0x6B4A10);

        static Color Hex(int rgb) => new Color(
            ((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);

        static Font _font;

        /// <summary>
        /// The pixel UI font, loaded from Resources so no scene wiring is needed. Falls back to
        /// Unity's built-in face when the TTF is absent, so the panel still renders.
        /// </summary>
        public static Font PixelFont
        {
            get
            {
                if (_font != null)
                    return _font;
                _font = Resources.Load<Font>("Fonts/Silkscreen-Regular");
                if (_font == null)
                    _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (_font == null)
                    _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                return _font;
            }
        }

        static readonly Dictionary<int, Sprite> Cache = new Dictionary<int, Sprite>();
        static Sprite _coin;
        static Sprite _gift;

        /// <summary>Raised bevel: white top-left, dark bottom-right. Buttons and window bodies.</summary>
        public static Sprite Raised(Color face) => Get(face, true);

        /// <summary>Sunken bevel: the inverse, for rows and fields that should read as recessed.</summary>
        public static Sprite Sunken(Color face) => Get(face, false);

        /// <summary>The same face, lightened. Used for hover instead of a whole-sprite multiply.</summary>
        public static Color Lighter(Color c, float amount = 0.10f) => new Color(
            Mathf.Clamp01(c.r + amount), Mathf.Clamp01(c.g + amount), Mathf.Clamp01(c.b + amount), c.a);

        public static Color Dimmer(Color c, float amount = 0.14f) => new Color(
            Mathf.Clamp01(c.r - amount), Mathf.Clamp01(c.g - amount), Mathf.Clamp01(c.b - amount), c.a);

        public static Sprite Coin => _coin != null ? _coin : _coin = BuildCoin();

        static Texture2D _resizeCursor;

        /// <summary>
        /// A north-west/south-east resize arrow, drawn to match the pixel UI rather than using the
        /// smooth OS cursor. Hotspot is the centre.
        /// </summary>
        public static Texture2D ResizeCursor
        {
            get
            {
                if (_resizeCursor != null)
                    return _resizeCursor;

                const int n = 16;
                var tex = NewTex(n, n);
                var px = new Color[n * n];
                for (var i = 0; i < px.Length; i++)
                    px[i] = Color.clear;

                void Put(int x, int y, Color c)
                {
                    if (x >= 0 && x < n && y >= 0 && y < n) px[y * n + x] = c;
                }

                var line = Color.white;
                var edge = new Color(0f, 0f, 0f, 1f);

                // Diagonal shaft from top-left to bottom-right, with arrow heads at both ends.
                for (var i = 3; i <= 12; i++)
                {
                    Put(i, n - 1 - i, line);
                    Put(i + 1, n - 1 - i, edge);
                    Put(i, n - i, edge);
                }

                for (var i = 0; i < 4; i++)
                {
                    Put(3 + i, n - 1 - 3, line);        // top-left head, horizontal
                    Put(3, n - 1 - 3 - i, line);        // top-left head, vertical
                    Put(12 - i, n - 1 - 12, line);      // bottom-right head, horizontal
                    Put(12, n - 1 - 12 + i, line);      // bottom-right head, vertical
                }

                tex.SetPixels(px);
                tex.Apply(false, false);
                _resizeCursor = tex;
                return tex;
            }
        }

        public static Sprite GiftSprite() => _gift != null ? _gift : _gift = BuildGift();

        static Sprite _white;

        /// <summary>
        /// A plain white pixel for chart bars and dividers.
        ///
        /// This is the one place where setting Image.color is right rather than wrong: the source
        /// is pure white, so the multiply reproduces the tint exactly. Everything with a bevel
        /// still needs its colour baked, because there a multiply would dirty the highlight.
        /// </summary>
        public static Sprite White
        {
            get
            {
                if (_white != null)
                    return _white;
                var tex = NewTex(1, 1);
                tex.SetPixels(new[] { Color.white });
                tex.Apply(false, false);
                _white = Sprite.Create(tex, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
                return _white;
            }
        }

        static Sprite Get(Color face, bool raised)
        {
            var key = ((int)(face.r * 255) << 17) | ((int)(face.g * 255) << 9)
                      | ((int)(face.b * 255) << 1) | (raised ? 1 : 0);
            if (Cache.TryGetValue(key, out var cached) && cached != null)
                return cached;

            var sprite = BuildBevel(face, raised);
            Cache[key] = sprite;
            return sprite;
        }

        /// <summary>
        /// An 8x8 9-sliced frame with a 3px border, so the outline and bevel stay exactly one
        /// pixel wide no matter how far the middle is stretched.
        /// </summary>
        static Sprite BuildBevel(Color face, bool raised)
        {
            const int n = 8;
            var topLeft = raised ? BevelLight : BevelDark;
            var bottomRight = raised ? BevelDark : BevelLight;

            var tex = NewTex(n, n);
            var px = new Color[n * n];
            for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                // Texture space: y = n-1 is the top row.
                var onEdge = x == 0 || y == 0 || x == n - 1 || y == n - 1;
                var innerTopLeft = x == 1 || y == n - 2;
                var innerBottomRight = x == n - 2 || y == 1;

                Color c;
                if (onEdge) c = Outline;
                else if (innerTopLeft) c = topLeft;
                else if (innerBottomRight) c = bottomRight;
                else c = face;

                px[y * n + x] = c;
            }

            tex.SetPixels(px);
            tex.Apply(false, false);
            return Sprite.Create(tex, new Rect(0f, 0f, n, n), new Vector2(0.5f, 0.5f), 100f,
                0, SpriteMeshType.FullRect, new Vector4(3, 3, 3, 3));
        }

        static Texture2D NewTex(int w, int h)
        {
            return new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                // Point filtering is what keeps the 1px bevel crisp instead of smeared.
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
        }

        static Sprite BuildCoin()
        {
            const int n = 16;
            var rim = new Color(0.42f, 0.27f, 0.04f);
            var body = new Color(0.98f, 0.76f, 0.20f);
            var shine = new Color(1f, 0.93f, 0.62f);
            var deep = new Color(0.76f, 0.52f, 0.10f);

            return Paint(n, n, (x, y) =>
            {
                var dx = (x - 7.5f) / 7.5f;
                var dy = (y - 7.5f) / 7.5f;
                var r = dx * dx + dy * dy;
                if (r > 1f) return Color.clear;
                if (r > 0.72f) return rim;
                if (r > 0.52f) return deep;
                if (dx + dy < -0.45f) return shine;
                if (Mathf.Abs(x - 7.5f) < 1.1f && Mathf.Abs(y - 7.5f) < 3.6f) return deep;
                return body;
            }, 16f);
        }

        static Sprite BuildGift()
        {
            const int n = 14;
            var box = new Color(0.85f, 0.29f, 0.35f);
            var boxDark = new Color(0.63f, 0.19f, 0.26f);
            var ribbon = new Color(1f, 0.88f, 0.45f);

            return Paint(n, n, (x, y) =>
            {
                if (y > 10) return Color.clear;
                var lidTop = y >= 8;
                if (lidTop && (x < 1 || x > 12)) return Color.clear;
                if (!lidTop && (x < 2 || x > 11)) return Color.clear;
                if (Mathf.Abs(x - 6.5f) < 1.1f) return ribbon;
                if (y == 8 || y == 7) return ribbon;
                return lidTop ? box : (y < 3 ? boxDark : box);
            }, 16f);
        }

        static Sprite Paint(int w, int h, System.Func<int, int, Color> shade, float ppu)
        {
            var tex = NewTex(w, h);
            var pixels = new Color[w * h];
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                pixels[y * w + x] = shade(x, y);
            tex.SetPixels(pixels);
            tex.Apply(false, false);
            return Sprite.Create(tex, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0.5f), ppu);
        }
    }
}
