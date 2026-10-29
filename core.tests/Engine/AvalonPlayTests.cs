using System.Collections.Generic;
using System.Linq;
using Core.Engine;
using Core.Net;
using Core.Rules;
using Core.Scripting;
using Newtonsoft.Json.Linq;
using Xunit.Abstractions;

namespace Core.Tests.Engine
{
    /// <summary>
    /// M10 T11.6 ⚗: games/avalon.json, built through the MCP tools by tools/mcp/build_avalon.py from
    /// docs/rulebooks/avalon.txt, played by random players at every count from 5 to 10, with and without optional roles,
    /// with their hands as at the table: the ritual's timed steps run out, the leader drags shields in front of players,
    /// everyone lays a vote card face down on their spot, the team lays quest cards on the pile. Every game must end with a
    /// team winning, without a Lua error; over the protocol, no client may hold another seat's role it was not shown, and
    /// nobody resyncs.
    /// </summary>
    public class AvalonPlayTests
    {
        private readonly ITestOutputHelper _output;

        public AvalonPlayTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private static GameEngine Load(int players, ulong seed, params string[] optional) => Load(players, seed, out _, optional);

        /// <summary>Over the protocol, the host's route for script commands (executed and sent to every client).</summary>
        [System.ThreadStatic] private static System.Func<Command, CommandResult>? Route;

        private static GameEngine Load(int players, ulong seed, out ScriptHost scripts, params string[] optional)
        {
            var state = TableJson.Parse(GameFixtures.GameJson("avalon")).State!;
            Assert.Empty(TableValidator.Validate(state));
            var game = new GameEngine(state, seed);
            // What scripts do on their own (a Wait timer turning cards over) goes the host's way when there is one.
            var host = new ScriptHost(() => game.Table, c => Route != null ? Route(c) : game.Execute(c)) { RulesEngine = () => game };
            game.Scripts = host;
            scripts = host;
            host.Load(0);
            var seats = SeatLayout.Spread(state.Players.Seats.Select(s => s.Id).ToList(), players);
            var start = game.Execute(new StartGameCommand { Seats = seats, Roles = optional.ToList() });
            Assert.True(start.Ok, start.Error?.ToString());
            return game;
        }

        private static string Number(string seat) => seat.Substring(seat.LastIndexOf('_') + 1);

        private static Vec3 At(GameEngine game, string zone) => game.State.Zones.Single(z => z.Id == zone).Transform.Position;

        /// <summary>A card in <paramref name="seat"/>'s hand whose own id starts with <paramref name="prefix"/>, or null.</summary>
        private static TableObject? InHand(GameEngine game, string seat, string prefix) =>
            game.Table.ObjectsIn("hand_" + Number(seat)).FirstOrDefault(o => TableEngine.OwnId(o).StartsWith(prefix));

        /// <summary>
        /// What a player at the table does next, as a command from that seat (null: nobody has anything to do): a timed
        /// step's time runs out, the leader drags a shield or proposes, a voter lays a vote card, a team member a quest
        /// card, the Assassin names someone.
        /// </summary>
        private static (string? Seat, Command Command)? Next(GameEngine game, DeterministicRandom chooser)
        {
            var s = game.Session!;
            var phase = game.Phase!;
            if (phase.Seconds != null || phase.HostEnds)
                return (null, new EndTimedPhaseCommand { Phase = s.Phase!, Visit = s.PhaseVisit });
            var playing = game.Playing.Select(p => p.Id).ToList();
            switch (s.Phase)
            {
                case "propose":
                    var team = ((JArray)s.Variables["team"]!).Select(t => (string)t!).ToList();
                    if (team.Count >= (int)s.Variables["size"]!)
                        return (s.CurrentSeat, new PerformActionCommand { Actor = s.CurrentSeat, ActionId = "propose" });
                    // The leader clicks players; now and then one already chosen, which takes them off again.
                    var member = team.Count > 0 && chooser.Next(4) == 0 ? team[chooser.Next(team.Count)] : playing.Where(p => !team.Contains(p)).ToList()[chooser.Next(playing.Count - team.Count)];
                    return (s.CurrentSeat, new PerformActionCommand { Actor = s.CurrentSeat, ActionId = "choose", Params = { ["member"] = member } });
                case "team_vote":
                    var voter = playing.First(p => s.Secrets.All(c => c.Seat != p || c.Action != "team_vote"));
                    var vote = InHand(game, voter, chooser.Next(3) == 0 ? "nay" : "aye")!;
                    return (voter, new MoveCommand { Actor = voter, ObjectId = vote.Id, Position = At(game, "vote_" + Number(voter)) });
                case "quest":
                    var played = ((JArray)s.Variables["played"]!).Select(t => (string)t!).ToList();
                    var player = ((JArray)s.Variables["team"]!).Select(t => (string)t!).First(p => !played.Contains(p));
                    var fail = game.TeamOf(player) == "evil" && chooser.Next(2) == 0;
                    var card = InHand(game, player, fail ? "fail" : "success")!;
                    return (player, new MoveCommand { Actor = player, ObjectId = card.Id, Position = At(game, "quest_drop") });
                default:
                    var options = playing.SelectMany(p => game.LegalActions(p).SelectMany(a => a.Choices.Select(c => (Seat: p, a.ActionId, Params: c)))).ToList();
                    if (options.Count == 0)
                        return null;
                    var pick = options[chooser.Next(options.Count)];
                    return (pick.Seat, new PerformActionCommand { Actor = pick.Seat, ActionId = pick.ActionId, Params = pick.Params });
            }
        }

