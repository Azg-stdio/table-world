using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Core.Rules
{
    public sealed class GameInfo
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        /// <summary>
        /// The game's own words for the lines the engine writes, keyed by what they say: <c>win</c> ("{seat} wins."),
        /// <c>choseInSecret</c>, <c>chose</c> ("{seat} chose {action}{choice}."), <c>blindRound</c>, <c>reveal</c>, <c>shows</c>
        /// ("{seat} shows {card}."). <c>{seat}</c> and the other names in braces are filled in; a missing key keeps the
        /// English line.
        /// </summary>
        public Dictionary<string, string>? Messages { get; set; }
        [System.ComponentModel.DefaultValue(1)]
        public int MinPlayers { get; set; } = 1;
        public int MaxPlayers { get; set; } = 8;
    }

    public sealed class ResourceDef
    {
        public string Id { get; set; } = "";
        public string? Name { get; set; }
        public double Initial { get; set; }
        public double? Min { get; set; }
        public double? Max { get; set; }
    }

    public enum TurnOrder { Clockwise, Counterclockwise }

    public sealed class TurnDef
    {
        public TurnOrder Order { get; set; }
        /// <summary>A seat id, <c>first</c> (first seat) or <c>random</c>.</summary>
        public string StartingPlayer { get; set; } = "first";
        /// <summary>Phase ids played in order every turn.</summary>
        public List<string> Phases { get; set; } = new List<string>();
    }

    /// <summary>When a phase ends by itself. Exactly one field is set.</summary>
    public sealed class AdvanceRule
    {
        /// <summary>Right after its onEnter effects.</summary>
        public bool Immediate { get; set; }
        /// <summary>Once every seat has performed an action in this phase.</summary>
        public bool AllPlayersActed { get; set; }
        /// <summary>Only through an <c>endPhase</c>/<c>endTurn</c> effect (an action of the current player).</summary>
        public bool CurrentPlayerEnds { get; set; }
        public Condition? Condition { get; set; }
    }

    public sealed class PhaseDef
    {
        public string Id { get; set; } = "";
        public string? Name { get; set; }
        /// <summary>Action ids allowed in this phase (whitelist).</summary>
        public List<string> Actions { get; set; } = new List<string>();
        public List<Effect> OnEnter { get; set; } = new List<Effect>();
        public List<Effect> OnExit { get; set; } = new List<Effect>();
        /// <summary>Null means <c>currentPlayerEnds</c>.</summary>
        public AdvanceRule? AdvanceWhen { get; set; }
    }

    public enum ActorRule { Current, Any }
    public enum ParamType { Object, Seat, Number, Choice }

    public sealed class ParamFilter
    {
        /// <summary>Zone or deck the object must be in (a reference like <c>$actor.hand</c> or an id).</summary>
        public string? InZone { get; set; }
        public string? Type { get; set; }
        public string? HasTag { get; set; }
    }

    public sealed class ParamDef
    {
        public string Name { get; set; } = "";
        public ParamType Type { get; set; }
        public ParamFilter? Filter { get; set; }
        public List<string> Options { get; set; } = new List<string>();
        /// <summary>
        /// What players read for each option (option → label: "mazmorra" → "Mazmorra"), so rules keep plain ids while
        /// buttons and logs speak the game's language. An option without a label shows as itself.
        /// </summary>
        public Dictionary<string, string>? Labels { get; set; }
    }

    public sealed class ActionDef
    {
        public string Id { get; set; } = "";
        public string? Name { get; set; }
        public ActorRule Actor { get; set; }
        public List<ParamDef> Params { get; set; } = new List<ParamDef>();
        public List<Condition> Preconditions { get; set; } = new List<Condition>();
        public List<Effect> Effects { get; set; } = new List<Effect>();
        /// <summary>How many times one seat may perform it per phase visit; null is unlimited.</summary>
        public int? LimitPerPhase { get; set; }
        /// <summary>
        /// Chosen in secret (simultaneous choices: everyone picks a space, then all are shown): performing it only records
        /// the seat's choice, which others cannot see, and its effects run when a <c>reveal</c> effect shows the choices.
        /// </summary>
        public bool Secret { get; set; }
        /// <summary>
        /// Shown as a button in the game window (default). False for actions players do with the pieces themselves (a
        /// card dragged from the hand), or that only scripts and agents perform: the HUD stays short.
        /// </summary>
        [System.ComponentModel.DefaultValue(true)]
        public bool Button { get; set; } = true;
    }

    /// <summary>A piece put in (or taken out of) a zone during a blind round, whose zone effects wait for the reveal.</summary>
    public sealed class ZoneMove
    {
        public string Object { get; set; } = "";
        public string Zone { get; set; } = "";
        public string? Seat { get; set; }
        public bool Enter { get; set; }
    }

    /// <summary>A secret action a seat performed, waiting for a <c>reveal</c>.</summary>
    public sealed class SecretChoice
    {
        public string Seat { get; set; } = "";
        public string Action { get; set; } = "";
        public Dictionary<string, string> Params { get; set; } = new Dictionary<string, string>();
    }

    public enum WinCheck { AnyCommand, TurnEnd }

    public sealed class WinCondition
    {
        /// <summary>Evaluated once per seat with <c>$player</c> bound to it.</summary>
        public Condition? When { get; set; }
        /// <summary>Who wins when it holds: usually <c>$player</c> or <c>$other</c>.</summary>
        public string Winner { get; set; } = "$player";
        public WinCheck CheckAfter { get; set; }
    }

    /// <summary>Game progress. Written by the engine, saved with the table; an authored game has none.</summary>
    public sealed class Session
    {
        public bool Started { get; set; }
        public bool Over { get; set; }
        public string? Winner { get; set; }
        public int Turn { get; set; }
        /// <summary>Seats playing this game, in seat order; empty means every seat (games saved before seats were chosen).</summary>
        public List<string> Players { get; set; } = new List<string>();
        public string? CurrentSeat { get; set; }
        public string? Phase { get; set; }
        /// <summary>Seats that acted in the current phase visit.</summary>
        public List<string> Acted { get; set; } = new List<string>();
        /// <summary>Actions performed this phase visit, keyed <c>seat/action</c>.</summary>
        public Dictionary<string, int> ActionCounts { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, JToken> Variables { get; set; } = new Dictionary<string, JToken>();
        /// <summary>seat → resource → value.</summary>
        public Dictionary<string, Dictionary<string, double>> Resources { get; set; } = new Dictionary<string, Dictionary<string, double>>();
        /// <summary>Zone effects held back by a blind round, run at its reveal: the last move of each piece.</summary>
        public List<ZoneMove> HeldZoneMoves { get; set; } = new List<ZoneMove>();
        /// <summary>Secret choices made and not yet revealed, in the order they were made.</summary>
        public List<SecretChoice> Secrets { get; set; } = new List<SecretChoice>();
        /// <summary>Messages from <c>message</c> effects, oldest first (last 50).</summary>
        public List<string> Log { get; set; } = new List<string>();
        /// <summary>Random generator state, so a saved game continues the same sequence.</summary>
        public ulong RandomState { get; set; }
    }

    public static class GameEvents
    {
        public const string OnGameStart = "onGameStart";
        public const string OnTurnStart = "onTurnStart";
        public const string OnTurnEnd = "onTurnEnd";
        /// <summary>After a blind round is revealed and the zone effects it held back have run.</summary>
        public const string OnReveal = "onReveal";
        public static readonly IReadOnlyList<string> All = new[] { OnGameStart, OnTurnStart, OnTurnEnd, OnReveal };
    }
}
