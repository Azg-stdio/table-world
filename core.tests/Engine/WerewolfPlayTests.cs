using System.Collections.Generic;
using System.Linq;
using Core.Engine;
using Core.Net;
using Core.Rules;
using Newtonsoft.Json.Linq;
using Xunit.Abstractions;

namespace Core.Tests.Engine
{
    /// <summary>
    /// M10 T11.14 ⚗: games/werewolf.json, built through the MCP tools by tools/mcp/build_werewolf.py, played by random
    /// players from 6 to 12 with and without the Doctor. Every game ends with a side winning; over the protocol, the
    /// wolves' night vote leaves no trace on the other machines (who voted, whom they chose), and nobody resyncs.
    /// </summary>
    public class WerewolfPlayTests
    {
        private readonly ITestOutputHelper _output;

        public WerewolfPlayTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private static GameEngine Load(int players, ulong seed, params string[] optional)
        {
            var state = TableJson.Parse(GameFixtures.GameJson("werewolf")).State!;
            Assert.Empty(TableValidator.Validate(state));
            var game = new GameEngine(state, seed);
            var seats = SeatLayout.Spread(state.Players.Seats.Select(s => s.Id).ToList(), players);
            var start = game.Execute(new StartGameCommand { Seats = seats, Roles = optional.ToList() });
            Assert.True(start.Ok, start.Error?.ToString());
            return game;
        }

        private static List<(string Seat, string ActionId, Dictionary<string, string> Params)> Options(GameEngine game) =>
            game.Playing.SelectMany(s => game.LegalActions(s.Id).SelectMany(a => a.Choices.Select(c => (s.Id, a.ActionId, c)))).ToList();

        [Fact]
        public void RandomPlayers_FinishEveryCount()
        {
            var wins = new Dictionary<string, int>();
            var nights = new List<int>();
            for (var players = 6; players <= 12; players++)
                for (ulong seed = 1; seed <= 12; seed++)
                {
                    var game = Load(players, seed, seed % 2 == 0 ? new[] { "doctor" } : new string[0]);
                    var chooser = new DeterministicRandom(seed * 131);
                    var steps = 0;
                    while (!game.Session!.Over)
                    {
                        Assert.True(steps++ < 2000, $"{players} players seed {seed}: no end (phase {game.Session.Phase}, night {game.Session.Variables["night"]})");
                        var options = Options(game);
                        Assert.True(options.Count > 0, $"{players} players seed {seed}: nobody can act in {game.Session.Phase}");
                        var pick = options[chooser.Next(options.Count)];
                        var result = game.Execute(new PerformActionCommand { Actor = pick.Seat, ActionId = pick.ActionId, Params = pick.Params });
                        Assert.True(result.Ok, $"{players} seed {seed}: legal {pick.ActionId} by {pick.Seat} failed: {result.Error}");
                    }
                    var winner = game.Session.WinnerTeam!;
                    wins[winner] = wins.TryGetValue(winner, out var w) ? w + 1 : 1;
                    nights.Add((int)game.Session.Variables["night"]!);
                    // The winners' condition holds at the end.
                    var wolves = game.Playing.Count(s => !game.IsOut(s.Id) && game.TeamOf(s.Id) == "wolves");
                    var village = game.Playing.Count(s => !game.IsOut(s.Id) && game.TeamOf(s.Id) == "village");
                    Assert.True(winner == "village" ? wolves == 0 : wolves >= village, $"{players} seed {seed}: {winner} won with {wolves} wolves and {village} villagers");
                }
            nights.Sort();
            _output.WriteLine($"84 games of Werewolf (6-12 players): wins {string.Join(", ", wins.Select(p => $"{p.Key} {p.Value}"))}; nights median {nights[nights.Count / 2]}, max {nights[^1]}");
        }

