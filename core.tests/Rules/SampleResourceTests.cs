using System.IO;
using System.Runtime.CompilerServices;
using Core.Rules;

namespace Core.Tests.Rules
{
    /// <summary>Tables shipped as Unity resources must stay valid for the current core.</summary>
    public class SampleResourceTests
    {
        [Theory]
        [InlineData("sample.json")]
        public void UnityTableResource_IsValid(string file)
        {
            var path = Path.Combine(RepoRoot(), "unity", "Assets", "_Project", "Resources", "Tables", file);

            var result = TableJson.Parse(File.ReadAllText(path));

            Assert.True(result.Ok, string.Join("\n", result.Errors));
        }

        private static string RepoRoot([CallerFilePath] string thisFile = "") =>
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
    }
}
