using UnityEngine;

namespace TableWorld.View.Builtin
{
    /// <summary>
    /// Procedural placeholder art for <c>builtin:</c> assets (MVP asset kit, T1.6). Nothing here comes from a third party.
    /// <list type="bullet">
    /// <item><c>builtin:standard_deck</c>: 10 × 7 sheet. Index = suit × 13 + rank − 1 with suits clubs, diamonds, hearts, spades and
    /// ranks A, 2–10, J, Q, K (0–51); 52 red joker, 53 black joker; 54–69 blank.</item>
    /// <item><c>builtin:standard_back</c>: one card back.</item>
    /// <item><c>builtin:dice_numbers</c>: 5 × 4 atlas with 1–20 (index = number − 1), 6 and 9 underlined.</item>
    /// </list>
    /// </summary>
    public static class BuiltinTextures
    {
        public const int DeckColumns = 10;
        public const int DeckRows = 7;
        public const int DiceColumns = 5;
        public const int DiceRows = 4;
        /// <summary>
        /// How far a die number reaches from its cell's centre, as a share of the cell. The rest of the cell is blank
        /// paper, so a face whose corners reach past its own cell (triangles, kites) shows no other number.
        /// </summary>
        public const float DiceGlyphHalf = 0.22f;

        private const int CardWidth = 180;
        private const int CardHeight = 252;

        private static readonly Color32 Paper = new Color32(246, 242, 232, 255);
        private static readonly Color32 Ink = new Color32(28, 28, 30, 255);
        private static readonly Color32 Red = new Color32(196, 30, 42, 255);
        private static readonly Color32 Border = new Color32(190, 184, 170, 255);
        private static readonly Color32 BackBlue = new Color32(28, 58, 120, 255);
        private static readonly Color32 BackLine = new Color32(70, 110, 190, 255);
        private static readonly string[] Ranks = { "A", "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K" };

        public static Texture2D Create(string name)
        {
            switch (name)
            {
                case "standard_deck": return StandardDeck();
                case "standard_back": return StandardBack();
                case "dice_numbers": return DiceNumbers();
                case "d6_template": return D6Template();
                default: return null;
            }
        }

        public static (int Columns, int Rows) GridOf(string name)
        {
            switch (name)
            {
                case "standard_deck": return (DeckColumns, DeckRows);
                case "dice_numbers": return (DiceColumns, DiceRows);
                default: return (1, 1);
            }
        }

        public static Texture2D StandardDeck()
        {
            var canvas = new Canvas(CardWidth * DeckColumns, CardHeight * DeckRows);
            for (var index = 0; index < DeckColumns * DeckRows; index++)
            {
                var x = index % DeckColumns * CardWidth;
                var y = index / DeckColumns * CardHeight;
                if (index < 52)
                    DrawCard(canvas, x, y, (PixelFont.Suit)(index / 13), index % 13);
                else if (index < 54)
                    DrawJoker(canvas, x, y, index == 52 ? Red : Ink);
                else
                    DrawBlank(canvas, x, y);
            }
            return canvas.ToTexture("builtin:standard_deck");
        }

        public static Texture2D StandardBack()
        {
            var canvas = new Canvas(CardWidth, CardHeight);
            canvas.FillRect(0, 0, CardWidth, CardHeight, Paper);
            canvas.FillRect(8, 8, CardWidth - 16, CardHeight - 16, BackBlue);
            canvas.StrokeRect(14, 14, CardWidth - 28, CardHeight - 28, 3, BackLine);
            // Diagonal lattice inside the frame.
            for (var py = 20; py < CardHeight - 20; py++)
            {
                for (var px = 20; px < CardWidth - 20; px++)
                {
                    if ((px + py) % 18 < 2 || (px - py + 1000) % 18 < 2)
                        canvas.FillRect(px, py, 1, 1, BackLine);
                }
            }
            return canvas.ToTexture("builtin:standard_back");
        }

        public static Texture2D DiceNumbers()
        {
            const int cell = 128;
            const int scale = 5;
            var canvas = new Canvas(cell * DiceColumns, cell * DiceRows);
            canvas.FillRect(0, 0, canvas.Width, canvas.Height, Paper);
            for (var n = 1; n <= DiceColumns * DiceRows; n++)
            {
                var x = (n - 1) % DiceColumns * cell;
                var y = (n - 1) / DiceColumns * cell;
                var text = n.ToString();
                var tx = x + (cell - PixelFont.TextWidth(text, scale)) / 2;
                var ty = y + (cell - PixelFont.GlyphHeight * scale) / 2 - 2;
                PixelFont.DrawText(canvas, text, tx, ty, scale, Ink);
                if (n == 6 || n == 9)
                    canvas.FillRect(tx, ty + PixelFont.GlyphHeight * scale + 4, PixelFont.TextWidth(text, scale), 4, Ink);
            }
            return canvas.ToTexture("builtin:dice_numbers");
        }