        /// <summary>Played with the hands to the end, two seconds between moves; returns the winning team and how many moves it took.</summary>
        private static (string Winner, int Actions) Play(GameEngine game, ScriptHost scripts, ulong seed)
        {
            var chooser = new DeterministicRandom(seed * 7919);
            var errors = new List<string>();
            scripts.Log += line => { if (line.Contains("rror")) errors.Add(line); };
            var count = 0;
            var now = 0.0;
            while (!game.Session!.Over)
            {
                Assert.True(count++ < 3000, $"seed {seed}: no winner after 3000 moves (phase {game.Session.Phase}, quest {game.Session.Variables["quest"]}).");
                now += 2;
                scripts.Tick(now);
                var next = Next(game, chooser);
                Assert.True(next != null, $"seed {seed}: nobody can act in phase {game.Session.Phase}.");
                var result = game.Execute(next!.Value.Command);
                Assert.True(result.Ok, $"seed {seed}: {next.Value.Command.GetType().Name} by {next.Value.Seat} in {game.Session.Phase} failed: {result.Error}");
            }
            Assert.Empty(errors);
            return (game.Session.WinnerTeam!, count);
        }

        [Fact]
        public void RandomPlayers_FinishEveryCount()
        {
            var wins = new Dictionary<string, int>();
            var actions = new List<int>();
            for (var players = 5; players <= 10; players++)
                for (ulong seed = 1; seed <= 15; seed++)
                {
                    var optional = seed % 3 == 0 ? new[] { "percival", "morgana" } : seed % 3 == 1 && players >= 7 ? new[] { "mordred", "oberon" } : new string[0];
                    var game = Load(players, seed, out var scripts, optional);
                    var (winner, count) = Play(game, scripts, seed);
                    Assert.Contains(winner, new[] { "good", "evil" });
                    wins[winner] = wins.TryGetValue(winner, out var w) ? w + 1 : 1;
                    actions.Add(count);
                    var s = game.Session!;
                    var byThree = (int)s.Variables["successes"]! >= 3 || (int)s.Variables["fails"]! >= 3 || (int)s.Variables["rejections"]! >= 5;
                    Assert.True(byThree, $"{players} players seed {seed}: the game ended without three quests or five rejections");
                }
            actions.Sort();
            _output.WriteLine($"90 games of Avalon (5-10 players): wins {string.Join(", ", wins.Select(p => $"{p.Key} {p.Value}"))}; " +
                              $"actions per game median {actions[actions.Count / 2]}, max {actions[^1]}");
        }

        /// <summary>
        /// When a quest starts, every member of the team holds two quest cards: the loyal two Success (Merlin and Percival
        /// too), a minion a Success and a Fail. Seen at every count, with and without the optional roles.
        /// </summary>
        [Fact]
        public void TheLoyal_HoldOnlySuccessCards_AndMinionsASuccessAndAFail()
        {
            var quests = 0;
            for (var players = 5; players <= 10; players++)
                for (ulong seed = 1; seed <= 6; seed++)
                {
                    var optional = seed % 2 == 0 ? new[] { "percival", "morgana" } : new string[0];
                    var game = Load(players, seed, out var scripts, optional);
                    var chooser = new DeterministicRandom(seed * 7);
                    var now = 0.0;
                    var lastVisit = -1;
                    for (var steps = 0; !game.Session!.Over && steps < 3000; steps++)
                    {
                        now += 2;
                        scripts.Tick(now);
                        var s = game.Session;
                        if (s.Phase == "quest" && s.PhaseVisit != lastVisit && ((JArray)s.Variables["played"]!).Count == 0)
                        {
                            lastVisit = s.PhaseVisit;
                            quests++;
                            foreach (var seat in ((JArray)s.Variables["team"]!).Select(t => (string)t!))
                            {
                                var cards = game.Table.ObjectsIn("hand_" + Number(seat))
                                    .Where(o => o.Tags.Contains("quest")).Select(o => TableEngine.OwnId(o).Split('_')[0]).OrderBy(c => c).ToList();
                                var expected = game.TeamOf(seat) == "good" ? new[] { "success", "success" } : new[] { "fail", "success" };
                                Assert.True(expected.SequenceEqual(cards),
                                    $"{players} players seed {seed}: {seat} ({game.RoleOf(seat, null)}) holds [{string.Join(", ", cards)}]");
                            }
                        }
                        var next = Next(game, chooser);
                        if (next == null)
                            break;
                        game.Execute(next.Value.Command);
                    }
                }
            Assert.True(quests > 50, $"only {quests} quests seen");
        }