        [Fact]
        public void TheDeadAreOut_AndTheDoctorSaves()
        {
            var game = Load(6, 3, "doctor");
            var s = game.Session!;
            var wolves = s.Roles.Where(p => p.Value == "wolf").Select(p => p.Key).ToList();
            var doctor = s.Roles.Single(p => p.Value == "doctor").Key;
            var seer = s.Roles.Single(p => p.Value == "seer").Key;
            var victim = s.Roles.First(p => p.Value == "villager").Key;
            CommandResult Act(string seat, string action, string key, string value) =>
                game.Execute(new PerformActionCommand { Actor = seat, ActionId = action, Params = { [key] = value } });

            Assert.Equal("wolves", s.Phase);
            Assert.False(game.IsAwake(victim));
            foreach (var wolf in wolves)
                Assert.True(Act(wolf, "hunt", GameEngine.VoteParam, victim).Ok);
            Assert.DoesNotContain(s.Log, l => l.Contains("voted") || l.Contains("Choose the victim"));
            Assert.Equal("seer", s.Phase);
            Assert.True(Act(seer, "peek", "target", wolves[0]).Ok);
            Assert.Contains(s.Knows[seer], k => k.Seat == wolves[0] && k.Team == "wolves");
            Assert.Equal("doctor", s.Phase);
            Assert.True(Act(doctor, "protect", "target", victim).Ok);
            Assert.Equal("day", s.Phase);
            Assert.Contains("Dawn: nobody died tonight.", s.Log);
            Assert.Empty(s.Eliminated);

            // Everyone lynches one wolf: out, role shown; the out can no longer act or be chosen.
            foreach (var seat in game.Playing.Select(p => p.Id).ToList())
                Assert.True(Act(seat, "lynch", GameEngine.VoteParam, wolves[0]).Ok);
            Assert.Contains(wolves[0], s.Eliminated);
            Assert.Contains(s.Log, l => l.EndsWith("is Werewolf."));
            Assert.Equal(ErrorCodes.NotPlaying, Act(wolves[0], "hunt", GameEngine.VoteParam, victim).Error!.Code);
            Assert.DoesNotContain(game.LegalActions(wolves[1]).Single(a => a.ActionId == "hunt").Choices, c => c[GameEngine.VoteParam] == wolves[0]);
            Assert.Equal(2, (int)s.Variables["night"]!);
        }

        [Fact]
        public void OverTheProtocol_TheWolvesNightLeavesNoTrace()
        {
            var game = Load(7, 5);
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
            var wolves = game.Session!.Roles.Where(p => p.Value == "wolf").Select(p => p.Key).ToList();
            var victim = game.Session.Roles.First(p => p.Value == "villager").Key;
            var village = seats.Where(s => !wolves.Contains(s)).ToList();
            foreach (var cleared in received.Values)
                cleared.Clear();

            foreach (var wolf in wolves)
            {
                var (result, intent) = clients[wolf].Submit(new PerformActionCommand { Actor = wolf, ActionId = "hunt", Params = { [GameEngine.VoteParam] = victim } });
                Assert.True(result.Ok, result.Error?.ToString());
                foreach (var outgoing in host.Receive(wolf, intent!))
                    Deliver(outgoing);
            }
            Assert.Equal("seer", game.Session.Phase);
            foreach (var seat in village)
            {
                Assert.DoesNotContain(received[seat], m => m.Contains($"\"choice\":\"{victim}\""));
                foreach (var wolf in wolves)
                    Assert.DoesNotContain(received[seat], m => m.Contains($"\"Seat\":\"{wolf}\",\"Action\":\"hunt\"") || m.Contains($"\"seat\":\"{wolf}\",\"action\":\"hunt\""));
                Assert.DoesNotContain(received[seat], m => m.Contains("voted."));
                Assert.Null(clients[seat].Confirmed!.Session!.LastVote);
                Assert.False(clients[seat].Confirmed!.Session!.Variables.ContainsKey("victim"), "the victim stays on the host until dawn");
            }
            foreach (var client in clients.Values)
            {
                Assert.Equal(0, client.Resyncs);
                Assert.Equal(SeatView.Hash(game.State), SeatView.Hash(client.Confirmed!.State));
            }
        }
    }
}
