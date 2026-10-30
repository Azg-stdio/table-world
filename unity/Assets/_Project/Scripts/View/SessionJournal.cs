using System.IO;
using Core.Engine;
using Core.Logging;
using UnityEngine;

namespace TableWorld.View
{
    /// <summary>
    /// This machine's journal of the session (docs/dev/logging.md): every command done or refused (by this player or
    /// arriving from the others), the rules' lines, the turns, and the game's own warnings, errors and tagged log lines
    /// (<c>[Lua]</c>, <c>[Net]</c>…). One file per run of the game in <see cref="Folder"/>, the newest kept; a client
    /// writes only what reaches it, the host everything its table holds. A problem report zips the newest of them.
    /// </summary>
    public sealed class SessionJournal : MonoBehaviour
    {
        /// <summary>Where journals go: <see cref="PlayerFolder"/>, unless set (tests write elsewhere).</summary>
        public static string Folder => FolderOverride ?? PlayerFolder;

        /// <summary><c>Logs</c> in the player's data folder, beside Games; the MCP server reads it (game.json's <c>logsFolder</c>).</summary>
        public static string PlayerFolder => Path.Combine(Application.persistentDataPath, "Logs");

        /// <summary>Set by the PlayMode tests, so their tables never prune a player's journals.</summary>
        public static string FolderOverride { get; set; }

        /// <summary>The journal being written, once a table has loaded.</summary>
        public static Journal Current { get; private set; }

        private TableView _table;
        private JournalRecorder _recorder;
        private string _tableKey;
        /// <summary>The journal being written is named after a game (not the unnamed sandbox).</summary>
        private bool _named;

        private void OnEnable()
        {
            _table = GetComponent<TableView>();
            _table.CommandExecuted += OnCommand;
            _table.Loaded += OnLoaded;
            Application.logMessageReceivedThreaded += OnLog;
            if (_table.Engine != null)
                OnLoaded();
        }

        private void OnDisable()
        {
            _table.CommandExecuted -= OnCommand;
            _table.Loaded -= OnLoaded;
            Application.logMessageReceivedThreaded -= OnLog;
            if (Current != null && _recorder != null && Current == _recorder.Journal)
            {
                Current.Dispose();
                Current = null;
            }
        }

        /// <summary>The seat whose view this machine has: a client's own; null on the host, which holds every secret.</summary>
        private string Viewer => _table.Networked && !_table.HostsTable ? _table.LocalSeat : null;

        private void OnLoaded()
        {
            var state = _table.Engine?.State;
            if (state == null)
                return;
            // The journal is named after the game: the unnamed sandbox the scene opens with gets its own file, and the
            // first named game that follows starts a file of its own.
            if (_recorder == null || !_named && state.Game?.Name != null)
            {
                Current?.Dispose();
                Current = Journal.Start(Folder, state.Game?.Name ?? "table");
                _recorder = new JournalRecorder(Current);
                _named = state.Game?.Name != null;
                _tableKey = null;
            }
            // Undo, a resync or a rebuilt prediction loads the same table again: only another table is a new chapter.
            var key = $"{state.Game?.Id}|{state.Game?.Name}|{state.HasRules}|{state.Session?.Started}";
            if (key == _tableKey)
                return;
            _tableKey = key;
            _recorder.Table(state, _table.PackageRoot, _table.Networked ? (_table.HostsTable ? "host" : "client") : "solo", _table.LocalSeat);
        }

        private void OnCommand(Command command, CommandResult result)
        {
            if (_recorder == null || _table.Engine == null)
                return;
            _recorder.Record(_table.Engine.State, command, result, Viewer);
        }

        /// <summary>Warnings, errors and exceptions always; plain lines only when tagged (<c>[Lua] …</c>), the rest is engine chatter.</summary>
        private static void OnLog(string text, string stack, LogType type)
        {
            var journal = Current;
            if (journal == null)
                return;
            switch (type)
            {
                case LogType.Log when text.StartsWith("[Lua]"):
                    journal.Text("script", text.Substring(5).TrimStart());
                    break;
                case LogType.Log when text.StartsWith("["):
                    journal.Text("log", text);
                    break;
                case LogType.Warning:
                    journal.Text("warning", text);
                    break;
                case LogType.Error:
                case LogType.Assert:
                case LogType.Exception:
                    journal.Write("error", new Newtonsoft.Json.Linq.JObject { ["text"] = text, ["stack"] = FirstLines(stack, 8) });
                    break;
            }
        }

        private static string FirstLines(string text, int count)
        {
            if (string.IsNullOrEmpty(text))
                return null;
            var lines = text.Split('\n');
            return string.Join("\n", lines, 0, Mathf.Min(count, lines.Length)).TrimEnd();
        }

        /// <summary>
        /// Zips the newest journals and the game's own log into <c>Reports</c> beside them, for a player to send. Returns
        /// the file written.
        /// </summary>
        public static string SaveReport()
        {
            var stamp = System.DateTime.Now.ToString("yyyy-MM-dd_HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            var zip = Path.Combine(Path.GetDirectoryName(Folder)!, "Reports", $"report_{stamp}.zip");
            var extra = string.IsNullOrEmpty(Application.consoleLogPath) ? new string[0] : new[] { Application.consoleLogPath };
            return Journal.Bundle(Folder, zip, journals: 2, extra: extra);
        }
    }
}
