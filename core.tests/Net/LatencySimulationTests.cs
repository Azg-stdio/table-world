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
    /// T3.5 ⚗: the sync protocol over a timed network like a bad Relay connection. Each one-way link has 200 ms of latency
    /// plus up to 40 ms of jitter; 2 % of messages are lost and resent one round trip later, and since the channel is
    /// reliable and ordered (Relay's), later messages on that link wait behind them. Two players send 500 random commands
    /// between them, about four a second each, while the host plays too. Every peer must end with the host's state, and
    /// the numbers (rebuilds, time to confirm) go into the milestone record.
    /// </summary>
    public class LatencySimulationTests
    {
        private readonly ITestOutputHelper _output;

        public LatencySimulationTests(ITestOutputHelper output) => _output = output;

        private sealed class Link
        {
            private double _lastArrival;
            private readonly Queue<(double At, JObject Message)> _inFlight = new Queue<(double, JObject)>();

            public void Send(double now, JObject message, DeterministicRandom r, ref int lost)
            {
                var arrival = now + 0.2 + r.Next(41) / 1000.0;
                if (r.Next(100) < 2)
                {
                    lost++;
                    arrival += 0.4;
                }
                // Ordered: nothing overtakes a message still in flight.
                arrival = System.Math.Max(arrival, _lastArrival);
                _lastArrival = arrival;
                _inFlight.Enqueue((arrival, (JObject)message.DeepClone()));
            }

            public IEnumerable<JObject> Arrived(double now)
            {
                while (_inFlight.Count > 0 && _inFlight.Peek().At <= now)
                    yield return _inFlight.Dequeue().Message;
            }

            public bool Empty => _inFlight.Count == 0;
        }

        [Theory]
        [InlineData(11UL, 0.5, 1.5)]
        [InlineData(12UL, 0.5, 1.5)]
        [InlineData(13UL, 0.1, 0.3)]
        public void TwoPlayersOnABadConnection_FiveHundredCommands_EndIdentical(ulong seed, double minGap, double spread)
        {
            var host = new SessionHost(new GameEngine(TableJson.Parse(EngineFixture.Json).State!, seed));
            var r = new DeterministicRandom(seed);
            var clients = new Dictionary<string, SessionClient> { ["a"] = new SessionClient("a"), ["b"] = new SessionClient("b") };
            var down = clients.Keys.ToDictionary(id => id, _ => new Link());
            var up = clients.Keys.ToDictionary(id => id, _ => new Link());
            var lost = 0;
            var now = 0.0;
            void Deliver(Outgoing outgoing)
            {
                foreach (var id in outgoing.To == null ? clients.Keys.ToList() : new List<string> { outgoing.To })
                    down[id].Send(now, outgoing.Message, r, ref lost);
            }
            foreach (var id in clients.Keys)
                Deliver(host.Snapshot(id));

            // When each client command left, to measure how long the host's confirmation took to come back.
            var sentAt = clients.Keys.ToDictionary(id => id, _ => new Queue<double>());
            var confirmTimes = new List<double>();
            foreach (var (id, client) in clients)
            {
                var me = id;
                client.Ordered += (origin, command, result) =>
                {
                    if (origin == me && sentAt[me].Count > 0)
                        confirmTimes.Add(now - sentAt[me].Dequeue());
                };
                // The host refused it: it never comes back ordered.
                client.Refused += _ =>
                {
                    if (sentAt[me].Count > 0)
                        sentAt[me].Dequeue();
                };
            }

            var sent = 0;
            var refusedLocally = 0;
            var maxPending = 0;
            const double step = 0.01;
            var nextCommand = clients.Keys.ToDictionary(id => id, _ => 0.5);
            var nextHostCommand = 0.5;
            while (sent < 500 || clients.Values.Any(c => c.PendingCount > 0) || down.Values.Concat(up.Values).Any(l => !l.Empty))
            {
                now += step;
                foreach (var id in clients.Keys)
                {
                    foreach (var message in up[id].Arrived(now).ToList())
                        foreach (var outgoing in host.Receive(id, message))
                            Deliver(outgoing);
                    foreach (var message in down[id].Arrived(now).ToList())
                        if (clients[id].Receive(message) is { } reply)
                            up[id].Send(now, reply, r, ref lost);
                }
                foreach (var (id, client) in clients)
                {
                    if (sent >= 500 || now < nextCommand[id] || !client.Synced)
                        continue;
                    // A player acts every minGap..minGap+spread seconds (a real player: one action every 0.5–2 s).
                    nextCommand[id] = now + minGap + r.Next((int)(spread * 1000)) / 1000.0;
                    var (result, intent) = client.Submit(RandomCommand(client.Predicted!, r));
                    if (intent == null)
                    {
                        refusedLocally++;
                        continue;
                    }
                    sent++;
                    sentAt[id].Enqueue(now);
                    up[id].Send(now, intent, r, ref lost);
                    maxPending = System.Math.Max(maxPending, client.PendingCount);
                }
                if (sent < 500 && now >= nextHostCommand)
                {
                    nextHostCommand = now + 0.5 + r.Next(500) / 1000.0;
                    foreach (var message in host.ExecuteLocal(RandomCommand(host.Game, r)).Messages)
                        Deliver(message);
                }
                Assert.True(now < 3600, "the simulation did not settle");
            }

            var hash = SeatView.Hash(host.Game.State);
            foreach (var (id, client) in clients)
            {
                Assert.True(client.Synced, $"{id} is not synced");
                Assert.Equal(hash, SeatView.Hash(client.Confirmed!.State));
                Assert.Equal(hash, SeatView.Hash(client.Predicted!.State));
                Assert.Equal(0, client.Resyncs);
            }
            confirmTimes.Sort();
            var median = confirmTimes[confirmTimes.Count / 2];
            var p95 = confirmTimes[(int)(confirmTimes.Count * 0.95)];
            var rebuilds = clients.Values.Sum(c => c.Rebuilds);
            _output.WriteLine($"seed {seed}, a command every {minGap:0.0}–{minGap + spread:0.0} s per player: {sent} commands sent over {now:0} s, {refusedLocally} refused locally, {lost} messages lost and resent; " +
                              $"host order {host.Order}; confirm median {median * 1000:0} ms, p95 {p95 * 1000:0} ms, max {confirmTimes.Last() * 1000:0} ms; " +
                              $"max pending {maxPending}; prediction rebuilds {rebuilds} ({100.0 * rebuilds / sent:0.0} % of commands)");
            // A round trip is 400–480 ms; a loss adds 400 more to that message and everything behind it.
            Assert.InRange(median, 0.4, 0.6);
            Assert.True(p95 < 1.2, $"p95 confirm {p95:0.00} s");
        }

        private static Command RandomCommand(GameEngine view, DeterministicRandom r)
        {
            var ids = view.State.Objects.Where(o => o.Container == null).Select(o => o.Id).ToList();
            string Pick() => ids[r.Next(ids.Count)];
            return r.Next(10) switch
            {
                0 => new MoveCommand { ObjectId = Pick(), Position = new Vec3(r.Next(100) / 100f - 0.5f, 0, r.Next(100) / 100f - 0.5f) },
                1 => new TakeCommand { ContainerId = Pick() },
                2 => new InsertCommand { ObjectId = Pick(), IntoId = Pick() },
                3 => new FlipCommand { ObjectId = Pick() },
                4 => new ShuffleCommand { ObjectId = Pick() },
                5 => new DealCommand { DeckId = Pick(), Count = 1, Seats = { "seat_1", "seat_2" } },
                6 => new RollCommand { ObjectId = Pick() },
                7 => new CutCommand { DeckId = Pick() },
                8 => new MoveCommand { ObjectId = Pick(), Position = new Vec3(r.Next(100) / 100f - 0.5f, 0, r.Next(100) / 100f - 0.5f) },
                _ => new LockCommand { ObjectId = Pick(), Locked = r.Next(3) == 0 },
            };
        }
    }
}
