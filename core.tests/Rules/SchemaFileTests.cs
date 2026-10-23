using System;
using System.IO;
using Core.Rules;
using Core.Tests.Engine;
using Newtonsoft.Json;

namespace Core.Tests.Rules
{
    public class SchemaFileTests
    {
        /// <summary>docs/rules-schema.v0.json must equal the generated schema. Set TW_UPDATE_SCHEMA=1 to rewrite it.</summary>
        [Fact]
        public void SchemaFile_MatchesTheModel()
        {
            var path = Path.Combine(GameFixtures.RepoRoot(), "docs", "rules-schema.v0.json");
            var generated = SchemaGenerator.Generate().ToString(Formatting.Indented).Replace("\r\n", "\n") + "\n";
            if (Environment.GetEnvironmentVariable("TW_UPDATE_SCHEMA") == "1")
                File.WriteAllText(path, generated);
            Assert.True(File.Exists(path), "Run the tests once with TW_UPDATE_SCHEMA=1 to create docs/rules-schema.v0.json.");
            Assert.True(File.ReadAllText(path).Replace("\r\n", "\n") == generated,
                "docs/rules-schema.v0.json is stale: rerun the tests with TW_UPDATE_SCHEMA=1 and commit it.");
        }
    }
}
