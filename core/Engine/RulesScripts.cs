using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Core.Engine
{
    /// <summary>
    /// The Lua the rules can call (<c>lua</c> effects): the host's script host. Peers that do not run scripts leave
    /// <see cref="GameEngine.Scripts"/> null; their <c>lua</c> effects are skipped and the host's state hash corrects them.
    /// </summary>
    public interface IRulesScripts
    {
        /// <summary>Runs the global function <paramref name="function"/>; null when it ran, else why it refused or failed.</summary>
        string? CallRule(string function, RuleCall call);
    }

    /// <summary>What a <c>lua</c> effect hands its function, and how the function's changes reach the game.</summary>
    public sealed class RuleCall
    {
        public string? Actor { get; set; }
        public IReadOnlyDictionary<string, string> Params { get; set; } = new Dictionary<string, string>();
        /// <summary>The seat or object id <c>$it</c> names inside a forEach, or null.</summary>
        public string? It { get; set; }
        public JObject? Args { get; set; }
        /// <summary>Runs a command inside the action's transaction (not sent to peers on its own).</summary>
        public Func<Command, CommandResult> Execute { get; set; } = _ => throw new InvalidOperationException();
    }
}
