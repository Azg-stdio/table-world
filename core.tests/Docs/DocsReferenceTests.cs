using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Core.Rules;
using Core.Scripting;
using Core.Tests.Engine;
using Core.Tests.Rules;
using Newtonsoft.Json.Linq;

namespace Core.Tests.Docs
{
    /// <summary>
    /// M8 T9.6: the generated pages of <c>docs/reference/</c> that come from core (error codes, Lua API, rules format).
    /// <c>TW_UPDATE_DOCS=1</c> rewrites them; otherwise a page that no longer matches the code fails the test.
    /// </summary>
    public class DocsReferenceTests
    {
        private static void Check(string name, string expected)
        {
            var page = Path.Combine(GameFixtures.RepoRoot(), "docs", "reference", name);
            if (Environment.GetEnvironmentVariable("TW_UPDATE_DOCS") == "1")
                File.WriteAllText(page, expected);
            Assert.True(File.Exists(page), $"docs/reference/{name} is missing: run TW_UPDATE_DOCS=1 dotnet test core.tests --filter DocsReference");
            Assert.True(File.ReadAllText(page).Replace("\r\n", "\n") == expected,
                $"docs/reference/{name} is stale: rerun with TW_UPDATE_DOCS=1 and commit it.");
        }

        private static string Header(string title, string source) =>
            $"# {title}\n\n> Generated from {source} by `core.tests/Docs/DocsReferenceTests.cs`. Do not edit: run\n" +
            "> `TW_UPDATE_DOCS=1 dotnet test core.tests --filter DocsReference`.\n";

