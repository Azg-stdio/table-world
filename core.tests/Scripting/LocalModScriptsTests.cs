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
    /// Objects are stand-ins (a token per scripted object, with its GUID, name, script, state and XmlUI) until the
    /// importer (M7) maps real ones, so errors about missing objects or contents are expected.
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
                var state = new TableState();
                state.Players.Seats.Add(new Seat { Id = "white", Color = "white" });
                state.Players.Seats.Add(new Seat { Id = "red", Color = "red" });
                if ((string?)save["LuaScript"] is { Length: > 0 } global)
                    state.Scripts[ScriptHost.GlobalName] = global;
                state.GlobalScriptState = (string?)save["LuaScriptState"];
                var xmlErrors = 0;
                List<UiNode> Ui(string? xml)
                {
                    try
                    {
                        return XmlUi.Parse(xml ?? "");
                    }
                    catch (System.Xml.XmlException)
                    {
                        xmlErrors++;
                        return new List<UiNode>();
                    }
                }
                state.ScriptUi = Ui((string?)save["XmlUI"]);
                var seen = new HashSet<string>();
                foreach (var o in Walk(save["ObjectStates"] as JArray))
                {
                    var guid = (string?)o["GUID"];
                    if (guid == null || !seen.Add(guid))
                        continue;
                    var script = (string?)o["LuaScript"];
                    var xml = (string?)o["XmlUI"];
                    state.Objects.Add(new Token
                    {
                        Id = guid, Name = (string?)o["Nickname"], Description = (string?)o["Description"], GmNotes = (string?)o["GMNotes"],
                        Script = string.IsNullOrWhiteSpace(script) ? null : script, ScriptState = (string?)o["LuaScriptState"],
                        Tags = (o["Tags"] as JArray)?.Select(t => (string)t!).ToList() ?? new List<string>(),
                        ScriptUi = Ui(xml),
                    });
                }
                var engine = new TableEngine(state, 1);
                var log = new List<string>();
                var said = 0;
                ScriptHost? host = null;
                host = new ScriptHost(() => engine, c =>
                {
                    var r = engine.Execute(c);
                    host!.OnCommand(c, r, null);
                    return r;
                });
                host.Log += log.Add;
                host.Broadcast += _ => said++;
                var started = DateTime.UtcNow;
                host.Load(0);
                for (var t = 1; t <= 300; t++)
                    host.Tick(t / 60.0);
                var seconds = (DateTime.UtcNow - started).TotalSeconds;
                var problems = log.Where(l => l.Contains(" error: ") || l.Contains(" does not load") || l.Contains(" stopped") || l.Contains(" refused")).ToList();
                var kinds = problems.Select(Kind).GroupBy(k => k).OrderByDescending(g => g.Count()).Take(8)
                    .Select(g => $"{g.Count()}× {g.Key}");
                if (Environment.GetEnvironmentVariable("TW_REPORT_ALL") is { Length: > 0 })
                    report.AddRange(problems.Select(p => "      " + p));
                report.Add($"{(string?)save["SaveName"] ?? Path.GetFileName(file)}: {host.ScriptCount} scripts, {problems.Count} problems, " +
                           $"{said} chat lines, {host.CommandsIssued} commands, {xmlErrors} unreadable XmlUI, {seconds:F1} s\n    " + string.Join("\n    ", kinds));
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
