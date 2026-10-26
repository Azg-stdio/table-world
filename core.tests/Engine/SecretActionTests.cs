using System.Linq;
using Core.Engine;
using Core.Rules;

namespace Core.Tests.Engine
{
    /// <summary>Simultaneous secret choices: each seat picks without others seeing, then a reveal runs every pick in turn order.</summary>
    public class SecretActionTests
    {
        // Three seats each pick a space (1–3) in secret; when all have picked, the phase ends and its onExit reveals.
        private const string Json = @"{
  ""players"": { ""seats"": [ { ""id"": ""a"", ""color"": ""red"" }, { ""id"": ""b"", ""color"": ""blue"" }, { ""id"": ""c"", ""color"": ""green"" } ] },
  ""resources"": [ { ""id"": ""space"", ""initial"": 0 } ],
  ""variables"": { ""order"": """" },
  ""turn"": { ""phases"": [ ""choose"", ""act"" ] },
  ""phases"": [
    { ""id"": ""choose"", ""actions"": [ ""pick"" ], ""advanceWhen"": { ""allPlayersActed"": true }, ""onExit"": [ { ""reveal"": {} } ] },
    { ""id"": ""act"", ""actions"": [ ""pass"" ], ""advanceWhen"": { ""currentPlayerEnds"": true } }
  ],
  ""actions"": [
    { ""id"": ""pick"", ""name"": ""Pick a space"", ""actor"": ""any"", ""secret"": true, ""limitPerPhase"": 1,
      ""params"": [ { ""name"": ""space"", ""type"": ""choice"", ""options"": [ ""1"", ""2"", ""3"" ] } ],
      ""effects"": [ { ""setResource"": { ""player"": ""$actor"", ""resource"": ""space"", ""value"": ""$param.space"" } },
                     { ""message"": { ""text"": ""{$actor} goes to {$param.space}"" } } ] },
    { ""id"": ""pass"", ""actor"": ""current"", ""effects"": [ { ""endTurn"": {} } ] }
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

        private static void Pick(GameEngine game, string seat, string space)
        {
            var result = game.Execute(new PerformActionCommand { Actor = seat, ActionId = "pick", Params = { ["space"] = space } });
            Assert.True(result.Ok, result.Error?.ToString());
        }

        [Fact]
        public void Choices_StayHidden_UntilEveryoneHasPicked_ThenRunInTurnOrder()
        {
            var game = Start();
            Pick(game, "b", "3");
            Pick(game, "a", "1");
            // Nothing happened yet: only that they chose.
            Assert.Equal(0, game.Session!.Resources["a"]["space"]);
            Assert.Equal(0, game.Session.Resources["b"]["space"]);
            Assert.Equal(2, game.Session.Secrets.Count);
            Assert.Contains("Blue chose in secret.", game.Session.Log);
            Assert.DoesNotContain(game.Session.Log, l => l.Contains("goes to"));
            Assert.Equal("choose", game.Session.Phase);

            Pick(game, "c", "1");
            Assert.Equal("act", game.Session.Phase);
            Assert.Empty(game.Session.Secrets);
            Assert.Equal(1, game.Session.Resources["a"]["space"]);
            Assert.Equal(3, game.Session.Resources["b"]["space"]);
            Assert.Equal(1, game.Session.Resources["c"]["space"]);
            // Revealed from the current seat (a) on, whatever order they chose in.
            var reveals = game.Session.Log.Where(l => l.Contains("goes to")).ToList();
            Assert.Equal(new[] { "Red goes to 1", "Blue goes to 3", "Green goes to 1" }, reveals);
        }

        [Fact]
        public void AChoiceIsChecked_WhenItIsMade()
        {
            var game = Start();
            var bad = game.Execute(new PerformActionCommand { Actor = "a", ActionId = "pick", Params = { ["space"] = "9" } });
            Assert.Equal(ErrorCodes.InvalidParam, bad.Error!.Code);
            Pick(game, "a", "2");
            var again = game.Execute(new PerformActionCommand { Actor = "a", ActionId = "pick", Params = { ["space"] = "3" } });
            Assert.Equal(ErrorCodes.LimitReached, again.Error!.Code);
        }

        [Fact]
        public void RevealOfANonSecretAction_IsReported()
        {
            var state = TableJson.Parse(Json.Replace(@"{ ""reveal"": {} }", @"{ ""reveal"": { ""action"": ""pass"" } }")).State!;
            var errors = TableValidator.Validate(state);
            Assert.Contains(errors, e => e.Path.EndsWith(".action") && e.Code == ErrorCodes.InvalidValue);
        }
    }
}
