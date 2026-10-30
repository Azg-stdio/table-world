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

    /// <summary>A sound to play (<see cref="PlaySoundCommand"/>) on the machines of <see cref="Seats"/>, or on all of them when null.</summary>
    public sealed class SoundCue : StateEvent
    {
        public string Sound { get; }
        public float Volume { get; }
        public List<string>? Seats { get; }

        public SoundCue(string sound, float volume, List<string>? seats) : base("")
        {
            Sound = sound;
            Volume = volume;
            Seats = seats;
        }

        /// <summary>Whether the machine of <paramref name="seat"/> (null: a spectator) plays it.</summary>
        public bool For(string? seat) => Seats == null || seat != null && Seats.Contains(seat);
    }

    /// <summary>The room's light changed (<see cref="SetLightingCommand"/>).</summary>
    public sealed class LightingChanged : StateEvent
    {
        public LightingChanged() : base("") { }
    }

    /// <summary>A table-wide setting changed (the physics mode). It concerns no single object.</summary>
    public sealed class TableSettingsChanged : StateEvent
    {
        public TableSettingsChanged() : base("") { }
    }

    public sealed class ObjectAdded : StateEvent
    {
        /// <summary>The deck or bag it came out of (a draw, a deal), so the view can show it travel from there; else null.</summary>
        public string? From { get; }

        public ObjectAdded(string objectId, string? from = null) : base(objectId)
        {
            From = from;
        }
    }

    public sealed class ObjectRemoved : StateEvent
    {
        public ObjectRemoved(string objectId) : base(objectId) { }
    }

    /// <summary>A one-off asset-bundle effect (TTS trigger effect <see cref="Index"/>) plays on the object (ADR 0008).</summary>
    public sealed class EffectTriggered : StateEvent
    {
        public EffectTriggered(string objectId, int index) : base(objectId) => Index = index;
        public int Index { get; }
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

    /// <summary>The big screen changed (another video, played, paused, moved).</summary>
    public sealed class ScreenChanged : StateEvent
    {
        public ScreenChanged() : base("") { }
    }

    /// <summary>The table's music changed (a track, playing or paused, the playlist).</summary>
    public sealed class MusicChanged : StateEvent
    {
        public MusicChanged() : base("") { }
    }

    /// <summary>A zone moved, turned or changed size; <see cref="StateEvent.ObjectId"/> is the zone's id.</summary>
    public sealed class ZoneChanged : StateEvent
    {
        public ZoneChanged(string zoneId) : base(zoneId)
        {
        }
    }

    /// <summary>Script UI changed (M5): the global UI when <see cref="StateEvent.ObjectId"/> is empty, else that object's.</summary>
    public sealed class ScriptUiChanged : StateEvent
    {
        public ScriptUiChanged(string? objectId) : base(objectId ?? "")
        {
        }
    }

    /// <summary>A file joined the table's assets while it was played (M5, a script's setCustomObject): views learn of it.</summary>
    public sealed class AssetAdded : StateEvent
    {
        public AssetAdded(string assetId) : base(assetId)
        {
        }
    }

    /// <summary>An object's script inputs changed (M5): made, edited or typed into.</summary>
    public sealed class InputsChanged : StateEvent
    {
        public InputsChanged(string objectId) : base(objectId)
        {
        }
    }

    /// <summary>An object's script buttons changed (M5); the view redraws them.</summary>
    public sealed class ButtonsChanged : StateEvent
    {
        public ButtonsChanged(string objectId) : base(objectId)
        {
        }
    }

    /// <summary>A notebook page was written, added or deleted (G25).</summary>
    public sealed class NotebookChanged : StateEvent
    {
        public NotebookChanged() : base("")
        {
        }
    }

    /// <summary>Drawings were added, erased or cleared (G28); the view redraws them.</summary>
    public sealed class DrawingsChanged : StateEvent
    {
        public DrawingsChanged() : base("")
        {
        }
    }

    /// <summary>A line for the game log (a <c>message</c> effect or a win). Not tied to an object.</summary>
    /// <summary>A card shown (<see cref="ShowCommand"/>): the views of <see cref="Seats"/> (everyone when null) show <see cref="Card"/>'s face big.</summary>
    public sealed class CardShown : StateEvent
    {
        /// <summary>What to draw: the card itself, or the top card of the deck that was shown.</summary>
        public Card Card { get; }
        public string? Seat { get; }
        public List<string>? Seats { get; }

        public CardShown(string objectId, Card card, string? seat, List<string>? seats = null) : base(objectId)
        {
            Card = card;
            Seat = seat;
            Seats = seats;
        }

        /// <summary>Whether the machine of <paramref name="seat"/> (null: a spectator) sees it.</summary>
        public bool For(string? seat) => Seats == null || seat != null && Seats.Contains(seat);
    }

    /// <summary>The rules made a player's move for them (an <c>auto</c> action with one possible choice).</summary>
    public sealed class AutoMoved : StateEvent
    {
        public string Seat { get; }
        public string Action { get; }
        public Dictionary<string, string> Params { get; }

        public AutoMoved(string seat, string action, Dictionary<string, string> parameters) : base("")
        {
            Seat = seat;
            Action = action;
            Params = parameters;
        }
    }

    public sealed class GameMessage : StateEvent
    {
        public string Text { get; }

        public GameMessage(string text) : base("")
        {
            Text = text;
        }
    }

    /// <summary>A line only <see cref="Seat"/> reads (its role, what it sees): the host sends it to that seat alone (ADR 0012).</summary>
    public sealed class PrivateMessage : StateEvent
    {
        public string Seat { get; }
        public string Text { get; }

        public PrivateMessage(string seat, string text) : base("")
        {
            Seat = seat;
            Text = text;
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

        /// <summary>
        /// True when a rules command failed after changing the table: objects, zones and session were restored to their
        /// state before the command, so the view must re-read everything.
        /// </summary>
        public bool StateRestored { get; internal set; }

        /// <summary>Id of the object a take, group or card-on-card insert created; null otherwise.</summary>
        public string? CreatedId { get; internal set; }

        /// <summary>Every object a split or spread created, in order.</summary>
        public IReadOnlyList<string>? CreatedIds { get; internal set; }

        /// <summary>Deck contents (top first) returned by a search of a deck.</summary>
        public IReadOnlyList<DeckCard>? DeckContents { get; internal set; }

        /// <summary>Object ids (top first) returned by a search of a bag.</summary>
        public IReadOnlyList<string>? BagContents { get; internal set; }

        /// <summary>Cards dealt to each seat, in dealing order; a deal stops early when the deck runs out.</summary>
        public IReadOnlyDictionary<string, List<string>>? Dealt { get; internal set; }

        /// <summary>
        /// A rules command ran Lua (a <c>lua</c> effect). Only the host runs scripts, so peers replaying the command cannot
        /// reproduce it: the host sends its state hash with this order and peers whose table differs ask for the table.
        /// </summary>
        public bool ScriptsRan { get; internal set; }

        /// <summary>
        /// The command read or changed something not every seat knows (a role, a secret choice): peers cannot replay it,
        /// so the host runs it and sends each its result (ADR 0012).
        /// </summary>
        public bool ReadsSecrets { get; internal set; }

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
                CreatedIds = CreatedIds,
                DeckContents = DeckContents,
                BagContents = BagContents,
                Dealt = Dealt,
                ScriptsRan = ScriptsRan,
                ReadsSecrets = ReadsSecrets,
            };
            return copy;
        }

        internal static CommandResult Fail(string field, string code, string message, string? suggestion = null) =>
            new CommandResult(new ValidationError(field, code, message, suggestion), new StateEvent[0]);

        internal static CommandResult Fail(ValidationError error) => new CommandResult(error, new StateEvent[0]);

        /// <summary>What a player reads for this refusal (<see cref="ValidationError.WithText"/>); returns this result.</summary>
        internal CommandResult Text(string key, params (string Name, string Value)[] fills)
        {
            Error?.WithText(key, fills);
            return this;
        }
    }
}
