using System.Linq;
using Core.Engine;
using Core.Rules;
using Core.Text;

namespace Core.Tests.Text
{
    /// <summary>
    /// T10.5: the language is chosen at start and kept in the session; names, labels, phases, messages, @ strings, the
    /// engine's lines and Lua all read it. A game in one language can still be started in any language Table World speaks.
    /// </summary>
    public class SessionLanguageTests
    {
        private const string Json = @"{
  ""game"": { ""id"": ""carrera"", ""name"": ""Carrera"", ""language"": ""es"", ""messages"": { ""win"": ""¡{seat} gana la carrera!"" } },
  ""players"": { ""seats"": [ { ""id"": ""a"", ""color"": ""red"", ""name"": ""Cazadora"" }, { ""id"": ""b"", ""color"": ""blue"" } ] },
  ""objects"": [ { ""type"": ""Token"", ""id"": ""ficha"", ""name"": ""Ficha"" } ],
  ""turn"": { ""phases"": [ ""turno"" ] },
  ""phases"": [ { ""id"": ""turno"", ""name"": ""Tu turno"", ""actions"": [ ""ir"", ""ganar"" ], ""advanceWhen"": { ""currentPlayerEnds"": true } } ],
  ""actions"": [
    { ""id"": ""ir"", ""name"": ""Ir"", ""actor"": ""current"", ""limitPerPhase"": 1,
      ""params"": [ { ""name"": ""a"", ""type"": ""choice"", ""options"": [ ""bosque"" ], ""labels"": { ""bosque"": ""Bosque"" } } ],
      ""effects"": [ { ""message"": { ""text"": ""@va"" } }, { ""lua"": { ""call"": ""carry"" } } ] },
    { ""id"": ""ganar"", ""name"": ""Ganar"", ""actor"": ""current"", ""effects"": [ { ""win"": { ""player"": ""$actor"" } } ] }
  ],
  ""scripts"": { ""global"": ""function carry() Game.log(Game.text('lleva', { n = 3 }) .. ' ' .. Game.getLanguage()) end"" },
  ""translations"": {
    ""es"": { ""strings.va"": ""{$actor.name} va al bosque."", ""strings.lleva"": ""Lleva {n}."" },
    ""en"": { ""seats.a.name"": ""Huntress"", ""actions.ir.name"": ""Go"", ""params.ir.a.bosque"": ""Forest"", ""phases.turno.name"": ""Your turn"",
              ""messages.win"": ""{seat} wins the race!"", ""objects.ficha.name"": ""Token"", ""game.name"": ""Race"",
              ""strings.va"": ""{$actor.name} goes to the forest."", ""strings.lleva"": ""Carries {n}."" }
  }
}";

        private static GameEngine Start(string? language, string json = Json)
        {
            var parsed = TableJson.Parse(json);
            Assert.True(parsed.Ok, string.Join("\n", parsed.Errors));
            var game = new GameEngine(parsed.State!, 1);
            var host = new Core.Scripting.ScriptHost(() => game.Table, c => game.Execute(c)) { RulesEngine = () => game };
            game.Scripts = host;
            host.Load(0);
            var started = game.Execute(new StartGameCommand { Language = language });
            Assert.True(started.Ok, started.Error?.ToString());
            return game;
        }

        private static CommandResult Go(GameEngine game) =>
            game.Execute(new PerformActionCommand { Actor = game.Session!.CurrentSeat, ActionId = "ir", Params = { ["a"] = "bosque" } });

        [Fact]
        public void StartedInEnglish_EveryTextReadsEnglish()
        {
            var game = Start("en");
            var state = game.State;
            Assert.Equal("en", game.Session!.Language);
            Assert.Equal("Race", Names.Game(state));
            Assert.Equal("Huntress", Names.Seat(state, "a"));
            Assert.Equal("Blue", Names.Seat(state, "b"));
            Assert.Equal("Token", Names.Of(state, state.Objects.First(o => o.Id == "ficha")));
            Assert.Equal("Go", Names.Action(state, state.Actions[0]));
            Assert.Equal("Your turn", Names.Phase(state, "turno"));
            Assert.Equal("Forest", game.OptionLabel("ir", "a", "bosque"));

            game.Session.CurrentSeat = "a";
            Assert.True(Go(game).Ok);
            Assert.Contains("Huntress goes to the forest.", game.Session.Log);
            Assert.Contains("Carries 3. en", game.Session.Log);
            var again = Go(game);
            Assert.Equal("You already did “Go” this phase.", Refusals.Text(Strings.For("en"), again.Error!));
            Assert.True(game.Execute(new PerformActionCommand { Actor = "a", ActionId = "ganar" }).Ok);
            Assert.Contains("Huntress wins the race!", game.Session.Log);
        }

