using System.Linq;
using Core.Engine;
using Core.Rules;

namespace Core.Tests.Engine
{
    /// <summary>Dice zones: the dice thrown in a zone are totalled, per owner, and rules read the totals.</summary>
    public class DiceZoneTests
    {
        private const string Json = @"{
  ""objects"": [
    { ""id"": ""red1"", ""type"": ""Die"", ""value"": 5, ""tags"": [ ""owner:a"" ], ""transform"": { ""position"": [0, 0, 0] } },
    { ""id"": ""red2"", ""type"": ""Die"", ""value"": 3, ""tags"": [ ""owner:a"" ], ""transform"": { ""position"": [0.03, 0, 0] } },
    { ""id"": ""blue1"", ""type"": ""Die"", ""value"": 6, ""tags"": [ ""owner:b"" ], ""transform"": { ""position"": [-0.03, 0, 0] } },
    { ""id"": ""loose"", ""type"": ""Die"", ""value"": 2, ""transform"": { ""position"": [0, 0, 0.03] } },
    { ""id"": ""outside"", ""type"": ""Die"", ""value"": 4, ""tags"": [ ""owner:b"" ], ""transform"": { ""position"": [0.6, 0, 0.4] } }
  ],
  ""zones"": [ { ""id"": ""arena"", ""type"": ""ScriptingZone"", ""diceTotal"": true, ""transform"": { ""position"": [0, 0.05, 0] }, ""size"": [0.2, 0.1, 0.2] } ],
  ""players"": { ""seats"": [ { ""id"": ""a"", ""color"": ""red"" }, { ""id"": ""b"", ""color"": ""blue"" } ] },
  ""variables"": { ""sum"": 0, ""mine"": 0 },
  ""turn"": { ""phases"": [ ""fight"" ] },
  ""phases"": [ { ""id"": ""fight"", ""actions"": [ ""count"" ], ""advanceWhen"": { ""currentPlayerEnds"": true } } ],
  ""actions"": [ { ""id"": ""count"", ""actor"": ""current"", ""effects"": [
    { ""setVariable"": { ""name"": ""sum"", ""value"": ""$diceTotal.arena"" } },
    { ""setVariable"": { ""name"": ""mine"", ""value"": ""$diceTotal.arena.actor"" } },
    { ""if"": { ""condition"": { ""compare"": { ""left"": ""$diceTotal.arena.a"", ""op"": "">"", ""right"": ""$diceTotal.arena.b"" } },
              ""then"": [ { ""message"": { ""text"": ""red wins the duel"" } } ] } } ] } ]
}";

        [Fact]
        public void TheDiceInAZone_AreTotalled_PerOwner_AndRulesReadThem()
        {
            var parsed = TableJson.Parse(Json);
            Assert.True(parsed.Ok, string.Join("\n", parsed.Errors));
            Assert.Empty(TableValidator.Validate(parsed.State!));
            var game = new GameEngine(parsed.State!, 1);
            var (total, dice, bySeat) = game.Table.DiceIn("arena");
            Assert.Equal(16, total);
            Assert.Equal(4, dice);
            Assert.Equal(8, bySeat["a"]);
            Assert.Equal(6, bySeat["b"]);

            Assert.True(game.Execute(new StartGameCommand()).Ok);
            Assert.True(game.Execute(new PerformActionCommand { Actor = "a", ActionId = "count" }).Ok);
            Assert.Equal(16, (int)game.Session!.Variables["sum"]);
            Assert.Equal(8, (int)game.Session.Variables["mine"]);
            Assert.Contains("red wins the duel", game.Session.Log);
        }

        [Fact]
        public void SeatPlaces_PutPiecesInFrontOfTheHand()
        {
            var (_, places) = SeatLayout.Arrange(4);
            var south = SeatLayout.InFrontOf(places[0], 0f);
            Assert.True(south.Z > places[0].Position.Z, "towards the centre from the south edge");
            var east = SeatLayout.InFrontOf(places[2], 0f);
            Assert.True(east.X < places[2].Position.X, "towards the centre from the east edge");
            Assert.Equal(new[] { 0.86f, 0.2f, 0.18f }, SeatLayout.Rgb("Red"));
        }
    }
}
