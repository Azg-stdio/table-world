using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Core.Rules
{
    public sealed class GameInfo
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Description { get; set; }
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
        public string? CurrentSeat { get; set; }
        public string? Phase { get; set; }
        /// <summary>Seats that acted in the current phase visit.</summary>
        public List<string> Acted { get; set; } = new List<string>();
        /// <summary>Actions performed this phase visit, keyed <c>seat/action</c>.</summary>
        public Dictionary<string, int> ActionCounts { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, JToken> Variables { get; set; } = new Dictionary<string, JToken>();
        /// <summary>seat → resource → value.</summary>
        public Dictionary<string, Dictionary<string, double>> Resources { get; set; } = new Dictionary<string, Dictionary<string, double>>();
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
        public static readonly IReadOnlyList<string> All = new[] { OnGameStart, OnTurnStart, OnTurnEnd };
    }
}
