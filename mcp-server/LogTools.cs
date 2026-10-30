using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Core.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;

namespace TableWorld.Mcp
{
    /// <summary>
    /// The journals an agent can read (docs/dev/logging.md): the game window's, beside the player's Games folder, and
    /// this server's own (<see cref="McpJournal"/>), which records every tool call.
    /// </summary>
    internal static class McpLogs
    {
        /// <summary>
        /// Where journals are: beside a Games folder given with <c>--games</c> (tests), else where the game says
        /// (<c>logsFolder</c> in its game.json), else beside the Games folder it names, else a temporary folder.
        /// </summary>
        public static string Folder()
        {
            if (!string.IsNullOrEmpty(GamesFolder.Override))
                return Beside(GamesFolder.Override!);
            try
            {
                if (File.Exists(GamesFolder.InfoPath) && (string?)JObject.Parse(File.ReadAllText(GamesFolder.InfoPath))["logsFolder"] is { Length: > 0 } folder)
                    return folder;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is Newtonsoft.Json.JsonException)
            {
            }
            return GamesFolder.Known() is { } games ? Beside(games) : Path.Combine(Path.GetTempPath(), "TableWorld", "Logs");
        }

        private static string Beside(string games) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(games).TrimEnd(Path.DirectorySeparatorChar)) ?? Path.GetTempPath(), "Logs");

        /// <summary>A tool's arguments for the journal: long values (a whole game document) cut short.</summary>
        public static JObject Arguments(IDictionary<string, System.Text.Json.JsonElement>? arguments)
        {
            var result = new JObject();
            if (arguments == null)
                return result;
            foreach (var (name, value) in arguments)
            {
                var text = value.ValueKind == System.Text.Json.JsonValueKind.String ? value.GetString() ?? "" : value.GetRawText();
                result[name] = text.Length > 300 ? text.Substring(0, 300) + "…" : text;
            }
            return result;
        }

        /// <summary>
        /// The call-tool filter: each call becomes a <c>tool</c> line (name, arguments, whether it worked, how long it took,
        /// the start of the reply), so a session with an agent can be read back.
        /// </summary>
        public static McpRequestHandler<CallToolRequestParams, CallToolResult> Record(Journal journal, McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
            async (request, cancellation) =>
            {
                var started = Stopwatch.GetTimestamp();
                var entry = new JObject { ["tool"] = request.Params?.Name, ["args"] = Arguments(request.Params?.Arguments) };
                try
                {
                    var result = await next(request, cancellation);
                    var text = result.Content?.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "";
                    entry["ok"] = result.IsError != true && !Refused(text);
                    entry["reply"] = text.Length > 400 ? text.Substring(0, 400) + "…" : text;
                    return result;
                }
                catch (Exception e)
                {
                    entry["ok"] = false;
                    entry["error"] = e.Message;
                    throw;
                }
                finally
                {
                    entry["ms"] = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    journal.Write("tool", entry);
                }
            };

        /// <summary>A reply that says <c>"ok": false</c> (a refused move, an invalid document).</summary>
        private static bool Refused(string text)
        {
            if (!text.TrimStart().StartsWith("{"))
                return false;
            try
            {
                return JObject.Parse(text)["ok"] is JValue { Type: JTokenType.Boolean } ok && !(bool)ok;
            }
            catch (Newtonsoft.Json.JsonException)
            {
                return false;
            }
        }
    }

    /// <summary>This server's journal: every tool call, and the commands of a game it runs itself (without a game window).</summary>
    public sealed class McpJournal
    {
        public Journal Journal { get; }

        public McpJournal(Journal journal)
        {
            Journal = journal;
        }
    }

    [McpServerToolType]
    public static class LogTools
    {
        [McpServerTool(Name = "get_log"), Description("Read what happened, from a journal (one line per event: seq, at, kind, seat, details). source game: the game window's journal of this session (every command players and rules did or refused with the reason, the rules' log lines, turns, moves the rules made by themselves, Lua prints, warnings and errors); source agent: this server's own (every tool call with its arguments, result and time, and the commands of a game run here without a window). Use it when a move was refused or something looks wrong, and to follow a playtest: pass the last seq you saw as after. Kinds: table, command, refused, rules, private, auto, shown, turn, script, warning, error, log, tool.")]
        public static string GetLog(GameSession session, McpJournal journal,
            [Description("'game' or 'agent'; default game when a game window is connected, else agent.")] string? source = null,
            [Description("Only lines after this seq.")] long after = 0,
            [Description("Comma-separated kinds to keep, e.g. 'refused,error'; empty for all.")] string? kinds = null,
            [Description("At most this many of the newest lines.")] int max = 50)
        {
            var wanted = (kinds ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            source ??= session.Remote ? "game" : "agent";
            List<JObject> lines;
            string? file;
            if (source == "agent")
            {
                file = journal.Journal.Path;
                lines = journal.Journal.Read(after, wanted, max);
            }
            else if (source == "game")
            {
                file = Journal.Files(McpLogs.Folder()).FirstOrDefault();
                if (file == null)
                    return GameTools.Json(new { ok = false, error = "No game journal yet: the game window writes one once it opens a table.", folder = McpLogs.Folder() });
                lines = Journal.ReadFile(file, after, wanted, max);
            }
            else
                return GameTools.Json(new { ok = false, error = $"'{source}' is not a source.", suggestion = "Use game or agent." });
            return GameTools.Json(new { ok = true, source, file, last = lines.Count > 0 ? (long?)lines[^1]["seq"] : null, lines });
        }
    }
}
