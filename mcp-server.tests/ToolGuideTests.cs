using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace TableWorld.Mcp.Tests
{
    /// <summary>
    /// M8 T9.9: every <c>```call</c> block of <c>docs/user/mcp-tools-guide.md</c> (a tool name, then its arguments as a
    /// JSON object) runs in page order against the real server, and every tool the server lists appears in one. Paths
    /// in the examples are redirected to a temporary folder.
    /// </summary>
    [Collection("server")]
    public class ToolGuideTests : IClassFixture<ServerFixture>
    {
        private static readonly Regex Block = new Regex(@"^```call\r?\n(.*?)\r?\n```", RegexOptions.Multiline | RegexOptions.Singleline);

        private readonly ServerFixture _server;

        public ToolGuideTests(ServerFixture server)
        {
            _server = server;
        }

        [Fact]
        public async Task GuideExamples_RunAgainstTheServer()
        {
            var page = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "user", "mcp-tools-guide.md"));
            var calls = Block.Matches(page).Select(m => Parse(m.Groups[1].Value)).ToList();
            Assert.NotEmpty(calls);

            var tools = (await _server.Client.ListToolsAsync()).Select(t => t.Name);
            var missing = tools.Except(calls.Select(c => c.Tool)).ToList();
            Assert.True(missing.Count == 0, "Tools without an example in the guide: " + string.Join(", ", missing));

            var folder = Path.Combine(Path.GetTempPath(), "tw-tool-guide-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                foreach (var (tool, args) in calls)
                {
                    if (args.TryGetValue("path", out var path) && path is string relative)
                        args["path"] = Path.Combine(folder, Path.GetFileName(relative));
                    // The import example needs a pack: a zip with one short track, as a player's would be.
                    if (tool == "import_music" && args["path"] is string zip && !File.Exists(zip))
                        MusicPack(zip);
                    var reply = await _server.Call(tool, args);
                    // Looking needs a game window, which these tests run without: the example must say so plainly.
                    if (tool == "screenshot" || tool == "preview_music")
                    {
                        Assert.Contains("No game window", (string?)reply["error"] ?? "");
                        continue;
                    }
                    // validate_rules answers ok = false when the document has problems, so this also checks it.
                    Assert.True(reply["ok"] == null || (bool?)reply["ok"] == true, $"{tool} refused the guide's example: {reply}");
                }
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        /// <summary>A zip with two one-second WAV tracks, laid out like a downloaded pack.</summary>
        private static void MusicPack(string zip)
        {
            var samples = 8000;
            var wav = new byte[44 + samples];
            System.Text.Encoding.ASCII.GetBytes("RIFF").CopyTo(wav, 0);
            BitConverter.GetBytes(36 + samples).CopyTo(wav, 4);
            System.Text.Encoding.ASCII.GetBytes("WAVEfmt ").CopyTo(wav, 8);
            BitConverter.GetBytes(16).CopyTo(wav, 16);
            BitConverter.GetBytes((short)1).CopyTo(wav, 20);
            BitConverter.GetBytes((short)1).CopyTo(wav, 22);
            BitConverter.GetBytes(8000).CopyTo(wav, 24);
            BitConverter.GetBytes(8000).CopyTo(wav, 28);
            BitConverter.GetBytes((short)1).CopyTo(wav, 32);
            BitConverter.GetBytes((short)8).CopyTo(wav, 34);
            System.Text.Encoding.ASCII.GetBytes("data").CopyTo(wav, 36);
            BitConverter.GetBytes(samples).CopyTo(wav, 40);
            using var archive = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create);
            foreach (var name in new[] { "Tracks/Ambient 1 Loop.wav", "Tracks/Dark 1.wav" })
            {
                using var stream = archive.CreateEntry(name).Open();
                stream.Write(wav, 0, wav.Length);
            }
        }

        private static (string Tool, Dictionary<string, object?> Args) Parse(string block)
        {
            var text = block.Trim();
            var space = text.IndexOfAny(new[] { ' ', '\n', '\r' });
            var tool = space < 0 ? text : text.Substring(0, space);
            var args = new Dictionary<string, object?>();
            if (space >= 0)
                foreach (var property in JObject.Parse(text.Substring(space)).Properties())
                    args[property.Name] = property.Value.Type switch
                    {
                        JTokenType.Integer => (object?)(long)property.Value,
                        JTokenType.Float => (double)property.Value,
                        JTokenType.Boolean => (bool)property.Value,
                        JTokenType.String => (string?)property.Value,
                        // The tools take JSON documents as strings: an example may write them as plain JSON.
                        _ => property.Value.ToString(Newtonsoft.Json.Formatting.None),
                    };
            return (tool, args);
        }

        private static string RepositoryRoot()
        {
            var folder = new DirectoryInfo(AppContext.BaseDirectory);
            while (folder != null && !File.Exists(Path.Combine(folder.FullName, "TableWorld.sln")))
                folder = folder.Parent;
            return folder?.FullName ?? throw new InvalidOperationException("TableWorld.sln not found above the test folder.");
        }
    }
}
