using System.Collections.Generic;

namespace Core.Rules
{
    /// <summary>
    /// A vote (ADR 0012 §5, T11.5): an action whose choice is cast in secret by the seats in <see cref="Who"/> and counted
    /// when the last of them has voted (or a <c>closeVote</c> effect closes it). The count is shown as <see cref="Reveal"/>
    /// says, decided as <see cref="Decide"/> says, and <see cref="OnResult"/> runs with <c>$vote</c> bound to it. Its choice
    /// is the action's <c>choice</c> parameter, given by <see cref="Options"/> or, with <see cref="Seats"/>, a seat.
    /// </summary>
    public sealed class VoteDef
    {
        public List<string> Options { get; set; } = new List<string>();
        /// <summary>Vote for a seat (a lynch, an accusation) instead of an option.</summary>
        public bool Seats { get; set; }
        /// <summary>Words players read for an option.</summary>
        public Dictionary<string, string>? Labels { get; set; }
        /// <summary>The colour of an option where players read it (<c>"reject": "red"</c>): a seat colour name or <c>#rrggbb</c>.</summary>
        public Dictionary<string, string>? Colors { get; set; }

        /// <summary>
        /// Who votes: every seat playing when left out or <c>all</c>, <c>team:&lt;id&gt;</c>, <c>role:&lt;id&gt;</c>, a seat,
        /// or a reference to a list of seats (<c>$var.quest_team</c>).
        /// </summary>
        public string? Who { get; set; }

        public VoteReveal Reveal { get; set; }
        public VoteDecision Decide { get; set; }

        /// <summary>The result on a tie, or when no option reaches what <see cref="Decide"/> needs; <c>tie</c> when left out.</summary>
        public string? Otherwise { get; set; }

        public List<Effect> OnResult { get; set; } = new List<Effect>();

        /// <summary>
        /// Only the voters know about it (the wolves' choice of a victim at night): "voted" and the count are told to them
        /// alone, never logged, and no other machine learns that anyone cast a ballot.
        /// </summary>
        public bool Private { get; set; }

        /// <summary>
        /// The vote closes this many seconds after its first ballot, counting the votes cast by then. The host's game
        /// window keeps the time (the rules have no clock) and closes it with a <c>CloseVoteCommand</c>.
        /// </summary>
        public int? Seconds { get; set; }
    }

    /// <summary>
    /// <c>named</c>: who voted what (a team vote); <c>count</c>: only how many chose each option, in no order (quest cards);
    /// <c>result</c>: only the outcome.
    /// </summary>
    public enum VoteReveal { Named, Count, Result }

    /// <summary>
    /// <c>most</c>: the option with the most votes; <c>majority</c>: more than half the votes cast; <c>unanimous</c>: every
    /// vote the same. Otherwise the vote's <see cref="VoteDef.Otherwise"/>.
    /// </summary>
    /// <summary>
    /// How a vote's result is decided: the most chosen option, more than half, everyone; or <c>none</c>, when the rules
    /// decide from the counts themselves (a quest fails on one fail card): no result is announced and <c>$vote.result</c> is empty.
    /// </summary>
    public enum VoteDecision { Most, Majority, Unanimous, None }

    /// <summary>A counted vote, as rules read it through <c>$vote</c>: the last one counted, and each action's last.</summary>
    public sealed class VoteTally
    {
        public string Action { get; set; } = "";
        public string Result { get; set; } = "";
        public Dictionary<string, int> Counts { get; set; } = new Dictionary<string, int>();
        /// <summary>Seat → its vote; empty unless the vote is revealed by name.</summary>
        public Dictionary<string, string> Votes { get; set; } = new Dictionary<string, string>();
        public int Total { get; set; }
    }
}
