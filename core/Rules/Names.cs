using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Core.Text;

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

        /// <summary>A seat as players say it in the session's language: its colour comes from the string table (<c>color.red</c>).</summary>
        public static string Of(TableState state, Seat seat) =>
            string.IsNullOrWhiteSpace(seat.Name) && !string.IsNullOrWhiteSpace(seat.Color) && !seat.Color.StartsWith("#")
                && Table(state).Raw("color." + seat.Color.ToLowerInvariant()) is { } colour
                ? colour
                : Of(seat);

        /// <summary>The seat <paramref name="seatId"/> as players say it; null is "The game" (the host or the rules).</summary>
        public static string Seat(TableState state, string? seatId) =>
            seatId == null ? Word(state, "theGame") : state.Players.Seats.FirstOrDefault(s => s.Id == seatId) is { } seat ? Of(state, seat) : Humanize(seatId);

        /// <summary>The session's language (<see cref="Session.Language"/>), English when none is set.</summary>
        public static string Language(TableState state) => string.IsNullOrWhiteSpace(state.Session?.Language) ? Strings.Fallback : state.Session!.Language!;

        // Built-in tables never change while running, so one per language is shared.
        private static readonly ConcurrentDictionary<string, Strings> Tables = new ConcurrentDictionary<string, Strings>();

        /// <summary>The string table of the session's language.</summary>
        public static Strings Table(TableState state) => Tables.GetOrAdd(Language(state), language => Strings.For(language));

        /// <summary>A word the engine joins texts with (<c>engine.and</c>, <c>engine.or</c>, <c>engine.theGame</c>), in the session's language.</summary>
        public static string Word(TableState state, string key) => Table(state).Get("engine." + key);

        /// <summary>
        /// A line the engine writes: the game's own words when it has them (<see cref="GameInfo.Messages"/>, key
        /// <paramref name="key"/>), else <c>engine.&lt;key&gt;</c> in the session's language; <c>{name}</c> placeholders are
        /// filled from <paramref name="fills"/>.
        /// </summary>
        public static string Message(TableState state, string key, params (string Name, string Value)[] fills) => Message(state, key, null, fills);

        /// <summary>
        /// <see cref="Message(TableState, string, (string, string)[])"/> for a count: when <paramref name="count"/> is one,
        /// <c>&lt;key&gt;.one</c> is read first (the singular), in the game's words and then the table's.
        /// </summary>
        public static string Message(TableState state, string key, int? count, params (string Name, string Value)[] fills)
        {
            string? Own(string k) => state.Game?.Messages != null && state.Game.Messages.TryGetValue(k, out var own) && !string.IsNullOrWhiteSpace(own) ? own : null;
            var table = Table(state);
            var one = count == 1;
            var text = (one ? Own(key + ".one") : null) ?? Own(key)
                ?? (one ? table.Raw("engine." + key + ".one") : null) ?? table.Raw("engine." + key) ?? key;
            return Strings.Fill(text, fills.Select(f => (f.Name, (object?)f.Value)).ToArray());
        }

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
