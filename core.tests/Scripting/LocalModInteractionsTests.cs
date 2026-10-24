using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Core.Engine;
using Core.Rules;
using Core.Scripting;
using Core.Tts;
using Xunit.Abstractions;

namespace Core.Tests.Scripting
{
    /// <summary>
    /// Plays a local TTS mod's interactions like a player (M5/M7): imports it, then presses every script button and every
    /// clickable script UI element in turn as a seated player, letting a few seconds of the scripts' timers run after
    /// each, and reports what each did: errors, chat lines, objects made or removed, whether the table changed.
    /// Needs <c>TW_TTS_DIR</c> (the Tabletop Simulator folder) and <c>TW_MOD</c> (the start of the save's name);
    /// <c>TW_REPORT</c> writes the report to a file. Without them it does nothing.
    /// </summary>
    public class LocalModInteractionsTests
    {
        private readonly ITestOutputHelper _output;

        public LocalModInteractionsTests(ITestOutputHelper output) => _output = output;

        [Fact]
        public void LocalMod_EveryButtonAndUiElement_IsPressed()
        {
            var root = Environment.GetEnvironmentVariable("TW_TTS_DIR");
            var mod = Environment.GetEnvironmentVariable("TW_MOD");
            if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(mod))
                return;
            var save = TtsPackage.Find(root).First(s => s.Name.StartsWith(mod, StringComparison.OrdinalIgnoreCase));
            var import = TtsImporter.Import(File.ReadAllText(save.File));
            var engine = new TableEngine(import.State, 1);
            var color = engine.State.Players.Seats.Select(s => s.Color).FirstOrDefault(c => c == "red") ?? engine.State.Players.Seats.FirstOrDefault()?.Color ?? "white";
            var log = new List<string>();
            var said = new List<string>();
            ScriptHost? host = null;
            var now = 0.0;
            host = new ScriptHost(() => engine, c =>
            {
                var r = engine.Execute(c);
                host!.OnCommand(c, r, null);
                return r;
            });
            host.Log += log.Add;
            host.Broadcast += said.Add;
            host.Load(now);
            void Run(double seconds)
            {
                for (var end = now + seconds; now < end; now += 1.0 / 60)
                    host.Tick(now);
            }
            Run(3);
            var report = new List<string> { $"{save.Name}: {engine.State.Objects.Count} objects, {host.ScriptCount} scripts, playing as {color}; load said {said.Count} lines, {log.Count(Problem)} problems" };
            report.AddRange(log.Where(Problem).Take(10).Select(l => "    load: " + l));

            var pressed = new HashSet<string>();
            for (var pass = 1; pass <= 2; pass++)
            {
                foreach (var (key, label, command) in Interactions(engine).ToList())
                {
                    if (!pressed.Add(key))
                        continue;
                    var logBefore = log.Count;
                    var saidBefore = said.Count;
                    var objectsBefore = engine.State.Objects.Count;
                    var before = TableJson.Hash(engine.State);
                    var result = engine.Execute(command);
                    host.OnCommand(command, result, color);
                    Run(3);
                    var problems = log.Skip(logBefore).Where(Problem).ToList();
                    var lines = said.Skip(saidBefore).ToList();
                    var changed = TableJson.Hash(engine.State) != before;
                    var delta = engine.State.Objects.Count - objectsBefore;
                    report.Add($"  [{(problems.Count > 0 ? "ERR" : result.Ok ? (changed || lines.Count > 0 ? "ok " : "-- ") : "REF")}] {label}" +
                               (result.Ok ? "" : $" refused: {result.Error?.Message}") +
                               (delta != 0 ? $"; {delta:+#;-#} objects" : "") + (changed ? "; table changed" : "") +
                               (lines.Count > 0 ? "; said: " + string.Join(" | ", lines.Take(3)).Replace("\n", " ") : ""));
                    report.AddRange(problems.Distinct().Take(4).Select(p => "        " + p));
                }
            }
            report.Add($"after: {engine.State.Objects.Count} objects; {pressed.Count} interactions pressed");
            foreach (var line in report)
                _output.WriteLine(line);
            if (Environment.GetEnvironmentVariable("TW_REPORT") is { Length: > 0 } path)
                File.WriteAllLines(path, report);
        }

        private static bool Problem(string line) => line.Contains(" error: ") || line.Contains(" does not load") || line.Contains(" stopped") || line.Contains("not supported");

        /// <summary>Every button on an object and every UI element with onClick (global, objects, zones), with a stable key.</summary>
        private static IEnumerable<(string Key, string Label, Command Command)> Interactions(TableEngine engine)
        {
            foreach (var obj in engine.State.Objects.Where(o => o.Container == null))
                for (var i = 0; i < obj.Buttons.Count; i++)
                {
                    var b = obj.Buttons[i];
                    var text = (b.Label.Length > 0 ? b.Label : b.Tooltip ?? b.Click).Replace("\n", " ");
                    yield return ($"button:{obj.Id}:{i}:{b.Click}", $"button '{text}' on {obj.Name ?? obj.Id}", new ClickButtonCommand { ObjectId = obj.Id, Index = i });
                }
            IEnumerable<(List<int> Path, UiNode Node)> Walk(List<UiNode> nodes, List<int> prefix)
            {
                for (var i = 0; i < nodes.Count; i++)
                {
                    var path = new List<int>(prefix) { i };
                    yield return (path, nodes[i]);
                    foreach (var inner in Walk(nodes[i].Children, path))
                        yield return inner;
                }
            }
            var trees = new List<(string? Owner, string Name, List<UiNode> Nodes)> { (null, "screen", engine.State.ScriptUi) };
            trees.AddRange(engine.State.Objects.Where(o => o.Container == null && o.ScriptUi.Count > 0).Select(o => ((string?)o.Id, o.Name ?? o.Id, o.ScriptUi)));
            trees.AddRange(engine.State.Zones.Where(z => z.ScriptUi.Count > 0).Select(z => ((string?)z.Id, z.Name ?? z.Id, z.ScriptUi)));
            foreach (var (owner, name, nodes) in trees)
                foreach (var (path, node) in Walk(nodes, new List<int>()))
                    if (node.Get("onClick") is { } handler)
                        yield return ($"ui:{owner}:{string.Join(".", path)}:{handler}", $"UI {node.Tag} '{node.Id ?? node.Get("text") ?? node.Value ?? handler}' ({name})",
                            new UiEventCommand { ObjectId = owner, Path = path, Value = "-1" });
        }
    }
}
