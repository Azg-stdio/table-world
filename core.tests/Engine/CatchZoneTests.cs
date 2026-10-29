using System.Linq;
using Core.Engine;
using Core.Rules;

namespace Core.Tests.Engine
{
    /// <summary>
    /// Zones that catch pieces (M12): a tagged card a player lets go anywhere but their own hand lands on their spot, its
    /// rules run as if they dropped it there; in their hand it stays; other players' pieces and scripts are left alone.
    /// </summary>
    public class CatchZoneTests
    {
        private const string Json = @"{
  ""players"": { ""seats"": [ { ""id"": ""a"", ""color"": ""red"", ""handZone"": ""hand_a"" }, { ""id"": ""b"", ""color"": ""blue"", ""handZone"": ""hand_b"" } ] },
  ""objects"": [
    { ""id"": ""card_a"", ""type"": ""Card"", ""text"": { ""title"": ""Vote"" }, ""tags"": [ ""vote"" ], ""transform"": { ""position"": [0, 0, -0.4] } },
    { ""id"": ""card_b"", ""type"": ""Card"", ""text"": { ""title"": ""Vote"" }, ""tags"": [ ""vote"" ], ""transform"": { ""position"": [0, 0, 0.4] } },
    { ""id"": ""plain"", ""type"": ""Card"", ""text"": { ""title"": ""Plain"" }, ""transform"": { ""position"": [0.5, 0, 0] } } ],
  ""zones"": [
    { ""id"": ""hand_a"", ""type"": ""HandZone"", ""owner"": ""a"", ""transform"": { ""position"": [0, 0.05, -0.45] }, ""size"": [0.5, 0.1, 0.12] },
    { ""id"": ""hand_b"", ""type"": ""HandZone"", ""owner"": ""b"", ""transform"": { ""position"": [0, 0.05, 0.45] }, ""size"": [0.5, 0.1, 0.12] },
    { ""id"": ""spot_a"", ""type"": ""LayoutZone"", ""facing"": ""faceDown"", ""transform"": { ""position"": [0.2, 0.05, -0.2] }, ""size"": [0.1, 0.1, 0.13],
      ""catch"": { ""tag"": ""vote"", ""for"": ""a"" },
      ""onEnter"": [ { ""setVariable"": { ""name"": ""laid"", ""add"": ""$actor"" } } ] } ],
  ""variables"": { ""laid"": [] },
  ""turn"": { ""phases"": [ ""vote"" ] },
  ""phases"": [ { ""id"": ""vote"", ""actions"": [] } ]
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

        private static CommandResult Move(GameEngine game, string id, string actor, float x, float z, bool byScript = false) =>
            game.Execute(new MoveCommand { ObjectId = id, Actor = actor, Position = new Vec3(x, 0f, z), ByScript = byScript });

        [Fact]
        public void ACaughtCard_LetGoAnywhere_LandsOnItsSpot_AndTheSpotsRulesRun()
        {
            var game = Start();
            Assert.Equal("spot_a", game.CatchingZone("card_a", "a")!.Id);
            Assert.True(Move(game, "card_a", "a", -0.6f, 0.3f).Ok);
            Assert.Contains("spot_a", game.Table.ZonesOf("card_a"));
            Assert.False(((Card)game.Table.Find("card_a")!).IsFaceUp, "the spot lays it face down");
            Assert.Equal(new[] { "a" }, game.Session!.Variables["laid"].ToObject<string[]>());
        }

        [Fact]
        public void InTheHand_ByAnotherPlayer_UntaggedOrByAScript_ItStaysWhereItWasPut()
        {
            var game = Start();
            Assert.True(Move(game, "card_a", "a", 0.1f, -0.45f).Ok);
            Assert.Contains("hand_a", game.Table.ZonesOf("card_a"));
            Assert.Null(game.CatchingZone("card_b", "b"));
            Assert.True(Move(game, "card_b", "b", -0.6f, 0.2f).Ok);
            Assert.Equal(-0.6f, game.Table.Find("card_b")!.Transform.Position.X, 3);
            Assert.True(Move(game, "plain", "a", -0.6f, 0.1f).Ok);
            Assert.DoesNotContain("spot_a", game.Table.ZonesOf("plain"));
            Assert.True(Move(game, "card_a", "a", 0.6f, 0.3f, byScript: true).Ok);
            Assert.Equal(0.6f, game.Table.Find("card_a")!.Transform.Position.X, 3);
        }

        [Fact]
        public void ACatchWithoutATag_IsAValidationError()
        {
            var bad = TableJson.Parse(Json.Replace(@"""tag"": ""vote"", ""for"": ""a""", @"""tag"": """", ""for"": ""a""")).State!;
            Assert.Contains(TableValidator.Validate(bad), e => e.Path.EndsWith("catch.tag"));
        }
    }
}
