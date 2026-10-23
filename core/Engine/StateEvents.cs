using System.Collections.Generic;
using Core.Rules;

namespace Core.Engine
{
    /// <summary>
    /// What changed after a command. The view reacts to these and re-reads the object from <see cref="TableEngine.State"/>;
    /// it never infers changes on its own.
    /// </summary>
    public abstract class StateEvent
    {
        public string ObjectId { get; }

        protected StateEvent(string objectId)
        {
            ObjectId = objectId;
        }
    }

    public sealed class ObjectAdded : StateEvent
    {
        public ObjectAdded(string objectId) : base(objectId) { }
    }

    public sealed class ObjectRemoved : StateEvent
    {
        public ObjectRemoved(string objectId) : base(objectId) { }
    }

    /// <summary>Transform, lock, die value, deck contents or any other field of an existing object changed.</summary>
    public sealed class ObjectUpdated : StateEvent
    {
        public ObjectUpdated(string objectId) : base(objectId) { }
    }

    /// <summary>An object entered or left a bag. Null means the table.</summary>
    public sealed class ContainerChanged : StateEvent
    {
        public string? From { get; }
        public string? To { get; }

        public ContainerChanged(string objectId, string? from, string? to) : base(objectId)
        {
            From = from;
            To = to;
        }
    }

    public sealed class Shuffled : StateEvent
    {
        public Shuffled(string objectId) : base(objectId) { }
    }

    public sealed class DieRolled : StateEvent
    {
        public int Value { get; }

        public DieRolled(string objectId, int value) : base(objectId)
        {
            Value = value;
        }
    }

    /// <summary>An object's position entered a zone's box (after a move, take, deal, ...).</summary>
    public sealed class ZoneEntered : StateEvent
    {
        public string ZoneId { get; }

        public ZoneEntered(string objectId, string zoneId) : base(objectId)
        {
            ZoneId = zoneId;
        }
    }

    /// <summary>An object left a zone: it moved out, went into a bag or deck, or was removed.</summary>
    public sealed class ZoneLeft : StateEvent
    {
        public string ZoneId { get; }

        public ZoneLeft(string objectId, string zoneId) : base(objectId)
        {
            ZoneId = zoneId;
        }
    }

    /// <summary>Someone looked through a container. Other players see that it happened, not what was seen.</summary>
    public sealed class Searched : StateEvent
    {
        public string? Actor { get; }

        public Searched(string objectId, string? actor) : base(objectId)
        {
            Actor = actor;
        }
    }

    public sealed class CommandResult
    {
        public bool Ok => Error == null;
        public ValidationError? Error { get; }
        public IReadOnlyList<StateEvent> Events { get; }

        /// <summary>Id of the object a take, group or card-on-card insert created; null otherwise.</summary>
        public string? CreatedId { get; internal set; }

        /// <summary>Deck contents (top first) returned by a search of a deck.</summary>
        public IReadOnlyList<DeckCard>? DeckContents { get; internal set; }

        /// <summary>Object ids (top first) returned by a search of a bag.</summary>
        public IReadOnlyList<string>? BagContents { get; internal set; }

        /// <summary>Cards dealt to each seat, in dealing order; a deal stops early when the deck runs out.</summary>
        public IReadOnlyDictionary<string, List<string>>? Dealt { get; internal set; }

        private CommandResult(ValidationError? error, IReadOnlyList<StateEvent> events)
        {
            Error = error;
            Events = events;
        }

        internal static CommandResult Success(List<StateEvent> events) => new CommandResult(null, events);

        internal CommandResult WithEvents(List<StateEvent> events)
        {
            var copy = new CommandResult(Error, events)
            {
                CreatedId = CreatedId,
                DeckContents = DeckContents,
                BagContents = BagContents,
                Dealt = Dealt,
            };
            return copy;
        }

        internal static CommandResult Fail(string field, string code, string message, string? suggestion = null) =>
            new CommandResult(new ValidationError(field, code, message, suggestion), new StateEvent[0]);
    }
}
