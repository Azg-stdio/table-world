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
    }

    /// <summary>
    /// Takes a card out of a deck (it becomes a <see cref="Card"/> object) or an object out of a bag.
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
    }

    /// <summary>
    /// Drops an object into another: a card or deck onto a deck (joins it), a card onto a card (forms a deck),
    /// anything into a bag. Inserted at the top unless <see cref="AtBottom"/>.
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
}
