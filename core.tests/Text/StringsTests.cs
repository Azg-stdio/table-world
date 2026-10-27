using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Core.Text;

namespace Core.Tests.Text
{
    /// <summary>T10.1: string tables: lookup with fallback, placeholders, and the check that keeps every language in step with English.</summary>
    public class StringsTests
    {
        private static Strings Spanish() => new Strings("es",
            new Dictionary<string, string> { ["win"] = "¡{seat} gana!" },
            new Strings("en", new Dictionary<string, string> { ["win"] = "{seat} wins.", ["pass"] = "{seat} passes." }));

        [Fact]
        public void Lookup_FallsBackToEnglish_ThenTheKey()
        {
            var es = Spanish();
            Assert.Equal("¡Rojo gana!", es.Get("win", ("seat", "Rojo")));
            Assert.Equal("Rojo passes.", es.Get("pass", ("seat", "Rojo")));
            Assert.Equal("hud.unknown", es.Get("hud.unknown"));
            Assert.Null(es.Raw("hud.unknown"));
            Assert.True(es.Has("win"));
            Assert.False(es.Has("pass"));
        }

        [Fact]
        public void Placeholders_AreFilled_UnknownOnesKept_BracesEscaped()
        {
            Assert.Equal("Start game (4 players)", Strings.Fill("Start game ({count} players)", ("count", 4)));
            Assert.Equal("3.5 m, {unit}", Strings.Fill("{size} m, {unit}", ("size", 3.5)));
            Assert.Equal("{literal} and Red", Strings.Fill("{{literal}} and {seat}", ("seat", "Red")));
            Assert.Equal("open { brace", Strings.Fill("open { brace"));
            Assert.Equal(new[] { "seat", "count" }, Strings.Placeholders("{seat} has {count}, {{not}}"));
        }

        [Fact]
        public void Parse_RefusesAnythingButTexts()
        {
            Assert.Equal("x", Strings.Parse("{\"a\":\"x\"}")["a"]);
            Assert.Throws<FormatException>(() => Strings.Parse("[\"a\"]"));
            Assert.Throws<FormatException>(() => Strings.Parse("{\"a\":{\"b\":\"c\"}}"));
            Assert.Throws<FormatException>(() => Strings.Parse("{\"a\":3}"));
        }

        [Fact]
        public void Check_ListsMissing_Extra_AndPlaceholderMismatches()
        {
            var en = new Dictionary<string, string> { ["a"] = "{seat} wins", ["b"] = "Pass", ["c"] = "{n} cards" };
            var es = new Dictionary<string, string> { ["a"] = "{jugador} gana", ["c"] = "{n} cartas", ["z"] = "Extra" };
            var (missing, extra, placeholders) = Strings.Check(en, es);
            Assert.Equal(new[] { "b" }, missing);
            Assert.Equal(new[] { "z" }, extra);
            Assert.Equal(new[] { "a" }, placeholders);
        }

        /// <summary>The table check: every built-in language has exactly English's keys, with the same placeholders.</summary>
        [Fact]
        public void BuiltInTables_MatchEnglish()
        {
            var languages = Strings.BuiltIn();
            Assert.Contains("en", languages);
            Assert.Contains("es", languages);
            var en = Strings.BuiltInTexts("en")!;
            Assert.NotEmpty(en);
            foreach (var language in languages.Where(l => l != "en"))
            {
                var (missing, extra, placeholders) = Strings.Check(en, Strings.BuiltInTexts(language)!);
                Assert.True(missing.Count == 0, $"{language}.json lacks: {string.Join(", ", missing)}");
                Assert.True(extra.Count == 0, $"{language}.json has keys English does not: {string.Join(", ", extra)}");
                Assert.True(placeholders.Count == 0, $"{language}.json fills other placeholders in: {string.Join(", ", placeholders)}");
            }
            Assert.Equal("Español", Strings.For("es").Get("language.name"));
            Assert.Equal("English", Strings.For("xx").Get("language.name"));
        }

        [Fact]
        public void AFolder_AddsALanguage_AndOverridesTexts()
        {
            var folder = Path.Combine(Path.GetTempPath(), "tw-strings-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                File.WriteAllText(Path.Combine(folder, "fr.json"), "{\"language.name\":\"Français\"}");
                File.WriteAllText(Path.Combine(folder, "es.json"), "{\"language.name\":\"Castellano\"}");
                File.WriteAllText(Path.Combine(folder, "broken.json"), "{\"language.name\":");
                var problems = new List<string>();
                var extra = Strings.LoadFolder(folder, problems);
                Assert.Single(problems);
                Assert.StartsWith("broken.json", problems[0]);
                Assert.Equal("Français", Strings.For("fr", extra).Get("language.name"));
                Assert.Equal("Castellano", Strings.For("es", extra).Get("language.name"));
                Assert.Empty(Strings.LoadFolder(Path.Combine(folder, "none")));
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }
    }
}
