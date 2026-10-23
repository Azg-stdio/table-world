using System.Linq;
using Core.Engine;
using Core.Rules;

namespace Core.Tests.Engine
{
    public class GameSmokeTests
    {
        [Theory]
        [InlineData("war")]
        [InlineData("uno-lite")]
        public void GameFile_IsValid_AndRoundTrips(string name)
        {
            var parsed = TableJson.Parse(GameFixtures.GameJson(name));
            Assert.True(parsed.Ok, string.Join("\n", parsed.Errors));
            var json = TableJson.Serialize(parsed.State!);
            var again = TableJson.Parse(json);
            Assert.True(again.Ok, string.Join("\n", again.Errors));
            Assert.Equal(json, TableJson.Serialize(again.State!));
        }

        [Fact]
        public void War_Start_DealsTwentySixEach()
        {
            var game = GameFixtures.Load("war");
            var start = game.Execute(new StartGameCommand());
            Assert.True(start.Ok, start.Error?.ToString());
            Assert.Equal(26, ((Deck)game.Table.Find("deck_south")!).Cards.Count);
            Assert.Equal(26, ((Deck)game.Table.Find("deck_north")!).Cards.Count);
            Assert.Equal("battle", game.Session!.Phase);
        }
    }
}
