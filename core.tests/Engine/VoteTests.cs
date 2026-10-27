using System.Collections.Generic;
using System.Linq;
using Core.Engine;
using Core.Net;
using Core.Rules;
using Newtonsoft.Json.Linq;

namespace Core.Tests.Engine
{
    /// <summary>
    /// T11.5 (ADR 0012 §5): votes for any game. A public vote by name with a majority, a secret count among a list of
    /// voters (only how many chose each option), a unanimous one with its fallback, a vote for a seat with a tie, a vote a
    /// rule closes early, and over the protocol nobody sees a vote before the count.
    /// </summary>
    public class VoteTests
    {
        private const string Json = @"{
  ""players"": { ""seats"": [ { ""id"": ""a"", ""color"": ""red"" }, { ""id"": ""b"", ""color"": ""blue"" }, { ""id"": ""c"", ""color"": ""green"" },
                              { ""id"": ""d"", ""color"": ""yellow"" }, { ""id"": ""e"", ""color"": ""purple"" } ] },
  ""variables"": { ""crew"": [ ""a"", ""c"", ""e"" ], ""outcome"": """", ""fails"": 0, ""picked"": """" },
  ""turn"": { ""phases"": [ ""vote"" ] },
  ""phases"": [ { ""id"": ""vote"", ""actions"": [ ""approve"", ""mission"", ""agree"", ""accuse"", ""call"" ] } ],
  ""actions"": [
    { ""id"": ""approve"", ""name"": ""Team vote"", ""actor"": ""any"",
      ""vote"": { ""options"": [ ""yes"", ""no"" ], ""labels"": { ""yes"": ""approve"", ""no"": ""reject"" }, ""decide"": ""majority"", ""otherwise"": ""no"",
                  ""onResult"": [ { ""setVariable"": { ""name"": ""outcome"", ""value"": ""$vote.result"" } } ] } },
    { ""id"": ""mission"", ""name"": ""Mission"", ""actor"": ""any"",
      ""vote"": { ""options"": [ ""success"", ""fail"" ], ""who"": ""$var.crew"", ""reveal"": ""count"",
                  ""onResult"": [ { ""setVariable"": { ""name"": ""fails"", ""value"": ""$vote.fail"" } } ] } },
    { ""id"": ""agree"", ""name"": ""Pact"", ""actor"": ""any"",
      ""vote"": { ""options"": [ ""keep"", ""break"" ], ""reveal"": ""result"", ""decide"": ""unanimous"", ""otherwise"": ""break"" } },
    { ""id"": ""accuse"", ""name"": ""Accusation"", ""actor"": ""any"",
      ""vote"": { ""seats"": true,
                  ""onResult"": [ { ""setVariable"": { ""name"": ""picked"", ""value"": ""$vote.result"" } } ] } },
    { ""id"": ""call"", ""actor"": ""any"", ""effects"": [ { ""closeVote"": { ""action"": ""accuse"" } } ] }
  ]
}";

        private static GameEngine Start()
        {
            var parsed = TableJson.Parse(Json);
            Assert.True(parsed.Ok, string.Join("\n", parsed.Errors));
            Assert.Empty(TableValidator.Validate(parsed.State!));
            var game = new GameEngine(parsed.State!, 1);
            Assert.True(game.Execute(new StartGameCommand()).Ok);
            return game;
        }

        private static CommandResult Vote(GameEngine game, string seat, string action, string choice)
        {
            var result = game.Execute(new PerformActionCommand { Actor = seat, ActionId = action, Params = { [GameEngine.VoteParam] = choice } });
            Assert.True(result.Ok, result.Error?.ToString());
            return result;
        }

        [Fact]
        public void ANamedVote_IsCountedWhenTheLastVoterVotes()
        {
            var game = Start();
            Vote(game, "a", "approve", "yes");
            Vote(game, "b", "approve", "no");
            Vote(game, "c", "approve", "yes");
            Vote(game, "d", "approve", "no");
            Assert.Equal("", (string)game.Session!.Variables["outcome"]!);
            Assert.Contains("Blue voted.", game.Session.Log);
            Assert.DoesNotContain(game.Session.Log, l => l.Contains("reject"));

            Vote(game, "e", "approve", "yes");
            Assert.Equal("yes", (string)game.Session.Variables["outcome"]!);
            Assert.Contains("Team vote: Red approve, Blue reject, Green approve, Yellow reject, Purple approve.", game.Session.Log);
            Assert.Contains("Team vote: approve.", game.Session.Log);
            Assert.Empty(game.Session.Secrets);
            Assert.Equal(3, game.Session.Votes["approve"].Counts["yes"]);
        }

