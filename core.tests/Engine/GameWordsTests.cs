using System.Linq;
using Core.Engine;
using Core.Rules;

namespace Core.Tests.Engine
{
    /// <summary>
    /// What players read: choice options by their labels, the engine's own lines in the game's words, secret choices
    /// counted and shown to the one who made them, and spaces drawn on a board holding only the pieces on them.
    /// </summary>
    public class GameWordsTests
    {
        private const string Json = @"{
  ""game"": { ""id"": ""words"", ""name"": ""Words"",
    ""messages"": { ""choseInSecret"": ""{seat} eligió en secreto."", ""chose"": ""{seat} va a {option}."", ""win"": ""¡{seat} gana!"" } },
  ""players"": { ""seats"": [ { ""id"": ""a"", ""color"": ""red"" }, { ""id"": ""b"", ""color"": ""blue"", ""name"": ""Ana"" }, { ""id"": ""c"", ""color"": ""green"" } ] },
  ""turn"": { ""phases"": [ ""choose"", ""act"" ] },
  ""phases"": [
    { ""id"": ""choose"", ""actions"": [ ""pick"", ""peek"" ], ""advanceWhen"": { ""allPlayersActed"": true }, ""onExit"": [ { ""reveal"": {} } ] },
    { ""id"": ""act"", ""actions"": [ ""claim"" ], ""advanceWhen"": { ""currentPlayerEnds"": true } }
  ],
  ""actions"": [
    { ""id"": ""pick"", ""name"": ""Pick a place"", ""actor"": ""any"", ""secret"": true, ""limitPerPhase"": 1,
      ""params"": [ { ""name"": ""place"", ""type"": ""choice"", ""options"": [ ""dungeon"", ""market"" ],
                     ""labels"": { ""dungeon"": ""Mazmorra"", ""market"": ""Mercado"" } } ] },
    { ""id"": ""peek"", ""actor"": ""any"", ""button"": false,
      ""effects"": [ { ""message"": { ""text"": ""{$secrets} waiting, {$secrets.pick} picks; {$actor.name} chose {$actor.chose.pick}"" } } ] },
    { ""id"": ""claim"", ""actor"": ""current"", ""effects"": [ { ""win"": { ""player"": ""$actor"" } } ] }
  ]
}";

        private static GameEngine Start(string json = Json)
        {
            var parsed = TableJson.Parse(json);
            Assert.True(parsed.Ok, string.Join("\n", parsed.Errors));
            Assert.Empty(TableValidator.Validate(parsed.State!));
            var game = new GameEngine(parsed.State!, 1);
            Assert.True(game.Execute(new StartGameCommand()).Ok);
            return game;
        }

        private static void Run(GameEngine game, string seat, string action, string? place = null)
        {
            var command = new PerformActionCommand { Actor = seat, ActionId = action };
            if (place != null)
                command.Params["place"] = place;
            var result = game.Execute(command);
            Assert.True(result.Ok, result.Error?.ToString());
        }

        [Fact]
        public void Options_ShowTheirLabels_AndTheEngineSpeaksTheGamesWords()
        {
            var game = Start();
            Assert.Equal("Mazmorra", game.OptionLabel("pick", "place", "dungeon"));
            Assert.Equal("elsewhere", game.OptionLabel("pick", "place", "elsewhere"));

            Run(game, "b", "pick", "dungeon");
            Assert.Contains("Ana eligió en secreto.", game.Session!.Log);
            Run(game, "a", "pick", "market");
            Run(game, "c", "pick", "market");
            Assert.Contains("Red va a Mercado.", game.Session.Log);
            Assert.Contains("Ana va a Mazmorra.", game.Session.Log);
            Assert.DoesNotContain(game.Session.Log, l => l.Contains("dungeon") || l.Contains("market"));

            Run(game, game.Session.CurrentSeat!, "claim");
            Assert.Contains("¡Red gana!", game.Session.Log);
        }

        [Fact]
        public void WithoutTheGamesWords_TheEnglishLinesStay()
        {
            var game = Start(Json.Replace(@"""messages"": { ""choseInSecret"": ""{seat} eligió en secreto."", ""chose"": ""{seat} va a {option}."", ""win"": ""¡{seat} gana!"" }", @"""messages"": { }"));
            Run(game, "a", "pick", "dungeon");
            Assert.Contains("Red chose in secret.", game.Session!.Log);
            Run(game, "b", "pick", "market");
            Run(game, "c", "pick", "market");
            Assert.Contains("Red chose Pick a place: Mazmorra.", game.Session.Log);
        }

        [Fact]
        public void SecretChoices_AreCounted_AndShownToWhoMadeThem()
        {
            var game = Start();
            Run(game, "b", "pick", "dungeon");
            Run(game, "b", "peek");
            Assert.Contains("1 waiting, 1 picks; Ana chose Mazmorra", game.Session!.Log);
        }

        [Fact]
        public void ASpaceDrawnOnABoard_HoldsOnlyThePiecesOnIt()
        {
            const string json = @"{
  ""players"": { ""seats"": [ { ""id"": ""a"", ""color"": ""red"" } ] },
  ""objects"": [
    { ""id"": ""map"", ""type"": ""Board"", ""size"": [0.8, 0.8], ""transform"": { ""position"": [0, 0, 0] } },
    { ""id"": ""pawn"", ""type"": ""Token"", ""transform"": { ""position"": [0.02, 0.02, 0] } } ],
  ""zones"": [ { ""id"": ""space"", ""type"": ""ScriptingZone"", ""transform"": { ""position"": [0, 0.05, 0] }, ""size"": [0.2, 0.2, 0.2] } ],
  ""turn"": { ""phases"": [ ""play"" ] },
  ""phases"": [ { ""id"": ""play"", ""actions"": [ ""look"" ] } ],
  ""actions"": [ { ""id"": ""look"", ""actor"": ""any"", ""effects"": [ { ""message"": { ""text"": ""{$obj.space.count} in the space"" } } ] } ]
}";
            var game = Start(json);
            Assert.Contains("space", game.Table.ZonesOf("map"));
            Run(game, "a", "look");
            Assert.Contains("1 in the space", game.Session!.Log);
        }
    }
}
