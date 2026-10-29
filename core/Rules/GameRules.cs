using System;
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
        /// ("{seat} shows {card}."), and for roles <c>youAre</c> ("You are {role} ({team})."), <c>youAreRole</c>,
        /// <c>youHave</c> ("{set}: {role}."), <c>seenAsRole</c>, <c>seenAsTeam</c>, <c>seenAsOneOf</c>, <c>revealedRole</c>,
        /// <c>revealedTeam</c>, <c>revealedTrait</c>, <c>nowRole</c>, <c>nowTeam</c>, <c>nowTrait</c>; for votes <c>voted</c>,
        /// <c>voteNamed</c> ("{vote}: {votes}."), <c>voteCount</c> ("{vote}: {counts}."), <c>voteResult</c> ("{vote}: {result}."),
        /// <c>teamWins</c> ("{team} win: {seats}."), <c>eyesClosed</c> (a sleeper's screen without its own sleepText). <c>{seat}</c> and the other names in
        /// braces are filled in; a missing key keeps the English line.
        /// </summary>
        public Dictionary<string, string>? Messages { get; set; }
        /// <summary>
        /// The language the game is written in (ADR 0011), e.g. <c>es</c>: its texts as they stand are in it, and
        /// <see cref="TableState.Translations"/> carries the others. Null: a game in one language.
        /// </summary>
        public string? Language { get; set; }
        /// <summary>The languages the game can be played in, its own first; empty means its own plus every translation.</summary>
        public List<string> Languages { get; set; } = new List<string>();
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

        /// <summary>
        /// An eyes-closed phase (ADR 0012): who is awake (<c>nobody</c>, <c>team:&lt;id&gt;</c>, <c>role:&lt;id&gt;</c>, a seat,
        /// or a list such as <c>$var.night_order</c>). The others sleep: no actions, a dark screen, no sight or sound of the
        /// awake. Null: everyone is awake.
        /// </summary>
        public string? Awake { get; set; }

        /// <summary>What the sleepers read ("Night falls. Eyes closed."); <c>{$ref}</c> parts are filled in.</summary>
        public string? SleepText { get; set; }

        /// <summary>Who hears whom in this phase; null is <c>all</c>, or <c>awake</c> in an eyes-closed phase.</summary>
        public PhaseVoice? Voice { get; set; }

        /// <summary>
        /// A timed phase: it ends this many seconds after it begins (the host's game window keeps the time and ends it;
        /// everyone sees the countdown, and the host can end it sooner). Null: it waits for the players.
        /// </summary>
        public int? Seconds { get; set; }

        /// <summary>The phase waits for the host to move on (the host's Next button), with or without <see cref="Seconds"/>.</summary>
        public bool HostEnds { get; set; }

        /// <summary>
        /// Whose thumbs are up in this phase, shown on their chalkers to whoever is awake (the minions' thumbs for Merlin):
        /// <c>team:&lt;id&gt;</c>, <c>role:&lt;id&gt;</c>, a seat or a list such as <c>$var.thumbs</c>. The game raises them;
        /// the players need not.
        /// </summary>
        public string? Raised { get; set; }

        /// <summary>The phase that follows this one when it ends, instead of the next in the turn (a sequence such as a ritual).</summary>
        public string? Next { get; set; }

        /// <summary>Big words for the players awake in this phase ("Minions, open your eyes"); <c>{$ref}</c> parts are filled in.</summary>
        public string? Banner { get; set; }
    }

    /// <summary>
    /// <c>all</c>: everyone hears everyone; <c>awake</c>: the awake hear each other and the sleepers hear nobody;
    /// <c>team</c>: each team hears only itself; <c>none</c>: silence. Spectators and a host without a seat hear all.
    /// </summary>
    public enum PhaseVoice { All, Awake, Team, None }

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
        /// <summary>A vote (ADR 0012): cast in secret by its voters, counted when the last one votes. Its choice is <c>choice</c>.</summary>
        public VoteDef? Vote { get; set; }
        /// <summary>
        /// Shown as a button in the game window (default). False for actions players do with the pieces themselves (a
        /// card dragged from the hand), or that only scripts and agents perform: the HUD stays short.
        /// </summary>
        [System.ComponentModel.DefaultValue(true)]
        public bool Button { get; set; } = true;

        /// <summary>
        /// Done by clicking a player: an action whose one parameter is a seat is performed with the seat whose chalker (or
        /// name) the player clicks, while it is legal for that seat (the leader picking the team). No button is shown.
        /// </summary>
        public bool OnPlayer { get; set; }
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
        /// <summary>With hidden roles, the team that wins when <see cref="When"/> holds (checked once, not per seat).</summary>
        public string? Team { get; set; }
        /// <summary>Evaluated once per seat with <c>$player</c> bound to it.</summary>
        public Condition? When { get; set; }
        /// <summary>Who wins when it holds: usually <c>$player</c> or <c>$other</c>.</summary>
        public string Winner { get; set; } = "$player";
        public WinCheck CheckAfter { get; set; }
    }

    /// <summary>Game progress. Written by the engine, saved with the table; an authored game has none.</summary>
    /// <summary>A mark over players' chalkers (<see cref="TableState.Marks"/>).</summary>
    public sealed class MarkDef
    {
        public string Id { get; set; } = "";
        /// <summary>Who wears it: <c>$current</c>, a list such as <c>$var.team</c>, <c>team:&lt;id&gt;</c>, <c>role:&lt;id&gt;</c> or a seat.</summary>
        public string Seats { get; set; } = "";
        public MarkShape Shape { get; set; }
        /// <summary>Its colour, [r, g, b] from 0 to 1; gold when left out.</summary>
        public float[]? Color { get; set; }
    }

    /// <summary><c>arrow</c>: a bobbing arrow pointing down at the head; <c>crown</c>: a crown floating over it.</summary>
    public enum MarkShape { Arrow, Crown }

    /// <summary>A collection written even when empty, because empty says something null does not (nobody awake, as told to a sleeper).</summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class KeepEmptyAttribute : Attribute
    {
    }

    public sealed class Session
    {
        /// <summary>
        /// The language this session's texts are in (ADR 0011): the engine's log lines and what it tells players. Null is
        /// English. Chosen when the game starts (T10.5); every player reads the same one.
        /// </summary>
        public string? Language { get; set; }
        public bool Started { get; set; }
        public bool Over { get; set; }
        public string? Winner { get; set; }
        /// <summary>Set when a team won (<c>win</c> with <c>team</c>); <see cref="Winner"/> is then the team id.</summary>
        public string? WinnerTeam { get; set; }
        public int Turn { get; set; }
        /// <summary>Seats playing this game, in seat order; empty means every seat (games saved before seats were chosen).</summary>
        public List<string> Players { get; set; } = new List<string>();
        public string? CurrentSeat { get; set; }
        public string? Phase { get; set; }
        /// <summary>Counts every phase entered, so a timed phase's end is not mistaken for a later visit of the same phase.</summary>
        public int PhaseVisit { get; set; }
        /// <summary>
        /// In an eyes-closed phase, the seats awake, as the host worked them out when it began; null otherwise. A seat's
        /// machine receives it only while that seat is awake (the awake see each other; a sleeper learns nothing).
        /// </summary>
        [KeepEmpty]
        public List<string>? Awake { get; set; }
        /// <summary>The seats whose thumbs the phase raises (<see cref="PhaseDef.Raised"/>); received like <see cref="Awake"/>, by the awake only.</summary>
        [KeepEmpty]
        public List<string>? Raised { get; set; }
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
        /// <summary>
        /// Messages logged since the game started, counting those dropped from <see cref="Log"/>: a view that showed the
        /// first N knows the lines after them are new even once the log is full.
        /// </summary>
        public int LogTotal { get; set; }
        /// <summary>
        /// Seats out of the game (<c>eliminate</c>: a lynch, an execution): they take no actions and cast no votes, sleep
        /// through eyes-closed phases, are not heard, and turns and seat choices pass them by.
        /// </summary>
        public List<string> Eliminated { get; set; } = new List<string>();
        /// <summary>Each vote action's last count (ADR 0012), read through <c>$vote.&lt;action&gt;</c>.</summary>
        public Dictionary<string, VoteTally> Votes { get; set; } = new Dictionary<string, VoteTally>();
        /// <summary>The vote counted last, which <c>$vote</c> reads.</summary>
        public string? LastVote { get; set; }
        /// <summary>Seat → its hidden role (ADR 0012). Each peer receives only its own.</summary>
        public Dictionary<string, string> Roles { get; set; } = new Dictionary<string, string>();
        /// <summary>Seat → set → its trait from an extra role set (<see cref="RoleDef.Set"/>). Each peer receives only its own.</summary>
        public Dictionary<string, Dictionary<string, string>> Traits { get; set; } = new Dictionary<string, Dictionary<string, string>>();
        /// <summary>Seat → its team when <c>setTeam</c> moved it from its role's. Each peer receives only its own.</summary>
        public Dictionary<string, string> Teams { get; set; } = new Dictionary<string, string>();
        /// <summary>Seat → what it knows of other seats' roles (dealt by <c>knows</c>, added by <c>revealRole</c>).</summary>
        public Dictionary<string, List<RoleSight>> Knows { get; set; } = new Dictionary<string, List<RoleSight>>();
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
        /// <summary>A player made a gesture at someone (a wink): <c>$actor</c>, <c>$param.gesture</c>, <c>$param.target</c>.</summary>
        public const string OnGesture = "onGesture";
        public static readonly IReadOnlyList<string> All = new[] { OnGameStart, OnTurnStart, OnTurnEnd, OnReveal, OnGesture };
    }
}
