using System.Collections.Generic;
using System.Linq;
using Core.Engine;
using Core.Net;
using Core.Rules;
using Core.Tests.Engine;
using Newtonsoft.Json.Linq;

namespace Core.Tests.Net
{
    /// <summary>
    /// T3.2 core: a host and clients over a simulated network (per-link FIFO queues delivered in random interleavings),
    /// all sending random commands. Every peer must end with the host's state hash, without resyncs.
    /// </summary>
    public class TableSyncTests
    {
        private sealed class Network
        {
            public readonly SessionHost Host;
            public readonly Dictionary<string, SessionClient> Clients = new Dictionary<string, SessionClient>();
            private readonly Dictionary<string, Queue<JObject>> _toClient = new Dictionary<string, Queue<JObject>>();
            private readonly Dictionary<string, Queue<JObject>> _toHost = new Dictionary<string, Queue<JObject>>();
            private readonly DeterministicRandom _random;

            public Network(GameEngine hostGame, ulong seed, params string[] clients)
            {
                Host = new SessionHost(hostGame);
                _random = new DeterministicRandom(seed);
                foreach (var id in clients)
                {
                    Clients[id] = new SessionClient(id);
                    _toClient[id] = new Queue<JObject>();
                    _toHost[id] = new Queue<JObject>();
                    Deliver(Host.Snapshot(id));
                }
            }

            public void Deliver(Outgoing outgoing)
            {
                foreach (var id in outgoing.To == null ? Clients.Keys.ToList() : new List<string> { outgoing.To })
                    _toClient[id].Enqueue((JObject)outgoing.Message.DeepClone());
            }

            public CommandResult? ClientDoes(string id, Command command)
            {
                var client = Clients[id];
                if (!client.Synced)
                    return null;
                var (result, intent) = client.Submit(command);
                if (intent != null)
                    _toHost[id].Enqueue(intent);
                return result;
            }

            public void HostDoes(Command command) => Deliver(Host.ExecuteLocal(command).Broadcast);

            /// <summary>Delivers one queued message on a random link, if any.</summary>
            public bool Step()
            {
                var links = _toClient.Where(p => p.Value.Count > 0).Select(p => (Client: p.Key, ToHost: false))
                    .Concat(_toHost.Where(p => p.Value.Count > 0).Select(p => (Client: p.Key, ToHost: true))).ToList();
                if (links.Count == 0)
                    return false;
                var (client, toHost) = links[_random.Next(links.Count)];
                if (toHost)
                    foreach (var outgoing in Host.Receive(client, _toHost[client].Dequeue()))
                        Deliver(outgoing);
                else if (Clients[client].Receive(_toClient[client].Dequeue()) is { } reply)
                    _toHost[client].Enqueue(reply);
                return true;
            }

            public void Flush()
            {
                while (Step())
                {
                }
            }
        }

