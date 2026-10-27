using System.Linq;
using Core.Rules;
using Core.Text;

namespace Core.Tests.Text
{
    /// <summary>T10.4: a game written in Spanish carrying English: every key kind, @ strings, localized files, validation.</summary>
    public class TranslationsTests
    {
        private const string Json = @"{
  ""game"": { ""id"": ""tornaris"", ""name"": ""Tornaris"", ""description"": ""Un juego de exploración."", ""language"": ""es"", ""languages"": [ ""es"", ""en"" ],
              ""messages"": { ""win"": ""¡{seat} gana!"" } },
  ""assets"": [ { ""id"": ""cartas"", ""type"": ""image"", ""path"": ""cartas_es.png"", ""columns"": 2, ""localized"": { ""en"": ""cards_en.png"" } } ],
  ""objects"": [
    { ""type"": ""Token"", ""id"": ""ficha"", ""name"": ""Ficha de Nyra"", ""description"": ""La exploradora."" },
    { ""type"": ""Card"", ""id"": ""espada"", ""text"": { ""title"": ""Espada"", ""body"": ""Ataque +1."" } },
    { ""type"": ""Deck"", ""id"": ""mazo"", ""faceAsset"": ""cartas"", ""backAsset"": ""cartas"",
      ""cards"": [ { ""id"": ""escudo"", ""name"": ""Escudo"", ""faceIndex"": 0 }, { ""faceIndex"": 1 } ] }
  ],
  ""players"": { ""seats"": [ { ""id"": ""a"", ""color"": ""red"", ""name"": ""Cazadora"" }, { ""id"": ""b"", ""color"": ""blue"" } ] },
  ""resources"": [ { ""id"": ""oro"", ""name"": ""Oro"" } ],
  ""variables"": { ""dia"": 1 },
  ""turn"": { ""phases"": [ ""elegir"" ] },
  ""phases"": [ { ""id"": ""elegir"", ""name"": ""Elegir casilla"", ""sleepText"": ""Duerme."", ""actions"": [ ""elegir"", ""votar"" ], ""advanceWhen"": { ""currentPlayerEnds"": true } } ],
  ""actions"": [
    { ""id"": ""elegir"", ""name"": ""Elegir"", ""actor"": ""current"",
      ""params"": [ { ""name"": ""casilla"", ""type"": ""choice"", ""options"": [ ""mazmorra"", ""bosque"" ], ""labels"": { ""mazmorra"": ""Mazmorra"" } } ],
      ""effects"": [ { ""message"": { ""text"": ""@noche"" } } ] },
    { ""id"": ""votar"", ""name"": ""Votar"", ""actor"": ""any"", ""vote"": { ""options"": [ ""si"", ""no"" ], ""labels"": { ""si"": ""Sí"" } } }
  ],
  ""ui"": { ""panels"": [ { ""id"": ""estado"", ""title"": ""Estado"", ""elements"": [ { ""type"": ""label"", ""text"": ""Día {$var.dia}"" } ] } ] },
  ""notebook"": [ { ""id"": ""reglas"", ""title"": ""Reglas"", ""body"": ""Juega bien."" } ],
  ""translations"": {
    ""es"": { ""strings.noche"": ""Es de noche."" },
    ""en"": { ""game.name"": ""Tornaris"", ""objects.ficha.name"": ""Nyra's token"", ""objects.espada.text.title"": ""Sword"",
              ""cards.escudo.name"": ""Shield"", ""seats.a.name"": ""Huntress"", ""actions.elegir.name"": ""Choose"",
              ""params.elegir.casilla.mazmorra"": ""Dungeon"", ""votes.votar.si"": ""Yes"", ""phases.elegir.name"": ""Choose a space"",
              ""ui.estado.title"": ""Status"", ""ui.estado.0.text"": ""Day {$var.dia}"", ""messages.win"": ""{seat} wins!"",
              ""notebook.reglas.body"": ""Play nicely."", ""strings.noche"": ""It is night."" }
  }
}";

        private static TableState Parse(string json = Json)
        {
            var parsed = TableJson.Parse(json);
            Assert.True(parsed.Ok, string.Join("\n", parsed.Errors));
            return parsed.State!;
        }

