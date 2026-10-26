using System;
using Newtonsoft.Json;

namespace Core.Rules
{
    /// <summary>
    /// Words printed on a card instead of an image (TTS has no such thing: its cards are always images). The game draws
    /// the face from these fields on every machine, so a designer can prototype a deck of 200 cards with nothing but text
    /// and edit any card later. A card shows its text face when it has <see cref="CardText"/> and no face image; with
    /// both, the image wins.
    /// </summary>
    public sealed class CardText
    {
        /// <summary>The card's name, in the band at the top.</summary>
        public string? Title { get; set; }
        /// <summary>A short line under the title: the card's type or category, e.g. "Equipment" or "Night".</summary>
        public string? Kind { get; set; }
        /// <summary>The rules text, wrapped and shrunk to fit.</summary>
        public string? Body { get; set; }
        /// <summary>A line at the bottom: cost, stats, flavour, e.g. "Price 10 · Mana 3".</summary>
        public string? Footer { get; set; }
        /// <summary>A short value in the top-right corner, e.g. a cost or strength ("10", "3x").</summary>
        public string? Corner { get; set; }
        /// <summary>Optional image asset drawn between the title and the body (a monster's portrait).</summary>
        public string? Art { get; set; }
        /// <summary>Cell of <see cref="Art"/> when it is a sprite sheet (row-major, 0 is top-left).</summary>
        public int ArtIndex { get; set; }

        public CardText Clone() => (CardText)MemberwiseClone();
    }

    /// <summary>
    /// How text cards look: colours (RGB, 0–1) of the face and the back, and the word printed on the back, which usually
    /// names the deck ("EQUIPMENT", "NIGHT EVENT") so players can tell the piles apart. Set on a deck for all its cards;
    /// a card may carry its own (a day/night deck where night cards are dark). A text card without a back image draws
    /// its back from this style, or from the defaults.
    /// </summary>
    public sealed class CardStyle
    {
        public float[]? Paper { get; set; }
        public float[]? Ink { get; set; }
        /// <summary>The title band and border.</summary>
        public float[]? Accent { get; set; }
        public float[]? BackPaper { get; set; }
        public float[]? BackInk { get; set; }
        public string? BackLabel { get; set; }

        public static readonly float[] DefaultPaper = { 0.96f, 0.94f, 0.88f };
        public static readonly float[] DefaultInk = { 0.12f, 0.11f, 0.1f };
        public static readonly float[] DefaultAccent = { 0.36f, 0.27f, 0.2f };
        public static readonly float[] DefaultBackPaper = { 0.24f, 0.2f, 0.3f };
        public static readonly float[] DefaultBackInk = { 0.92f, 0.86f, 0.7f };

        /// <summary>Same colours and label (a card whose style matches its deck's keeps following the deck).</summary>
        public static bool Same(CardStyle? a, CardStyle? b)
        {
            if (ReferenceEquals(a, b))
                return true;
            if (a == null || b == null)
                return false;
            static bool Rgb(float[]? x, float[]? y) => x == null ? y == null : y != null && System.Linq.Enumerable.SequenceEqual(x, y);
            return Rgb(a.Paper, b.Paper) && Rgb(a.Ink, b.Ink) && Rgb(a.Accent, b.Accent) && Rgb(a.BackPaper, b.BackPaper) &&
                   Rgb(a.BackInk, b.BackInk) && a.BackLabel == b.BackLabel;
        }

        public CardStyle Clone()
        {
            var copy = (CardStyle)MemberwiseClone();
            copy.Paper = (float[]?)Paper?.Clone();
            copy.Ink = (float[]?)Ink?.Clone();
            copy.Accent = (float[]?)Accent?.Clone();
            copy.BackPaper = (float[]?)BackPaper?.Clone();
            copy.BackInk = (float[]?)BackInk?.Clone();
            return copy;
        }
    }

    /// <summary>
    /// Names for the images the game draws for text cards: <c>text:f…</c> for a face, <c>text:b…</c> for a back, each a
    /// hash of exactly what is printed (words, colours, proportions), so identical cards share one drawing and an edited
    /// card gets a new one. Views ask for these ids like any image asset; core and the game compute the same ones.
    /// </summary>
    public static class TextFaces
    {
        public const string Prefix = "text:";

        public static bool IsTextId(string? assetId) => assetId != null && assetId.StartsWith(Prefix, StringComparison.Ordinal);

        public static string? FaceId(Card card) => card.TextFace ? FaceId(card.Text!, card.Style, card.Size ?? Card.DefaultSize) : null;
        public static string? BackId(Card card) => card.TextBack ? BackId(card.Style, card.Size ?? Card.DefaultSize) : null;

        public static string? FaceId(Deck deck, DeckCard entry) =>
            entry.Text != null && (entry.FaceAsset ?? deck.FaceAsset).Length == 0
                ? FaceId(entry.Text, entry.Style ?? deck.Style, deck.Size ?? Card.DefaultSize)
                : null;

        public static string? BackId(Deck deck, DeckCard entry) =>
            (entry.BackAsset ?? deck.BackAsset).Length == 0 && (entry.Text != null || (entry.Style ?? deck.Style) != null)
                ? BackId(entry.Style ?? deck.Style, deck.Size ?? Card.DefaultSize)
                : null;

        public static string FaceId(CardText text, CardStyle? style, Vec2 size) =>
            Prefix + "f" + Hash(JsonConvert.SerializeObject(new object?[]
            {
                text.Title, text.Kind, text.Body, text.Footer, text.Corner, text.Art, text.ArtIndex,
                style?.Paper, style?.Ink, style?.Accent, Aspect(size),
            }));

        public static string BackId(CardStyle? style, Vec2 size) =>
            Prefix + "b" + Hash(JsonConvert.SerializeObject(new object?[] { style?.BackPaper, style?.BackInk, style?.BackLabel, Aspect(size) }));

        /// <summary>Height over width, rounded: a drawing depends on the card's proportions, not its scale.</summary>
        public static float Aspect(Vec2 size) => size.X > 0 ? (float)Math.Round(size.Y / size.X, 3) : 1.4f;

        private static string Hash(string text)
        {
            // FNV-1a, 64 bits: stable across runs and machines (string.GetHashCode is not).
            var hash = 14695981039346656037UL;
            foreach (var c in text)
            {
                hash ^= c;
                hash *= 1099511628211UL;
            }
            return hash.ToString("x16");
        }
    }
}