        /// <summary>
        /// A custom d6 image in the TTS template (G21): the top third empty, then 2 4 5 / 1 3 6, the 1 and 6 upside down,
        /// as TTS expects. Faces are tinted so a rolled die reads at a glance; makers can copy the layout.
        /// </summary>
        public static Texture2D D6Template()
        {
            const int cell = 96;
            const int scale = 7;
            var canvas = new Canvas(cell * 3, cell * 3);
            canvas.FillRect(0, 0, canvas.Width, canvas.Height, Paper);
            var values = new[] { 2, 4, 5, 1, 3, 6 };
            var colors = new[]
            {
                new Color32(248, 226, 226, 255), new Color32(230, 236, 245, 255), new Color32(238, 230, 246, 255),
                new Color32(245, 232, 214, 255), new Color32(226, 242, 228, 255), new Color32(246, 242, 214, 255),
            };
            for (var i = 0; i < 6; i++)
            {
                var x = i % 3 * cell;
                var y = cell + i / 3 * cell;
                var value = values[i];
                canvas.FillRect(x, y, cell, cell, colors[value - 1]);
                canvas.StrokeRect(x + 4, y + 4, cell - 8, cell - 8, 3, Border);
                var text = value.ToString();
                var width = PixelFont.TextWidth(text, scale);
                var tx = x + (cell - width) / 2;
                var ty = y + (cell - PixelFont.GlyphHeight * scale) / 2;
                var upsideDown = value == 1 || value == 6;
                PixelFont.DrawText(canvas, text, upsideDown ? tx + width : tx, ty,
                    scale, value == 1 ? Red : Ink, rotate180: upsideDown);
                // A bar under the 6 (above it, as it is printed upside down) tells it from a 9.
                if (value == 6)
                    canvas.FillRect(tx, ty - 7, width, 4, Ink);
            }
            return canvas.ToTexture("builtin:d6_template");
        }

        /// <summary>
        /// What an image shows when its file is not there (not in the package, not in the TTS cache, or its address no
        /// longer works): a grey card with a question mark in each of the image's <paramref name="columns"/> ×
        /// <paramref name="rows"/> cells, so every card of a missing sheet says so instead of showing a fragment or a
        /// blank. No words: it reads the same in every language.
        /// </summary>
        public static Texture2D Unavailable(int columns, int rows)
        {
            columns = Mathf.Max(1, columns);
            rows = Mathf.Max(1, rows);
            var cell = Mathf.Max(16, Mathf.Min(128, 2048 / Mathf.Max(columns, rows)));
            var canvas = new Canvas(cell * columns, cell * rows);
            var fill = new Color32(200, 198, 194, 255);
            var mark = new Color32(120, 118, 114, 255);
            var scale = Mathf.Max(1, cell / 16);
            for (var i = 0; i < columns * rows; i++)
            {
                var x = i % columns * cell;
                var y = i / columns * cell;
                canvas.FillRect(x, y, cell, cell, fill);
                canvas.StrokeRect(x + scale, y + scale, cell - 2 * scale, cell - 2 * scale, Mathf.Max(1, scale / 2), mark);
                PixelFont.DrawText(canvas, "?", x + (cell - PixelFont.TextWidth("?", scale)) / 2, y + (cell - PixelFont.GlyphHeight * scale) / 2, scale, mark);
            }
            return canvas.ToTexture($"builtin:unavailable_{columns}x{rows}");
        }

        private static void DrawFrame(Canvas canvas, int x, int y)
        {
            canvas.FillRect(x, y, CardWidth, CardHeight, Paper);
            canvas.StrokeRect(x + 2, y + 2, CardWidth - 4, CardHeight - 4, 2, Border);
        }

        private static void DrawCard(Canvas canvas, int x, int y, PixelFont.Suit suit, int rank)
        {
            DrawFrame(canvas, x, y);
            var color = suit == PixelFont.Suit.Hearts || suit == PixelFont.Suit.Diamonds ? Red : Ink;
            var label = Ranks[rank];

            // Corner indices, the bottom-right one rotated like a printed card.
            const int textScale = 4;
            const int suitScale = 3;
            PixelFont.DrawText(canvas, label, x + 12, y + 12, textScale, color);
            PixelFont.DrawSuit(canvas, suit, x + 12, y + 12 + 7 * textScale + 6, suitScale, color);
            var right = x + CardWidth - 12;
            var bottom = y + CardHeight - 12;
            PixelFont.DrawText(canvas, label, right, bottom - 7 * textScale, textScale, color, rotate180: true);
            PixelFont.DrawSuit(canvas, suit, right - 7 * suitScale, bottom - 7 * textScale - 6 - 7 * suitScale, suitScale, color, rotate180: true);

            // Centre: big suit for pips, big letter above a suit for court cards.
            var cx = x + CardWidth / 2;
            var cy = y + CardHeight / 2;
            if (rank >= 10)
            {
                const int letterScale = 10;
                PixelFont.DrawText(canvas, label, cx - PixelFont.TextWidth(label, letterScale) / 2, cy - 7 * letterScale / 2 - 26, letterScale, color);
                PixelFont.DrawSuit(canvas, suit, cx - 7 * 4 / 2, cy + 26, 4, color);
            }
            else
            {
                const int pipScale = 10;
                PixelFont.DrawSuit(canvas, suit, cx - 7 * pipScale / 2, cy - 7 * pipScale / 2, pipScale, color);
            }
        }

        private static void DrawJoker(Canvas canvas, int x, int y, Color32 color)
        {
            DrawFrame(canvas, x, y);
            const int scale = 5;
            const string word = "JOKER";
            PixelFont.DrawText(canvas, word, x + (CardWidth - PixelFont.TextWidth(word, scale)) / 2, y + CardHeight / 2 - 7 * scale / 2, scale, color);
            PixelFont.DrawText(canvas, "J", x + 12, y + 12, 4, color);
            PixelFont.DrawText(canvas, "J", x + CardWidth - 12, y + CardHeight - 12 - 28, 4, color, rotate180: true);
        }

        private static void DrawBlank(Canvas canvas, int x, int y)
        {
            DrawFrame(canvas, x, y);
        }
    }
}
