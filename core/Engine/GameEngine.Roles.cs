using System.Collections.Generic;
using System.Linq;
using Core.Rules;
using Core.Text;
using Newtonsoft.Json.Linq;

namespace Core.Engine
{
    /// <summary>
    /// Hidden roles (ADR 0012 §2, T11.4): dealt at random when the game starts, each seat told privately who it is and
    /// what its role lets it see. Later, rules show a role to others (<c>revealRole</c>), change one (<c>setRole</c>,
    /// <c>swapRoles</c>), move a seat to another team (<c>setTeam</c>) or tell a group something privately (<c>message</c>
    /// with <c>to</c>). Extra role sets deal more hidden traits beside the role. Reading or changing any of it marks the
    /// command (<see cref="CommandResult.ReadsSecrets"/>) so the host runs it and peers receive its result.
    /// </summary>
    public sealed partial class GameEngine
    {
        private bool _secretsRead;

        /// <summary>
        /// A seat's role (or its trait in <paramref name="set"/>), for code that may know it (the host, Lua's
        /// <c>Game.getRole</c>); null without one.
        /// </summary>
        public string? RoleOf(string seat, string? set = null)
        {
            if (Session == null)
                return null;
            if (set == null)
                return Session.Roles.TryGetValue(seat, out var role) ? role : null;
            return Session.Traits.TryGetValue(seat, out var traits) && traits.TryGetValue(set, out var trait) ? trait : null;
        }

        /// <summary>A seat's team: the one <c>setTeam</c> moved it to, else its role's; null without a role.</summary>
        public string? TeamOf(string seat) => TeamOf(State, seat);

        private static string? TeamOf(TableState state, string seat)
        {
            if (!(state.Session is { } session))
                return null;
            if (session.Teams.TryGetValue(seat, out var moved))
                return moved;
            return session.Roles.TryGetValue(seat, out var role) ? state.Roles?.List.FirstOrDefault(r => r.Id == role)?.Team : null;
        }

        /// <summary>A role as players read it, in the session's language.</summary>
        public static string RoleName(TableState state, RolesDef roles, string roleId) =>
            roles.List.FirstOrDefault(r => r.Id == roleId) is { Name: { } name } && name.Length > 0
                ? Translations.Text(state, $"roles.{roleId}.name", name)! : Names.Humanize(roleId);

        /// <summary>A team as players read it, in the session's language.</summary>
        public static string TeamName(TableState state, RolesDef roles, string teamId) =>
            roles.Teams.FirstOrDefault(t => t.Id == teamId) is { Name: { } name } && name.Length > 0
                ? Translations.Text(state, $"teams.{teamId}.name", name)! : Names.Humanize(teamId);

        /// <summary>The extra role sets the game deals beside the role, in the order they first appear.</summary>
        public static IEnumerable<string> RoleSets(RolesDef roles) => roles.List.Where(r => r.Set != null).Select(r => r.Set!).Distinct();

