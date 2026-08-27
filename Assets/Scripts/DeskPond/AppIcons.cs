using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace DeskPond
{
    /// <summary>
    /// Application icons and real product names for the stats panel, pulled from each executable
    /// once and cached on disk.
    ///
    /// The cache is keyed by process name and holds a PNG per app, so nothing has to store an
    /// executable path. That keeps folder names, which often contain the user's account name, out
    /// of the activity log entirely, and lets icons still appear for apps in old history that are
    /// not running right now.
    ///
    /// Windows only; elsewhere every lookup simply misses and the panel falls back to text.
    /// </summary>
    public static class AppIcons
    {
        const int IconSize = 32;

        static string CacheDir => Path.Combine(Application.persistentDataPath, "icons");

        static readonly Dictionary<string, Sprite> Loaded = new Dictionary<string, Sprite>();
        static readonly Dictionary<string, string> Names = new Dictionary<string, string>();
        static readonly HashSet<string> Attempted = new HashSet<string>();

        /// <summary>Icon for a process name, or null when none has been cached.</summary>
        public static Sprite Get(string process)
        {
            if (string.IsNullOrEmpty(process))
                return null;
            var key = process.ToLowerInvariant();
            if (Loaded.TryGetValue(key, out var cached))
                return cached;

            Sprite sprite = null;
            try
            {
                var path = Path.Combine(CacheDir, key + ".png");
                if (File.Exists(path))
                {
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                    {
                        filterMode = FilterMode.Point,
                        wrapMode = TextureWrapMode.Clamp
                    };
                    if (tex.LoadImage(File.ReadAllBytes(path)))
                    {
                        sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height),
                            new Vector2(0.5f, 0.5f), tex.height);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("DeskPond: could not load icon for " + process + ". " + ex.Message);
            }

            Loaded[key] = sprite;
            return sprite;
        }

        /// <summary>The executable's own product name, e.g. "chrome" becomes "Google Chrome".</summary>
        public static string FriendlyName(string process)
        {
            if (string.IsNullOrEmpty(process))
                return null;
            var key = process.ToLowerInvariant();
            if (Names.TryGetValue(key, out var cached))
                return cached;

            string name = null;
            try
            {
                var path = Path.Combine(CacheDir, key + ".txt");
                if (File.Exists(path))
                {
                    name = File.ReadAllText(path).Trim();
                    if (name.Length == 0)
                        name = null;
                }
            }
            catch
            {
                // A missing or unreadable name just means the panel shows the process name.
            }

            Names[key] = name;
            return name;
        }

        /// <summary>
        /// Extracts and stores the icon and product name for one executable, at most once per
        /// process name per session. Safe to call from the foreground poll.
        /// </summary>
        public static void EnsureCached(string process, string exePath)
        {
            if (string.IsNullOrEmpty(process) || string.IsNullOrEmpty(exePath))
                return;

            var key = process.ToLowerInvariant();
            if (!Attempted.Add(key))
                return;

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            try
            {
                Directory.CreateDirectory(CacheDir);

                var iconPath = Path.Combine(CacheDir, key + ".png");
                if (!File.Exists(iconPath))
                {
                    var tex = Extract(exePath);
                    if (tex != null)
                    {
                        File.WriteAllBytes(iconPath, tex.EncodeToPNG());
                        UnityEngine.Object.Destroy(tex);
                    }
                }

                var namePath = Path.Combine(CacheDir, key + ".txt");
                if (!File.Exists(namePath))
                {
                    var friendly = ReadProductName(exePath);
                    if (!string.IsNullOrEmpty(friendly))
                        File.WriteAllText(namePath, friendly);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("DeskPond: could not cache icon for " + process + ". " + ex.Message);
            }
#endif
        }

        /// <summary>Forgets the cached icons and names, for the panel's clear button.</summary>
        public static void ClearCache()
        {
            Loaded.Clear();
            Names.Clear();
            Attempted.Clear();
            try
            {
                if (Directory.Exists(CacheDir))
                    Directory.Delete(CacheDir, true);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("DeskPond: could not clear icon cache. " + ex.Message);
            }
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        /// <summary>
        /// Shell icon to a Texture2D. The colour bitmap is read as top-down 32bpp; icons that
        /// predate alpha come back fully transparent, so the AND mask supplies the cutout instead.
        /// </summary>
        static Texture2D Extract(string exePath)
        {
            var info = new SHFILEINFO();
            var result = SHGetFileInfo(exePath, 0, ref info, (uint)Marshal.SizeOf(info),
                SHGFI_ICON | SHGFI_LARGEICON);
            if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
                return null;

            try
            {
                if (!GetIconInfo(info.hIcon, out var icon))
                    return null;

                try
                {
                    var pixels = ReadBitmap(icon.hbmColor, out var w, out var h);
                    if (pixels == null)
                        return null;

                    var opaque = false;
                    for (var i = 3; i < pixels.Length; i += 4)
                    {
                        if (pixels[i] != 0) { opaque = true; break; }
                    }

                    if (!opaque)
                    {
                        var mask = ReadBitmap(icon.hbmMask, out var mw, out var mh);
                        if (mask != null && mw == w && mh == h)
                        {
                            // In the AND mask, white means "leave the background alone".
                            for (var i = 0; i < pixels.Length; i += 4)
                                pixels[i + 3] = mask[i] != 0 ? (byte)0 : (byte)255;
                        }
                        else
                        {
                            for (var i = 3; i < pixels.Length; i += 4)
                                pixels[i] = 255;
                        }
                    }

                    return BuildTexture(pixels, w, h);
                }
                finally
                {
                    if (icon.hbmColor != IntPtr.Zero) DeleteObject(icon.hbmColor);
                    if (icon.hbmMask != IntPtr.Zero) DeleteObject(icon.hbmMask);
                }
            }
            finally
            {
                DestroyIcon(info.hIcon);
            }
        }

        static byte[] ReadBitmap(IntPtr bitmap, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (bitmap == IntPtr.Zero)
                return null;

            var bmp = new BITMAP();
            if (GetObject(bitmap, Marshal.SizeOf(bmp), ref bmp) == 0)
                return null;

            width = bmp.bmWidth;
            height = bmp.bmHeight;
            if (width <= 0 || height <= 0 || width > 512 || height > 512)
                return null;

            var header = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf(typeof(BITMAPINFOHEADER)),
                biWidth = width,
                // Negative height asks for top-down rows, which spares a manual flip.
                biHeight = -height,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0
            };

            var buffer = new byte[width * height * 4];
            var dc = GetDC(IntPtr.Zero);
            try
            {
                var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                try
                {
                    var scanned = GetDIBits(dc, bitmap, 0, (uint)height,
                        handle.AddrOfPinnedObject(), ref header, 0);
                    if (scanned == 0)
                        return null;
                }
                finally
                {
                    handle.Free();
                }
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, dc);
            }

            return buffer;
        }

        /// <summary>BGRA rows, top row first, into a Unity texture whose first row is the bottom.</summary>
        static Texture2D BuildTexture(byte[] bgra, int w, int h)
        {
            var colors = new Color32[w * h];
            for (var y = 0; y < h; y++)
            {
                var src = y * w * 4;
                var dst = (h - 1 - y) * w;
                for (var x = 0; x < w; x++)
                {
                    var i = src + x * 4;
                    colors[dst + x] = new Color32(bgra[i + 2], bgra[i + 1], bgra[i], bgra[i + 3]);
                }
            }

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            tex.SetPixels32(colors);
            tex.Apply(false, false);

            if (w == IconSize && h == IconSize)
                return tex;

            var scaled = Downsample(tex, IconSize);
            UnityEngine.Object.Destroy(tex);
            return scaled;
        }

        /// <summary>Nearest neighbour, to keep the icon crisp beside the pixel UI.</summary>
        static Texture2D Downsample(Texture2D source, int size)
        {
            var output = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var sx = Mathf.Min(source.width - 1, x * source.width / size);
                var sy = Mathf.Min(source.height - 1, y * source.height / size);
                pixels[y * size + x] = source.GetPixel(sx, sy);
            }

            output.SetPixels32(pixels);
            output.Apply(false, false);
            return output;
        }

        static string ReadProductName(string exePath)
        {
            var size = GetFileVersionInfoSize(exePath, out _);
            if (size == 0)
                return null;

            var block = new byte[size];
            if (!GetFileVersionInfo(exePath, 0, size, block))
                return null;

            // Ask the file which language block it actually ships rather than assuming US English.
            if (!VerQueryValue(block, "\\VarFileInfo\\Translation", out var langPtr, out var langLen)
                || langLen < 4)
                return null;

            var language = Marshal.ReadInt16(langPtr);
            var codepage = Marshal.ReadInt16(langPtr, 2);
            var prefix = $"\\StringFileInfo\\{(ushort)language:x4}{(ushort)codepage:x4}\\";

            foreach (var field in new[] { "FileDescription", "ProductName" })
            {
                if (VerQueryValue(block, prefix + field, out var valuePtr, out var valueLen)
                    && valueLen > 1)
                {
                    var text = Marshal.PtrToStringUni(valuePtr, (int)valueLen - 1);
                    if (!string.IsNullOrWhiteSpace(text))
                        return text.Trim();
                }
            }

            return null;
        }

        const uint SHGFI_ICON = 0x000000100;
        const uint SHGFI_LARGEICON = 0x000000000;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct ICONINFO
        {
            public bool fIcon;
            public int xHotspot;
            public int yHotspot;
            public IntPtr hbmMask;
            public IntPtr hbmColor;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct BITMAP
        {
            public int bmType;
            public int bmWidth;
            public int bmHeight;
            public int bmWidthBytes;
            public ushort bmPlanes;
            public ushort bmBitsPixel;
            public IntPtr bmBits;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public uint biCompression;
            public uint biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public uint biClrUsed;
            public uint biClrImportant;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SHGetFileInfo(string path, uint attributes, ref SHFILEINFO info,
            uint size, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetIconInfo(IntPtr icon, out ICONINFO info);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool DestroyIcon(IntPtr icon);

        [DllImport("gdi32.dll")]
        static extern int GetObject(IntPtr handle, int count, ref BITMAP obj);

        [DllImport("gdi32.dll")]
        static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines,
            IntPtr bits, ref BITMAPINFOHEADER info, uint usage);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool DeleteObject(IntPtr obj);

        [DllImport("user32.dll")]
        static extern IntPtr GetDC(IntPtr window);

        [DllImport("user32.dll")]
        static extern int ReleaseDC(IntPtr window, IntPtr dc);

        [DllImport("version.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern int GetFileVersionInfoSize(string file, out int handle);

        [DllImport("version.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetFileVersionInfo(string file, int handle, int len, byte[] data);

        [DllImport("version.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool VerQueryValue(byte[] block, string path, out IntPtr value, out uint len);
#endif
    }
}
