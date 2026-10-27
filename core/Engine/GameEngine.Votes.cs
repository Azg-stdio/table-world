using System.Collections.Generic;
using System.Linq;
using Core.Rules;
using Newtonsoft.Json.Linq;

namespace Core.Engine
{
    /// <summary>
    /// Votes (ADR 0012 §5, T11.5): a vote action records each voter's choice as a secret, and the last vote counts them
    /// all. The count is logged as the vote says (who voted what, how many chose each option, or only the outcome), kept for
    /// <c>$vote</c>, and the vote's <c>onResult</c> runs. Nothing here knows any game: options, voters and outcomes are the
    /// game's.
    /// </summary>
    public sealed partial class GameEngine
    {
        public const string VoteParam = "choice";

        /// <summary>An action's parameters: its own, and a vote's <c>choice</c>.</summary>
        public static IEnumerable<ParamDef> ParamsOf(ActionDef action)
        {
            foreach (var param in action.Params)
                yield return param;
            if (action.Vote is { } vote && action.Params.All(p => p.Name != VoteParam))
                yield return vote.Seats
                    ? new ParamDef { Name = VoteParam, Type = ParamType.Seat }
                    : new ParamDef { Name = VoteParam, Type = ParamType.Choice, Options = vote.Options, Labels = vote.Labels };
        }

        /// <summary>The seats that vote in <paramref name="action"/>'s vote now, in seat order.</summary>
        public List<string> VotersOf(ActionDef action)
        {
            var playing = Playing.Select(s => s.Id).Where(s => !IsOut(s)).ToList();
            var who = action.Vote?.Who;
            if (who == null || who == "all")
                return playing;
            if (who.StartsWith("team:") || who.StartsWith("role:"))
                return (Audience(who, "vote.who") ?? playing).Where(playing.Contains).ToList();
            if (!References.IsReference(who))
                return playing.Where(s => s == who).ToList();
            var named = Resolve(who, "vote.who") switch
            {
                Seat seat => new List<string> { seat.Id },
                JArray list => list.Select(t => t.Type == JTokenType.String ? (string)t! : null).Where(s => s != null).Select(s => s!).ToList(),
                JValue { Type: JTokenType.String } one => new List<string> { (string)one! },
                _ => new List<string>(),
            };
            return playing.Where(named.Contains).ToList();
        }

        /// <summary>Records <paramref name="seat"/>'s vote; the last voter's counts them all.</summary>
        private void CastVote(ActionDef action, string seat, Dictionary<string, string> choice)
        {
            var session = Session!;
            if (!VotersOf(action).Contains(seat))
                throw new RuleException("actor", ErrorCodes.NotYourSeat, $"{Names.Seat(State, seat)} does not vote in '{action.Name ?? action.Id}'.");
            var again = session.Secrets.Any(s => s.Seat == seat && s.Action == action.Id);
            session.Secrets.RemoveAll(s => s.Seat == seat && s.Action == action.Id);
            session.Secrets.Add(new SecretChoice { Seat = seat, Action = action.Id, Params = new Dictionary<string, string>(choice) });
            if (!again)
                Say(action, Names.Message(State, "voted", ("seat", Names.Seat(State, seat))));
            if (VotersOf(action).All(v => session.Secrets.Any(s => s.Seat == v && s.Action == action.Id)))
                CountVote(action);
        }

