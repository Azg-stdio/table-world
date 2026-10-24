using System.Collections.Generic;
using Core.Rules;

namespace Core.Engine
{
    /// <summary>
    /// An intent from a player (or the host). The view never mutates <see cref="TableState"/>: every gesture becomes a
    /// command that <see cref="TableEngine"/> validates and executes. Commands are plain data so they can cross the network (M3).
    /// </summary>
    public abstract class Command
    {
        /// <summary>Seat id of the player issuing the command, or null for the host/system.</summary>
        public string? Actor { get; set; }
    }

    /// <summary>Final pose of an object on the table, typically after physics settles a drop.</summary>
    public sealed class MoveCommand : Command
    {
        public string ObjectId { get; set; } = "";
        public Vec3 Position { get; set; }
        public Vec3 Rotation { get; set; }

        /// <summary>
        /// For a die thrown or dropped by hand: the face physics left on top. Physics decides physical throws (as in TTS);
        /// <see cref="RollCommand"/> is the engine's own roll. Null leaves the value unchanged.
        /// </summary>
        public int? DieValue { get; set; }
    }

    /// <summary>
    /// Takes a card out of a deck (it becomes a <see cref="Card"/> object), an object out of a bag, or pieces off a stack (G14).
    /// Pick by <see cref="EntryId"/> (after a search), or by <see cref="Index"/> counted from the top (default 0), or from the bottom.
    /// </summary>
    public sealed class TakeCommand : Command
    {
        public string ContainerId { get; set; } = "";
        public int Index { get; set; }
        public bool FromBottom { get; set; }
        public string? EntryId { get; set; }
        /// <summary>Where the taken object lands; null places it beside the container.</summary>
        public Vec3? Position { get; set; }
        /// <summary>Null keeps the container's rotation (a card taken from a face-down deck stays face down).</summary>
        public Vec3? Rotation { get; set; }
        /// <summary>From a stack: how many pieces to take, as one smaller stack (default 1, fewer than the stack holds).</summary>
        public int? Count { get; set; }
    }

    /// <summary>
    /// Drops an object into another: a card or deck onto a deck (joins it), a card onto a card (forms a deck),
    /// anything into a bag, a stackable piece onto the same kind of piece (the counts add up, G14). Inserted at the top unless <see cref="AtBottom"/>.
    /// </summary>
    public sealed class InsertCommand : Command
    {
        public string ObjectId { get; set; } = "";
        public string IntoId { get; set; } = "";
        public bool AtBottom { get; set; }
    }

    /// <summary>Turns an object over around its Z axis. Flipping a deck also reverses its order, like a real deck.</summary>
    public sealed class FlipCommand : Command
    {
        public string ObjectId { get; set; } = "";
    }

    /// <summary>Randomizes a deck's cards or a bag's contents with the engine's seeded generator.</summary>
    public sealed class ShuffleCommand : Command
    {
        public string ObjectId { get; set; } = "";
    }

    /// <summary>
    /// Someone peeked at the hidden side of an object (G15, TTS Alt + Shift): nothing changes, but everyone is told, as
    /// TTS announces peeks.
    /// </summary>
    public sealed class PeekCommand : Command
    {
        public string ObjectId { get; set; } = "";
    }

    /// <summary>
    /// A player let go of an object (TTS's drop): nothing changes (the move that follows is its own command), but table
    /// scripts hear <c>onObjectDrop</c> (M5).
    /// </summary>
    public sealed class DropCommand : Command
    {
        public string ObjectId { get; set; } = "";
    }

    /// <summary>Replaces an object's script buttons (M5). Host only: the host's scripts are the only ones that make buttons.</summary>
    public sealed class SetButtonsCommand : Command, IHostCommand
    {
        public string ObjectId { get; set; } = "";
        public List<ButtonDef> Buttons { get; set; } = new List<ButtonDef>();
    }

    /// <summary>
    /// A player clicked a script button (M5): nothing changes; the host's scripts run the button's function with the
    /// player's colour. <see cref="Alt"/> is a right click.
    /// </summary>
    public sealed class ClickButtonCommand : Command
    {
        public string ObjectId { get; set; } = "";
        public int Index { get; set; }
        public bool Alt { get; set; }
    }

    /// <summary>A player picked an object up (TTS's pick up): nothing changes; table scripts hear <c>onObjectPickUp</c> (M5).</summary>
    public sealed class PickUpCommand : Command
    {
        public string ObjectId { get; set; } = "";
    }

