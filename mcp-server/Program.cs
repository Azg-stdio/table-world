using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TableWorld.Mcp;

var builder = Host.CreateApplicationBuilder(args);

// stdout carries the MCP protocol; every log line goes to stderr.
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

var file = Arguments.Value(args, "--file");
builder.Services.AddSingleton(new GameSession(file));
builder.Services
    .AddMcpServer(options => options.ServerInfo = new() { Name = "table-world", Version = Core.Version.Get() })
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();

namespace TableWorld.Mcp
{
    internal static class Arguments
    {
        public static string? Value(string[] args, string name)
        {
            var i = System.Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
