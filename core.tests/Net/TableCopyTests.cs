using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Core.Rules;
using Core.Tts;
using Xunit;
using Xunit.Abstractions;

namespace Core.Tests.Net
{
    /// <summary>T3.5: the sync copies whole tables on every rebuild; <see cref="TableJson.Copy"/> must equal a parse.</summary>
    public class TableCopyTests
    {
        private readonly ITestOutputHelper _output;

        public TableCopyTests(ITestOutputHelper output) => _output = output;

        [Theory]
        [InlineData("war.json")]
        [InlineData("uno-lite.json")]
        public void Copy_GivesTheSameTable(string game)
        {
            var state = TableJson.Parse(File.ReadAllText(Path.Combine(Root(), "games", game))).State!;
            var copy = TableJson.Copy(state);
            Assert.NotSame(state, copy);
            Assert.Equal(TableJson.Hash(state), TableJson.Hash(copy));
            Assert.NotSame(state.Objects[0], copy.Objects[0]);
        }

        /// <summary>
        /// The biggest local Tabletop Simulator save (TW_TTS_DIR; TW_MOD picks another): the copy equals a parse, and
        /// the times of each way are printed. Without TW_TTS_DIR the test does nothing.
        /// </summary>
        [Fact]
        public void LocalMod_CopyMatchesParse_AndIsFaster()
        {
            var root = Environment.GetEnvironmentVariable("TW_TTS_DIR");
            if (string.IsNullOrEmpty(root))
                return;
            var mod = Environment.GetEnvironmentVariable("TW_MOD");
            var saves = TtsPackage.Find(root);
            var save = string.IsNullOrEmpty(mod)
                ? saves.OrderByDescending(s => new FileInfo(s.File).Length).First()
                : saves.First(s => s.Name.StartsWith(mod, StringComparison.OrdinalIgnoreCase));
            var state = TtsImporter.Import(File.ReadAllText(save.File)).State;

            var hash = TableJson.Hash(state);
            Assert.Equal(hash, TableJson.Hash(TableJson.Copy(state)));
            Assert.Equal(hash, TableJson.Hash(TableJson.Parse(TableJson.Serialize(state, indented: false)).State!));

            double Time(Action action)
            {
                action();
                var watch = Stopwatch.StartNew();
                for (var i = 0; i < 3; i++)
                    action();
                return watch.Elapsed.TotalMilliseconds / 3;
            }
            var parse = Time(() => TableJson.Parse(TableJson.Serialize(state, indented: false)));
            var copy = Time(() => TableJson.Copy(state));
            var hashing = Time(() => TableJson.Hash(state));
            _output.WriteLine($"{save.Name}: {state.Objects.Count} objects; serialize + parse {parse:F0} ms, copy {copy:F0} ms, hash {hashing:F0} ms");
            Assert.True(copy < parse, $"copy {copy:F0} ms is not faster than a parse ({parse:F0} ms)");
        }

        private static string Root()
        {
            var folder = new DirectoryInfo(AppContext.BaseDirectory);
            while (folder != null && !File.Exists(Path.Combine(folder.FullName, "TableWorld.sln")))
                folder = folder.Parent;
            return folder?.FullName ?? throw new InvalidOperationException("TableWorld.sln not found above the test folder.");
        }
    }
}
