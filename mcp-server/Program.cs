using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TableWorld.Mcp;

var builder = Host.CreateApplicationBuilder(args);

// stdout carries the MCP protocol; every log line goes to stderr.
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

var file = Arguments.Value(args, "--file");
// --no-bridge: headless only (tests, CI); no game window can attach.
var withBridge = System.Array.IndexOf(args, "--no-bridge") < 0;
if (!withBridge)
    builder.Services.AddSingleton(new GameSession(file));
else
{
    builder.Services.AddSingleton<UnityBridge>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<UnityBridge>());
    builder.Services.AddSingleton(sp =>
    {
        var session = new GameSession(file) { Bridge = sp.GetRequiredService<UnityBridge>() };
        var bridge = session.Bridge;
        bridge.StateReported += session.Report;
        // A game window that (re)connects gets the current table straight away.
        bridge.GameConnected += () => System.Threading.Tasks.Task.Run(() => session.Locked(() => { session.PushToGame(); return 0; }));
        return session;
    });
}
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
