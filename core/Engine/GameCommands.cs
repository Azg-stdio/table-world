using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Core.Engine
{
    /// <summary>Starts the game described by the table's rules: session, onGameStart, first turn.</summary>
    public sealed class StartGameCommand : Command
    {
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
    /// <c>addResource</c>, <c>setVariable</c>, <c>endTurn</c>, <c>endPhase</c>, <c>gotoPhase</c>, <c>win</c> or <c>log</c>.
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