    /// <summary>A player put on or took off a blindfold (G15, TTS B): nothing changes; everyone is told.</summary>
    public sealed class BlindfoldCommand : Command
    {
        public bool On { get; set; } = true;
    }

    /// <summary>Looks through a deck or bag without changing it; the contents come back in the result.</summary>
    public sealed class SearchCommand : Command
    {
        public string ContainerId { get; set; } = "";
    }

    /// <summary>Deals cards one at a time, round robin from the top, into each seat's hand zone.</summary>
    public sealed class DealCommand : Command
    {
        public string DeckId { get; set; } = "";
        /// <summary>Cards per seat.</summary>
        public int Count { get; set; } = 1;
        /// <summary>Seat ids in dealing order; empty means every seat in <c>players.seats</c> order.</summary>
        public List<string> Seats { get; set; } = new List<string>();
    }

    public sealed class LockCommand : Command
    {
        public string ObjectId { get; set; } = "";
        public bool Locked { get; set; } = true;
    }

    /// <summary>
    /// Gathers cards and decks into one deck, in list order from top to bottom. If the first object is a deck it keeps
    /// its id; otherwise a new deck is created at the first card's pose.
    /// </summary>
    public sealed class GroupCommand : Command
    {
        public List<string> ObjectIds { get; set; } = new List<string>();
    }

    /// <summary>Rolls a die. The engine picks the value; the view shows that face.</summary>
    public sealed class RollCommand : Command
    {
        public string ObjectId { get; set; } = "";
    }

    /// <summary>A table-wide setting only the host may change; the network host refuses it from clients.</summary>
    public interface IHostCommand
    {
    }

    /// <summary>
    /// Switches free-play turns on or off and sets their order (TTS Turns). Host only. Refused while a rules game is
    /// running, whose own turns apply. <see cref="First"/> (default: the first seat of the order) starts.
    /// </summary>
    public sealed class SetTurnOrderCommand : Command, IHostCommand
    {
        public bool Enabled { get; set; } = true;
        public List<string> Seats { get; set; } = new List<string>();
        public bool Reverse { get; set; }
        public bool SkipEmptyHands { get; set; }
        public string? First { get; set; }
    }

    /// <summary>Ends the current free-play turn; the next seat in order (skipping empty hands if set) plays. Sent by the seat whose turn it is.</summary>
    public sealed class EndTurnCommand : Command
    {
    }

    /// <summary>Writes a notebook page (G25): replaces the page with the same id, or adds it (an id is minted when empty).</summary>
    public sealed class SetNoteTabCommand : Command
    {
        public NoteTab Tab { get; set; } = new NoteTab();
    }

    /// <summary>Deletes a notebook page (G25).</summary>
    public sealed class DeleteNoteTabCommand : Command
    {
        public string TabId { get; set; } = "";
    }

    /// <summary>Adds a line or arrow drawn on the table (G28). The engine gives it an id when it has none.</summary>
    public sealed class AddDrawingCommand : Command
    {
        public DrawingDef Drawing { get; set; } = new DrawingDef();
    }

    /// <summary>Erases one drawing (G28).</summary>
    public sealed class EraseDrawingCommand : Command
    {
        public string DrawingId { get; set; } = "";
    }

    /// <summary>Erases every drawing; with <see cref="Seat"/>, only that seat's (G28).</summary>
    public sealed class ClearDrawingsCommand : Command
    {
        public string? Seat { get; set; }
    }

    /// <summary>Sets the table grid (G28): lines, snapping, shape and size; null removes it. Host only.</summary>
    public sealed class SetGridCommand : Command, IHostCommand
    {
        public GridDef? Grid { get; set; }
    }

    /// <summary>Arranges the unlocked objects in a <see cref="LayoutZone"/> in its grid (G18, TTS "Lay out").</summary>
    public sealed class ArrangeZoneCommand : Command
    {
        public string ZoneId { get; set; } = "";
    }

    /// <summary>Shuffles where the unlocked objects in a <see cref="RandomizeZone"/> lie, and the decks and bags among them (G18).</summary>
    public sealed class RandomizeZoneCommand : Command
    {
        public string ZoneId { get; set; } = "";
    }

    /// <summary>Sets how the table simulates objects (TTS: Options > Physics). Host only.</summary>
    public sealed class SetPhysicsCommand : Command, IHostCommand
    {
        public PhysicsMode Mode { get; set; }
    }
}
