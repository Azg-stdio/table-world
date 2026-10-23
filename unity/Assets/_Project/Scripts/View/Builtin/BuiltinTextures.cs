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
            const int cell = 64;
            const int scale = 4;
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
                    canvas.FillRect(tx, ty + PixelFont.GlyphHeight * scale + 3, PixelFont.TextWidth(text, scale), 3, Ink);
            }
            return canvas.ToTexture("builtin:dice_numbers");
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