        /// <summary>
        /// The roles of <paramref name="set"/> (null: the role itself) for <paramref name="players"/> seats with the optional
        /// roles in <paramref name="chosen"/>, in list order, or the reason they do not add up. Team sizes apply to the role
        /// only; an extra set deals from its counts and one fill.
        /// </summary>
        public static (List<string>? Roles, string? Problem) RolesFor(RolesDef roles, int players, ICollection<string> chosen, string? set = null)
        {
            var inPlay = roles.List.Where(r => r.Set == set && (!r.Optional || chosen.Contains(r.Id))).ToList();
            var label = set == null ? "roles" : $"'{set}' roles";
            var dealt = new List<string>();
            if (set == null && roles.ByPlayers.Count > 0)
            {
                if (!roles.ByPlayers.TryGetValue(players.ToString(), out var sizes))
                    return (null, $"The roles have no team sizes for {players} players ({string.Join(", ", roles.ByPlayers.Keys)}).");
                foreach (var team in roles.Teams)
                {
                    var size = sizes.TryGetValue(team.Id, out var n) ? n : 0;
                    var members = inPlay.Where(r => r.Team == team.Id).ToList();
                    var fixedCount = members.Sum(r => r.Fixed);
                    if (fixedCount > size)
                        return (null, $"{(string.IsNullOrWhiteSpace(team.Name) ? Names.Humanize(team.Id) : team.Name)} has {size} seats with {players} players, but {fixedCount} roles.");
                    var fill = members.FirstOrDefault(r => r.Fills);
                    if (fixedCount < size && fill == null)
                        return (null, $"{(string.IsNullOrWhiteSpace(team.Name) ? Names.Humanize(team.Id) : team.Name)} has {size} seats with {players} players, but only {fixedCount} roles and none to fill the rest.");
                    foreach (var role in members)
                        dealt.AddRange(Enumerable.Repeat(role.Id, role.Fills ? size - fixedCount : role.Fixed));
                }
                if (dealt.Count != players)
                    return (null, $"The team sizes for {players} players add up to {dealt.Count}.");
                return (dealt, null);
            }
            var fixedTotal = inPlay.Sum(r => r.Fixed);
            var fills = inPlay.Where(r => r.Fills).ToList();
            if (fixedTotal > players || fixedTotal < players && fills.Count == 0)
                return (null, $"{fixedTotal} {label} for {players} players.");
            foreach (var role in inPlay)
                dealt.AddRange(Enumerable.Repeat(role.Id, role.Fills ? players - fixedTotal : role.Fixed));
            return (dealt, null);
        }

        /// <summary>Deals the roles (and every extra set) to the seats that play, then tells each seat who it is and what it sees.</summary>
        private void DealRoles(IReadOnlyList<Seat> seats, ICollection<string> chosen)
        {
            var roles = State.Roles;
            if (roles == null || roles.List.Count == 0)
                return;
            foreach (var id in chosen.Where(id => roles.List.All(r => r.Id != id || !r.Optional)))
                throw new RuleException("roles", ErrorCodes.UnknownRole, $"'{id}' is not an optional role.",
                    Suggest.OneOf(id, roles.List.Where(r => r.Optional).Select(r => r.Id), "an optional role"));
            var session = Session!;
            _secretsRead = true;
            foreach (var set in new string?[] { null }.Concat(RoleSets(roles)))
            {
                if (set == null && roles.List.All(r => r.Set != null))
                    continue;
                var (bag, problem) = RolesFor(roles, seats.Count, chosen, set);
                if (bag == null)
                    throw new RuleException("roles", ErrorCodes.RoleCount, problem!, "Change the roles, the team sizes (byPlayers) or the optional roles chosen.");
                for (var i = bag.Count - 1; i > 0; i--)
                {
                    var j = Table.Random.Next(i + 1);
                    (bag[i], bag[j]) = (bag[j], bag[i]);
                }
                for (var i = 0; i < seats.Count; i++)
                {
                    if (set == null)
                        session.Roles[seats[i].Id] = bag[i];
                    else
                    {
                        if (!session.Traits.TryGetValue(seats[i].Id, out var traits))
                            session.Traits[seats[i].Id] = traits = new Dictionary<string, string>();
                        traits[set] = bag[i];
                    }
                }
            }

            foreach (var seat in seats)
            {
                var sights = new List<RoleSight>();
                if (session.Roles.TryGetValue(seat.Id, out var mine))
                {
                    var team = roles.List.First(r => r.Id == mine).Team;
                    // A role's own rules replace its team's (Oberon is evil but sees none of the other minions); a rule may hold
                    // only for some player counts.
                    var counted = roles.Knows.Where(k => (k.MinPlayers ?? 0) <= seats.Count && seats.Count <= (k.MaxPlayers ?? int.MaxValue)).ToList();
                    var rules = counted.Any(k => k.Role == mine) ? counted.Where(k => k.Role == mine) : counted.Where(k => k.Role == null && k.Team == team);
                    foreach (var rule in rules)
                        foreach (var other in seats.Where(o => o.Id != seat.Id && session.Roles.ContainsKey(o.Id) && Matches(rule.Sees, session.Roles[o.Id])))
                        {
                            var theirs = session.Roles[other.Id];
                            var sight = rule.As switch
                            {
                                KnowsAs.Role => new RoleSight { Seat = other.Id, Role = theirs },
                                KnowsAs.OneOf => new RoleSight { Seat = other.Id, OneOf = rule.Sees.Roles.ToList() },
                                _ => new RoleSight { Seat = other.Id, Team = roles.List.First(r => r.Id == theirs).Team },
                            };
                            sights.RemoveAll(s => s.Seat == other.Id);
                            sights.Add(sight);
                        }
                }
                session.Knows[seat.Id] = sights;
                foreach (var line in RoleLines(State, seat.Id))
                    Tell(seat.Id, line);
            }
        }

