using System.Collections.Generic;
using System.Linq;
using Core.Engine;
using Core.Net;
using Core.Rules;
using Newtonsoft.Json.Linq;

namespace Core.Tests.Engine
{
    /// <summary>
    /// T11.13 (ADR 0012 §6): gestures as moves, for any game. A wink at someone runs onGesture with the gesture and the
    /// target; rules decide what it means (here only the murderer's wink counts, and only the target is told); and over
    /// the protocol nobody else learns whom it was aimed at.
    /// </summary>
    public class SocialGestureTests
    {
        private const string Json = @"{
  ""players"": { ""seats"": [ { ""id"": ""a"", ""color"": ""red"" }, { ""id"": ""b"", ""color"": ""blue"" }, { ""id"": ""c"", ""color"": ""green"" },
                              { ""id"": ""d"", ""color"": ""yellow"" } ] },
  ""roles"": {
    ""teams"": [ { ""id"": ""town"", ""name"": ""Town"" }, { ""id"": ""killer"", ""name"": ""Killer"" } ],
    ""list"": [ { ""id"": ""murderer"", ""name"": ""Murderer"", ""team"": ""killer"" }, { ""id"": ""guest"", ""name"": ""Guest"", ""team"": ""town"", ""count"": ""fill"" } ]
  },
  ""variables"": { ""out"": [] },
  ""turn"": { ""phases"": [ ""party"" ] },
  ""phases"": [ { ""id"": ""party"", ""actions"": [] } ],
  ""events"": {
    ""onGesture"": [ { ""if"": {
      ""condition"": { ""and"": [ { ""compare"": { ""left"": ""$param.gesture"", ""right"": ""wink"" } },
                                 { ""compare"": { ""left"": ""$actor.role"", ""right"": ""murderer"" } },
                                 { ""compare"": { ""left"": ""$param.target"", ""op"": ""!="", ""right"": null } } ] },
      ""then"": [ { ""setVariable"": { ""name"": ""out"", ""add"": ""$param.target"" } },
                  { ""message"": { ""text"": ""You have been winked at. Count to five, then fall."", ""to"": ""$param.target"" } } ] } } ]
  }
}";

        private static GameEngine Start()
        {
            var parsed = TableJson.Parse(Json);
            Assert.True(parsed.Ok, string.Join("\n", parsed.Errors));
            Assert.Empty(TableValidator.Validate(parsed.State!));
            var game = new GameEngine(parsed.State!, 4);
            Assert.True(game.Execute(new StartGameCommand()).Ok);
            return game;
        }

        [Fact]
        public void TheMurderersWink_ReachesOnlyItsTarget()
        {
            var game = Start();
            var murderer = game.Session!.Roles.Single(p => p.Value == "murderer").Key;
            var guests = game.Session.Roles.Where(p => p.Value == "guest").Select(p => p.Key).ToList();

            var harmless = game.Execute(new GestureCommand { Actor = guests[0], Gesture = "wink", Target = guests[1] });
            Assert.True(harmless.Ok);
            Assert.True(harmless.ReadsSecrets, "a gesture is the host's to run");
            Assert.Empty((JArray)game.Session.Variables["out"]!);

            var wave = game.Execute(new GestureCommand { Actor = murderer, Gesture = "wave", Target = guests[0] });
            Assert.True(wave.Ok);
            Assert.Empty((JArray)game.Session.Variables["out"]!);

            var wink = game.Execute(new GestureCommand { Actor = murderer, Gesture = "wink", Target = guests[1] });
            Assert.True(wink.Ok, wink.Error?.ToString());
            Assert.Equal(new[] { guests[1] }, ((JArray)game.Session.Variables["out"]!).Select(t => (string)t!));
            var told = wink.Events.OfType<PrivateMessage>().ToList();
            Assert.Equal(new[] { guests[1] }, told.Select(m => m.Seat));
            Assert.DoesNotContain(game.Session.Log, l => l.Contains("winked"));
        }

        [Fact]
        public void AGestureAtNobody_OrOutsideAGame_ChangesNothing()
        {
            var game = Start();
            var murderer = game.Session!.Roles.Single(p => p.Value == "murderer").Key;
            Assert.True(game.Execute(new GestureCommand { Actor = murderer, Gesture = "wink" }).Ok);
            Assert.Empty((JArray)game.Session.Variables["out"]!);
            var stranger = game.Execute(new GestureCommand { Actor = murderer, Gesture = "wink", Target = "zz" });
            Assert.Equal(ErrorCodes.UnknownSeat, stranger.Error!.Code);

            var free = new GameEngine(TableJson.Parse(EngineFixture.Json).State!, 1);
            var result = free.Execute(new GestureCommand { Actor = "seat_1", Gesture = "wink", Target = "seat_2" });
            Assert.True(result.Ok);
            Assert.Empty(result.Events);
        }

        [Fact]
        public void OverTheProtocol_OnlyTheTwoKnowWhomItWasAimedAt()
        {
            var game = Start();
            var host = new SessionHost(game);
            var seats = game.State.Players.Seats.Select(s => s.Id).ToList();
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
            var murderer = game.Session!.Roles.Single(p => p.Value == "murderer").Key;
            var victim = seats.First(s => s != murderer);
            var (_, intent) = clients[murderer].Submit(new GestureCommand { Actor = murderer, Gesture = "wink", Target = victim });
            foreach (var outgoing in host.Receive(murderer, intent!))
                Deliver(outgoing);

            // The orders after the snapshot (which holds the rules themselves, their texts included).
            List<string> Orders(string seat) => received[seat].Skip(1).ToList();
            foreach (var seat in seats.Where(s => s != murderer && s != victim))
                Assert.DoesNotContain(Orders(seat), m => m.Contains($"\"Target\":\"{victim}\""));
            Assert.Contains(Orders(murderer), m => m.Contains($"\"Target\":\"{victim}\""));
            Assert.Contains(Orders(victim), m => m.Contains("You have been winked at."));
            foreach (var seat in seats.Where(s => s != victim))
                Assert.DoesNotContain(Orders(seat), m => m.Contains("You have been winked at."));
            foreach (var client in clients.Values)
            {
                Assert.Equal(0, client.Resyncs);
                Assert.Equal(SeatView.Hash(game.State), SeatView.Hash(client.Confirmed!.State));
            }
        }
    }
}
