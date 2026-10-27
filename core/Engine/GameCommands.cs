using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Core.Engine
{
    /// <summary>Starts the game described by the table's rules: session, onGameStart, first turn.</summary>
    public sealed class StartGameCommand : Command
    {
        /// <summary>
        /// The seats that play, in any order (turn order follows the table's seat list); empty means every seat. A table
        /// keeps all its seats: a 3–6 player game has six, and starts with the people sitting at it.
        /// </summary>
        public List<string> Seats { get; set; } = new List<string>();

        /// <summary>The optional roles to deal (Avalon's Percival, Morgana); the others stay out of the game.</summary>
        public List<string> Roles { get; set; } = new List<string>();
    }

    /// <summary>
    /// A face or body gesture made at someone (ADR 0012 §6, T11.13): a wink, an emote, eyes closed, aimed at the seat whose
    /// face <see cref="Command.Actor"/> was looking at (<see cref="Target"/>, null for nobody). It runs the rules'
    /// <c>onGesture</c> effects with <c>$param.gesture</c> and <c>$param.target</c>. Gestures pass between two players:
    /// the host runs them, and other seats receive the command without its target.
    /// </summary>
    public sealed class GestureCommand : Command
    {
        public string Gesture { get; set; } = "";
        public string? Target { get; set; }
    }

    /// <summary>
    /// Counts a vote now with the ballots cast so far: the host's game window sends it when a vote's time is up
    /// (<c>VoteDef.Seconds</c>). Nothing happens if the vote has no ballot waiting.
    /// </summary>
    public sealed class CloseVoteCommand : Command, IHostCommand
    {
        public string Action { get; set; } = "";
    }

    /// <summary>Performs a rules action (docs/rules-schema.md, "Actions") as <see cref="Command.Actor"/>.</summary>
    public sealed class PerformActionCommand : Command
    {
        public string ActionId { get; set; } = "";
        /// <summary>Parameter name → value (an object id, a seat id, a number or a choice).</summary>
        public Dictionary<string, string> Params { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// A table script's change to the game in play (the Lua <c>Game</c> API): <see cref="Op"/> is <c>setResource</c>,
    /// <c>addResource</c>, <c>setVariable</c>, <c>endTurn</c>, <c>endPhase</c>, <c>gotoPhase</c>, <c>win</c>, <c>log</c> or
    /// <c>revealRole</c> (<see cref="Seat"/> shown to <see cref="Name"/>, every seat when null; <see cref="Value"/> true for the team only),
    /// <c>setRole</c> or <c>setTeam</c> (<see cref="Seat"/> given <see cref="Name"/>; <see cref="Value"/> true tells the player),
    /// <c>winTeam</c> (<see cref="Name"/> the team).
    /// Inside a <c>lua</c> effect it joins that action's transaction; from elsewhere (a button) it is a command of its own,
    /// sent to every peer like any other. Only the host's scripts issue it.
    /// </summary>
    public sealed class ScriptRulesCommand : Command, IHostCommand
    {
        public string Op { get; set; } = "";
        public string? Seat { get; set; }
        public string? Name { get; set; }
        public JToken? Value { get; set; }
    }

    /// <summary>An action a seat may perform now, with every parameter combination that passes its preconditions.</summary>
    public sealed class LegalAction
    {
        public string ActionId { get; }
        public string? Name { get; }
        /// <summary>One entry per valid parameter combination; a single empty entry for an action without parameters.</summary>
        public IReadOnlyList<Dictionary<string, string>> Choices { get; }

        public LegalAction(string actionId, string? name, IReadOnlyList<Dictionary<string, string>> choices)
        {
            ActionId = actionId;
            Name = name;
            Choices = choices;
        }
    }
}