        [Fact]
        public void ErrorCodesPage_MatchesTheCodes()
        {
            var text = new StringBuilder(Header("Error codes", "`ErrorCodes` in `core/Rules/ValidationError.cs`"));
            text.Append("\nEvery refusal from a command, the validator or an MCP tool carries one of these codes. They never change\n");
            text.Append("name: agents and tests match on them.\n\n| Code | Constant |\n|---|---|\n");
            foreach (var field in typeof(ErrorCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
                         .Where(f => f.IsLiteral && f.FieldType == typeof(string))
                         .OrderBy(f => (string)f.GetRawConstantValue()!, StringComparer.Ordinal))
                text.Append($"| `{field.GetRawConstantValue()}` | `{field.Name}` |\n");
            Check("error-codes.md", text.ToString());
        }

        [Fact]
        public void LuaApiPage_MatchesTheScriptHost()
        {
            // Globals as a script sees them: functions, and tables with their functions.
            var walk = @"
                local std = { string = 1, table = 1, math = 1, coroutine = 1, bit32 = 1, os = 1, _G = 1, _VERSION = 1 }
                local out = {}
                for name, value in pairs(_G) do
                    if not std[name] and not string.find(name, '^_') then
                        local kind = type(value)
                        if kind == 'function' then
                            table.insert(out, name .. '|function|')
                        elseif kind == 'table' then
                            local members = {}
                            for member, v in pairs(value) do
                                if type(member) == 'string' and type(v) == 'function' then table.insert(members, member) end
                            end
                            table.sort(members)
                            table.insert(out, name .. '|table|' .. table.concat(members, ' '))
                        end
                    end
                end
                table.sort(out)
                broadcastToAll(table.concat(out, '\n'))
                local o = getObjectFromGUID('d6')
                local present = {}
                for _, name in ipairs(probe) do
                    local v = o[name]
                    if v ~= nil then table.insert(present, name .. '|' .. type(v)) end
                end
                broadcastToAll(table.concat(present, '\n'))
                local zone = getObjectFromGUID('hand_1')
                local zoneMembers = {}
                for name, v in pairs(zone) do
                    if type(name) == 'string' then table.insert(zoneMembers, name .. '|' .. type(v)) end
                end
                table.sort(zoneMembers)
                broadcastToAll(table.concat(zoneMembers, '\n'))";
            // Object members are made on first use, so they are probed by the names ScriptApi handles.
            var source = File.ReadAllText(Path.Combine(GameFixtures.RepoRoot(), "core", "Scripting", "ScriptApi.cs"));
            var names = Regex.Matches(source, "case \"([A-Za-z_][A-Za-z0-9_]*)\"").Select(m => m.Groups[1].Value)
                .Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
            var probe = "probe = {" + string.Join(", ", names.Select(n => $"'{n}'")) + "}\n";
            var engine = EngineFixture.Create();
            engine.State.Scripts[ScriptHost.GlobalName] = probe + walk;
            var said = new List<string>();
            var log = new List<string>();
            var host = new ScriptHost(() => engine, c => engine.Execute(c));
            host.Broadcast += said.Add;
            host.Log += log.Add;
            host.Load(0);
            Assert.True(said.Count == 3, string.Join("\n", log));

            var text = new StringBuilder(Header("Lua API", "a running script host (globals) and `core/Scripting/ScriptApi.cs` (object members, each checked on a real object)"));
            text.Append("\nWhat table scripts can call (M5, ADR 0003). Scripts run on the host only, in a sandbox without files, OS access\n");
            text.Append("or code loading, with a step budget per call. The Lua standard library (`string`, `table`, `math`, `coroutine`)\n");
            text.Append("is there without `io`, `os.execute`, `load` and `dofile`. Names follow Tabletop Simulator's; how much of TTS's\n");
            text.Append("API the local mods' calls find is measured in [`docs/data/lua-coverage-*.md`](../data/).\n");
            text.Append("\n## Globals\n\n| Name | Kind | Functions |\n|---|---|---|\n");
            foreach (var line in said[0].Split('\n').Where(l => l.Length > 0))
            {
                var parts = line.Split('|');
                var members = parts[2].Length == 0 ? "" : string.Join(", ", parts[2].Split(' ').Select(m => $"`{m}`"));
                text.Append($"| `{parts[0]}` | {parts[1]} | {members} |\n");
            }
            text.Append("\n## Object members\n\nOn every object a script gets (`getObjectFromGUID`, `self`, event arguments). Functions are called\n");
            text.Append("`obj.name(...)` or `obj:name(...)`; values are read as `obj.name`.\n\n| Member | Kind |\n|---|---|\n");
            foreach (var line in said[1].Split('\n').Where(l => l.Length > 0))
            {
                var parts = line.Split('|');
                text.Append($"| `{parts[0]}` | {(parts[1] == "function" ? "function" : "value")} |\n");
            }
            text.Append("\n## Zone members\n\nOn scripting zones, hand zones and the other zones.\n\n| Member | Kind |\n|---|---|\n");
            foreach (var line in said[2].Split('\n').Where(l => l.Length > 0))
            {
                var parts = line.Split('|');
                text.Append($"| `{parts[0]}` | {(parts[1] == "function" ? "function" : "value")} |\n");
            }
            Check("lua-api.md", text.ToString());
        }

        [Fact]
        public void RulesFormatPage_MatchesTheSchema()
        {
            var schema = JObject.Parse(File.ReadAllText(Path.Combine(GameFixtures.RepoRoot(), "docs", "rules-schema.v0.json")));
            var text = new StringBuilder(Header("Rules format", "`docs/rules-schema.v0.json`"));
            text.Append("\nThe JSON document that holds a table and its rules. Every type of the schema, with its fields. For the ideas\n");
            text.Append("behind it (phases, actions, effects, conditions) read [rules-schema.md](../rules-schema.md).\n");
            var defs = (JObject)schema["$defs"]!;
            text.Append("\n## Types\n\n");
            text.Append(string.Join(" · ", defs.Properties().Select(p => $"[{p.Name}](#{p.Name.ToLowerInvariant()})"))).Append('\n');
            foreach (var def in defs.Properties())
            {
                text.Append($"\n### {def.Name}\n\n");
                var value = (JObject)def.Value;
                if (value["properties"] is JObject properties)
                {
                    var required = new HashSet<string>((value["required"] as JArray)?.Select(r => (string)r!) ?? Enumerable.Empty<string>());
                    text.Append("| Field | Type | Required |\n|---|---|---|\n");
                    foreach (var property in properties.Properties())
                        text.Append($"| `{property.Name}` | {TypeOf(property.Value)} | {(required.Contains(property.Name) ? "yes" : "")} |\n");
                }
                else
                    text.Append($"{TypeOf(value)}\n");
            }
            Check("rules-format.md", text.ToString());
        }

        private static string TypeOf(JToken token)
        {
            if (token["$ref"] is JValue reference)
            {
                var name = ((string)reference!).Split('/').Last();
                return $"[{name}](#{name.ToLowerInvariant()})";
            }
            if (token["enum"] is JArray values)
                return "one of " + string.Join(", ", values.Select(v => $"`{v}`"));
            if (token["oneOf"] is JArray options || token["anyOf"] is JArray)
                return string.Join(" or ", ((JArray)(token["oneOf"] ?? token["anyOf"])!).Select(TypeOf));
            var type = token["type"];
            if (type is JArray types)
                return string.Join(" or ", types.Select(t => (string)t!));
            var kind = (string?)type ?? "any";
            if (kind == "array" && token["items"] is JToken items)
                return $"array of {TypeOf(items)}";
            if (kind == "object" && token["additionalProperties"] is JObject map)
                return $"map of {TypeOf(map)}";
            return kind;
        }
    }
}
