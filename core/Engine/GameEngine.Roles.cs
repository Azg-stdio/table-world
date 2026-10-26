using System.Collections.Generic;
using System.Linq;
using Core.Rules;

namespace Core.Engine
{
    /// <summary>
    /// Hidden roles (ADR 0012 §2, T11.4): dealt at random when the game starts, each seat told privately who it is and
    /// what its role lets it see; <c>revealRole</c> shows one seat's role to others later. Reading or changing a role
    /// marks the command (<see cref="CommandResult.ReadsSecrets"/>) so the host runs it and peers receive its result.
    /// </summary>
    public sealed partial class GameEngine
    {
        private bool _secretsRead;

        /// <summary>A seat's role, for code that may know it (the host, Lua's <c>Game.getRole</c>); null without one.</summary>
        public string? RoleOf(string seat) => Session != null && Session.Roles.TryGetValue(seat, out var role) ? role : null;

        public static string RoleName(RolesDef roles, string roleId) =>
            roles.List.FirstOrDefault(r => r.Id == roleId) is { Name: { } name } && name.Length > 0 ? name : Names.Humanize(roleId);

        public static string TeamName(RolesDef roles, string teamId) =>
            roles.Teams.FirstOrDefault(t => t.Id == teamId) is { Name: { } name } && name.Length > 0 ? name : Names.Humanize(teamId);

