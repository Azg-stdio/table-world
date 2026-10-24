using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Core.Engine;
using Core.Rules;
using Core.Scripting;
using Core.Tts;
using Newtonsoft.Json.Linq;
using Xunit.Abstractions;

namespace Core.Tests.Scripting
{
    /// <summary>
    /// T6.4: runs the scripts of the Tabletop Simulator mods on this machine in the sandbox (global and object scripts,
    /// their onLoad, and a few seconds of timers) and reports the errors by kind. The mods never enter the repository:
    /// set <c>TW_TTS_DIR</c> to the Tabletop Simulator folder to run it; without it the test does nothing.
    /// The table is what the importer (T8.1) makes of the save, without its images and models (not needed by scripts).
    /// </summary>
    public class LocalModScriptsTests
    {
        private readonly ITestOutputHelper _output;

        public LocalModScriptsTests(ITestOutputHelper output) => _output = output;

        [Fact]
        public void LocalMods_RunInTheSandbox()
        {
            var root = Environment.GetEnvironmentVariable("TW_TTS_DIR");
            if (string.IsNullOrEmpty(root))
                return;
            var workshop = Path.Combine(root, "Mods", "Workshop");
            var report = new List<string>();
            foreach (var file in Directory.GetFiles(workshop, "*.json"))
            {
                if (!(JToken.Parse(File.ReadAllText(file)) is JObject save) || save["ObjectStates"] == null)
                    continue;
                // The table as the importer makes it (T8.1): real decks, bags, zones and their scripts.
                var imported = TtsImporter.Import(save.ToString());
                var state = imported.State;
                var xmlErrors = imported.Notes.Count(n => n.Contains("XmlUI") && n.Contains("does not read"));
                var engine = new TableEngine(state, 1);
                var log = new List<string>();
                var said = 0;
                ScriptHost? host = null;
                // Time in the engine by command, to tell slow table changes from slow Lua.
                var byCommand = new Dictionary<string, (int Count, double Ms)>();
                host = new ScriptHost(() => engine, c =>
                {
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    var r = engine.Execute(c);
                    var name = c.GetType().Name;
                    byCommand.TryGetValue(name, out var seen);
                    byCommand[name] = (seen.Count + 1, seen.Ms + watch.Elapsed.TotalMilliseconds);
                    host!.OnCommand(c, r, null);
                    return r;
                });
                host.Log += log.Add;
                host.Profile = new Dictionary<string, double>();
                host.Broadcast += _ => said++;
                var started = DateTime.UtcNow;
                host.Load(0);
                var loadSeconds = (DateTime.UtcNow - started).TotalSeconds;
                for (var t = 1; t <= 300; t++)
                    host.Tick(t / 60.0);
                var seconds = (DateTime.UtcNow - started).TotalSeconds;
                var problems = log.Where(l => l.Contains(" error: ") || l.Contains(" does not load") || l.Contains(" stopped") || l.Contains(" refused")).ToList();
                var kinds = problems.Select(Kind).GroupBy(k => k).OrderByDescending(g => g.Count()).Take(8)
                    .Select(g => $"{g.Count()}× {g.Key}");
                report.Add($"      proxies made: {host.ProxiesMade}, objects: {state.Objects.Count}");
                report.AddRange(host.Profile.OrderByDescending(p => p.Value).Take(5).Select(p => $"      slowest: {p.Key} {p.Value / 1000:F2} s"));
                if (Environment.GetEnvironmentVariable("TW_REPORT_ALL") is { Length: > 0 })
                    report.AddRange(problems.Select(p => "      " + p));
                report.Add($"{(string?)save["SaveName"] ?? Path.GetFileName(file)}: {host.ScriptCount} scripts, {problems.Count} problems, " +
                           $"{said} chat lines, {host.CommandsIssued} commands, {xmlErrors} unreadable XmlUI, {seconds:F1} s ({loadSeconds:F1} s loading; engine: " +
                           string.Join(", ", byCommand.OrderByDescending(p => p.Value.Ms).Take(3).Select(p => $"{p.Key} {p.Value.Count}× {p.Value.Ms / 1000:F1} s")) +
                           ")\n    " + string.Join("\n    ", kinds));
            }
            foreach (var line in report)
                _output.WriteLine(line);
            if (Environment.GetEnvironmentVariable("TW_REPORT") is { Length: > 0 } path)
                File.WriteAllText(path, string.Join("\n", report));
        }

        private static IEnumerable<JObject> Walk(JArray? objects)
        {
            if (objects == null)
                yield break;
            foreach (var o in objects.OfType<JObject>())
            {
                yield return o;
                foreach (var inner in Walk(o["ContainedObjects"] as JArray))
                    yield return inner;
            }
        }

        /// <summary>An error with its specifics removed, so the same kind counts once.</summary>
        private static string Kind(string line)
        {
            var at = line.IndexOf("] ", StringComparison.Ordinal);
            var text = at >= 0 ? line.Substring(at + 2) : line;
            text = Regex.Replace(text, @"chunk_\d+:\(\d+,\d+(-\d+)?\)", "");
            text = Regex.Replace(text, @"'[0-9a-f]{6}'", "'<guid>'");
            text = Regex.Replace(text, @"\d+", "#");
            return text.Length > 140 ? text.Substring(0, 140) : text;
        }
    }
}
