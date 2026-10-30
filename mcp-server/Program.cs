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
// This server's journal (docs/dev/logging.md): every tool call, and the commands of a game it runs without a window.
var journal = Core.Logging.Journal.Start(McpLogs.Folder(), file != null ? System.IO.Path.GetFileNameWithoutExtension(file) : "agent", Core.Logging.Journal.McpPrefix);
journal.Write("server", new Newtonsoft.Json.Linq.JObject { ["version"] = Core.Version.Get(), ["file"] = file, ["games"] = games, ["bridge"] = withBridge });
builder.Services.AddSingleton(new McpJournal(journal));
var recorder = new Core.Logging.JournalRecorder(journal);
if (!withBridge)
    builder.Services.AddSingleton(new GameSession(file) { GamesFolder = games, Recorder = recorder });
else
{
    builder.Services.AddSingleton<UnityBridge>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<UnityBridge>());
    builder.Services.AddSingleton(sp =>
    {
        var session = new GameSession(file) { Bridge = sp.GetRequiredService<UnityBridge>(), GamesFolder = games, Recorder = recorder };
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
    .WithRequestFilters(filters => filters.AddCallToolFilter(next => McpLogs.Record(journal, next)))
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