        private static GameEngine Game(ulong seed) => new GameEngine(TableJson.Parse(EngineFixture.Json).State!, seed);

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
                8 => new CloneCommand { ObjectId = Pick() },
                _ => new LockCommand { ObjectId = Pick(), Locked = r.Next(3) == 0 },
            };
        }

        [Fact]
        public void CommandCodec_RoundTripsEveryCommandType()
        {
            var commands = new Command[]
            {
                new MoveCommand { ObjectId = "a", Position = new Vec3(1, 2, 3), DieValue = 4 },
                new TakeCommand { ContainerId = "d", Position = null, EntryId = "c1" },
                new DealCommand { DeckId = "d", Count = 2, Seats = { "s1" } },
                new GroupCommand { ObjectIds = { "a", "b" } },
                new PerformActionCommand { ActionId = "play", Actor = "s1", Params = { ["card"] = "c1" } },
                new SetPropertiesCommand { ObjectId = "a", Tint = new[] { 1f, 0f, 0f } },
            };
            foreach (var command in commands)
            {
                var encoded = CommandCodec.Encode(command);
                var decoded = CommandCodec.Decode(JObject.Parse(encoded.ToString()));
                Assert.Equal(encoded.ToString(), CommandCodec.Encode(decoded).ToString());
            }
            Assert.Throws<System.FormatException>(() => CommandCodec.Decode(new JObject { ["type"] = "Command" }));
        }

        [Theory]
        [InlineData(1UL)]
        [InlineData(2UL)]
        [InlineData(3UL)]
        public void HostAndTwoClients_HundredsOfRandomCommands_EndIdentical(ulong seed)
        {
            var net = new Network(Game(seed), seed, "a", "b");
            net.Flush();
            var r = new DeterministicRandom(seed * 31);
            var accepted = 0;
            for (var i = 0; i < 300; i++)
            {
                var who = r.Next(3);
                if (who == 2)
                    net.HostDoes(RandomCommand(net.Host.Game, r));
                else
                {
                    var id = who == 0 ? "a" : "b";
                    if (net.ClientDoes(id, RandomCommand(net.Clients[id].Predicted!, r)) is { Ok: true })
                        accepted++;
                }
                // Deliver a random handful: commands overlap in flight.
                for (var k = r.Next(4); k > 0; k--)
                    net.Step();
            }
            net.Flush();

            var hash = TableJson.Hash(net.Host.Game.State);
            foreach (var (id, client) in net.Clients)
            {
                Assert.True(client.Synced, $"{id} is not synced");
                Assert.Equal(0, client.PendingCount);
                Assert.Equal(hash, TableJson.Hash(client.Confirmed!.State));
                Assert.Equal(hash, TableJson.Hash(client.Predicted!.State));
                Assert.Equal(0, client.Resyncs);
            }
            Assert.True(accepted > 50, $"only {accepted} client commands were accepted locally");
            Assert.True(net.Host.Order > 100);
            Assert.Empty(TableValidator.Validate(net.Host.Game.State));
        }

        [Fact]
        public void ConflictingCommands_HostOrderWins_AndTheLoserIsTold()
        {
            var net = new Network(Game(5), 5, "a", "b");
            net.Flush();
            var refused = new List<ValidationError>();
            net.Clients["b"].Refused += refused.Add;

            // Both lock and delete the same die before hearing from each other; a's delete is ordered first.
            net.ClientDoes("a", new DeleteCommand { ObjectId = "d6" });
            net.ClientDoes("b", new FlipCommand { ObjectId = "d6" });
            Assert.NotNull(net.Clients["b"].Predicted!.Table.Find("d6"));
            net.Flush();

            Assert.Null(net.Clients["b"].Predicted!.Table.Find("d6"));
            Assert.Equal(ErrorCodes.UnknownObject, Assert.Single(refused).Code);
            Assert.Equal(TableJson.Hash(net.Host.Game.State), TableJson.Hash(net.Clients["b"].Predicted!.State));
        }

        [Fact]
        public void RemoteCommands_ApplyInPlace_WhenNothingIsPending()
        {
            var net = new Network(Game(6), 6, "a");
            net.Flush();
            var rebuildsAfterJoin = net.Clients["a"].Rebuilds;
            var applied = new List<Command>();
            net.Clients["a"].Applied += (command, _) => applied.Add(command);

            for (var i = 0; i < 20; i++)
                net.HostDoes(new RollCommand { ObjectId = "d6" });
            net.Flush();

            Assert.Equal(20, applied.Count);
            Assert.Equal(rebuildsAfterJoin, net.Clients["a"].Rebuilds);
            Assert.Equal(TableJson.Hash(net.Host.Game.State), TableJson.Hash(net.Clients["a"].Predicted!.State));
        }

        [Fact]
        public void DivergedClient_DetectsTheHashMismatch_AndResyncs()
        {
            var net = new Network(Game(9), 9, "a");
            net.Flush();
            // Corrupt the client's confirmed table behind the protocol's back.
            net.Clients["a"].Confirmed!.Table.Find("d6")!.Name = "tampered";

            net.HostDoes(new FlipCommand { ObjectId = "loose" });
            net.Flush();

            Assert.Equal(1, net.Clients["a"].Resyncs);
            Assert.Equal(TableJson.Hash(net.Host.Game.State), TableJson.Hash(net.Clients["a"].Confirmed!.State));
        }

        [Fact]
        public void SeatedClient_CannotActForAnotherSeat()
        {
            var host = GameFixtures.Load("war", 5);
            var net = new Network(host, 5, "north");
            net.Host.Seats["north"] = "seat_north";
            net.HostDoes(new StartGameCommand());
            net.Flush();
            var refused = new List<ValidationError>();
            net.Clients["north"].Refused += refused.Add;
            var before = TableJson.Hash(host.State);

            // As South, and with no actor (which would mean "the current seat"): both refused, nothing changes.
            var play = host.LegalActions("seat_south").First();
            net.ClientDoes("north", new PerformActionCommand { Actor = "seat_south", ActionId = play.ActionId });
            net.Flush();
            var unseated = net.ClientDoes("north", new PerformActionCommand { ActionId = play.ActionId });
            net.Flush();
            // The second is refused by the host, or already by the client's own prediction (then it is never sent).
            Assert.Equal(unseated!.Ok ? 2 : 1, refused.Count);
            Assert.All(refused, e => Assert.Equal(ErrorCodes.NotYourSeat, e.Code));
            Assert.Equal(before, TableJson.Hash(host.State));
            Assert.Equal(before, TableJson.Hash(net.Clients["north"].Predicted!.State));

            // As itself: accepted.
            var own = host.LegalActions("seat_north").First();
            net.ClientDoes("north", new PerformActionCommand { Actor = "seat_north", ActionId = own.ActionId });
            net.Flush();
            Assert.NotEqual(before, TableJson.Hash(host.State));
            Assert.Equal(TableJson.Hash(host.State), TableJson.Hash(net.Clients["north"].Predicted!.State));
        }

        [Fact]
        public void RulesGame_PlaysThroughTheProtocol()
        {
            var host = GameFixtures.Load("uno-lite", 4);
            var net = new Network(host, 4, "north");
            net.HostDoes(new StartGameCommand());
            net.Flush();
            var chooser = new DeterministicRandom(77);
            for (var i = 0; i < 200 && !host.Session!.Over; i++)
            {
                var current = host.Session.CurrentSeat!;
                var game = current == "seat_north" ? net.Clients["north"].Predicted! : host;
                var options = game.LegalActions(current).SelectMany(a => a.Choices.Select(c => (a.ActionId, c))).ToList();
                var (action, parameters) = options[chooser.Next(options.Count)];
                var command = new PerformActionCommand { Actor = current, ActionId = action, Params = parameters };
                if (current == "seat_north")
                    net.ClientDoes("north", command);
                else
                    net.HostDoes(command);
                net.Flush();
                Assert.Equal(TableJson.Hash(host.State), TableJson.Hash(net.Clients["north"].Predicted!.State));
            }
        }
    }
}
