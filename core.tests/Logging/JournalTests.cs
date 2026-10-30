using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Core.Engine;
using Core.Logging;
using Core.Rules;
using Newtonsoft.Json.Linq;

namespace Core.Tests.Logging
{
    /// <summary>
    /// The session journal (docs/dev/logging.md): one JSON line per event, kept on disk as it happens, readable back by
    /// kind and position, pruned to the newest sessions, capped in size, bundled for a problem report; and the recorder
    /// that turns a game's commands into its story.
    /// </summary>
    public class JournalTests : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "tw-journal-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_folder))
                Directory.Delete(_folder, recursive: true);
        }

        [Fact]
        public void Lines_AreWritten_AsTheyHappen_AndReadBackByKindAndPosition()
        {
            using var journal = Journal.Start(_folder, "Love Letter");
            Assert.EndsWith("_love-letter.jsonl", journal.Path);
            journal.Text("unity", "a warning");
            journal.Write("command", new JObject { ["command"] = "Flip", ["missing"] = null }, "seat_1");
            journal.Text("rules", "Red plays the Priest.");

            // On disk already (no closing needed), one object per line.
            var lines = Journal.ReadFile(journal.Path!);
            Assert.Equal(new long[] { 1, 2, 3 }, lines.Select(l => (long)l["seq"]!));
            Assert.Equal("seat_1", (string?)lines[1]["seat"]);
            Assert.Null(lines[1]["missing"]);
            Assert.Equal(new[] { "Red plays the Priest." }, Journal.ReadFile(journal.Path!, after: 1, kinds: new[] { "rules" }).Select(l => (string?)l["text"]));
            Assert.Equal(2, journal.Read(max: 2).Count);
            Assert.Equal(3L, (long)journal.Read(max: 1).Single()["seq"]!);
        }

        [Fact]
        public void OnlyTheNewestJournals_AreKept()
        {
            for (var i = 0; i < Journal.KeepFiles + 5; i++)
                using (var journal = Journal.Start(_folder, "game " + i))
                    journal.Text("table", "started");
            Assert.Equal(Journal.KeepFiles, Journal.Files(_folder).Count);
            using (var mcp = Journal.Start(_folder, "agent", Journal.McpPrefix))
                mcp.Text("tool", "new_game");
            Assert.Equal(Journal.KeepFiles, Journal.Files(_folder).Count);
            Assert.Single(Journal.Files(_folder, Journal.McpPrefix));
        }

        [Fact]
        public void AJournal_StopsGrowing_AtItsCap_SayingSo()
        {
            using var journal = Journal.Start(_folder, "loop");
            var big = new string('x', 64 * 1024);
            for (var i = 0; i < Journal.MaxBytes / big.Length + 10; i++)
                journal.Text("unity", big);
            Assert.InRange(new FileInfo(journal.Path!).Length, 0, Journal.MaxBytes + 1024);
            Assert.Equal("journal", (string?)Journal.ReadFile(journal.Path!, max: 1).Single()["kind"]);
        }

        [Fact]
        public void AProblemReport_ZipsTheNewestJournals_AndExtraFiles()
        {
            using (var first = Journal.Start(_folder, "first"))
                first.Text("table", "one");
            System.Threading.Thread.Sleep(20);
            using var second = Journal.Start(_folder, "second");
            second.Text("table", "two");
            var extra = Path.Combine(_folder, "Player.log");
            File.WriteAllText(extra, "unity log");
            var zip = Journal.Bundle(_folder, Path.Combine(_folder, "Reports", "report.zip"), journals: 1, extra: new[] { extra });
            using var archive = ZipFile.OpenRead(zip);
            Assert.Equal(new[] { Path.GetFileName(second.Path!), "Player.log" }.OrderBy(n => n, StringComparer.Ordinal), archive.Entries.Select(e => e.Name).OrderBy(n => n, StringComparer.Ordinal));
        }

        private const string Game = @"{
  ""assets"": [ { ""id"": ""sheet"", ""type"": ""image"", ""path"": ""builtin:standard_deck"", ""columns"": 10, ""rows"": 7 },
                { ""id"": ""back"", ""type"": ""image"", ""path"": ""builtin:standard_back"" } ],
  ""players"": { ""seats"": [ { ""id"": ""s1"", ""color"": ""red"" }, { ""id"": ""s2"", ""color"": ""blue"" } ] },
  ""turn"": { ""phases"": [ ""main"", ""aim"" ] },
  ""phases"": [ { ""id"": ""main"", ""actions"": [ ""pass"", ""aimAt"", ""whisper"" ] }, { ""id"": ""aim"", ""actions"": [ ""pick"" ] } ],
  ""actions"": [
    { ""id"": ""pass"", ""name"": ""Pass"", ""actor"": ""current"", ""effects"": [ { ""message"": { ""text"": ""{$actor.name} passes."" } }, { ""endTurn"": {} } ] },
    { ""id"": ""whisper"", ""actor"": ""current"", ""effects"": [ { ""message"": { ""text"": ""Only for blue."", ""to"": ""s2"" } } ] },
    { ""id"": ""aimAt"", ""actor"": ""current"", ""effects"": [ { ""gotoPhase"": { ""phase"": ""aim"" } } ] },
    { ""id"": ""pick"", ""name"": ""Pick"", ""actor"": ""current"", ""auto"": true, ""params"": [ { ""name"": ""target"", ""type"": ""seat"" } ],
      ""preconditions"": [ { ""check"": { ""condition"": { ""compare"": { ""left"": ""$param.target.id"", ""op"": ""!="", ""right"": ""$actor.id"" } }, ""reason"": ""Not yourself."" } } ],
      ""effects"": [ { ""endTurn"": {} } ] } ]
}";

        [Fact]
        public void TheRecorder_TellsTheGamesStory()
        {
            var game = new GameEngine(TableJson.Parse(Game).State!, 1);
            var journal = new Journal(null);
            var recorder = new JournalRecorder(journal);
            recorder.Table(game.State, "games/test.json", "host", "s1");
            void Run(Command command, string? viewer = "s1") => recorder.Record(game.State, command, game.Execute(command), viewer);

            Run(new StartGameCommand());
            Run(new PerformActionCommand { Actor = "s1", ActionId = "pass" });
            Run(new PerformActionCommand { Actor = "s1", ActionId = "pass" });
            Run(new PerformActionCommand { Actor = "s2", ActionId = "whisper" });
            Run(new PerformActionCommand { Actor = "s2", ActionId = "aimAt" });

            var lines = journal.Read(max: 100);
            string Kinds() => string.Join(",", lines.Select(l => (string?)l["kind"]));
            Assert.Equal("table,command,turn,command,rules,turn,refused,command,command,auto,turn", Kinds());
            var refused = lines.Single(l => (string?)l["kind"] == "refused");
            Assert.Equal("s1", (string?)refused["seat"]);
            Assert.Equal(ErrorCodes.NotYourTurn, (string?)refused["code"]);
            Assert.Equal("Pass", (string?)refused["action"]);
            Assert.Equal("Red passes.", (string?)lines.Single(l => (string?)l["kind"] == "rules")["text"]);
            // The line only blue reads is not in red's journal; the move the rules made for blue is.
            Assert.DoesNotContain(lines, l => (string?)l["kind"] == "private");
            var auto = lines.Single(l => (string?)l["kind"] == "auto");
            Assert.Equal("s2", (string?)auto["seat"]);
            Assert.Equal("s1", (string?)auto["args"]!["target"]);
            Assert.Equal("s1", (string?)lines.Last()["current"]);

            // A host machine (no seat of its own in the story) writes every private line.
            var hostJournal = new Journal(null);
            var host = new JournalRecorder(hostJournal);
            game = new GameEngine(TableJson.Parse(Game).State!, 1);
            host.Record(game.State, new StartGameCommand(), game.Execute(new StartGameCommand()), null);
            host.Record(game.State, new PerformActionCommand { Actor = "s1", ActionId = "whisper" }, game.Execute(new PerformActionCommand { Actor = "s1", ActionId = "whisper" }), null);
            Assert.Equal("Only for blue.", (string?)hostJournal.Read(kinds: new[] { "private" }).Single()["text"]);
        }
    }
}