        /// <summary>
        /// The roles for <paramref name="players"/> seats with the optional roles in <paramref name="chosen"/>, in list order,
        /// or the reason they do not add up.
        /// </summary>
        public static (List<string>? Roles, string? Problem) RolesFor(RolesDef roles, int players, ICollection<string> chosen)
        {
            var inPlay = roles.List.Where(r => !r.Optional || chosen.Contains(r.Id)).ToList();
            var dealt = new List<string>();
            if (roles.ByPlayers.Count > 0)
            {
                if (!roles.ByPlayers.TryGetValue(players.ToString(), out var sizes))
                    return (null, $"The roles have no team sizes for {players} players ({string.Join(", ", roles.ByPlayers.Keys)}).");
                foreach (var team in roles.Teams)
                {
                    var size = sizes.TryGetValue(team.Id, out var n) ? n : 0;
                    var members = inPlay.Where(r => r.Team == team.Id).ToList();
                    var fixedCount = members.Sum(r => r.Fixed);
                    if (fixedCount > size)
                        return (null, $"{TeamName(roles, team.Id)} has {size} seats with {players} players, but {fixedCount} roles.");
                    var fill = members.FirstOrDefault(r => r.Fills);
                    if (fixedCount < size && fill == null)
                        return (null, $"{TeamName(roles, team.Id)} has {size} seats with {players} players, but only {fixedCount} roles and none to fill the rest.");
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
                return (null, $"{fixedTotal} roles for {players} players.");
            foreach (var role in inPlay)
                dealt.AddRange(Enumerable.Repeat(role.Id, role.Fills ? players - fixedTotal : role.Fixed));
            return (dealt, null);
        }

        /// <summary>Deals the roles to the seats that play, then tells each seat who it is and what it sees.</summary>
        private void DealRoles(IReadOnlyList<Seat> seats, ICollection<string> chosen)
        {
            var roles = State.Roles;
            if (roles == null || roles.List.Count == 0)
                return;
            foreach (var id in chosen.Where(id => roles.List.All(r => r.Id != id || !r.Optional)))
                throw new RuleException("roles", ErrorCodes.UnknownRole, $"'{id}' is not an optional role.",
                    Suggest.OneOf(id, roles.List.Where(r => r.Optional).Select(r => r.Id), "an optional role"));
            var (bag, problem) = RolesFor(roles, seats.Count, chosen);
            if (bag == null)
                throw new RuleException("roles", ErrorCodes.RoleCount, problem!, "Change the roles, the team sizes (byPlayers) or the optional roles chosen.");
            for (var i = bag.Count - 1; i > 0; i--)
            {
                var j = Table.Random.Next(i + 1);
                (bag[i], bag[j]) = (bag[j], bag[i]);
            }
            var session = Session!;
            for (var i = 0; i < seats.Count; i++)
                session.Roles[seats[i].Id] = bag[i];
            _secretsRead = true;

            foreach (var seat in seats)
            {
                var mine = session.Roles[seat.Id];
                var team = roles.List.First(r => r.Id == mine).Team;
                var sights = new List<RoleSight>();
                // A role's own rules replace its team's (Oberon is evil but sees none of the other minions).
                var rules = roles.Knows.Any(k => k.Role == mine) ? roles.Knows.Where(k => k.Role == mine) : roles.Knows.Where(k => k.Role == null && k.Team == team);
                foreach (var rule in rules)
                {
                    var seen = seats.Where(other => other.Id != seat.Id && Matches(rule.Sees, session.Roles[other.Id])).ToList();
                    foreach (var other in seen)
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
        /// What <paramref name="seat"/> knows, as it reads it: "You are Merlin (Loyal servants of Arthur).", then its sights
        /// grouped ("Red and Blue are Minions of Mordred."). Empty without a role. The HUD shows the same lines.
        /// </summary>
        public static List<string> RoleLines(TableState state, string seat)
        {
            var lines = new List<string>();
            if (!(state.Roles is { } roles) || !(state.Session is { } session) || !session.Roles.TryGetValue(seat, out var mine))
                return lines;
            var team = roles.List.FirstOrDefault(r => r.Id == mine)?.Team ?? "";
            lines.Add(Names.Message(state, "youAre", "You are {role} ({team}).", ("role", RoleName(roles, mine)), ("team", TeamName(roles, team))));
            lines.AddRange(Describe(state, roles, session.Knows.TryGetValue(seat, out var sights) ? sights : new List<RoleSight>()));
            return lines;
        }

        /// <summary>What a seat's sights say, grouped: "Red and Blue are Minions of Mordred."</summary>
        private static IEnumerable<string> Describe(TableState state, RolesDef roles, List<RoleSight> sights)
        {
            foreach (var group in sights.GroupBy(s => s.Role != null ? "r:" + s.Role : s.Team != null ? "t:" + s.Team : "o:" + string.Join(",", s.OneOf ?? new List<string>())))
            {
                var first = group.First();
                var who = Join(group.Select(s => Names.Seat(state, s.Seat)).ToList());
                var plural = group.Count() > 1;
                if (first.Role != null)
                    yield return Names.Message(state, "seenAsRole", plural ? "{seats} are {role}." : "{seats} is {role}.", ("seats", who), ("role", RoleName(roles, first.Role)));
                else if (first.Team != null)
                    yield return Names.Message(state, "seenAsTeam", plural ? "{seats} are {team}." : "{seats} is {team}.", ("seats", who), ("team", TeamName(roles, first.Team)));
                else
                    yield return Names.Message(state, "seenAsOneOf", "{seats}: each one of {roles}.", ("seats", who),
                        ("roles", Join((first.OneOf ?? new List<string>()).Select(r => RoleName(roles, r)).ToList(), "or")));
            }
        }

        private static string Join(List<string> items, string last = "and") =>
            items.Count <= 1 ? string.Join("", items) : string.Join(", ", items.Take(items.Count - 1)) + $" {last} " + items[items.Count - 1];

        /// <summary>A line only <paramref name="seat"/> reads (never the shared log).</summary>
        private void Tell(string seat, string text) => _events.Add(new PrivateMessage(seat, text));

        /// <summary>
        /// Shows <paramref name="seat"/>'s role (or only its team) to <paramref name="to"/>, or to every seat when null.
        /// </summary>
        private void RevealRole(string seat, IReadOnlyList<string>? to, bool teamOnly)
        {
            var roles = State.Roles;
            var session = Session!;
            _secretsRead = true;
            if (roles == null || !session.Roles.TryGetValue(seat, out var role))
                throw new RuleException("revealRole.seat", ErrorCodes.UnknownRole, $"{Names.Seat(State, seat)} has no role.");
            var team = roles.List.First(r => r.Id == role).Team;
            var viewers = to ?? session.Roles.Keys.ToList();
            var sight = teamOnly ? new RoleSight { Seat = seat, Team = team } : new RoleSight { Seat = seat, Role = role };
            var line = teamOnly
                ? Names.Message(State, "revealedTeam", "{seat} is {team}.", ("seat", Names.Seat(State, seat)), ("team", TeamName(roles, team)))
                : Names.Message(State, "revealedRole", "{seat} is {role}.", ("seat", Names.Seat(State, seat)), ("role", RoleName(roles, role)));
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
            if (to == null)
                Log(line);
            else
                foreach (var viewer in viewers.Where(v => v != seat))
                    Tell(viewer, line);
        }

        /// <summary>A seat's role or team, as <c>$seat.role</c> and <c>$seat.team</c> read them; null when unknown here.</summary>
        private string? RoleStep(Seat seat, string part)
        {
            _secretsRead = true;
            var role = RoleOf(seat.Id);
            if (role == null)
                return null;
            return part == "role" ? role : State.Roles?.List.FirstOrDefault(r => r.Id == role)?.Team;
        }
    }
}
