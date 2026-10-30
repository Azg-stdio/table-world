using System;
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
    /// T11.2 (ADR 0012): each peer receives its seat's view. Secret choices reach only their seat until the reveal, peers
    /// with different secrets agree on the public hash, host-run orders carry events that survive the wire, and a
    /// client's own roll shows the host's value.
    /// </summary>
    public class SeatViewTests
    {
        /// <summary>A seatless host and one client per seat, messages delivered at once; every text a client got is kept.</summary>
        private sealed class Table
        {
            public readonly SessionHost Host;
            public readonly Dictionary<string, SessionClient> Clients = new Dictionary<string, SessionClient>();
            public readonly Dictionary<string, List<string>> Received = new Dictionary<string, List<string>>();

            public Table(GameEngine game, params string[] seats)
            {
                Host = new SessionHost(game);
                foreach (var seat in seats)
                {
                    Host.Seats[seat] = seat;
                    Clients[seat] = new SessionClient(seat);
                    Received[seat] = new List<string>();
                    Deliver(Host.Snapshot(seat));
                }
            }

            public void Deliver(Outgoing outgoing)
            {
                foreach (var id in outgoing.To == null ? Clients.Keys.ToList() : new List<string> { outgoing.To })
                {
                    Received[id].Add(outgoing.Message.ToString(Newtonsoft.Json.Formatting.None));
                    if (Clients[id].Receive((JObject)outgoing.Message.DeepClone()) is { } reply)
                        foreach (var answer in Host.Receive(id, reply))
                            Deliver(answer);
                }
            }

            public CommandResult Does(string seat, Command command)
            {
                var (result, intent) = Clients[seat].Submit(command);
                Assert.True(result.Ok, result.Error?.ToString());
                foreach (var outgoing in Host.Receive(seat, intent!))
                    Deliver(outgoing);
                return result;
            }
        }

        private static GameEngine SecretGame()
        {
            var game = new GameEngine(TableJson.Parse(SecretActionTests.Json).State!, 7);
            Assert.True(game.Execute(new StartGameCommand()).Ok);
            return game;
        }

        private static PerformActionCommand Pick(string seat, string space) =>
            new PerformActionCommand { Actor = seat, ActionId = "pick", Params = { ["space"] = space } };

        [Fact]
        public void SecretChoices_ReachOnlyTheirSeat_UntilTheReveal()
        {
            var table = new Table(SecretGame(), "a", "b", "c");
            table.Does("a", Pick("a", "2"));
            table.Does("b", Pick("b", "3"));

            // Nobody else received a's or b's choice, in any message.
            Assert.DoesNotContain(table.Received["b"].Concat(table.Received["c"]), m => m.Contains("\"space\":\"2\""));
            Assert.DoesNotContain(table.Received["a"].Concat(table.Received["c"]), m => m.Contains("\"space\":\"3\""));
            // Each knows its own, and that the others chose.
            Assert.Equal("2", table.Clients["a"].Confirmed!.Session!.Secrets.Single(s => s.Seat == "a").Params["space"]);
            Assert.Empty(table.Clients["c"].Confirmed!.Session!.Secrets.Single(s => s.Seat == "a").Params);
            Assert.Equal(2, table.Clients["c"].Confirmed!.Session!.Secrets.Count);
            // Different secrets, one public table.
            var hash = SeatView.Hash(table.Host.Game.State);
            foreach (var client in table.Clients.Values)
            {
                Assert.Equal(hash, SeatView.Hash(client.Confirmed!.State));
                Assert.Equal(hash, SeatView.Hash(client.Predicted!.State));
            }

            // The last choice reveals all three on every screen.
            table.Does("c", Pick("c", "1"));
            foreach (var client in table.Clients.Values)
            {
                Assert.Equal(SeatView.Hash(table.Host.Game.State), SeatView.Hash(client.Confirmed!.State));
                Assert.Equal(2, client.Confirmed!.Session!.Resources["a"]["space"]);
                Assert.Equal(3, client.Confirmed!.Session!.Resources["b"]["space"]);
                Assert.Contains("Blue goes to 3", client.Confirmed!.Session!.Log);
                Assert.Equal(0, client.Resyncs);
            }
        }

        [Fact]
        public void Snapshots_AndSessions_HoldNoGenerator()
        {
            var game = SecretGame();
            Assert.NotEqual(0UL, game.Session!.RandomState);
            var host = new SessionHost(game);
            var snapshot = host.Snapshot("a").Message;
            Assert.Null(snapshot["random"]);
            var state = TableJson.Parse((string)snapshot["state"]!).State!;
            Assert.Equal(0UL, state.Session!.RandomState);
            Assert.NotEqual(0UL, game.Session.RandomState);
        }

        [Fact]
        public void OwnRoll_IsNotGuessed_AndShowsTheHostsValue()
        {
            var host = new GameEngine(TableJson.Parse(EngineFixture.Json).State!, 3);
            var table = new Table(host, "seat_1");
            var applied = new List<(Command Command, CommandResult Result)>();
            table.Clients["seat_1"].Applied += (command, result) => applied.Add((command, result));

            var (guess, intent) = table.Clients["seat_1"].Submit(new RollCommand { ObjectId = "d6" });
            Assert.True(guess.Ok);
            Assert.Empty(guess.Events);
            foreach (var outgoing in table.Host.Receive("seat_1", intent!))
                table.Deliver(outgoing);

            var rolled = applied.Single().Result.Events.OfType<DieRolled>().Single();
            Assert.Equal(((Die)host.Table.Find("d6")!).Value, rolled.Value);
            Assert.Equal(rolled.Value, ((Die)table.Clients["seat_1"].Predicted!.Table.Find("d6")!).Value);
            Assert.Equal(0, table.Clients["seat_1"].PendingCount);
        }

        /// <summary>T11.16: a rules game's cards are secret on the wire, and come back whole when a view is sent back.</summary>
        [Fact]
        public void HandsAndFaceDownDecks_ReachOthersAsUnknownCards()
        {
            var game = GameFixtures.Load("uno-lite", 3);
            Assert.True(game.Execute(new StartGameCommand()).Ok);
            var seats = game.State.Players.Seats.Select(s => s.Id).ToList();
            var view = TableJson.Parse(SeatView.Serialize(game.State, seats[0])).State!;
            var viewer = new GameEngine(view, 1);
            var theirs = view.Objects.OfType<Card>().Where(c => viewer.Table.IsHiddenFrom(c.Id, seats[0])).ToList();
            Assert.NotEmpty(theirs);
            Assert.All(theirs, c => Assert.True(c.Unknown && c.Meta == null && c.Name == null && c.EntryId == null, c.Id));
            var mine = view.Objects.OfType<Card>().Where(c => viewer.Table.ZonesOf(c.Id).Contains(game.State.Players.Seats[0].HandZone!)).ToList();
            Assert.NotEmpty(mine);
            Assert.All(mine, c => Assert.False(c.Unknown));
            var draw = view.Objects.OfType<Deck>().Single(d => !d.IsFaceUp);
            Assert.All(draw.Cards, c => Assert.True(c.Unknown && c.Id == null && c.Meta == null));
            Assert.Equal(game.State.Objects.OfType<Deck>().Single(d => d.Id == draw.Id).Cards.Count, draw.Cards.Count);
            // A free-play table keeps TTS's honest play.
            var free = TableJson.Parse(EngineFixture.Json).State!;
            Assert.DoesNotContain("\"unknown\":true", SeatView.Serialize(free, "seat_1"));

            // A view sent back (an agent's edit) gets its real cards from the live table.
            SeatView.Restore(view, game.State);
            Assert.DoesNotContain(view.Objects.OfType<Card>(), c => c.Unknown);
            Assert.DoesNotContain(view.Objects.OfType<Deck>().SelectMany(d => d.Cards), c => c.Unknown);
            Assert.Equal(TableJson.Hash(game.State), TableJson.Hash(view));
        }

        /// <summary>Whoever searches a face-down deck sees its cards (TTS too); the others keep seeing backs.</summary>
        [Fact]
        public void SearchingAFaceDownDeck_ShowsItToTheSearcherAlone()
        {
            var game = GameFixtures.Load("uno-lite", 5);
            Assert.True(game.Execute(new StartGameCommand()).Ok);
            var seats = game.State.Players.Seats.Select(s => s.Id).ToArray();
            var table = new Table(game, seats);
            var draw = game.State.Objects.OfType<Deck>().Single(d => !d.IsFaceUp).Id;
            Deck DeckOn(string seat) => (Deck)table.Clients[seat].Predicted!.Table.Find(draw)!;
            Assert.All(DeckOn(seats[0]).Cards, c => Assert.True(c.Unknown));

            var (_, intent) = table.Clients[seats[0]].Submit(new SearchCommand { ContainerId = draw, Actor = seats[0] });
            foreach (var outgoing in table.Host.Receive(seats[0], intent!))
                table.Deliver(outgoing);
            Assert.All(DeckOn(seats[0]).Cards, c => Assert.False(c.Unknown));
            Assert.Equal(((Deck)game.Table.Find(draw)!).Cards.Select(c => c.Id), DeckOn(seats[0]).Cards.Select(c => c.Id));
            Assert.All(DeckOn(seats[1]).Cards, c => Assert.True(c.Unknown));
            foreach (var client in table.Clients.Values)
            {
                Assert.Equal(0, client.Resyncs);
                Assert.Equal(SeatView.Hash(game.State), SeatView.Hash(client.Confirmed!.State));
            }
        }

        [Fact]
        public void EveryEventType_SurvivesTheWire()
        {
            var events = new StateEvent[]
            {
                new LightingChanged(), new TableSettingsChanged(), new ObjectAdded("a", "bag"), new ObjectRemoved("a"),
                new EffectTriggered("a", 2), new ObjectUpdated("a"), new ContainerChanged("a", "bag", null), new Shuffled("deck"),
                new DieRolled("d6", 5), new ZoneEntered("a", "z"), new ZoneLeft("a", "z"), new MusicChanged(), new ZoneChanged("z"),
                new ScriptUiChanged(null), new AssetAdded("img"), new InputsChanged("a"), new ButtonsChanged("a"),
                new NotebookChanged(), new DrawingsChanged(), new CardShown("c", new Card { Id = "c", Name = "Ace", FaceIndex = 3 }, "s1"),
                new GameMessage("Red wins."), new PrivateMessage("s1", "You are Merlin."), new Searched("deck", "s1"), new ScreenChanged(),
                new SoundCue("gong", 0.5f, new List<string> { "s1" }),
                new AutoMoved("s1", "target", new Dictionary<string, string> { ["target"] = "s2" }),
            };
            var all = typeof(StateEvent).Assembly.GetTypes().Where(t => typeof(StateEvent).IsAssignableFrom(t) && !t.IsAbstract).Select(t => t.Name);
            Assert.Equal(all.OrderBy(n => n), events.Select(e => e.GetType().Name).OrderBy(n => n));
            foreach (var e in events)
            {
                var encoded = EventCodec.Encode(e);
                var decoded = EventCodec.Decode(JObject.Parse(encoded.ToString()));
                Assert.Equal(e.GetType(), decoded.GetType());
                Assert.Equal(encoded.ToString(), EventCodec.Encode(decoded).ToString());
            }
            Assert.Equal(5, ((DieRolled)EventCodec.Decode(EventCodec.Encode(new DieRolled("d6", 5)))).Value);
            Assert.Equal("Ace", ((CardShown)EventCodec.Decode(EventCodec.Encode(events.OfType<CardShown>().Single()))).Card.Name);
            Assert.Equal("s2", ((AutoMoved)EventCodec.Decode(EventCodec.Encode(events.OfType<AutoMoved>().Single()))).Params["target"]);
        }
    }
}