        [Fact]
        public void ACountedVote_AmongAList_ShowsOnlyTheNumbers()
        {
            var game = Start();
            var outsider = game.Execute(new PerformActionCommand { Actor = "b", ActionId = "mission", Params = { [GameEngine.VoteParam] = "fail" } });
            Assert.Equal(ErrorCodes.NotYourSeat, outsider.Error!.Code);
            Assert.DoesNotContain(game.LegalActions("b"), a => a.ActionId == "mission");
            Assert.Contains(game.LegalActions("a"), a => a.ActionId == "mission" && a.Choices.Count == 2);

            Vote(game, "a", "mission", "success");
            Vote(game, "c", "mission", "fail");
            Vote(game, "e", "mission", "success");
            Assert.Equal(1, (int)game.Session!.Variables["fails"]!);
            Assert.Contains("Mission: 2 Success, 1 Fail.", game.Session.Log);
            Assert.Empty(game.Session.Votes["mission"].Votes);
            Assert.DoesNotContain(game.Session.Log, l => l.Contains("Green") && l.Contains("Fail"));
        }

        [Fact]
        public void AUnanimousVote_FallsBackWhenOneDisagrees()
        {
            var game = Start();
            foreach (var seat in new[] { "a", "b", "c", "d" })
                Vote(game, seat, "agree", "keep");
            Vote(game, "e", "agree", "break");
            Assert.Equal("break", game.Session!.Votes["agree"].Result);
            Assert.Contains("Pact: Break.", game.Session.Log);
            Assert.DoesNotContain(game.Session.Log, l => l.StartsWith("Pact:") && l.Contains("Keep"));
        }

        [Fact]
        public void AVoteForASeat_CanTie_AndARuleCanCloseItEarly()
        {
            var game = Start();
            Vote(game, "a", "accuse", "b");
            Vote(game, "b", "accuse", "a");
            // Two of five voted: a rule calls it now.
            Assert.True(game.Execute(new PerformActionCommand { Actor = "c", ActionId = "call" }).Ok);
            Assert.Equal("tie", game.Session!.Votes["accuse"].Result);
            Assert.Equal("tie", (string)game.Session.Variables["picked"]!);
            Assert.Contains("Accusation: Red Blue, Blue Red.", game.Session.Log);

            foreach (var seat in new[] { "a", "b", "c", "d", "e" })
                Vote(game, seat, "accuse", seat == "d" ? "e" : "d");
            Assert.Equal("d", (string)game.Session.Variables["picked"]!);
            Assert.Contains("Accusation: Yellow.", game.Session.Log);
        }

        [Fact]
        public void OverTheProtocol_NobodySeesAVoteBeforeTheCount()
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
            void Cast(string seat, string choice)
            {
                var (result, intent) = clients[seat].Submit(new PerformActionCommand { Actor = seat, ActionId = "approve", Params = { [GameEngine.VoteParam] = choice } });
                Assert.True(result.Ok, result.Error?.ToString());
                foreach (var outgoing in host.Receive(seat, intent!))
                    Deliver(outgoing);
            }
            Cast("a", "yes");
            Cast("b", "no");
            Assert.DoesNotContain(received["c"], m => m.Contains("\"choice\":\"yes\"") || m.Contains("\"choice\":\"no\""));
            Assert.Contains(received["a"], m => m.Contains("\"choice\":\"yes\""));
            foreach (var seat in new[] { "c", "d", "e" })
                Cast(seat, "yes");
            foreach (var client in clients.Values)
            {
                Assert.Equal(0, client.Resyncs);
                Assert.Equal(SeatView.Hash(game.State), SeatView.Hash(client.Confirmed!.State));
                Assert.Equal("yes", (string)client.Confirmed!.Session!.Variables["outcome"]!);
            }
        }

        [Fact]
        public void Validation_CatchesBrokenVotes()
        {
            var doc = JObject.Parse(Json);
            var actions = (JArray)doc["actions"]!;
            actions[0]!["vote"]!["options"] = new JArray("yes");
            actions[0]!["vote"]!["otherwise"] = "maybe";
            actions[1]!["vote"]!["who"] = "team:pirates";
            actions[2]!["effects"] = JArray.Parse(@"[ { ""endTurn"": {} } ]");
            actions[4]!["effects"] = JArray.Parse(@"[ { ""closeVote"": { ""action"": ""call"" } } ]");
            var errors = TableValidator.Validate(TableJson.Parse(doc.ToString()).State!);
            Assert.Contains(errors, e => e.Path == "actions[0].vote.options");
            Assert.Contains(errors, e => e.Path == "actions[0].vote.otherwise");
            Assert.Contains(errors, e => e.Path == "actions[1].vote.who" && e.Code == ErrorCodes.UnknownTeam);
            Assert.Contains(errors, e => e.Path == "actions[2].vote");
            Assert.Contains(errors, e => e.Code == ErrorCodes.UnknownAction && e.Path.EndsWith("closeVote.action"));
        }
    }
}
