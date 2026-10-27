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
    /// M10 T11.15 ⚗: games/wink-murder.json, built through the MCP tools by tools/mcp/build_wink_murder.py. Random players
    /// wink at random faces and take random legal actions; every game ends. A wink from the Murderer reaches only its
    /// target, and nobody else's machine learns who was winked at.
    /// </summary>
    public class WinkMurderPlayTests
    {
        private readonly ITestOutputHelper _output;

        public WinkMurderPlayTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private static GameEngine Load(int players, ulong seed, params string[] optional)
        {
            var state = TableJson.Parse(GameFixtures.GameJson("wink-murder")).State!;
            Assert.Empty(TableValidator.Validate(state));
            var game = new GameEngine(state, seed);
            var seats = SeatLayout.Spread(state.Players.Seats.Select(s => s.Id).ToList(), players);
            Assert.True(game.Execute(new StartGameCommand { Seats = seats, Roles = optional.ToList() }).Ok);
            return game;
        }

        [Fact]
        public void RandomWinksAndAccusations_EndEveryGame()
        {
            var wins = new Dictionary<string, int>();
            for (var players = 5; players <= 12; players++)
                for (ulong seed = 1; seed <= 10; seed++)
                {
                    var game = Load(players, seed, seed % 2 == 0 ? new[] { "detective" } : new string[0]);
                    var chooser = new DeterministicRandom(seed * 977);
                    var steps = 0;
                    while (!game.Session!.Over)
                    {
                        Assert.True(steps++ < 5000, $"{players} players seed {seed}: no end");
                        var alive = game.Playing.Where(s => !game.IsOut(s.Id)).Select(s => s.Id).ToList();
                        // Mostly winks (anyone may wink; only the Murderer's count), sometimes a legal action.
                        if (chooser.Next(4) > 0)
                        {
                            var from = alive[chooser.Next(alive.Count)];
                            var at = alive[chooser.Next(alive.Count)];
                            Assert.True(game.Execute(new GestureCommand { Actor = from, Gesture = "wink", Target = at == from ? null : at }).Ok);
                            continue;
                        }
                        var options = alive.SelectMany(s => game.LegalActions(s).SelectMany(a => a.Choices.Select(c => (Seat: s, a.ActionId, Params: c))))
                            // Accusing is the end of someone: rarer than falling.
                            .Where(o => o.ActionId == "fall" || chooser.Next(6) == 0).ToList();
                        if (options.Count == 0)
                            continue;
                        var pick = options[chooser.Next(options.Count)];
                        Assert.True(game.Execute(new PerformActionCommand { Actor = pick.Seat, ActionId = pick.ActionId, Params = pick.Params }).Ok);
                    }
                    wins[game.Session.WinnerTeam!] = wins.TryGetValue(game.Session.WinnerTeam!, out var w) ? w + 1 : 1;
                }
            _output.WriteLine($"80 games of Wink Murder (5-12 players): wins {string.Join(", ", wins.Select(p => $"{p.Key} {p.Value}"))}");
        }

        [Fact]
        public void TheMurderersWink_KillsOnlyWhenTheTargetFalls()
        {
            var game = Load(6, 2);
            var s = game.Session!;
            var murderer = s.Roles.Single(p => p.Value == "murderer").Key;
            var guest = s.Roles.First(p => p.Value == "guest").Key;
            var other = s.Roles.Last(p => p.Value == "guest").Key;
            var wink = game.Execute(new GestureCommand { Actor = murderer, Gesture = "wink", Target = guest });
            Assert.Equal(new[] { guest }, wink.Events.OfType<PrivateMessage>().Select(m => m.Seat));
            Assert.Contains(game.LegalActions(guest), a => a.ActionId == "fall");
            Assert.DoesNotContain(game.LegalActions(other), a => a.ActionId == "fall");
            Assert.True(game.Execute(new PerformActionCommand { Actor = guest, ActionId = "fall" }).Ok);
            Assert.True(game.IsOut(guest));
            Assert.Contains(s.Log, l => l.EndsWith("is Guest."));

            // A wrong accusation puts the accuser out; the right one ends it.
            var innocent = s.Roles.First(p => p.Value == "guest" && !game.IsOut(p.Key) && p.Key != other).Key;
            Assert.True(game.Execute(new PerformActionCommand { Actor = other, ActionId = "accuse", Params = { ["suspect"] = innocent } }).Ok);
            Assert.True(game.IsOut(other));
            Assert.True(game.Execute(new PerformActionCommand { Actor = innocent, ActionId = "accuse", Params = { ["suspect"] = murderer } }).Ok);
            Assert.Equal("town", s.WinnerTeam);
        }

        [Fact]
        public void OverTheProtocol_OnlyTheVictimKnows()
        {
            var game = Load(6, 7);
            var host = new SessionHost(game);
            var seats = game.Playing.Select(p => p.Id).ToList();
            var clients = seats.ToDictionary(s => s, s => new SessionClient(s));
            var received = seats.ToDictionary(s => s, s => new List<string>());
            void Deliver(Outgoing outgoing)
            {
                foreach (var id in outgoing.To == null ? seats : new List<string> { outgoing.To })
                {
                    received[id].Add(outgoing.Message.ToString(Newtonsoft.Json.Formatting.None));
                    Assert.Null(clients[id].Receive((JObject)outgoing.Message.DeepClone()));
                }
            }
            foreach (var seat in seats)
            {
                host.Seats[seat] = seat;
                Deliver(host.Snapshot(seat));
            }
            foreach (var list in received.Values)
                list.Clear();
            var murderer = game.Session!.Roles.Single(p => p.Value == "murderer").Key;
            var victim = seats.First(x => x != murderer);
            var (_, intent) = clients[murderer].Submit(new GestureCommand { Actor = murderer, Gesture = "wink", Target = victim });
            foreach (var outgoing in host.Receive(murderer, intent!))
                Deliver(outgoing);
            foreach (var seat in seats.Where(x => x != murderer && x != victim))
            {
                Assert.DoesNotContain(received[seat], m => m.Contains($"\"Target\":\"{victim}\"") || m.Contains("\"winked\""));
                Assert.False(clients[seat].Confirmed!.Session!.Traits.ContainsKey(victim), "nobody else holds the victim's fate");
            }
            Assert.Contains(received[victim], m => m.Contains("You have been winked at."));
            Assert.Contains(clients[victim].Confirmed!.LegalActions(victim), a => a.ActionId == "fall");
        }
    }
}