        private bool Matches(SeesDef sees, string roleId)
        {
            if (sees.Except.Contains(roleId))
                return false;
            var team = State.Roles!.List.FirstOrDefault(r => r.Id == roleId)?.Team;
            return sees.Roles.Contains(roleId) || sees.Team != null && sees.Team == team;
        }

        /// <summary>
        /// What <paramref name="seat"/> knows, as it reads it: "You are Merlin (Loyal servants of Arthur).", its traits from
        /// extra sets ("Objective: Spy."), then its sights grouped ("Red and Blue are Minions of Mordred."). Empty without a
        /// role. The HUD shows the same lines.
        /// </summary>
        public static List<string> RoleLines(TableState state, string seat)
        {
            var lines = new List<string>();
            if (!(state.Roles is { } roles) || !(state.Session is { } session))
                return lines;
            if (session.Roles.TryGetValue(seat, out var mine))
            {
                var team = TeamOf(state, seat) ?? "";
                lines.Add(team.Length == 0
                    ? Names.Message(state, "youAreRole", ("role", RoleName(state, roles, mine)))
                    : Names.Message(state, "youAre", ("role", RoleName(state, roles, mine)), ("team", TeamName(state, roles, team))));
            }
            if (session.Traits.TryGetValue(seat, out var traits))
                foreach (var set in RoleSets(roles).Where(traits.ContainsKey))
                    lines.Add(Names.Message(state, "youHave", ("set", Names.Humanize(set)), ("role", RoleName(state, roles, traits[set]))));
            lines.AddRange(Describe(state, roles, session.Knows.TryGetValue(seat, out var sights) ? sights : new List<RoleSight>()));
            return lines;
        }

        /// <summary>What a seat's sights say, grouped: "Red and Blue are Minions of Mordred."</summary>
        private static IEnumerable<string> Describe(TableState state, RolesDef roles, List<RoleSight> sights)
        {
            foreach (var group in sights.GroupBy(s => s.Role != null ? "r:" + s.Role : s.Team != null ? "t:" + s.Team : "o:" + string.Join(",", s.OneOf ?? new List<string>())))
            {
                var first = group.First();
                var who = Join(state, group.Select(s => Names.Seat(state, s.Seat)).ToList());
                var plural = group.Count() > 1;
                if (first.Role != null)
                    yield return Names.Message(state, "seenAsRole", plural ? 2 : 1, ("seats", who), ("role", RoleName(state, roles, first.Role)));
                else if (first.Team != null)
                    yield return Names.Message(state, "seenAsTeam", plural ? 2 : 1, ("seats", who), ("team", TeamName(state, roles, first.Team)));
                else
                    yield return Names.Message(state, "seenAsOneOf", ("seats", who),
                        ("roles", Join(state, (first.OneOf ?? new List<string>()).Select(r => RoleName(state, roles, r)).ToList(), "or")));
            }
        }

        private string Join(List<string> items, string last = "and") => Join(State, items, last);

        /// <summary>"A, B and C" in the session's language (<paramref name="last"/> is <c>and</c> or <c>or</c>).</summary>
        private static string Join(TableState state, List<string> items, string last = "and") =>
            items.Count <= 1 ? string.Join("", items) : string.Join(", ", items.Take(items.Count - 1)) + $" {Names.Word(state, last)} " + items[items.Count - 1];

