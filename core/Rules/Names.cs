using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Core.Rules
{
    /// <summary>
    /// What players read for a thing: its name, a text card's title, or as a last resort its id made into words
    /// ("eventos_noche-12" → "Eventos noche"). Ids are for rules and scripts; players never see underscores.
    /// </summary>
    public static class Names
    {
        public static string Of(TableObject obj) =>
            !string.IsNullOrWhiteSpace(obj.Name) ? obj.Name!
            : obj is Card { Text: { Title: { } title } } && !string.IsNullOrWhiteSpace(title) ? title
            : Humanize(obj.Id);

        public static string Of(DeckCard card) =>
            !string.IsNullOrWhiteSpace(card.Name) ? card.Name!
            : !string.IsNullOrWhiteSpace(card.Text?.Title) ? card.Text!.Title!
            : Humanize(card.Id ?? "card");

        /// <summary>A seat as players say it: its name, else its colour ("Red"), else its id as words.</summary>
        public static string Of(Seat seat) =>
            !string.IsNullOrWhiteSpace(seat.Name) ? seat.Name!
            : !string.IsNullOrWhiteSpace(seat.Color) && !seat.Color.StartsWith("#") ? Humanize(seat.Color)
            : Humanize(seat.Id);

        /// <summary>The seat <paramref name="seatId"/> as players say it; null is "The game" (the host or the rules).</summary>
        public static string Seat(TableState state, string? seatId) =>
            seatId == null ? "The game" : state.Players.Seats.FirstOrDefault(s => s.Id == seatId) is { } seat ? Of(seat) : Humanize(seatId);

        /// <summary>An id as words: separators become spaces, a trailing copy number goes, the first letter is a capital.</summary>
        public static string Humanize(string id)
        {
            var words = Regex.Replace(id ?? "", @"[-_]+\d+$", "");
            words = Regex.Replace(words, @"[_\-.]+", " ").Trim();
            // camelCase → separate words.
            words = Regex.Replace(words, @"(?<=[a-z])(?=[A-Z])", " ");
            if (words.Length == 0)
                return "Piece";
            words = string.Join(" ", words.Split(' ').Where(w => w.Length > 0));
            return char.ToUpper(words[0], CultureInfo.InvariantCulture) + words.Substring(1).ToLowerInvariant();
        }
    }
}
