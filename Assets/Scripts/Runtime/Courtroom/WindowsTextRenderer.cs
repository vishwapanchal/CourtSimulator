using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace CourtSim.Runtime.Courtroom
{
    /// <summary>
    /// Draws one line of text into a texture with Windows' own text engine (GDI).
    /// </summary>
    /// <remarks>
    /// Unity's TextMesh places letters one by one, which breaks Kannada and Devanagari: their letters join into
    /// conjuncts and vowel signs move around the consonant. Windows shapes them properly, so court signs are drawn
    /// here. On other platforms <see cref="Render"/> returns null and the caller shows English instead.
    /// </remarks>
    internal static class WindowsTextRenderer
    {
        /// <summary>
        /// Returns <paramref name="text"/> as white letters on a transparent background, with mipmaps, or null when
        /// not running on Windows or when drawing fails. The caller owns (and must destroy) the texture.
        /// </summary>
        public static Texture2D Render(string text, string fontFace, int pixelHeight, bool bold)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            try
            {
                return RenderWithGdi(text, fontFace, pixelHeight, bold);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Could not draw sign text with Windows fonts: {ex.Message}");
                return null;
            }
#else
            return null;
#endif
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private const int DefaultCharset = 1;
        private const int AntialiasedQuality = 4; // grey-scale smoothing; ClearType's coloured fringes would show
        private const int TransparentBackground = 1;
        private const uint DtCenter = 0x1;
        private const uint DtVCenter = 0x4;
        private const uint DtSingleLine = 0x20;
        private const uint DtCalcRect = 0x400;
        private const uint DtNoPrefix = 0x800;

        private static Texture2D RenderWithGdi(string text, string fontFace, int pixelHeight, bool bold)
        {
            IntPtr dc = CreateCompatibleDC(IntPtr.Zero);
            IntPtr font = IntPtr.Zero;
            IntPtr bitmap = IntPtr.Zero;
            try
            {
                font = CreateFontW(-pixelHeight, 0, 0, 0, bold ? 700 : 400, 0, 0, 0, DefaultCharset, 0, 0, AntialiasedQuality, 0, fontFace);
                SelectObject(dc, font);

                // Measure first so the bitmap fits the shaped line exactly, plus a margin for overhanging marks.
                var measured = new GdiRect();
                DrawTextW(dc, text, -1, ref measured, DtCalcRect | DtSingleLine | DtNoPrefix);
                int margin = Mathf.Max(2, pixelHeight / 5);
                int width = measured.Right + (2 * margin);
                int height = measured.Bottom + (2 * margin);
                if (measured.Right <= 0 || measured.Bottom <= 0)
                {
                    return null;
                }

                var header = new BitmapInfoHeader
                {
                    Size = 40,
                    Width = width,
                    Height = -height, // negative: rows run top to bottom
                    Planes = 1,
                    BitCount = 32,
                };
                bitmap = CreateDIBSection(dc, ref header, 0, out IntPtr bits, IntPtr.Zero, 0);
                if (bitmap == IntPtr.Zero || bits == IntPtr.Zero)
                {
                    return null;
                }

                SelectObject(dc, bitmap);
                SetBkMode(dc, TransparentBackground);
                SetTextColor(dc, 0x00FFFFFF);
                var area = new GdiRect { Left = margin, Top = margin, Right = width - margin, Bottom = height - margin };
                DrawTextW(dc, text, -1, ref area, DtCenter | DtVCenter | DtSingleLine | DtNoPrefix);
                GdiFlush();

                var raw = new byte[width * height * 4];
                Marshal.Copy(bits, raw, 0, raw.Length);

                // White letters on black: the brightness is how much of each pixel the letter covers.
                var pixels = new Color32[width * height];
                for (int y = 0; y < height; y++)
                {
                    int sourceRow = (height - 1 - y) * width; // Unity textures start at the bottom row
                    for (int x = 0; x < width; x++)
                    {
                        int source = (sourceRow + x) * 4;
                        byte coverage = Math.Max(raw[source], Math.Max(raw[source + 1], raw[source + 2]));
                        pixels[(y * width) + x] = new Color32(255, 255, 255, coverage);
                    }
                }

                var texture = new Texture2D(width, height, TextureFormat.RGBA32, true)
                {
                    name = "SignText",
                    hideFlags = HideFlags.DontSave,
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Trilinear,
                    anisoLevel = 4,
                };
                texture.SetPixels32(pixels);
                texture.Apply(true, true);
                return texture;
            }
            finally
            {
                DeleteDC(dc);
                if (bitmap != IntPtr.Zero)
                {
                    DeleteObject(bitmap);
                }

                if (font != IntPtr.Zero)
                {
                    DeleteObject(font);
                }
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct GdiRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfoHeader
        {
            public uint Size;
            public int Width;
            public int Height;
            public ushort Planes;
            public ushort BitCount;
            public uint Compression;
            public uint SizeImage;
            public int XPelsPerMeter;
            public int YPelsPerMeter;
            public uint ClrUsed;
            public uint ClrImportant;
        }

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr dc);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr dc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr dc, IntPtr gdiObject);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr gdiObject);

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateFontW(
            int height, int width, int escapement, int orientation, int weight, uint italic, uint underline, uint strikeOut,
            uint charSet, uint outPrecision, uint clipPrecision, uint quality, uint pitchAndFamily, string faceName);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfoHeader header, uint usage, out IntPtr bits, IntPtr section, uint offset);

        [DllImport("gdi32.dll")]
        private static extern int SetBkMode(IntPtr dc, int mode);

        [DllImport("gdi32.dll")]
        private static extern uint SetTextColor(IntPtr dc, uint color);

        [DllImport("gdi32.dll")]
        private static extern bool GdiFlush();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int DrawTextW(IntPtr dc, string text, int length, ref GdiRect rect, uint format);
#endif
    }
}