        /// <summary>A line only <paramref name="seat"/> reads (never the shared log).</summary>
        private void Tell(string seat, string text)
        {
            // Only the host sends a seat its own lines.
            _secretsRead = true;
            _events.Add(new PrivateMessage(seat, text));
        }

        /// <summary>
        /// The seats a <c>to</c> field names: a seat reference or id, <c>team:&lt;id&gt;</c>, <c>role:&lt;id&gt;</c>; null
        /// (every seat with a role, or every seat playing) for null or <c>all</c>.
        /// </summary>
        private List<string>? Audience(string? to, string field)
        {
            if (to == null || to == "all")
                return null;
            var playing = Playing.Select(s => s.Id).ToList();
            if (to.StartsWith("team:"))
            {
                _secretsRead = true;
                return playing.Where(s => TeamOf(s) == to.Substring(5)).ToList();
            }
            if (to.StartsWith("role:"))
            {
                _secretsRead = true;
                return playing.Where(s => RoleOf(s) == to.Substring(5)).ToList();
            }
            return new List<string> { ResolveSeat(to, field) };
        }

        /// <summary>A private line for each seat <paramref name="to"/> names (a message effect with <c>to</c>).</summary>
        private void TellAudience(string to, string text)
        {
            foreach (var seat in Audience(to, "message.to") ?? Playing.Select(s => s.Id).ToList())
                Tell(seat, text);
        }

        /// <summary>
        /// Shows <paramref name="seat"/>'s role (or only its team, or its trait in <paramref name="set"/>) to
        /// <paramref name="to"/>; every seat when null.
        /// </summary>
        private void RevealRole(string seat, IReadOnlyList<string>? to, bool teamOnly, string? set = null)
        {
            var roles = State.Roles;
            var session = Session!;
            _secretsRead = true;
            var role = RoleOf(seat, set);
            if (roles == null || role == null)
                throw new RuleException("revealRole.seat", ErrorCodes.UnknownRole, $"{Names.Seat(State, seat)} has no {(set == null ? "role" : $"'{set}' role")}.");
            var viewers = to ?? session.Roles.Keys.Concat(session.Traits.Keys).Distinct().ToList();
            string line;
            if (set != null)
                line = Names.Message(State, "revealedTrait", ("seat", Names.Seat(State, seat)), ("set", Names.Humanize(set).ToLowerInvariant()), ("role", RoleName(State, roles, role)));
            else
            {
                var team = TeamOf(seat) ?? "";
                var sight = teamOnly ? new RoleSight { Seat = seat, Team = team } : new RoleSight { Seat = seat, Role = role };
                line = teamOnly
                    ? Names.Message(State, "revealedTeam", ("seat", Names.Seat(State, seat)), ("team", TeamName(State, roles, team)))
                    : Names.Message(State, "revealedRole", ("seat", Names.Seat(State, seat)), ("role", RoleName(State, roles, role)));
                foreach (var viewer in viewers.Where(v => v != seat))
                {
                    if (!session.Knows.TryGetValue(viewer, out var sights))
                        session.Knows[viewer] = sights = new List<RoleSight>();
                    // A role shown outright replaces a team or a one-of; a team never hides a role already known.
                    if (teamOnly && sights.Any(s => s.Seat == seat && s.Role != null))
                        continue;
                    sights.RemoveAll(s => s.Seat == seat);
                    sights.Add(sight);
                }
            }
            if (to == null)
                Log(line);
            else
                foreach (var viewer in viewers.Where(v => v != seat))
                    Tell(viewer, line);
        }

