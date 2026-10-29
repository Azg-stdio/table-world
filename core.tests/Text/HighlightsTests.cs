using System.Linq;
using Core.Engine;
using Core.Rules;
using Core.Text;

namespace Core.Tests.Text
{
    /// <summary>
    /// Coloured words (M12, T13.6): seats in their seat colour, teams and roles in their team's, vote options in theirs,
    /// and a game's own words; whole words only, readable on the panel, plain text kept as written.
    /// </summary>
    public class HighlightsTests
    {
        private const string Json = @"{
  ""game"": { ""id"": ""h"", ""name"": ""H"", ""highlights"": { ""Excalibur"": ""#ffd040"" } },
  ""players"": { ""seats"": [ { ""id"": ""a"", ""color"": ""red"" }, { ""id"": ""b"", ""color"": ""blue"", ""name"": ""Ana"" }, { ""id"": ""c"", ""color"": ""#204020"" } ] },
  ""roles"": {
    ""teams"": [ { ""id"": ""good"", ""name"": ""Loyal servants"", ""color"": ""#4a8cff"" }, { ""id"": ""evil"", ""name"": ""Minions"", ""color"": ""red"" } ],
    ""list"": [ { ""id"": ""merlin"", ""name"": ""Merlin"", ""team"": ""good"" }, { ""id"": ""servant"", ""team"": ""good"", ""count"": ""fill"" },
                { ""id"": ""assassin"", ""name"": ""Assassin"", ""team"": ""evil"", ""color"": ""#ff8020"" }, { ""id"": ""minion"", ""team"": ""evil"", ""count"": ""fill"" } ],
    ""byPlayers"": { ""3"": { ""good"": 2, ""evil"": 1 } } },
  ""turn"": { ""phases"": [ ""vote"" ] },
  ""phases"": [ { ""id"": ""vote"", ""actions"": [ ""team_vote"" ] } ],
  ""actions"": [ { ""id"": ""team_vote"", ""name"": ""Team vote"", ""actor"": ""any"",
      ""vote"": { ""options"": [ ""approve"", ""reject"" ], ""labels"": { ""approve"": ""Approve"", ""reject"": ""Reject"" },
                ""colors"": { ""approve"": ""#3fae5a"", ""reject"": ""red"" }, ""reveal"": ""named"" } } ]
}";

        private static TableState State(string json = Json)
        {
            var parsed = TableJson.Parse(json);
            Assert.True(parsed.Ok, string.Join("\n", parsed.Errors));
            Assert.Empty(TableValidator.Validate(parsed.State!));
            return parsed.State!;
        }

        [Fact]
        public void SeatsTeamsRolesOptionsAndTheGamesWords_AreColoured()
        {
            var terms = Highlights.Terms(State());
            Assert.Equal(SeatLayout.Rgb("red"), terms["Red"]);
            Assert.Equal(SeatLayout.Rgb("blue"), terms["Ana"]);
            Assert.Equal("#4a8cff", Highlights.Hex(terms["Loyal servants"]));
            Assert.Equal("#4a8cff", Highlights.Hex(terms["Merlin"]));
            Assert.Equal("#ff8020", Highlights.Hex(terms["Assassin"]));
            Assert.Equal("#3fae5a", Highlights.Hex(terms["Approve"]));
            Assert.Equal(SeatLayout.Rgb("red"), terms["Reject"]);
            Assert.Equal("#ffd040", Highlights.Hex(terms["Excalibur"]));
            // A role without a name of its own and a team colour still reads in its team's colour.
            Assert.True(terms.ContainsKey("Servant"));
        }

        [Fact]
        public void ALine_ColoursWholeWordsOnly_AndStripGivesItBack()
        {
            var state = State();
            const string line = "Team vote: Red Reject, Ana Approve. Fred and Reddish are not seats.";
            var coloured = Highlights.Colorize(state, line);
            Assert.Contains(">Red</color> <color=", coloured);
            Assert.Contains(">Reject</color>", coloured);
            Assert.Contains(">Ana</color>", coloured);
            Assert.Contains(">Approve</color>", coloured);
            Assert.Contains("Fred and Reddish", coloured);
            Assert.Equal(line, Highlights.Strip(coloured));
        }

        [Fact]
        public void TheLongestName_Wins_AndTagsInTheText_AreShownAsWritten()
        {
            var state = State();
            // "Loyal servants" is one term, not "Servant" inside it; a "<" in a game's text never starts a tag.
            var coloured = Highlights.Colorize(state, "Loyal servants win <b>now</b>");
            Assert.StartsWith("<color=#", coloured);
            Assert.Contains(">Loyal servants</color>", coloured);
            Assert.Contains("<noparse> win <b>now</b></noparse>", coloured);
            Assert.Equal("Loyal servants win <b>now</b>", Highlights.Strip(coloured));
        }

        [Fact]
        public void Colours_StayReadable_OnDarkAndOnLightPanels()
        {
            var state = State();
            // Seat c is a dark green: lightened on a dark panel, kept on a light one.
            var dark = Highlights.Readable(Highlights.Terms(state)[Names.Of(state, state.Players.Seats[2])], false);
            Assert.True(Highlights.Luminance(dark) >= Highlights.MinOnDark - 0.001f);
            Assert.True(dark[1] > dark[0], "it stays green");
            var white = Highlights.Readable(new[] { 0.95f, 0.95f, 0.95f }, true);
            Assert.True(Highlights.Luminance(white) <= Highlights.MaxOnLight + 0.001f);
        }

        [Fact]
        public void Translations_AreColouredInTheSessionsLanguage()
        {
            var json = Json.Replace(@"""game"": { ""id"": ""h"",", @"""game"": { ""id"": ""h"", ""language"": ""en"", ""languages"": [ ""en"", ""es"" ],")
                .Replace(@"""actions"": [ { ""id"": ""team_vote""", @"""translations"": { ""es"": { ""votes.team_vote.reject"": ""Rechazar"", ""roles.merlin.name"": ""Merlín"" } },
  ""actions"": [ { ""id"": ""team_vote""");
            var game = new GameEngine(State(json), 1);
            Assert.True(game.Execute(new StartGameCommand { Language = "es" }).Ok);
            var terms = Highlights.Terms(game.State);
            Assert.True(terms.ContainsKey("Rechazar"));
            Assert.True(terms.ContainsKey("Merlín"));
            Assert.False(terms.ContainsKey("Reject"));
        }

        [Fact]
        public void ABadColour_IsAValidationError()
        {
            var bad = TableJson.Parse(Json.Replace(@"""color"": ""#4a8cff""", @"""color"": ""sky""").Replace(@"""reject"": ""red"" }", @"""maybe"": ""red"" }")).State!;
            var errors = TableValidator.Validate(bad);
            Assert.Contains(errors, e => e.Path == "roles.teams[0].color");
            Assert.Contains(errors, e => e.Path.EndsWith("vote.colors.maybe"));
        }
    }
}
