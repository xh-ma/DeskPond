using UnityEditor;
using UnityEngine;

/// <summary>
/// Forces pixel-art import settings on anything under an "Art" folder, so a newly added PNG is
/// never silently imported with bilinear filtering and compression. That blurs pixel art and is
/// the classic "why does my sprite look fuzzy" trap.
///
/// This is an AssetPostprocessor: Unity calls it automatically during import. It only runs on
/// first import (or a manual Reimport), so it never fights settings you change by hand afterwards.
/// </summary>
public class PixelArtImporter : AssetPostprocessor
{
    const float PixelsPerUnit = 16f;

    void OnPreprocessTexture()
    {
        // Covers the hand-drawn sprites and the animation strips under Resources.
        var path = assetPath.Replace('\\', '/');
        if (!path.Contains("/Art/") && !path.Contains("/CatFrames/") && !path.Contains("/Icon/"))
            return;

        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.spritePixelsPerUnit = PixelsPerUnit;
        importer.textureCompression = TextureImporterCompression.Uncompressed;

        // Palette-swap skins call GetPixels() on these textures at runtime, which needs the CPU
        // copy kept around. These sprites are a few hundred bytes each, so the cost is nil.
        importer.isReadable = true;

        var settings = importer.GetDefaultPlatformTextureSettings();
        settings.format = TextureImporterFormat.RGBA32;
        settings.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SetPlatformTextureSettings(settings);

        Debug.Log("PixelArtImporter: applied point-filter settings to " + path);
    }

    /// <summary>
    /// Pixel fonts must render without anti-aliasing or the glyphs turn to grey mush at small
    /// sizes. HintedRaster snaps each glyph to the pixel grid and skips smoothing entirely.
    /// </summary>
    void OnPreprocessAsset()
    {
        if (!(assetImporter is TrueTypeFontImporter font))
            return;

        var path = assetPath.Replace('\\', '/');
        if (!path.Contains("/Fonts/"))
            return;

        font.fontRenderingMode = FontRenderingMode.HintedRaster;
        font.fontTextureCase = FontTextureCase.Dynamic;
        font.includeFontData = true;
        Debug.Log("PixelArtImporter: set raster (no anti-alias) font rendering for " + path);
    }
}