        [Fact]
        public void StartedInItsOwnLanguage_ReadsAsWritten_AndTheEnginesLinesToo()
        {
            var game = Start(null);
            Assert.Equal("es", game.Session!.Language);
            game.Session.CurrentSeat = "b";
            Assert.True(Go(game).Ok);
            Assert.Contains("Azul va al bosque.", game.Session.Log);
            Assert.Contains("Lleva 3. es", game.Session.Log);
            Assert.Equal("Bosque", game.OptionLabel("ir", "a", "bosque"));
            Assert.Equal("Ya hiciste «Ir» en esta fase.", Refusals.Text(Strings.For("es"), Go(game).Error!));
        }

        [Fact]
        public void ALanguageTheGameLacks_IsRefused_AGameInOneLanguageTakesAnyTableWorldSpeaks()
        {
            var parsed = TableJson.Parse(Json);
            var game = new GameEngine(parsed.State!, 1);
            var refused = game.Execute(new StartGameCommand { Language = "fr" });
            Assert.False(refused.Ok);
            Assert.Equal("language", refused.Error!.Path);
            Assert.Contains("es, en", refused.Error.Suggestion);

            var plain = TableJson.Parse(@"{ ""game"": { ""id"": ""p"", ""name"": ""P"" }, ""players"": { ""seats"": [ { ""id"": ""a"", ""color"": ""red"" } ] },
  ""turn"": { ""phases"": [ ""t"" ] }, ""phases"": [ { ""id"": ""t"", ""actions"": [ ""w"" ], ""advanceWhen"": { ""currentPlayerEnds"": true } } ],
  ""actions"": [ { ""id"": ""w"", ""actor"": ""current"", ""effects"": [ { ""win"": { ""player"": ""$actor"" } } ] } ] }");
            var spanish = new GameEngine(plain.State!, 1);
            Assert.Equal(new[] { "en", "es" }, spanish.PlayableLanguages());
            Assert.True(spanish.Execute(new StartGameCommand { Language = "es" }).Ok);
            Assert.True(spanish.Execute(new PerformActionCommand { Actor = "a", ActionId = "w" }).Ok);
            Assert.Contains("Rojo gana.", spanish.Session!.Log);
        }

        /// <summary>T10.8 gap: a script's own sentences translate by their words (Game.tr), and translate_game finds them.</summary>
        [Fact]
        public void AScriptsSentences_TranslateByTheirWords()
        {
            var json = Json
                .Replace(@"Game.log(Game.text('lleva', { n = 3 }) .. ' ' .. Game.getLanguage())",
                    @"local tr = Game.tr; Game.log(tr('Lleva el oro.')); Game.log(Game.tr(\""Dice \\\""hola\\\"".\""))")
                .Replace(@"""strings.lleva"": ""Carries {n}.""", @"""strings.lleva"": ""Carries {n}."", ""tr:Lleva el oro."": ""Carries the gold.""");
            var texts = Translations.Texts(TableJson.Parse(json).State!);
            Assert.Equal("Lleva el oro.", texts["tr:Lleva el oro."]);
            Assert.Equal("Dice \"hola\".", texts["tr:Dice \"hola\"."]);

            var english = Start("en", json);
            english.Session!.CurrentSeat = "a";
            Assert.True(Go(english).Ok);
            Assert.Contains("Carries the gold.", english.Session.Log);
            Assert.Contains("Dice \"hola\".", english.Session.Log);

            var spanish = Start("es", json);
            spanish.Session!.CurrentSeat = "a";
            Assert.True(Go(spanish).Ok);
            Assert.Contains("Lleva el oro.", spanish.Session.Log);
        }

        [Fact]
        public void ZoneNames_AreTranslatable()
        {
            var state = TableJson.Parse(Json.Replace(@"""objects"": [", @"""zones"": [ { ""type"": ""HandZone"", ""id"": ""mano"", ""name"": ""Mano"", ""owner"": ""a"" } ],
  ""objects"": [")).State!;
            Assert.Equal("Mano", Translations.Texts(state)["zones.mano.name"]);
        }

        [Fact]
        public void TheActionLog_ReadsTheSessionsLanguage()
        {
            var game = Start("es");
            var result = game.Execute(new FlipCommand { ObjectId = "ficha" });
            var lines = ActionLog.Describe(game.Table, new FlipCommand { ObjectId = "ficha" }, result, "Rojo", "a");
            Assert.Contains(lines, l => l.Text == "Rojo volteó Ficha");
            game.Session!.Language = "en";
            lines = ActionLog.Describe(game.Table, new FlipCommand { ObjectId = "ficha" }, result, null, "a");
            Assert.Contains(lines, l => l.Text == "Flipped Token");
        }
    }
}
