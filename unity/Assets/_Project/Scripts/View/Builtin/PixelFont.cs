using System.Collections.Generic;
using UnityEngine;

namespace TableWorld.View.Builtin
{
    /// <summary>
    /// A 5×7 bitmap font (digits, the letters a card deck needs) and 7×7 suit glyphs, so the builtin textures need no
    /// font asset. Glyph rows go top to bottom; '#' is ink.
    /// </summary>
    public static class PixelFont
    {
        public const int GlyphWidth = 5;
        public const int GlyphHeight = 7;

        private static readonly Dictionary<char, string[]> Glyphs = new Dictionary<char, string[]>
        {
            ['0'] = new[] { " ### ", "#   #", "#  ##", "# # #", "##  #", "#   #", " ### " },
            ['1'] = new[] { "  #  ", " ##  ", "  #  ", "  #  ", "  #  ", "  #  ", " ### " },
            ['2'] = new[] { " ### ", "#   #", "    #", "   # ", "  #  ", " #   ", "#####" },
            ['3'] = new[] { "#####", "   # ", "  #  ", "   # ", "    #", "#   #", " ### " },
            ['4'] = new[] { "   # ", "  ## ", " # # ", "#  # ", "#####", "   # ", "   # " },
            ['5'] = new[] { "#####", "#    ", "#### ", "    #", "    #", "#   #", " ### " },
            ['6'] = new[] { "  ## ", " #   ", "#    ", "#### ", "#   #", "#   #", " ### " },
            ['7'] = new[] { "#####", "    #", "   # ", "  #  ", " #   ", " #   ", " #   " },
            ['8'] = new[] { " ### ", "#   #", "#   #", " ### ", "#   #", "#   #", " ### " },
            ['9'] = new[] { " ### ", "#   #", "#   #", " ####", "    #", "   # ", " ##  " },
            ['A'] = new[] { " ### ", "#   #", "#   #", "#####", "#   #", "#   #", "#   #" },
            ['J'] = new[] { "  ###", "   # ", "   # ", "   # ", "   # ", "#  # ", " ##  " },
            ['Q'] = new[] { " ### ", "#   #", "#   #", "#   #", "# # #", "#  # ", " ## #" },
            ['K'] = new[] { "#   #", "#  # ", "# #  ", "##   ", "# #  ", "#  # ", "#   #" },
            ['O'] = new[] { " ### ", "#   #", "#   #", "#   #", "#   #", "#   #", " ### " },
            ['E'] = new[] { "#####", "#    ", "#    ", "#### ", "#    ", "#    ", "#####" },
            ['R'] = new[] { "#### ", "#   #", "#   #", "#### ", "# #  ", "#  # ", "#   #" },
            ['_'] = new[] { "     ", "     ", "     ", "     ", "     ", "     ", "#####" },
        };

        public enum Suit { Clubs, Diamonds, Hearts, Spades }

        private static readonly Dictionary<Suit, string[]> SuitGlyphs = new Dictionary<Suit, string[]>
        {
            [Suit.Clubs] = new[] { "  ###  ", "  ###  ", "## # ##", "#######", "## # ##", "   #   ", "  ###  " },
            [Suit.Diamonds] = new[] { "   #   ", "  ###  ", " ##### ", "#######", " ##### ", "  ###  ", "   #   " },
            [Suit.Hearts] = new[] { " ## ## ", "#######", "#######", "#######", " ##### ", "  ###  ", "   #   " },
            [Suit.Spades] = new[] { "   #   ", "  ###  ", " ##### ", "#######", "#######", "   #   ", "  ###  " },
        };

        public static int TextWidth(string text, int scale) => text.Length == 0 ? 0 : (text.Length * (GlyphWidth + 1) - 1) * scale;

        /// <summary>Draws text with its top-left corner at (x, y), y measured from the top of the canvas.</summary>
        public static void DrawText(Canvas canvas, string text, int x, int y, int scale, Color32 color, bool rotate180 = false)
        {
            for (var i = 0; i < text.Length; i++)
            {
                if (!Glyphs.TryGetValue(text[i], out var rows))
                    continue;
                var gx = rotate180 ? x - (i + 1) * (GlyphWidth + 1) * scale + scale : x + i * (GlyphWidth + 1) * scale;
                DrawBitmap(canvas, rows, gx, y, scale, color, rotate180);
            }
        }

        public static void DrawSuit(Canvas canvas, Suit suit, int x, int y, int scale, Color32 color, bool rotate180 = false) =>
            DrawBitmap(canvas, SuitGlyphs[suit], x, y, scale, color, rotate180);

        private static void DrawBitmap(Canvas canvas, string[] rows, int x, int y, int scale, Color32 color, bool rotate180)
        {
            var height = rows.Length;
            var width = rows[0].Length;
            for (var row = 0; row < height; row++)
            {
                for (var col = 0; col < width; col++)
                {
                    var r = rotate180 ? height - 1 - row : row;
                    var c = rotate180 ? width - 1 - col : col;
                    if (rows[r][c] == '#')
                        canvas.FillRect(x + col * scale, y + row * scale, scale, scale, color);
                }
            }
        }
    }

    /// <summary>A CPU pixel buffer addressed from the top-left, converted to a Texture2D at the end.</summary>
    public sealed class Canvas
    {
        public readonly int Width;
        public readonly int Height;
        private readonly Color32[] _pixels;

        public Canvas(int width, int height)
        {
            Width = width;
            Height = height;
            _pixels = new Color32[width * height];
        }

        public void FillRect(int x, int y, int w, int h, Color32 color)
        {
            var x0 = Mathf.Max(0, x);
            var y0 = Mathf.Max(0, y);
            var x1 = Mathf.Min(Width, x + w);
            var y1 = Mathf.Min(Height, y + h);
            for (var py = y0; py < y1; py++)
            {
                var row = (Height - 1 - py) * Width;
                for (var px = x0; px < x1; px++)
                    _pixels[row + px] = color;
            }
        }

        public void StrokeRect(int x, int y, int w, int h, int thickness, Color32 color)
        {
            FillRect(x, y, w, thickness, color);
            FillRect(x, y + h - thickness, w, thickness, color);
            FillRect(x, y, thickness, h, color);
            FillRect(x + w - thickness, y, thickness, h, color);
        }

        public Texture2D ToTexture(string name)
        {
            var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, mipChain: true)
            {
                name = name,
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 4,
            };
            texture.SetPixels32(_pixels);
            texture.Apply(updateMipmaps: true, makeNoLongerReadable: true);
            return texture;
        }
    }
}
