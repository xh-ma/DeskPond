using System.Collections.Generic;
using UnityEngine;

namespace DeskPond
{
    /// <summary>
    /// One recolouring of a sprite's material colours. An HSV shift rather than a list of
    /// replacement colours, so every tone moves together and the shading ramp survives.
    /// </summary>
    public struct SkinDef
    {
        public string Name;
        public float HueShift;   // degrees, applied to the material band
        public float SatScale;
        public float ValScale;

        // Eyes get their own shift so they contrast with the coat instead of disappearing into
        // it: a blue-eyed cat with blue fur reads as a blob. Also an HSV offset, so eyes drawn
        // with more than one tone keep their shading.
        public float EyeHueShift;
        public float EyeSatScale;
        public float EyeValScale;

        public int Cost;

        public SkinDef(string name, float hueShift, float satScale, float valScale, int cost,
            float eyeHueShift = 0f, float eyeSatScale = 1f, float eyeValScale = 1f)
        {
            Name = name;
            HueShift = hueShift;
            SatScale = satScale;
            ValScale = valScale;
            EyeHueShift = eyeHueShift;
            EyeSatScale = eyeSatScale;
            EyeValScale = eyeValScale;
            Cost = cost;
        }

        public bool IsDefault => Mathf.Approximately(HueShift, 0f)
                                 && Mathf.Approximately(SatScale, 1f)
                                 && Mathf.Approximately(ValScale, 1f)
                                 && Mathf.Approximately(EyeHueShift, 0f)
                                 && Mathf.Approximately(EyeSatScale, 1f)
                                 && Mathf.Approximately(EyeValScale, 1f);
    }

    /// <summary>
    /// Rebuilds a texture with the material colours remapped and everything else untouched.
    /// A SpriteRenderer tint would multiply the whole sprite, dulling the outline and the eyes
    /// along with the fur.
    ///
    /// Which pixels count as "material" is decided by hue: the cat's fur and the basket's wicker
    /// both sit in the warm 10-60 degree band, while the outline has no saturation and the eyes are
    /// blue at ~193 degrees. So the filter below leaves outline and eyes alone automatically.
    ///
    /// Results are cached per (texture, skin) because a swap allocates a whole new texture.
    /// </summary>
    public static class PaletteSwap
    {
        const float MaterialHueMin = 5f;
        const float MaterialHueMax = 65f;
        const float MaterialMinSat = 0.12f;

        // The cool band: the cat's eyes sit at ~193 degrees, well clear of the warm coat.
        const float EyeHueMin = 150f;
        const float EyeHueMax = 270f;

        static readonly Dictionary<(int, int), Texture2D> TextureCache =
            new Dictionary<(int, int), Texture2D>();
        static readonly Dictionary<(int, int), Sprite> SpriteCache =
            new Dictionary<(int, int), Sprite>();

        /// <summary>Recolours a whole texture. Returns the original when the skin is a no-op.</summary>
        public static Texture2D Remap(Texture2D source, SkinDef skin)
        {
            if (source == null || skin.IsDefault)
                return source;

            var key = (source.GetInstanceID(), SkinKey(skin));
            if (TextureCache.TryGetValue(key, out var cached) && cached != null)
                return cached;

            Color[] pixels;
            try
            {
                pixels = source.GetPixels();
            }
            catch (UnityException)
            {
                // Texture is not marked readable; fall back to the original rather than crashing.
                Debug.LogWarning("DeskPond: " + source.name +
                                 " is not Read/Write enabled, so its skin cannot be recoloured.");
                return source;
            }

            for (var i = 0; i < pixels.Length; i++)
                pixels[i] = RemapPixel(pixels[i], skin);

            var result = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = source.name + "_" + skin.Name
            };
            result.SetPixels(pixels);
            result.Apply(false, false);

            TextureCache[key] = result;
            return result;
        }

        /// <summary>Recolours a sprite, keeping its rect, pivot and pixels-per-unit.</summary>
        public static Sprite Remap(Sprite source, SkinDef skin)
        {
            if (source == null || skin.IsDefault)
                return source;

            var key = (source.GetInstanceID(), SkinKey(skin));
            if (SpriteCache.TryGetValue(key, out var cached) && cached != null)
                return cached;

            var tex = Remap(source.texture, skin);
            if (tex == source.texture)
                return source;

            var pivot = new Vector2(
                (source.pivot.x) / source.rect.width,
                (source.pivot.y) / source.rect.height);
            var result = Sprite.Create(tex, source.rect, pivot, source.pixelsPerUnit,
                0, SpriteMeshType.FullRect, source.border);
            result.name = source.name + "_" + skin.Name;

            SpriteCache[key] = result;
            return result;
        }

        static Color RemapPixel(Color c, SkinDef skin)
        {
            if (c.a <= 0.02f)
                return c;

            Color.RGBToHSV(c, out var h, out var s, out var v);
            var degrees = h * 360f;

            // The outline has no saturation at all, so it never matches either band and stays black.
            if (s < MaterialMinSat)
                return c;

            if (degrees >= EyeHueMin && degrees <= EyeHueMax)
                return Shift(c, s, v, degrees, skin.EyeHueShift, skin.EyeSatScale, skin.EyeValScale);

            if (degrees >= MaterialHueMin && degrees <= MaterialHueMax)
                return Shift(c, s, v, degrees, skin.HueShift, skin.SatScale, skin.ValScale);

            return c;
        }

        static Color Shift(Color original, float s, float v, float degrees,
            float hueShift, float satScale, float valScale)
        {
            var newHue = Mathf.Repeat(degrees + hueShift, 360f) / 360f;
            var newSat = Mathf.Clamp01(s * satScale);
            var newVal = Mathf.Clamp01(v * valScale);
            var result = Color.HSVToRGB(newHue, newSat, newVal);
            result.a = original.a;
            return result;
        }

        static int SkinKey(SkinDef s) =>
            Mathf.RoundToInt(s.HueShift) * 73856093
            ^ Mathf.RoundToInt(s.SatScale * 100f) * 19349663
            ^ Mathf.RoundToInt(s.ValScale * 100f) * 83492791
            ^ Mathf.RoundToInt(s.EyeHueShift) * 26591
            ^ Mathf.RoundToInt(s.EyeSatScale * 100f) * 40503
            ^ Mathf.RoundToInt(s.EyeValScale * 100f) * 15485863;

        /// <summary>The swatch colour shown in the shop: the skin applied to a representative tone.</summary>
        public static Color Preview(Color representative, SkinDef skin) =>
            RemapPixel(representative, skin);
    }
}
