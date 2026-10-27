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
    /// docs/rulebooks/avalon.txt, played by random players at every count from 5 to 10, with and without optional roles.
    /// Every game must end with a team winning; over the protocol, no client may hold another seat's role it was not
    /// shown, and nobody resyncs.
    /// </summary>
    public class AvalonPlayTests
    {
        private readonly ITestOutputHelper _output;

        public AvalonPlayTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private static GameEngine Load(int players, ulong seed, params string[] optional)
        {
            var state = TableJson.Parse(GameFixtures.GameJson("avalon")).State!;
            Assert.Empty(TableValidator.Validate(state));
            var game = new GameEngine(state, seed);
            var host = new ScriptHost(() => game.Table, c => game.Execute(c)) { RulesEngine = () => game };
            game.Scripts = host;
            host.Load(0);
            var seats = SeatLayout.Spread(state.Players.Seats.Select(s => s.Id).ToList(), players);
            var start = game.Execute(new StartGameCommand { Seats = seats, Roles = optional.ToList() });
            Assert.True(start.Ok, start.Error?.ToString());
            return game;
        }

        /// <summary>Random legal moves to the end; returns the winning team and how many actions it took.</summary>
        private static (string Winner, int Actions) Play(GameEngine game, ulong seed)
        {
            var chooser = new DeterministicRandom(seed * 7919);
            var count = 0;
            while (!game.Session!.Over)
            {
                Assert.True(count++ < 3000, $"seed {seed}: no winner after 3000 actions (phase {game.Session.Phase}, quest {game.Session.Variables["quest"]}).");
                var options = game.Playing
                    .SelectMany(s => game.LegalActions(s.Id).SelectMany(a => a.Choices.Select(c => (Seat: s.Id, a.ActionId, Params: c))))
                    .ToList();
                Assert.True(options.Count > 0, $"seed {seed}: nobody can act in phase {game.Session.Phase}.");
                var pick = options[chooser.Next(options.Count)];
                var result = game.Execute(new PerformActionCommand { Actor = pick.Seat, ActionId = pick.ActionId, Params = pick.Params });
                Assert.True(result.Ok, $"seed {seed}: legal {pick.ActionId} by {pick.Seat} failed: {result.Error}");
            }
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
                    var game = Load(players, seed, optional);
                    var (winner, count) = Play(game, seed);
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

        [Fact]
        public void QuestSizes_FollowTheTable_AndGoodCannotFail()
        {
            var game = Load(7, 4);
            var s = game.Session!;
            Assert.Equal(2, (int)s.Variables["size"]!);
            Assert.Equal("propose", s.Phase);
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
                var game = Load(6, seed, "percival", "morgana");
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
                while (!game.Session!.Over && steps++ < 3000)
                {
                    // Each client acts from its own view, as a player would.
                    var options = seats.SelectMany(s => clients[s].Predicted!.LegalActions(s).SelectMany(a => a.Choices.Select(c => (Seat: s, a.ActionId, Params: c)))).ToList();
                    var pick = options[chooser.Next(options.Count)];
                    var (result, intent) = clients[pick.Seat].Submit(new PerformActionCommand { Actor = pick.Seat, ActionId = pick.ActionId, Params = pick.Params });
                    if (intent == null)
                        continue;
                    foreach (var outgoing in host.Receive(pick.Seat, intent))
                        Deliver(outgoing);
                }
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
                }
            }
        }
    }
}
