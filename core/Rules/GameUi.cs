using System.Collections.Generic;

namespace Core.Rules
{
    /// <summary>
    /// Declarative game UI (T4.6), our equivalent of TTS XmlUI: panels anchored to the screen with labels, counters,
    /// buttons and images. Rendered by the game with UI Toolkit; texts use <c>{$reference}</c> templates.
    /// </summary>
    public sealed class UiDef
    {
        public List<PanelDef> Panels { get; set; } = new List<PanelDef>();
    }

    public enum UiAnchor { TopLeft, Top, TopRight, Left, Center, Right, BottomLeft, Bottom, BottomRight }

    public sealed class PanelDef
    {
        public string Id { get; set; } = "";
        public string? Title { get; set; }
        public UiAnchor Anchor { get; set; }
        /// <summary>Who sees the panel: seat ids, or <c>host</c> (the player running the game, seated or not); empty means everyone.</summary>
        public List<string> VisibleTo { get; set; } = new List<string>();
        public List<UiElementDef> Elements { get; set; } = new List<UiElementDef>();

        /// <summary>The role in <c>visibleTo</c> lists that means the player running the game.</summary>
        public const string Host = "host";

        /// <summary>
        /// True when a viewer at <paramref name="seat"/> (hosting when <paramref name="host"/>) is in
        /// <paramref name="visibleTo"/>: empty lets everyone see.
        /// </summary>
        public static bool Shows(List<string> visibleTo, string? seat, bool host) =>
            visibleTo.Count == 0 || (seat != null && visibleTo.Contains(seat)) || (host && visibleTo.Contains(Host));
    }

    public enum UiElementType { Label, Counter, Button, Image }

    public sealed class UiElementDef
    {
        public UiElementType Type { get; set; }
        /// <summary>Label/button text, or a counter's caption. <c>{$ref}</c> parts are replaced by values.</summary>
        public string? Text { get; set; }
        /// <summary>Counter value: a reference, e.g. <c>$seat.seat_south.deck.count</c> or <c>$viewer.res.score</c>.</summary>
        public string? Value { get; set; }
        /// <summary>Button: the action it performs.</summary>
        public string? Action { get; set; }
        /// <summary>Button: who performs it (a seat id or reference); default <c>$viewer</c>.</summary>
        public string? Seat { get; set; }
        /// <summary>Button: parameter values for the action.</summary>
        public Dictionary<string, string> Params { get; set; } = new Dictionary<string, string>();
        /// <summary>
        /// Who sees this element, like a panel's <c>visibleTo</c>: seat ids or <c>host</c>; empty means whoever sees the
        /// panel. A button only the host presses (deal the next day, reveal) sits in a panel everyone sees.
        /// </summary>
        public List<string> VisibleTo { get; set; } = new List<string>();
        /// <summary>Image: an image asset id.</summary>
        public string? Asset { get; set; }
    }
}
