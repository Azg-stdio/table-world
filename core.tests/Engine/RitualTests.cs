using System.Linq;
using Core.Engine;
using Core.Net;
using Core.Rules;
using Newtonsoft.Json.Linq;

namespace Core.Tests.Engine
{
    /// <summary>
    /// What a table ritual is made of, for any game (Avalon's "minions, open your eyes"): timed phases that lead to a named
    /// next one and show a banner to the awake; an awake list the host works out and only the awake receive (no `knows`
    /// needed for the awake to see each other); and ballots cast by pieces laid down (`castBallot`).
    /// </summary>
    public class RitualTests
    {
        private const string Json = @"{
  ""players"": { ""seats"": [ { ""id"": ""a"", ""color"": ""red"" }, { ""id"": ""b"", ""color"": ""blue"" }, { ""id"": ""c"", ""color"": ""green"" },
                              { ""id"": ""d"", ""color"": ""yellow"" } ] },
  ""roles"": {
    ""teams"": [ { ""id"": ""good"", ""name"": ""Good"" }, { ""id"": ""evil"", ""name"": ""Evil"" } ],
    ""list"": [ { ""id"": ""loner"", ""team"": ""evil"" }, { ""id"": ""minion"", ""team"": ""evil"" }, { ""id"": ""servant"", ""team"": ""good"", ""count"": ""fill"" } ]
  },
  ""variables"": { ""eyes"": [], ""said"": """" },
  ""secretVariables"": [ ""eyes"" ],
  ""turn"": { ""phases"": [ ""talk"", ""vote"" ] },
  ""phases"": [
    { ""id"": ""close"", ""awake"": ""nobody"", ""seconds"": 3, ""next"": ""evil"", ""sleepText"": ""Eyes closed."" },
    { ""id"": ""evil"", ""awake"": ""$var.eyes"", ""seconds"": 8, ""next"": ""talk"", ""banner"": ""Minions, open your eyes."" },
    { ""id"": ""talk"", ""actions"": [ ""next"" ] },
    { ""id"": ""vote"", ""actions"": [ ""ballot"" ] }
  ],
  ""actions"": [
    { ""id"": ""next"", ""actor"": ""any"", ""effects"": [ { ""endPhase"": {} } ] },
    { ""id"": ""ballot"", ""name"": ""Ballot"", ""actor"": ""any"",
      ""preconditions"": [ { ""or"": [ { ""compare"": { ""left"": ""$actor.team"", ""right"": ""evil"" } }, { ""compare"": { ""left"": ""$param.choice"", ""right"": ""yes"" } } ] } ],
      ""vote"": { ""options"": [ ""yes"", ""no"" ], ""reveal"": ""named"",
                  ""onResult"": [ { ""setVariable"": { ""name"": ""said"", ""value"": ""$vote.result"" } } ] } }
  ],
  ""zones"": [
    { ""id"": ""spot_a"", ""type"": ""LayoutZone"", ""facing"": ""faceDown"", ""transform"": { ""position"": [ 0, 0.05, -0.3 ] }, ""size"": [ 0.12, 0.1, 0.14 ],
      ""onEnter"": [ { ""castBallot"": { ""action"": ""ballot"", ""seat"": ""a"", ""choice"": ""$it.meta.vote"" } } ] }
  ],
  ""objects"": [
    { ""id"": ""no_card"", ""type"": ""Card"", ""meta"": { ""vote"": ""no"" }, ""text"": { ""title"": ""No"" }, ""transform"": { ""position"": [ 0.4, 0, 0 ] } },
    { ""id"": ""yes_card"", ""type"": ""Card"", ""meta"": { ""vote"": ""yes"" }, ""text"": { ""title"": ""Yes"" }, ""transform"": { ""position"": [ 0.6, 0, 0 ] } }
  ],
  ""events"": { ""onGameStart"": [ { ""forEach"": { ""players"": true, ""do"": [ { ""if"": { ""condition"": { ""compare"": { ""left"": ""$it.role"", ""right"": ""loner"" } },
                                     ""then"": [ { ""setVariable"": { ""name"": ""eyes"", ""add"": ""$it"" } } ] } } ] } }, { ""gotoPhase"": { ""phase"": ""close"" } } ] }
}";

        private static GameEngine Start(ulong seed = 3)
        {
            var parsed = TableJson.Parse(Json);
            Assert.True(parsed.Ok, string.Join("\n", parsed.Errors));
            Assert.Empty(TableValidator.Validate(parsed.State!));
            var game = new GameEngine(parsed.State!, seed);
            var start = game.Execute(new StartGameCommand());
            Assert.True(start.Ok, start.Error?.ToString());
            return game;
        }

        private static string With(GameEngine game, string role) => game.Session!.Roles.Single(p => p.Value == role).Key;

        private static void EndTimed(GameEngine game) =>
            Assert.True(game.Execute(new EndTimedPhaseCommand { Phase = game.Session!.Phase!, Visit = game.Session.PhaseVisit }).Ok);

        [Fact]
        public void TimedPhases_LeadToTheirNext_AndALateEndChangesNothing()
        {
            var game = Start();
            Assert.Equal("close", game.Session!.Phase);
            Assert.Equal(3, game.Phase!.Seconds);
            var visit = game.Session.PhaseVisit;
            EndTimed(game);
            Assert.Equal("evil", game.Session.Phase);
            Assert.Equal("Minions, open your eyes.", game.BannerText);

            // The end of a visit already over (a second click, a message late on the wire) does nothing.
            Assert.True(game.Execute(new EndTimedPhaseCommand { Phase = "close", Visit = visit }).Ok);
            Assert.Equal("evil", game.Session.Phase);
            EndTimed(game);
            Assert.Equal("talk", game.Session.Phase);
            Assert.Null(game.BannerText);
        }

        [Fact]
        public void TheAwakeSeeEachOther_ASleeperLearnsNothing()
        {
            // Two evil roles; only the one on the list (worked out by the rules, kept secret) opens its eyes.
            var game = Start();
            EndTimed(game);
            var loner = With(game, "loner");
            var minion = With(game, "minion");
            Assert.Equal(new[] { loner }, game.Session!.Awake);
            Assert.True(game.IsAwake(loner));
            Assert.False(game.IsAwake(minion));

            var awake = new GameEngine(TableJson.Parse(SeatView.Serialize(game.State, loner)).State!, 1);
            var asleep = new GameEngine(TableJson.Parse(SeatView.Serialize(game.State, minion)).State!, 1);
            Assert.True(awake.IsAwake(loner, loner), "without any `knows`, the awake machine tells it is awake");
            Assert.False(awake.IsAwake(minion, loner));
            Assert.Empty(asleep.Session!.Awake!);
            Assert.False(asleep.IsAwake(loner, minion), "a sleeper cannot tell who is awake");
            Assert.False(asleep.Session.Variables.ContainsKey("eyes"));
            Assert.Null(TableJson.Parse(SeatView.Serialize(game.State, null)).State!.Session!.Awake);
        }

        [Fact]
        public void ACardLaidDown_CastsABallot_AndOneTheVoteRefusesGoesBack()
        {
            var game = Start();
            EndTimed(game);
            EndTimed(game);
            Assert.True(game.Execute(new PerformActionCommand { Actor = game.Session!.CurrentSeat, ActionId = "next" }).Ok);
            Assert.Equal("vote", game.Session.Phase);
            var spot = game.State.Zones.Single(z => z.Id == "spot_a");
            var a = game.State.Players.Seats[0].Id;
            var good = game.Session.Roles[a] == "servant";

            var drop = game.Execute(new MoveCommand { Actor = a, ObjectId = "no_card", Position = spot.Transform.Position });
            if (good)
            {
                // The vote's precondition: good says yes. The card is refused and stays where it was.
                Assert.False(drop.Ok);
                Assert.Empty(game.Session.Secrets);
                drop = game.Execute(new MoveCommand { Actor = a, ObjectId = "yes_card", Position = spot.Transform.Position });
            }
            Assert.True(drop.Ok, drop.Error?.ToString());
            Assert.Single(game.Session.Secrets, s => s.Seat == a && s.Action == "ballot");
            Assert.True(game.Table.Find(good ? "yes_card" : "no_card")!.Transform.Rotation.Z > 90f, "laid face down");
            Assert.Contains($"{game.State.Players.Seats[0].Color switch { _ => "Red" }} voted.", game.Session.Log);

            foreach (var seat in game.State.Players.Seats.Skip(1))
                Assert.True(game.Execute(new PerformActionCommand { Actor = seat.Id, ActionId = "ballot", Params = { ["choice"] = "yes" } }).Ok);
            Assert.Empty(game.Session.Secrets);
            Assert.NotEqual("", (string)game.Session.Variables["said"]!);
        }

        [Fact]
        public void Validation_CatchesAnUnknownNext_ABadDuration_AndABallotForNoVote()
        {
            var parsed = TableJson.Parse(Json.Replace(@"""next"": ""evil""", @"""next"": ""nowhere""").Replace(@"""seconds"": 8", @"""seconds"": 0")
                .Replace(@"""castBallot"": { ""action"": ""ballot""", @"""castBallot"": { ""action"": ""next"""));
            var errors = TableValidator.Validate(parsed.State!);
            Assert.Contains(errors, e => e.Path == "phases[0].next" && e.Code == ErrorCodes.UnknownPhase);
            Assert.Contains(errors, e => e.Path == "phases[1].seconds");
            Assert.Contains(errors, e => e.Path.EndsWith("castBallot.action") && e.Code == ErrorCodes.UnknownAction);
        }
    }
}
