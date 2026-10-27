using Core.Engine;
using Core.Rules;
using Core.Text;

namespace Core.Tests.Text
{
    /// <summary>
    /// T10.3: the engine writes its lines in the session's language, seats by their colour in that language; a refused
    /// move reads in the player's language while agents keep the English; a game's own words still win.
    /// </summary>
    public class EngineLanguageTests
    {
        private const string Json = @"{
  ""game"": { ""id"": ""words"", ""name"": ""Words"" },
  ""players"": { ""seats"": [ { ""id"": ""a"", ""color"": ""red"" }, { ""id"": ""b"", ""color"": ""blue"", ""name"": ""Ana"" } ] },
  ""turn"": { ""phases"": [ ""choose"", ""act"" ] },
  ""phases"": [
    { ""id"": ""choose"", ""actions"": [ ""pick"" ], ""advanceWhen"": { ""allPlayersActed"": true }, ""onExit"": [ { ""reveal"": {} } ] },
    { ""id"": ""act"", ""actions"": [ ""claim"", ""nope"" ], ""advanceWhen"": { ""currentPlayerEnds"": true } }
  ],
  ""actions"": [
    { ""id"": ""pick"", ""name"": ""Elegir lugar"", ""actor"": ""any"", ""secret"": true, ""limitPerPhase"": 1,
      ""params"": [ { ""name"": ""place"", ""type"": ""choice"", ""options"": [ ""dungeon"", ""market"" ] } ] },
    { ""id"": ""claim"", ""name"": ""Reclamar"", ""actor"": ""current"", ""effects"": [ { ""win"": { ""player"": ""$actor"" } } ] },
    { ""id"": ""nope"", ""actor"": ""current"", ""effects"": [ { ""refuse"": { ""reason"": ""Aquí no, {$actor.name}."" } } ] }
  ]
}";

        private static GameEngine Start(string language, string json = Json)
        {
            var parsed = TableJson.Parse(json);
            Assert.True(parsed.Ok, string.Join("\n", parsed.Errors));
            var game = new GameEngine(parsed.State!, 1);
            Assert.True(game.Execute(new StartGameCommand()).Ok);
            game.Session!.Language = language;
            return game;
        }

        private static CommandResult Run(GameEngine game, string seat, string action, string? place = null)
        {
            var command = new PerformActionCommand { Actor = seat, ActionId = action };
            if (place != null)
                command.Params["place"] = place;
            return game.Execute(command);
        }

        [Fact]
        public void InSpanish_TheEngineSpeaksSpanish_AndRefusalsReadInSpanish()
        {
            var game = Start("es");
            Assert.True(Run(game, "a", "pick", "dungeon").Ok);
            Assert.Contains("Rojo eligió en secreto.", game.Session!.Log);
            Assert.True(Run(game, "b", "pick", "market").Ok);
            Assert.Contains("Ana eligió en secreto.", game.Session.Log);

            // Out of turn: refused, in the player's language, the seat by its name.
            var current = game.Session.CurrentSeat!;
            var other = current == "a" ? "b" : "a";
            var refused = Run(game, other, "claim");
            Assert.False(refused.Ok);
            Assert.Equal(ErrorCodes.NotYourTurn, refused.Error!.Code);
            var currentName = current == "a" ? "Rojo" : "Ana";
            Assert.Equal($"Es el turno de {currentName}.", Refusals.Text(Strings.For("es"), refused.Error));
            // Names belong to the session, so they stay in its language whatever the interface reads.
            Assert.Equal($"It is {currentName}'s turn.", Refusals.Text(Strings.For("en"), refused.Error));
            Assert.Contains($"It is {current}'s turn", refused.Error.Message);

            // The game's own reason reads as the game wrote it.
            var nope = Run(game, current, "nope");
            Assert.Equal($"Aquí no, {currentName}.", Refusals.Text(Strings.For("es"), nope.Error!));

            Assert.True(Run(game, current, "claim").Ok);
            Assert.Contains($"{currentName} gana.", game.Session.Log);
        }

        [Fact]
        public void ACodeWithoutAKey_ReadsByItsCode_ElseItsMessage()
        {
            var es = Strings.For("es");
            Assert.Equal("La partida terminó.", Refusals.Text(es, new ValidationError("", ErrorCodes.GameOver, "The game is over.")));
            Assert.Equal("Something odd.", Refusals.Text(es, new ValidationError("", ErrorCodes.InvalidValue, "Something odd.")));
            Assert.Equal("Las reglas no lo permiten.", Refusals.Text(es, new ValidationError("", ErrorCodes.Refused, "x").WithText("refusal.refused")));
        }

        [Fact]
        public void TheGamesOwnWords_StillWin_AndSingularsHaveTheirOwnLine()
        {
            var game = Start("es", Json.Replace(@"""name"": ""Words"" }", @"""name"": ""Words"", ""messages"": { ""win"": ""¡{seat} se lleva todo!"" } }"));
            Assert.True(Run(game, "a", "pick", "dungeon").Ok);
            Assert.True(Run(game, "b", "pick", "market").Ok);
            Assert.True(Run(game, game.Session!.CurrentSeat!, "claim").Ok);
            Assert.Contains(game.Session.Log, l => l.EndsWith("se lleva todo!"));

            var state = TableJson.Parse(Json).State!;
            state.Session = new Session { Language = "es" };
            Assert.Equal("Rojo reveló la ronda a ciegas (1 pieza).", Names.Message(state, "reveal", 1, ("seat", "Rojo"), ("count", "1")));
            Assert.Equal("Rojo reveló la ronda a ciegas (3 piezas).", Names.Message(state, "reveal", 3, ("seat", "Rojo"), ("count", "3")));
            Assert.Equal("El juego", Names.Seat(state, null));
            state.Session.Language = null;
            Assert.Equal("Red revealed the blind round (1 piece).", Names.Message(state, "reveal", 1, ("seat", "Red"), ("count", "1")));
        }
    }
}
