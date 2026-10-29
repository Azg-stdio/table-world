using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TableWorld.Mcp;

var builder = Host.CreateApplicationBuilder(args);

// stdout carries the MCP protocol; every log line goes to stderr.
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

var file = Arguments.Value(args, "--file");
// --games names the player's Games folder (tests point it at a temporary one); otherwise the game says where it is.
GamesFolder.Override = Arguments.Value(args, "--games");
// --music names the music library (tests point it at a temporary one); otherwise the player's, beside the game's files.
AtmosphereTools.LibraryFolder = Arguments.Value(args, "--music");
// Without --file, each new game gets its own file in the player's Games folder, so the game window lists it.
var games = file == null ? Arguments.Value(args, "--games") ?? GamesFolder.Find() : null;
// --no-bridge: headless only (tests, CI); no game window can attach.
var withBridge = System.Array.IndexOf(args, "--no-bridge") < 0;
if (!withBridge)
    builder.Services.AddSingleton(new GameSession(file) { GamesFolder = games });
else
{
    builder.Services.AddSingleton<UnityBridge>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<UnityBridge>());
    builder.Services.AddSingleton(sp =>
    {
        var session = new GameSession(file) { Bridge = sp.GetRequiredService<UnityBridge>(), GamesFolder = games };
        var bridge = session.Bridge;
        bridge.StateReported += session.Report;
        bridge.StateChanged += session.MarkChanged;
        // A game window that (re)connects gets the current table straight away.
        bridge.GameConnected += () => System.Threading.Tasks.Task.Run(() => session.Locked(() => { session.PushToGame(); return 0; }));
        return session;
    });
}
builder.Services
    .AddMcpServer(options => options.ServerInfo = new() { Name = "table-world", Version = Core.Version.Get() })
    .WithStdioServerTransport()
    .WithToolsFromAssembly()
    .WithResourcesFromAssembly()
    .WithPromptsFromAssembly();

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
