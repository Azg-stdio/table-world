using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Core.Rules
{
    /// <summary>
    /// Hidden roles (ADR 0012 §2, T11.4): teams, the roles dealt at random when the game starts, team sizes by player
    /// count, and who knows whom. A seat's role reaches only that seat and the seats a <see cref="KnowsRule"/> or a
    /// <c>revealRole</c> effect shows it to (<c>Core.Net.SeatView</c>).
    /// </summary>
    public sealed class RolesDef
    {
        public List<TeamDef> Teams { get; set; } = new List<TeamDef>();
        public List<RoleDef> List { get; set; } = new List<RoleDef>();

        /// <summary>
        /// Team sizes by number of players (<c>"5": { "good": 3, "evil": 2 }</c>): each team's fixed roles go first and its
        /// <c>fill</c> role takes the rest. Without it the fixed counts stand and one <c>fill</c> role takes the rest.
        /// </summary>
        public Dictionary<string, Dictionary<string, int>> ByPlayers { get; set; } = new Dictionary<string, Dictionary<string, int>>();

        public List<KnowsRule> Knows { get; set; } = new List<KnowsRule>();
    }

    public sealed class TeamDef
    {
        public string Id { get; set; } = "";
        public string? Name { get; set; }
        /// <summary>The team's colour where players read its name (and its roles'): a seat colour name or <c>#rrggbb</c>.</summary>
        public string? Color { get; set; }
    }

    public sealed class RoleDef
    {
        public string Id { get; set; } = "";
        public string? Name { get; set; }
        /// <summary>The role's team; roles of an extra <see cref="Set"/> may have none.</summary>
        public string Team { get; set; } = "";
        /// <summary>
        /// Another hidden trait dealt beside the role (<c>"objective"</c>): each set is dealt one per seat on its own, from
        /// its counts (no <see cref="RolesDef.ByPlayers"/>), and read as <c>$actor.role.objective</c>. Null is the role.
        /// </summary>
        public string? Set { get; set; }
        /// <summary>How many seats get it: a number (1 when left out) or <c>"fill"</c> (the seats left in its team).</summary>
        public JToken? Count { get; set; }
        /// <summary>The host ticks it when starting (Avalon's Percival, Morgana); left out otherwise.</summary>
        public bool Optional { get; set; }
        public string? Description { get; set; }
        /// <summary>The role's colour where players read its name; null is its team's.</summary>
        public string? Color { get; set; }

        [JsonIgnore]
        public bool Fills => Count is JValue { Type: JTokenType.String } v && (string?)v == "fill";

        /// <summary>The fixed count (0 for a fill role).</summary>
        [JsonIgnore]
        public int Fixed => Fills ? 0 : Count is JValue { Type: JTokenType.Integer } n ? (int)n : 1;
    }

    /// <summary>
    /// What a role or a team sees at the deal: the seats whose role matches <see cref="Sees"/>, shown <see cref="As"/> their
    /// role, their team, or one of a set (Percival sees Merlin and Morgana without knowing which is which). A role's own
    /// rules replace its team's: <c>{ "role": "oberon", "sees": {} }</c> keeps Oberon blind to the other minions.
    /// </summary>
    public sealed class KnowsRule
    {
        public string? Role { get; set; }
        public string? Team { get; set; }
        public SeesDef Sees { get; set; } = new SeesDef();
        public KnowsAs As { get; set; }
        /// <summary>The rule holds only with at least this many players (a role that knows its team only in small games).</summary>
        public int? MinPlayers { get; set; }
        /// <summary>The rule holds only with at most this many players.</summary>
        public int? MaxPlayers { get; set; }
    }

    public sealed class SeesDef
    {
        public string? Team { get; set; }
        public List<string> Roles { get; set; } = new List<string>();
        public List<string> Except { get; set; } = new List<string>();
    }

    public enum KnowsAs { Team, Role, OneOf }

    /// <summary>What one seat knows of another's role: the role, only the team, or one of several roles.</summary>
    public sealed class RoleSight
    {
        public string Seat { get; set; } = "";
        public string? Role { get; set; }
        public string? Team { get; set; }
        public List<string>? OneOf { get; set; }
    }
}
