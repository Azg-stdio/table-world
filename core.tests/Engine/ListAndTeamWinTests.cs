using System.Linq;
using Core.Engine;
using Core.Rules;
using Newtonsoft.Json.Linq;

namespace Core.Tests.Engine
{
    /// <summary>
    /// General pieces T11.6 asked for: a list variable picked one item at a time (a team), read by its count and position
    /// and checked with inList; and a team that wins (hidden roles), named in the log with its players.
    /// </summary>
    public class ListAndTeamWinTests
    {
        private const string Json = @"{
  ""players"": { ""seats"": [ { ""id"": ""a"", ""color"": ""red"" }, { ""id"": ""b"", ""color"": ""blue"" }, { ""id"": ""c"", ""color"": ""green"" } ] },
  ""roles"": { ""teams"": [ { ""id"": ""crew"", ""name"": ""The crew"" }, { ""id"": ""mutineers"", ""name"": ""Mutineers"" } ],
               ""list"": [ { ""id"": ""sailor"", ""team"": ""crew"", ""count"": ""fill"" }, { ""id"": ""mutineer"", ""team"": ""mutineers"" } ] },
  ""variables"": { ""team"": [], ""size"": 2 },
  ""turn"": { ""phases"": [ ""pick"" ] },
  ""phases"": [ { ""id"": ""pick"", ""actions"": [ ""add"", ""drop"", ""mutiny"" ] } ],
  ""actions"": [
    { ""id"": ""add"", ""actor"": ""current"", ""params"": [ { ""name"": ""who"", ""type"": ""seat"" } ],
      ""preconditions"": [ { ""not"": { ""inList"": { ""list"": ""$var.team"", ""value"": ""$param.who"" } } },
                           { ""compare"": { ""left"": ""$var.team.count"", ""op"": ""<"", ""right"": ""$var.size"" } } ],
      ""effects"": [ { ""setVariable"": { ""name"": ""team"", ""add"": ""$param.who"" } } ] },
    { ""id"": ""drop"", ""actor"": ""current"", ""params"": [ { ""name"": ""who"", ""type"": ""seat"" } ],
      ""effects"": [ { ""setVariable"": { ""name"": ""team"", ""remove"": ""$param.who"" } } ] },
    { ""id"": ""mutiny"", ""actor"": ""any"",
      ""preconditions"": [ { ""compare"": { ""left"": ""$var.team.0"", ""right"": ""$actor.id"" } } ],
      ""effects"": [ { ""win"": { ""team"": ""mutineers"" } } ] }
  ]
}";

        private static GameEngine Start()
        {
            var parsed = TableJson.Parse(Json);
            Assert.True(parsed.Ok, string.Join("\n", parsed.Errors));
            Assert.Empty(TableValidator.Validate(parsed.State!));
            var game = new GameEngine(parsed.State!, 2);
            Assert.True(game.Execute(new StartGameCommand()).Ok);
            return game;
        }

        private static CommandResult Pick(GameEngine game, string action, string who) =>
            game.Execute(new PerformActionCommand { Actor = "a", ActionId = action, Params = { ["who"] = who } });

        [Fact]
        public void AList_GrowsAndShrinks_OneItemAtATime()
        {
            var game = Start();
            Assert.True(Pick(game, "add", "b").Ok);
            Assert.Equal(ErrorCodes.PreconditionFailed, Pick(game, "add", "b").Error!.Code);
            Assert.True(Pick(game, "add", "c").Ok);
            Assert.Equal(ErrorCodes.PreconditionFailed, Pick(game, "add", "a").Error!.Code);
            Assert.Equal(new[] { "b", "c" }, ((JArray)game.Session!.Variables["team"]!).Select(t => (string)t!));
            Assert.True(Pick(game, "drop", "b").Ok);
            Assert.Equal(new[] { "c" }, ((JArray)game.Session.Variables["team"]!).Select(t => (string)t!));
            // Legal choices already leave out who is in.
            Assert.DoesNotContain(game.LegalActions("a").Single(a => a.ActionId == "add").Choices, c => c["who"] == "c");
        }

        [Fact]
        public void ATeamWins_AndTheLogNamesItsPlayers()
        {
            var game = Start();
            Assert.True(Pick(game, "add", "c").Ok);
            var result = game.Execute(new PerformActionCommand { Actor = "c", ActionId = "mutiny" });
            Assert.True(result.Ok, result.Error?.ToString());
            Assert.True(game.Session!.Over);
            Assert.Equal("mutineers", game.Session.WinnerTeam);
            var mutineer = game.Session.Roles.Single(p => p.Value == "mutineer").Key;
            Assert.Contains($"Mutineers win: {Names.Seat(game.State, mutineer)}.", game.Session.Log);
        }

        [Fact]
        public void Validation_CatchesAnUnknownWinningTeam_AndAnAmbiguousSet()
        {
            var doc = JObject.Parse(Json);
            doc["actions"]![2]!["effects"] = JArray.Parse(@"[ { ""win"": { ""team"": ""pirates"" } } ]");
            doc["actions"]![0]!["effects"]![0]!["setVariable"]!["value"] = "x";
            var errors = TableValidator.Validate(TableJson.Parse(doc.ToString()).State!);
            Assert.Contains(errors, e => e.Code == ErrorCodes.UnknownTeam && e.Path.EndsWith("win.team"));
            Assert.Contains(errors, e => e.Path.EndsWith("setVariable") && e.Code == ErrorCodes.InvalidValue);
        }
    }
}