        [Fact]
        public void QuestSizes_FollowTheTable_AndGoodCannotFail()
        {
            var game = Load(7, 4);
            var s = game.Session!;
            Assert.Equal("ritual_look", s.Phase);
            // Every good player's only quest choice is success.
            foreach (var seat in game.Playing.Select(p => p.Id).Where(id => game.TeamOf(id) == "good"))
            {
                s.Variables["team"] = new JArray(seat);
                var legal = game.LegalActions(seat);
                s.Variables["team"] = new JArray();
                Assert.DoesNotContain(legal, a => a.ActionId == "quest");
            }
        }

        [Fact]
        public void OverTheProtocol_NoRoleLeaks_AndNobodyResyncs()
        {
            for (ulong seed = 1; seed <= 4; seed++)
            {
                var game = Load(6, seed, out var scripts, "percival", "morgana");
                var host = new SessionHost(game);
                var seats = game.Playing.Select(p => p.Id).ToList();
                var clients = seats.ToDictionary(s => s, s => new SessionClient(s));
                var received = seats.ToDictionary(s => s, s => new List<string>());
                void Deliver(Outgoing outgoing)
                {
                    foreach (var id in outgoing.To == null ? seats : new List<string> { outgoing.To })
                    {
                        received[id].Add(outgoing.Message.ToString(Newtonsoft.Json.Formatting.None));
                        if (clients[id].Receive((JObject)outgoing.Message.DeepClone()) is { } reply)
                            foreach (var answer in host.Receive(id, reply))
                                Deliver(answer);
                    }
                }
                foreach (var seat in seats)
                {
                    host.Seats[seat] = seat;
                    Deliver(host.Snapshot(seat));
                }
                var chooser = new DeterministicRandom(seed * 31);
                var steps = 0;
                var now = 0.0;
                // What each client had received before the move that ended the game (which turns every role card over).
                var beforeEnd = seats.ToDictionary(s => s, s => 0);
                Route = c =>
                {
                    var (done, messages) = host.ExecuteLocal(c);
                    foreach (var outgoing in messages)
                        Deliver(outgoing);
                    return done;
                };
                while (!game.Session!.Over && steps++ < 3000)
                {
                    foreach (var seat in seats)
                        beforeEnd[seat] = received[seat].Count;
                    now += 2;
                    scripts.Tick(now);
                    // Each move made by its seat's client, as a player at the table would.
                    var next = Next(game, chooser)!.Value;
                    if (next.Seat == null)
                    {
                        // The host's own window ends the timed steps.
                        foreach (var outgoing in host.ExecuteLocal(next.Command).Messages)
                            Deliver(outgoing);
                        continue;
                    }
                    var by = next.Seat;
                    var (result, intent) = clients[by].Submit(next.Command);
                    if (intent == null)
                    {
                        _output.WriteLine($"step {steps}: {next.Command.GetType().Name} by {by} in {game.Session.Phase}: {result?.Error}");
                        continue;
                    }
                    foreach (var outgoing in host.Receive(by, intent))
                        Deliver(outgoing);
                }
                Route = null;
                Assert.True(game.Session.Over, $"seed {seed}: no end through the protocol");
                foreach (var seat in seats)
                {
                    Assert.Equal(0, clients[seat].Resyncs);
                    Assert.Equal(SeatView.Hash(game.State), SeatView.Hash(clients[seat].Confirmed!.State));
                    foreach (var other in seats.Where(o => o != seat))
                    {
                        // Only the roles shown to this seat ever reached it (the Assassin's target is shown to all at the end).
                        var shown = game.Session.Knows.TryGetValue(seat, out var sights) && sights.Any(k => k.Seat == other && k.Role != null);
                        if (!shown)
                            Assert.DoesNotContain(received[seat], m => m.Contains($"\"{other}\":\"{game.Session.Roles[other]}\""));
                    }
                    // Nor another player's role card: until the end, a seat only ever learns its own card's id.
                    var mine = "role_" + game.Session.Roles[seat];
                    foreach (var message in received[seat].Take(beforeEnd[seat]))
                        foreach (System.Text.RegularExpressions.Match card in System.Text.RegularExpressions.Regex.Matches(message, "entryId[^a-z]+(role_[a-z_0-9]+)"))
                            Assert.StartsWith(mine, card.Groups[1].Value);
                    Assert.Contains(received[seat].Take(beforeEnd[seat]), m => System.Text.RegularExpressions.Regex.IsMatch(m, "entryId[^a-z]+" + mine));
                }
            }
        }
    }
}
