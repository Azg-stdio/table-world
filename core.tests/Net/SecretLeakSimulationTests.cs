using System.Collections.Generic;
using System.Linq;
using Core.Engine;
using Core.Net;
using Core.Rules;
using Core.Tests.Engine;
using Newtonsoft.Json.Linq;
using Xunit.Abstractions;

namespace Core.Tests.Net
{
    /// <summary>
    /// T11.1 ⚗ (ADR 0012): what a client receives today that its seat may not know. A seatless host and one client per
    /// seat play UNO-lite and War through the table protocol; after every order each client's confirmed table is searched
    /// for other seats' hand cards, the order of face-down decks, and the host's random generator. The numbers go to
    /// docs/milestones/M10-social-games.md; the assertions pin today's behaviour, so T11.2 must flip them on purpose.
    /// </summary>
    public class SecretLeakSimulationTests
    {
        private readonly ITestOutputHelper _output;

        public SecretLeakSimulationTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private sealed class Report
        {
            public int Orders;
            public int Snapshots;
            public readonly List<int> OrderBytes = new List<int>();
            public readonly List<int> SnapshotBytes = new List<int>();
            /// <summary>Per order and client: other seats' hand cards the client could name.</summary>
            public readonly List<int> OtherHandCards = new List<int>();
            /// <summary>Per order and client: cards inside decks whose identity and order the client holds.</summary>
            public readonly List<int> DeckCardsKnown = new List<int>();
            public int GeneratorMatches;
            public int GeneratorChecks;
            /// <summary>Commands (by action id) that advanced the host's generator, with how often.</summary>
            public readonly Dictionary<string, int> RandomCommands = new Dictionary<string, int>();
        }

        [Theory]
        [InlineData("uno-lite", 20)]
        [InlineData("war", 10)]
        public void Clients_ReceiveEverySecret_Today(string name, int games)
        {
            var report = new Report();
            for (var g = 0; g < games; g++)
                PlayOne(name, (ulong)(g + 1), report);

            static string Spread(List<int> values)
            {
                var sorted = values.OrderBy(v => v).ToList();
                return $"median {sorted[sorted.Count / 2]}, max {sorted[^1]}";
            }
            _output.WriteLine($"{name}: {games} games, {report.Orders} orders, {report.Snapshots} snapshots");
            _output.WriteLine($"  order bytes {Spread(report.OrderBytes)}; snapshot bytes {Spread(report.SnapshotBytes)}");
            _output.WriteLine($"  other seats' hand cards a client could name, per order: {Spread(report.OtherHandCards)}");
            _output.WriteLine($"  deck cards whose identity and order a client holds, per order: {Spread(report.DeckCardsKnown)}");
            _output.WriteLine($"  client generator equal to the host's: {report.GeneratorMatches} of {report.GeneratorChecks} checks");
            _output.WriteLine($"  commands that advanced the generator: {string.Join(", ", report.RandomCommands.OrderByDescending(p => p.Value).Select(p => $"{p.Key} {p.Value}"))}");

            // Today's behaviour, recorded on purpose: T11.2 and T11.3 turn each of these around.
            // UNO hides other hands; War has no hands, only face-down piles whose order decides every battle.
            Assert.True(report.OtherHandCards.Max() + report.DeckCardsKnown.Max() > 0, "clients should still hold secrets before T11.2");
            Assert.Equal(report.GeneratorChecks, report.GeneratorMatches);
        }

        private static void PlayOne(string name, ulong seed, Report report)
        {
            var host = new SessionHost(GameFixtures.Load(name, seed));
            var seats = host.Game.State.Players.Seats.Select(s => s.Id).ToList();
            var clients = seats.ToDictionary(s => "client_" + s, s => new SessionClient("client_" + s));
            foreach (var (id, seat) in clients.Keys.Zip(seats, (id, seat) => (id, seat)))
            {
                host.Seats[id] = seat;
                var snapshot = host.Snapshot(id).Message;
                report.Snapshots++;
                report.SnapshotBytes.Add(snapshot.ToString(Newtonsoft.Json.Formatting.None).Length);
                clients[id].Receive(snapshot);
            }

            void Run(Command command, string label)
            {
                var before = host.Game.Table.Random.State;
                var (result, broadcast) = host.ExecuteLocal(command);
                Assert.True(result.Ok, $"{name} seed {seed}: {label} failed: {result.Error}");
                if (host.Game.Table.Random.State != before)
                    report.RandomCommands[label] = report.RandomCommands.TryGetValue(label, out var n) ? n + 1 : 1;
                report.Orders++;
                report.OrderBytes.Add(broadcast.Message.ToString(Newtonsoft.Json.Formatting.None).Length);
                foreach (var (id, client) in clients)
                {
                    client.Receive((JObject)broadcast.Message.DeepClone());
                    var seat = host.Seats[id];
                    var view = client.Confirmed!;
                    report.OtherHandCards.Add(view.State.Objects.Count(o => o is Card && view.Table.IsHiddenFrom(o.Id, seat)));
                    report.DeckCardsKnown.Add(view.State.Objects.OfType<Deck>().Sum(d => d.Cards.Count));
                    report.GeneratorChecks++;
                    if (view.Table.Random.State == host.Game.Table.Random.State)
                        report.GeneratorMatches++;
                }
            }

            Run(new StartGameCommand(), "start");
            var chooser = new DeterministicRandom(seed * 7919);
            var count = 0;
            while (!host.Game.Session!.Over && count++ < GamePlayTests.MaxActions)
            {
                var game = host.Game;
                var options = game.State.Players.Seats
                    .SelectMany(s => game.LegalActions(s.Id).SelectMany(a => a.Choices.Select(c => (Seat: s.Id, a.ActionId, Params: c))))
                    .ToList();
                var pick = options[chooser.Next(options.Count)];
                Run(new PerformActionCommand { Actor = pick.Seat, ActionId = pick.ActionId, Params = pick.Params }, pick.ActionId);
            }
        }
    }
}
