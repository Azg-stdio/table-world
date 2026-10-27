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
    /// M10 T11.15b ⚗, the sandbox check: games/secret-dictator.json, a hidden-role game the milestone does not name, built
    /// through the MCP tools alone (tools/mcp/build_secret_dictator.py). Random players finish it at every count; the
    /// Dictator's knowledge follows the player count; the policies stay at 17 cards; nobody resyncs over the protocol.
    /// </summary>
    public class SecretDictatorPlayTests
    {
        private readonly ITestOutputHelper _output;

        public SecretDictatorPlayTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private static GameEngine Load(int players, ulong seed)
        {
            var state = TableJson.Parse(GameFixtures.GameJson("secret-dictator")).State!;
            Assert.Empty(TableValidator.Validate(state));
            var game = new GameEngine(state, seed);
            var seats = SeatLayout.Spread(state.Players.Seats.Select(s => s.Id).ToList(), players);
            var start = game.Execute(new StartGameCommand { Seats = seats });
            Assert.True(start.Ok, start.Error?.ToString());
            return game;
        }

        private static int Policies(GameEngine game) =>
            game.State.Objects.Sum(o => o switch { Deck d => d.Cards.Count, Card _ => 1, _ => 0 });

        [Fact]
        public void RandomPlayers_FinishEveryCount_WithSeventeenPolicies()
        {
            var wins = new Dictionary<string, int>();
            var rounds = new List<int>();
            for (var players = 5; players <= 10; players++)
                for (ulong seed = 1; seed <= 12; seed++)
                {
                    var game = Load(players, seed);
                    Assert.Equal(17, Policies(game));
                    var chooser = new DeterministicRandom(seed * 313);
                    var steps = 0;
                    while (!game.Session!.Over)
                    {
                        Assert.True(steps++ < 3000, $"{players} players seed {seed}: no end (phase {game.Session.Phase})");
                        var options = game.Playing.SelectMany(s => game.LegalActions(s.Id).SelectMany(a => a.Choices.Select(c => (Seat: s.Id, a.ActionId, Params: c)))).ToList();
                        Assert.True(options.Count > 0, $"{players} players seed {seed}: nobody can act in {game.Session.Phase}");
                        var pick = options[chooser.Next(options.Count)];
                        var result = game.Execute(new PerformActionCommand { Actor = pick.Seat, ActionId = pick.ActionId, Params = pick.Params });
                        Assert.True(result.Ok, $"{players} seed {seed}: legal {pick.ActionId} by {pick.Seat} failed: {result.Error}");
                        Assert.Equal(17, Policies(game));
                    }
                    wins[game.Session.WinnerTeam!] = wins.TryGetValue(game.Session.WinnerTeam!, out var w) ? w + 1 : 1;
                    rounds.Add(game.Session.Turn);
                }
            rounds.Sort();
            _output.WriteLine($"72 games of Secret Dictator (5-10 players): wins {string.Join(", ", wins.Select(p => $"{p.Key} {p.Value}"))}; rounds median {rounds[rounds.Count / 2]}, max {rounds[^1]}");
        }

        [Theory]
        [InlineData(5, true)]
        [InlineData(6, true)]
        [InlineData(7, false)]
        [InlineData(10, false)]
        public void TheDictator_KnowsTheFascists_OnlyInSmallGames(int players, bool knows)
        {
            var game = Load(players, 2);
            var s = game.Session!;
            var dictator = s.Roles.Single(p => p.Value == "dictator").Key;
            var fascists = s.Roles.Where(p => p.Value == "fascist").Select(p => p.Key).ToList();
            Assert.Equal(knows, s.Knows[dictator].Count > 0);
            foreach (var fascist in fascists)
                Assert.Contains(s.Knows[fascist], k => k.Seat == dictator && k.Role == "dictator");
        }

        [Fact]
        public void OverTheProtocol_PlaysThroughWithoutAResync()
        {
            var game = Load(7, 9);
            var host = new SessionHost(game);
            var seats = game.Playing.Select(p => p.Id).ToList();
            var clients = seats.ToDictionary(s => s, s => new SessionClient(s));
            void Deliver(Outgoing outgoing)
            {
                foreach (var id in outgoing.To == null ? seats : new List<string> { outgoing.To })
                    if (clients[id].Receive((JObject)outgoing.Message.DeepClone()) is { } reply)
                        foreach (var answer in host.Receive(id, reply))
                            Deliver(answer);
            }
            foreach (var seat in seats)
            {
                host.Seats[seat] = seat;
                Deliver(host.Snapshot(seat));
            }
            var chooser = new DeterministicRandom(77);
            var steps = 0;
            while (!game.Session!.Over && steps++ < 3000)
            {
                var options = seats.SelectMany(s => clients[s].Predicted!.LegalActions(s).SelectMany(a => a.Choices.Select(c => (Seat: s, a.ActionId, Params: c)))).ToList();
                if (options.Count == 0)
                    break;
                var pick = options[chooser.Next(options.Count)];
                var (_, intent) = clients[pick.Seat].Submit(new PerformActionCommand { Actor = pick.Seat, ActionId = pick.ActionId, Params = pick.Params });
                if (intent != null)
                    foreach (var outgoing in host.Receive(pick.Seat, intent))
                        Deliver(outgoing);
            }
            Assert.True(game.Session.Over, "the game ends through the protocol");
            foreach (var client in clients.Values)
            {
                Assert.Equal(0, client.Resyncs);
                Assert.Equal(SeatView.Hash(game.State), SeatView.Hash(client.Confirmed!.State));
            }
        }
    }
}
