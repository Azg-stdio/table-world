using Core.Engine;
using Core.Rules;

namespace Core.Tests.Engine
{
    /// <summary>The game log keeps its last lines and counts every line, so a view still sees new ones once it is full.</summary>
    public class GameLogTests
    {
        private const string Json = @"{
  ""players"": { ""seats"": [ { ""id"": ""a"", ""color"": ""red"" }, { ""id"": ""b"", ""color"": ""blue"" } ] },
  ""variables"": { ""n"": 0 },
  ""turn"": { ""phases"": [ ""talk"" ] },
  ""phases"": [ { ""id"": ""talk"", ""actions"": [ ""say"" ] } ],
  ""actions"": [ { ""id"": ""say"", ""actor"": ""any"", ""effects"": [
    { ""setVariable"": { ""name"": ""n"", ""delta"": 1 } },
    { ""message"": { ""text"": ""line {$var.n}"" } } ] } ]
}";

        [Fact]
        public void AFullLog_KeepsItsLastLines_AndCountsEveryOne()
        {
            var parsed = TableJson.Parse(Json);
            Assert.True(parsed.Ok, string.Join("\n", parsed.Errors));
            var game = new GameEngine(parsed.State!, 1);
            Assert.True(game.Execute(new StartGameCommand()).Ok);
            var before = game.Session!.LogTotal;
            for (var i = 0; i < GameEngine.MaxLog + 10; i++)
                Assert.True(game.Execute(new PerformActionCommand { Actor = "a", ActionId = "say" }).Ok);
            Assert.Equal(GameEngine.MaxLog, game.Session.Log.Count);
            Assert.Equal(before + GameEngine.MaxLog + 10, game.Session.LogTotal);
            Assert.Equal($"line {GameEngine.MaxLog + 10}", game.Session.Log[^1]);
            // It travels with the session (saves, the host's state to clients).
            var copy = TableJson.Parse(TableJson.Serialize(game.State));
            Assert.Equal(game.Session.LogTotal, copy.State!.Session!.LogTotal);
        }
    }
}
