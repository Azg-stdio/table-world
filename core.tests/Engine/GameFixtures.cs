using System.IO;
using System.Runtime.CompilerServices;
using Core.Engine;
using Core.Rules;

namespace Core.Tests.Engine
{
    internal static class GameFixtures
    {
        public static string RepoRoot([CallerFilePath] string thisFile = "") =>
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

        public static string GameJson(string name) => File.ReadAllText(Path.Combine(RepoRoot(), "games", name + ".json"));

        public static GameEngine Load(string name, ulong seed = 1)
        {
            var parsed = TableJson.Parse(GameJson(name));
            Assert.True(parsed.Ok, string.Join("\n", parsed.Errors));
            return new GameEngine(parsed.State!, seed);
        }
    }
}
