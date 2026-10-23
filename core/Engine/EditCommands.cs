using Core.Rules;

namespace Core.Engine
{
    /// <summary>Multiplies an object's scale (TTS "+"/"−" keys). Each axis stays within <see cref="TableEngine.MinScale"/>..<see cref="TableEngine.MaxScale"/>.</summary>
    public sealed class ScaleCommand : Command
    {
        public string ObjectId { get; set; } = "";
        public float Factor { get; set; } = 1.1f;
    }

    /// <summary>Removes an object from the table; a bag goes with everything inside it.</summary>
    public sealed class DeleteCommand : Command
    {
        public string ObjectId { get; set; } = "";
    }

    /// <summary>Copies an object (with a bag's contents, a deck's cards and its states) under new ids (copy/paste).</summary>
    public sealed class CloneCommand : Command
    {
        public string ObjectId { get; set; } = "";
        /// <summary>Where the copy lands; null puts it beside the original.</summary>
        public Vec3? Position { get; set; }
    }

    /// <summary>Shows another state of a multi-state object (TTS states; number keys or menu).</summary>
    public sealed class SetStateCommand : Command
    {
        public string ObjectId { get; set; } = "";
        /// <summary>Key in the object's <c>states</c>, e.g. "2".</summary>
        public string State { get; set; } = "";
    }

    /// <summary>Lifts the top <see cref="Count"/> cards (default half) off a deck into a new deck beside it.</summary>
    public sealed class CutCommand : Command
    {
        public string DeckId { get; set; } = "";
        public int? Count { get; set; }
        public Vec3? Position { get; set; }
    }

    /// <summary>Splits a deck into <see cref="Piles"/> decks of (almost) equal size laid out in a row; the original keeps the top part.</summary>
    public sealed class SplitCommand : Command
    {
        public string DeckId { get; set; } = "";
        public int Piles { get; set; } = 2;
    }

    /// <summary>Lays every card of a deck out in an overlapping row, top card first; the deck stays, empty.</summary>
    public sealed class SpreadCommand : Command
    {
        public string DeckId { get; set; } = "";
        /// <summary>Distance between card centres along the deck's width, metres.</summary>
        public float Spacing { get; set; } = 0.025f;
    }

    /// <summary>Turns a die to a chosen face (context menu), without rolling.</summary>
    public sealed class SetDieValueCommand : Command
    {
        public string ObjectId { get; set; } = "";
        public int Value { get; set; }
    }

    /// <summary>Renames, describes or tints an object (context menu). Null fields stay as they are.</summary>
    public sealed class SetPropertiesCommand : Command
    {
        public string ObjectId { get; set; } = "";
        public string? Name { get; set; }
        public string? Description { get; set; }
        public float[]? Tint { get; set; }
        public bool ClearTint { get; set; }
    }
}