        /// <summary>Counts <paramref name="action"/>'s votes cast so far (all of them, or those in when a rule closes it).</summary>
        private void CountVote(ActionDef action)
        {
            var session = Session!;
            var vote = action.Vote!;
            var ballots = session.Secrets.Where(s => s.Action == action.Id).ToList();
            session.Secrets.RemoveAll(ballots.Contains);
            _secretsRead = true;
            var picks = ballots.ToDictionary(b => b.Seat, b => b.Params.TryGetValue(VoteParam, out var v) ? v : "");
            var options = vote.Seats ? picks.Values.Distinct().OrderBy(v => v).ToList() : vote.Options;
            var counts = options.ToDictionary(o => o, o => picks.Values.Count(v => v == o));
            var total = picks.Count;
            var otherwise = vote.Otherwise ?? "tie";
            string result;
            var top = counts.Count == 0 ? 0 : counts.Values.Max();
            var leaders = counts.Where(c => c.Value == top && top > 0).Select(c => c.Key).ToList();
            switch (vote.Decide)
            {
                case VoteDecision.Majority:
                    result = leaders.Count == 1 && top * 2 > total ? leaders[0] : otherwise;
                    break;
                case VoteDecision.Unanimous:
                    result = leaders.Count == 1 && top == total ? leaders[0] : otherwise;
                    break;
                default:
                    result = leaders.Count == 1 ? leaders[0] : otherwise;
                    break;
            }
            var tally = new VoteTally
            {
                Action = action.Id, Result = result, Counts = counts, Total = total,
                Votes = vote.Reveal == VoteReveal.Named ? picks : new Dictionary<string, string>(),
            };
            session.Votes[action.Id] = tally;
            session.LastVote = action.Id;

            string Label(string option) => vote.Seats ? Names.Seat(State, option) : VoteLabel(vote, option);
            var name = action.Name ?? Names.Humanize(action.Id);
            if (vote.Reveal == VoteReveal.Named)
                Say(action, Names.Message(State, "voteNamed", ("vote", name),
                    ("votes", string.Join(", ", Playing.Where(s => picks.ContainsKey(s.Id)).Select(s => $"{Names.Of(State, s)} {Label(picks[s.Id])}")))));
            else if (vote.Reveal == VoteReveal.Count)
                Say(action, Names.Message(State, "voteCount", ("vote", name),
                    ("counts", string.Join(", ", counts.Where(c => c.Value > 0 || !vote.Seats).Select(c => $"{c.Value} {Label(c.Key)}")))));
            Say(action, Names.Message(State, "voteResult", ("vote", name),
                ("result", result == otherwise && !counts.ContainsKey(result) ? Names.Humanize(result) : Label(result))));
            RunEffects(vote.OnResult);
        }

        /// <summary>A vote's line: in the shared log, or for a private vote to its voters alone.</summary>
        private void Say(ActionDef action, string line)
        {
            if (action.Vote is { Private: true })
                foreach (var voter in VotersOf(action))
                    Tell(voter, line);
            else
                Log(line);
        }

        private static string VoteLabel(VoteDef vote, string option) =>
            vote.Labels != null && vote.Labels.TryGetValue(option, out var label) && !string.IsNullOrWhiteSpace(label) ? label : Names.Humanize(option);

        /// <summary>A rule closes a vote before everyone has voted (a timer ran out): the votes in are counted.</summary>
        private void CloseVote(string actionId)
        {
            var action = State.Actions.FirstOrDefault(a => a.Id == actionId && a.Vote != null)
                ?? throw new RuleException("closeVote.action", ErrorCodes.UnknownAction, $"'{actionId}' is not a vote.",
                    Suggest.OneOf(actionId, State.Actions.Where(a => a.Vote != null).Select(a => a.Id), "a vote"));
            CountVote(action);
        }

        /// <summary><c>$vote.result</c>, <c>$vote.total</c>, <c>$vote.&lt;option&gt;</c>, <c>$vote.of.&lt;seat&gt;</c>; <c>$vote.&lt;action&gt;.…</c> for one vote.</summary>
        private object? VoteReference(string[] parts)
        {
            var session = Session;
            if (session == null)
                return null;
            var i = 1;
            VoteTally? tally;
            if (parts.Length > 1 && session.Votes.TryGetValue(parts[1], out var named))
            {
                tally = named;
                i = 2;
            }
            else
                tally = session.LastVote != null && session.Votes.TryGetValue(session.LastVote, out var last) ? last : null;
            if (tally == null || i >= parts.Length)
                return null;
            return parts[i] switch
            {
                "result" => new JValue(tally.Result),
                "total" => new JValue(tally.Total),
                "of" when i + 1 < parts.Length => tally.Votes.TryGetValue(parts[i + 1], out var cast) ? new JValue(cast) : null,
                var option => tally.Counts.TryGetValue(option, out var count) ? new JValue(count) : new JValue(0),
            };
        }
    }
}
