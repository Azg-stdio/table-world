using System;

namespace Core.Rules
{
    /// <summary>
    /// A page of the table's notebook (G25, TTS Notebook <c>TabStates</c>): a title and text. A tab without a colour (TTS
    /// "Grey") is for everyone; a tab with a seat colour is that player's own notes. As with hidden zones, every machine
    /// has the text: hiding is for honest play, not secrecy against a modified client.
    /// </summary>
    public sealed class NoteTab
    {
        public const int MaxTabs = 64;
        public const int MaxTitle = 64;
        public const int MaxBody = 50_000;

        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Body { get; set; } = "";
        /// <summary>Seat colour of its owner ("red"), or null for everyone.</summary>
        public string? Color { get; set; }

        /// <summary>
        /// With hidden roles: only the players of this role or team read it (<c>role:expert</c>, <c>team:wolves</c>), e.g. a
        /// manual only the experts have. Like every page, the text reaches every machine: hiding is for honest play.
        /// </summary>
        public string? For { get; set; }

        /// <summary>True when a player whose seat has <paramref name="seatColor"/> may read it (the Game Master reads all).</summary>
        public bool VisibleTo(string? seatColor, bool seesEverything) =>
            seesEverything || Color == null || string.Equals(Color, seatColor, StringComparison.OrdinalIgnoreCase);

        /// <summary>As <see cref="VisibleTo(string, bool)"/>, also checking <see cref="For"/> against <paramref name="seat"/>'s role.</summary>
        public bool VisibleTo(string? seatColor, bool seesEverything, TableState state, string? seat) =>
            seesEverything || VisibleTo(seatColor, false) && (For == null || state.Names(new[] { For }, seat));
    }
}