        private const string OneLanguage = @"{ ""game"": { ""id"": ""x"", ""name"": ""X"" }, ""players"": { ""seats"": [ { ""id"": ""a"", ""color"": ""red"" } ] },
  ""objects"": [ { ""type"": ""Token"", ""id"": ""t"", ""name"": ""@handle"" } ] }";

        [Fact]
        public void EveryKeyKind_IsListed_WithItsWrittenText()
        {
            var texts = Translations.Texts(Parse());
            Assert.Equal("Tornaris", texts["game.name"]);
            Assert.Equal("Un juego de exploración.", texts["game.description"]);
            Assert.Equal("Ficha de Nyra", texts["objects.ficha.name"]);
            Assert.Equal("La exploradora.", texts["objects.ficha.description"]);
            Assert.Equal("Espada", texts["objects.espada.text.title"]);
            Assert.Equal("Ataque +1.", texts["objects.espada.text.body"]);
            Assert.Equal("Escudo", texts["cards.escudo.name"]);
            Assert.Equal("Cazadora", texts["seats.a.name"]);
            Assert.False(texts.ContainsKey("seats.b.name"), "a seat called by its colour has no text of its own");
            Assert.Equal("Oro", texts["resources.oro.name"]);
            Assert.Equal("Elegir", texts["actions.elegir.name"]);
            Assert.Equal("Mazmorra", texts["params.elegir.casilla.mazmorra"]);
            Assert.Equal("bosque", texts["params.elegir.casilla.bosque"]);
            Assert.Equal("Sí", texts["votes.votar.si"]);
            Assert.Equal("Elegir casilla", texts["phases.elegir.name"]);
            Assert.Equal("Duerme.", texts["phases.elegir.sleepText"]);
            Assert.Equal("Estado", texts["ui.estado.title"]);
            Assert.Equal("Día {$var.dia}", texts["ui.estado.0.text"]);
            Assert.Equal("¡{seat} gana!", texts["messages.win"]);
            Assert.Equal("Reglas", texts["notebook.reglas.title"]);
            Assert.Equal("Es de noche.", texts["strings.noche"]);
        }

        [Fact]
        public void TheSessionsLanguage_PicksTheText_MissingOnesReadAsWritten()
        {
            var state = Parse();
            Assert.Equal("Ficha de Nyra", Translations.Text(state, "objects.ficha.name", "Ficha de Nyra"));
            state.Session = new Session { Language = "en" };
            Assert.Equal("Nyra's token", Translations.Text(state, "objects.ficha.name", "Ficha de Nyra"));
            Assert.Equal("La exploradora.", Translations.Text(state, "objects.ficha.description", "La exploradora."));
            Assert.Equal("It is night.", Translations.Resolve(state, "@noche"));
            Assert.Equal("@noche literal", Translations.Resolve(state, "@@noche literal"));
            Assert.Equal("plain", Translations.Resolve(state, "plain"));
            Assert.Equal("cards_en.png", Translations.AssetPath(state, state.Assets[0]));

            state.Session.Language = "es";
            Assert.Equal("Es de noche.", Translations.Resolve(state, "@noche"));
            Assert.Equal("cartas_es.png", Translations.AssetPath(state, state.Assets[0]));
            // A language the game does not have reads the game's own.
            state.Session.Language = "fr";
            Assert.Equal("es", Translations.Current(state));
            Assert.Equal("Ficha de Nyra", Translations.Text(state, "objects.ficha.name", "Ficha de Nyra"));
            Assert.Equal(new[] { "es", "en" }, Translations.Languages(state));
        }

        [Fact]
        public void AGameInOneLanguage_ReadsAsWritten_AndHasNothingToCheck()
        {
            var state = Parse(OneLanguage);
            state.Session = new Session { Language = "en" };
            Assert.Equal("Token", Translations.Text(state, "objects.t.name", "Token"));
            Assert.Empty(Translations.Validate(state));
            Assert.Empty(Translations.Missing(state));
        }

        [Fact]
        public void Validation_FindsUnknownKeys_UnlistedLanguages_AndMissingStrings()
        {
            Assert.Empty(TableValidator.Validate(Parse()));
            // Parsing validates, so the game is broken after it parses.
            var bad = Parse();
            bad.Translations["en"]["objects.nada.name"] = "Nothing";
            bad.Translations["en"]["strings.extra"] = "Extra";
            bad.Translations["es"]["game.name"] = "Otro";
            bad.Translations["fr"] = new System.Collections.Generic.Dictionary<string, string>();
            bad.Ui!.Panels[0].Elements[0].Text = "@dia";
            bad.Assets[0].Localized["de"] = "karten.png";
            var errors = Translations.Validate(bad);
            Assert.Contains(errors, e => e.Path == "translations.en.objects.nada.name" && e.Code == ErrorCodes.UnknownKey);
            Assert.Contains(errors, e => e.Path == "translations.en.strings.extra" && e.Code == ErrorCodes.UnknownKey);
            Assert.Contains(errors, e => e.Path == "translations.es.game.name" && e.Code == ErrorCodes.UnknownKey);
            Assert.Contains(errors, e => e.Path == "translations.fr" && e.Code == ErrorCodes.InvalidValue);
            Assert.Contains(errors, e => e.Path == "assets.cartas.localized.de");
            Assert.Contains(errors, e => e.Path == "translations.es.strings.dia" && e.Code == ErrorCodes.MissingField);
            Assert.Contains(errors, e => e.Path == "translations.es.strings.dia");
            Assert.Contains(TableValidator.Validate(bad), e => e.Path == "translations.en.objects.nada.name");

            var noLanguage = Parse();
            noLanguage.Game!.Language = null;
            Assert.Contains(Translations.Validate(noLanguage), e => e.Path == "game.language");
        }

        [Fact]
        public void Missing_ListsWhatEachLanguageLacks()
        {
            var missing = Translations.Missing(Parse());
            Assert.Equal(new[] { "en" }, missing.Keys);
            Assert.Contains("objects.ficha.description", missing["en"]);
            Assert.Contains("phases.elegir.sleepText", missing["en"]);
            Assert.DoesNotContain("objects.ficha.name", missing["en"]);
            Assert.DoesNotContain("strings.noche", missing["en"]);
        }

        [Fact]
        public void Translations_SurviveASaveAndLoad()
        {
            var state = Parse();
            var again = Parse(TableJson.Serialize(state));
            Assert.Equal("es", again.Game!.Language);
            Assert.Equal(new[] { "es", "en" }, again.Game.Languages);
            Assert.Equal("Shield", again.Translations["en"]["cards.escudo.name"]);
            Assert.Equal("cards_en.png", again.Assets[0].Localized["en"]);
            Assert.DoesNotContain("translations", TableJson.Serialize(Parse(OneLanguage)));
        }
    }
}