        /// <summary>Gives <paramref name="seat"/> <paramref name="role"/> (in <paramref name="set"/>); a new role brings its own team.</summary>
        private void SetRole(string seat, string role, string? set, bool tell)
        {
            var roles = State.Roles ?? throw new RuleException("setRole", ErrorCodes.UnknownRole, "This game has no roles.");
            _secretsRead = true;
            if (roles.List.All(r => r.Id != role || r.Set != set))
                throw new RuleException("setRole.role", ErrorCodes.UnknownRole, $"'{role}' is not a {(set == null ? "role" : $"'{set}' role")}.",
                    Suggest.OneOf(role, roles.List.Where(r => r.Set == set).Select(r => r.Id), "a role id"));
            var session = Session!;
            if (set == null)
            {
                session.Roles[seat] = role;
                session.Teams.Remove(seat);
            }
            else
            {
                if (!session.Traits.TryGetValue(seat, out var traits))
                    session.Traits[seat] = traits = new Dictionary<string, string>();
                traits[set] = role;
            }
            if (tell)
                TellNow(seat, set);
        }

        private void SwapRoles(string a, string b, string? set, bool tell)
        {
            _secretsRead = true;
            var roleA = RoleOf(a, set) ?? throw new RuleException("swapRoles.a", ErrorCodes.UnknownRole, $"{Names.Seat(State, a)} has no role to swap.");
            var roleB = RoleOf(b, set) ?? throw new RuleException("swapRoles.b", ErrorCodes.UnknownRole, $"{Names.Seat(State, b)} has no role to swap.");
            var session = Session!;
            // Each seat's team goes with its role card.
            session.Teams.TryGetValue(a, out var teamA);
            session.Teams.TryGetValue(b, out var teamB);
            SetRole(a, roleB, set, tell: false);
            SetRole(b, roleA, set, tell: false);
            if (set == null)
            {
                if (teamB != null) session.Teams[a] = teamB;
                if (teamA != null) session.Teams[b] = teamA;
            }
            if (tell)
            {
                TellNow(a, set);
                TellNow(b, set);
            }
        }

        private void SetTeam(string seat, string team, bool tell)
        {
            var roles = State.Roles ?? throw new RuleException("setTeam", ErrorCodes.UnknownTeam, "This game has no roles.");
            _secretsRead = true;
            if (roles.Teams.All(t => t.Id != team))
                throw new RuleException("setTeam.team", ErrorCodes.UnknownTeam, $"Team '{team}' does not exist.", Suggest.OneOf(team, roles.Teams.Select(t => t.Id), "a team id"));
            var session = Session!;
            if (session.Roles.TryGetValue(seat, out var role) && roles.List.FirstOrDefault(r => r.Id == role)?.Team == team)
                session.Teams.Remove(seat);
            else
                session.Teams[seat] = team;
            if (tell)
                Tell(seat, Names.Message(State, "nowTeam", ("team", TeamName(State, roles, team))));
        }

        /// <summary>A role or team id written plainly, or read from a reference (<c>$param.target.role</c>).</summary>
        private string RoleId(string text, string field) =>
            References.IsReference(text) ? (Resolve(text, field) as JValue)?.ToString() ?? throw new RuleException(field, ErrorCodes.InvalidReference, $"'{text}' names no role right now.") : text;

        /// <summary>Tells a seat its new role (or trait) after a change.</summary>
        private void TellNow(string seat, string? set)
        {
            var roles = State.Roles!;
            if (set != null)
                Tell(seat, Names.Message(State, "nowTrait", ("set", Names.Humanize(set).ToLowerInvariant()), ("role", RoleName(State, roles, RoleOf(seat, set)!))));
            else
                Tell(seat, Names.Message(State, "nowRole", ("role", RoleName(State, roles, RoleOf(seat)!)), ("team", TeamName(State, roles, TeamOf(seat) ?? ""))));
        }

        /// <summary>
        /// <c>$seat.role</c> (or <c>$seat.role.&lt;set&gt;</c> for an extra set) and <c>$seat.team</c>; null when unknown here.
        /// </summary>
        private string? RoleStep(Seat seat, string part, string[] parts, ref int i)
        {
            _secretsRead = true;
            if (part == "team")
                return TeamOf(seat.Id);
            if (i + 1 < parts.Length && State.Roles is { } roles && RoleSets(roles).Contains(parts[i + 1]))
                return RoleOf(seat.Id, parts[++i]);
            return RoleOf(seat.Id);
        }
    }
}
