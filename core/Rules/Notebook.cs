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

        /// <summary>True when a player whose seat has <paramref name="seatColor"/> may read it (the Game Master reads all).</summary>
        public bool VisibleTo(string? seatColor, bool seesEverything) =>
            seesEverything || Color == null || string.Equals(Color, seatColor, StringComparison.OrdinalIgnoreCase);
    }
}
