using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using ModelContextProtocol.Server;
using TableWorld.Mcp;
using Xunit;

namespace McpServer.Tests
{
    /// <summary>
    /// M8 T9.6: <c>docs/reference/mcp-tools.md</c> is generated from the server's tool methods. <c>TW_UPDATE_DOCS=1</c>
    /// rewrites it; otherwise the test fails when the page and the code differ (a tool or parameter added, renamed or
    /// described differently without regenerating).
    /// </summary>
    public class DocsReferenceTests
    {
        [Fact]
        public void McpToolsPage_MatchesTheTools()
        {
            var page = Path.Combine(RepositoryRoot(), "docs", "reference", "mcp-tools.md");
            var expected = Render();
            if (Environment.GetEnvironmentVariable("TW_UPDATE_DOCS") == "1")
                File.WriteAllText(page, expected);
            Assert.True(File.Exists(page), "docs/reference/mcp-tools.md is missing: run TW_UPDATE_DOCS=1 dotnet test mcp-server.tests --filter DocsReference");
            Assert.Equal(expected, File.ReadAllText(page).Replace("\r\n", "\n"));
        }

        private static string Render()
        {
            var text = new StringBuilder();
            text.Append("# MCP tools\n\n");
            text.Append("> Generated from the server's tool methods by `mcp-server.tests/DocsReferenceTests.cs`. Do not edit: run\n");
            text.Append("> `TW_UPDATE_DOCS=1 dotnet test mcp-server.tests --filter DocsReference`.\n\n");
            text.Append("Every tool returns JSON with `ok`, plus `errors` (path, code, message, suggestion) when it refuses, or\n");
            text.Append("`problems` when an edit was kept but the document is still incomplete.\n");
            var types = typeof(GameTools).Assembly.GetTypes()
                .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() != null)
                .OrderBy(t => t.Name, StringComparer.Ordinal);
            foreach (var type in types)
            {
                var tools = type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
                    .Select(m => (Method: m, Tool: m.GetCustomAttribute<McpServerToolAttribute>()))
                    .Where(t => t.Tool != null)
                    .ToList();
                if (tools.Count == 0)
                    continue;
                text.Append($"\n## {type.Name}\n");
                foreach (var (method, tool) in tools.OrderBy(t => t.Tool!.Name, StringComparer.Ordinal))
                {
                    text.Append($"\n### `{tool!.Name}`\n\n");
                    text.Append(Clean(method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "")).Append('\n');
                    var parameters = method.GetParameters().Where(p => p.GetCustomAttribute<DescriptionAttribute>() != null).ToList();
                    if (parameters.Count == 0)
                        continue;
                    text.Append("\n| Parameter | Type | Default | Description |\n|---|---|---|---|\n");
                    foreach (var p in parameters)
                    {
                        var optional = p.HasDefaultValue ? (p.DefaultValue == null ? "none" : $"`{Format(p.DefaultValue)}`") : "required";
                        text.Append($"| `{p.Name}` | {TypeName(p.ParameterType)} | {optional} | {Clean(p.GetCustomAttribute<DescriptionAttribute>()!.Description)} |\n");
                    }
                }
            }
            return text.ToString();
        }

        private static string Clean(string text) => text.Replace("\r", "").Replace("\n", " ").Replace("|", "\\|").Trim();

        private static string Format(object value) => value switch
        {
            bool b => b ? "true" : "false",
            string s => s,
            IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "",
        };

        private static string TypeName(Type type)
        {
            var inner = Nullable.GetUnderlyingType(type) ?? type;
            var name = inner == typeof(string) ? "string" : inner == typeof(int) ? "integer" : inner == typeof(bool) ? "boolean"
                : inner == typeof(float) || inner == typeof(double) ? "number" : inner.Name;
            return name;
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
