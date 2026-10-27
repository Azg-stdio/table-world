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
    /// T11.1 ⚗ and T11.2 (ADR 0012): what a client receives that its seat may not know. A seatless host and one client per
    /// seat play UNO-lite and War through the table protocol. Every message is searched for the host's random generator
    /// state (gone since T11.2: host-run orders carry results instead), and every client must end on the host's public
    /// hash without a resync. Other seats' hand cards and deck order still travel (T11.16); they are counted.
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
            public int HostRun;
            public int WholeTables;
            public int Snapshots;
            public int Resyncs;
            public int GeneratorLeaks;
            public readonly List<int> OrderBytes = new List<int>();
            public readonly List<int> HostRunBytes = new List<int>();
            public readonly List<int> EventBytes = new List<int>();
            public readonly List<int> PatchBytes = new List<int>();
            public readonly List<int> SnapshotBytes = new List<int>();
            /// <summary>Per order and client: other seats' hand cards the client could name (until T11.16).</summary>
            public readonly List<int> OtherHandCards = new List<int>();
            /// <summary>Per order and client: cards inside decks whose identity and order the client holds (until T11.16).</summary>
            public readonly List<int> DeckCardsKnown = new List<int>();
        }

        [Theory]
        [InlineData("uno-lite", 20)]
        [InlineData("war", 10)]
        public void Clients_NeverReceiveTheGenerator_AndStayInSync(string name, int games)
        {
            // TW_GAMES=1000 for the recorded T11.3 run.
            if (int.TryParse(System.Environment.GetEnvironmentVariable("TW_GAMES"), out var n))
                games = n;
            var report = new Report();
            for (var g = 0; g < games; g++)
                PlayOne(name, (ulong)(g + 1), report);

            static string Spread(List<int> values)
            {
                if (values.Count == 0)
                    return "none";
                var sorted = values.OrderBy(v => v).ToList();
                return $"median {sorted[sorted.Count / 2]}, max {sorted[^1]}";
            }
            _output.WriteLine($"{name}: {games} games, {report.Orders} orders ({report.HostRun} host-run, {report.WholeTables} as whole tables), {report.Snapshots} snapshots, {report.Resyncs} resyncs");
            _output.WriteLine($"  bytes: replayed orders {Spread(report.OrderBytes)}; host-run orders per client {Spread(report.HostRunBytes)} (events {Spread(report.EventBytes)}, patch {Spread(report.PatchBytes)}); snapshots {Spread(report.SnapshotBytes)}");
            _output.WriteLine($"  messages carrying the host's generator: {report.GeneratorLeaks}");
            _output.WriteLine($"  other hands' cards a client could name per order {Spread(report.OtherHandCards)}; face-down deck cards it could name {Spread(report.DeckCardsKnown)}");

            Assert.Equal(0, report.GeneratorLeaks);
            Assert.Equal(0, report.Resyncs);
            // T11.16: a rules game's cards are secret on the wire too.
            Assert.Equal(0, report.OtherHandCards.Max());
            Assert.Equal(0, report.DeckCardsKnown.Max());
            Assert.True(report.HostRun > 0, "shuffles and deals ran on the host");
        }

        private static void PlayOne(string name, ulong seed, Report report)
        {
            var host = new SessionHost(GameFixtures.Load(name, seed));
            var seats = host.Game.State.Players.Seats.Select(s => s.Id).ToList();
            var clients = new Dictionary<string, SessionClient>();
            for (var i = 0; i < seats.Count; i++)
            {
                var id = "client_" + seats[i];
                host.Seats[id] = seats[i];
                clients[id] = new SessionClient(id);
                var snapshot = host.Snapshot(id).Message;
                report.Snapshots++;
                report.SnapshotBytes.Add(Text(snapshot).Length);
                clients[id].Receive(snapshot);
            }

            void Deliver(Outgoing outgoing)
            {
                var text = Text(outgoing.Message);
                // The generator's state, as the snapshot used to carry it and as a saved session holds it.
                if (text.Contains(host.Game.Table.Random.State.ToString()))
                    report.GeneratorLeaks++;
                if (outgoing.Message["patch"] != null || outgoing.Message["state"] != null)
                {
                    report.HostRunBytes.Add(text.Length);
                    report.EventBytes.Add(outgoing.Message["events"]!.ToString(Newtonsoft.Json.Formatting.None).Length);
                    report.PatchBytes.Add((outgoing.Message["patch"] ?? outgoing.Message["state"])!.ToString(Newtonsoft.Json.Formatting.None).Length);
                    if (outgoing.Message["state"] != null)
                        report.WholeTables++;
                }
                else if ((string?)outgoing.Message["kind"] == "order")
                    report.OrderBytes.Add(text.Length);
                foreach (var id in outgoing.To == null ? clients.Keys.ToList() : new List<string> { outgoing.To })
                    if (clients[id].Receive((JObject)outgoing.Message.DeepClone()) is { } reply)
                        foreach (var answer in host.Receive(id, reply))
                        {
                            report.Resyncs++;
                            Deliver(answer);
                        }
            }

            void Run(Command command, string label)
            {
                var (result, messages) = host.ExecuteLocal(command);
                Assert.True(result.Ok, $"{name} seed {seed}: {label} failed: {result.Error}");
                report.Orders++;
                if (messages.Any(m => m.To != null))
                    report.HostRun++;
                messages.ForEach(Deliver);
                foreach (var (id, client) in clients)
                {
                    var view = client.Confirmed!;
                    // What actually arrived: hidden cards whose face this machine holds, face-down decks whose order it holds.
                    report.OtherHandCards.Add(view.State.Objects.Count(o => o is Card { Unknown: false } && view.Table.IsHiddenFrom(o.Id, host.Seats[id])));
                    report.DeckCardsKnown.Add(view.State.Objects.OfType<Deck>().Where(d => !d.IsFaceUp).Sum(d => d.Cards.Count(c => !c.Unknown)));
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
            var hash = SeatView.Hash(host.Game.State);
            foreach (var (id, client) in clients)
            {
                Assert.Equal(hash, SeatView.Hash(client.Confirmed!.State));
                Assert.Equal(hash, SeatView.Hash(client.Predicted!.State));
            }
        }

        private static string Text(JObject message) => message.ToString(Newtonsoft.Json.Formatting.None);
    }
}
