using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Core.Logging
{
    /// <summary>
    /// A session's journal (docs/dev/logging.md): what happened, one JSON object per line, written as it happens so a crash
    /// keeps everything up to it. Each line is <c>{ "seq", "at", "kind", "seat"?, ...data }</c>. Each machine writes its
    /// own, holding only what that machine knows. The newest <see cref="MemoryEntries"/> also stay in memory for readers
    /// in the same process (the MCP server's <c>get_log</c>). A journal without a path keeps memory only.
    /// </summary>
    public sealed class Journal : IDisposable
    {
        /// <summary>Journals kept in a folder: older ones are deleted when a new one starts.</summary>
        public const int KeepFiles = 20;
        /// <summary>A journal stops growing at this size (a last line says so): a runaway loop cannot fill the disk.</summary>
        public const long MaxBytes = 20L * 1024 * 1024;
        public const int MemoryEntries = 2000;
        public const string Extension = ".jsonl";

        private readonly object _lock = new object();
        private readonly LinkedList<JObject> _recent = new LinkedList<JObject>();
        private StreamWriter? _writer;
        private long _bytes;
        private bool _full;
        private readonly Func<DateTime> _clock;

        /// <summary>The file written, or null for a journal in memory only.</summary>
        public string? Path { get; }

        /// <summary>The number given to the last line (lines count from 1).</summary>
        public long Seq { get; private set; }

        public Journal(string? path, Func<DateTime>? clock = null)
        {
            Path = path;
            _clock = clock ?? (() => DateTime.UtcNow);
            if (path == null)
                return;
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            // Shared for reading: the MCP server and a problem report read it while it is written.
            var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            _writer = new StreamWriter(stream) { AutoFlush = true };
            _bytes = stream.Length;
        }

        /// <summary>
        /// A new journal in <paramref name="folder"/> named after the time and <paramref name="name"/> (the game), with the
        /// oldest journals beyond <see cref="KeepFiles"/> deleted.
        /// </summary>
        public static Journal Start(string folder, string name, string? prefix = null)
        {
            Directory.CreateDirectory(folder);
            Prune(folder, KeepFiles - 1, prefix);
            var stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            var path = System.IO.Path.Combine(folder, $"{prefix}{stamp}_{Slug(name)}{Extension}");
            for (var n = 2; File.Exists(path); n++)
                path = System.IO.Path.Combine(folder, $"{prefix}{stamp}_{Slug(name)}-{n}{Extension}");
            return new Journal(path);
        }

        /// <summary>Adds a line: <paramref name="kind"/>, the seat it concerns, and <paramref name="data"/>'s fields.</summary>
        public void Write(string kind, JObject? data = null, string? seat = null)
        {
            lock (_lock)
            {
                var entry = new JObject
                {
                    ["seq"] = ++Seq,
                    ["at"] = _clock().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture),
                    ["kind"] = kind,
                };
                if (seat != null)
                    entry["seat"] = seat;
                if (data != null)
                    foreach (var property in data.Properties())
                        if (property.Value.Type != JTokenType.Null && !(property.Value is JValue { Value: null }))
                            entry[property.Name] = property.Value.DeepClone();
                _recent.AddLast(entry);
                if (_recent.Count > MemoryEntries)
                    _recent.RemoveFirst();
                if (_writer == null || _full)
                    return;
                var line = entry.ToString(Formatting.None);
                if (_bytes + line.Length > MaxBytes)
                {
                    _full = true;
                    line = new JObject { ["seq"] = Seq, ["kind"] = "journal", ["text"] = $"The journal reached {MaxBytes / (1024 * 1024)} MB and stops here." }.ToString(Formatting.None);
                }
                try
                {
                    _writer.WriteLine(line);
                    _bytes += line.Length + Environment.NewLine.Length;
                }
                catch (IOException)
                {
                    // A full disk or a removed folder never stops the game.
                    _full = true;
                }
            }
        }

        /// <summary>A line of text (a warning, a script's print, a network event).</summary>
        public void Text(string kind, string text, string? seat = null) => Write(kind, new JObject { ["text"] = text }, seat);

        /// <summary>The lines in memory after <paramref name="after"/>, of the given kinds (all when empty), the newest <paramref name="max"/>.</summary>
        public List<JObject> Read(long after = 0, IReadOnlyCollection<string>? kinds = null, int max = 100)
        {
            lock (_lock)
                return Select(_recent, after, kinds, max);
        }

        /// <summary>Lines of a journal file (another process's, a past session's), as <see cref="Read"/> selects them.</summary>
        public static List<JObject> ReadFile(string path, long after = 0, IReadOnlyCollection<string>? kinds = null, int max = 100)
        {
            var lines = new List<JObject>();
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream))
            {
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    try
                    {
                        if (JToken.Parse(line) is JObject entry)
                            lines.Add(entry);
                    }
                    catch (JsonException)
                    {
                        // A line cut short by a crash.
                    }
                }
            }
            return Select(lines, after, kinds, max);
        }

        private static List<JObject> Select(IEnumerable<JObject> lines, long after, IReadOnlyCollection<string>? kinds, int max) =>
            lines.Where(e => (long?)e["seq"] > after && (kinds == null || kinds.Count == 0 || kinds.Contains((string?)e["kind"] ?? "")))
                .Reverse().Take(Math.Max(1, max)).Reverse().ToList();

        /// <summary>The journals in <paramref name="folder"/>, newest first.</summary>
        public static List<string> Files(string folder, string? prefix = null) =>
            !Directory.Exists(folder)
                ? new List<string>()
                : Directory.GetFiles(folder, $"{prefix}*{Extension}")
                    .Where(f => prefix != null || !System.IO.Path.GetFileName(f).StartsWith(McpPrefix, StringComparison.Ordinal))
                    .OrderByDescending(File.GetLastWriteTimeUtc).ThenByDescending(f => f, StringComparer.Ordinal).ToList();

        /// <summary>The MCP server's journals start with this, beside the game's.</summary>
        public const string McpPrefix = "mcp_";

        /// <summary>
        /// A problem report: the newest <paramref name="journals"/> journals of <paramref name="folder"/> and any
        /// <paramref name="extra"/> files (the game's own log), zipped into <paramref name="zipPath"/>.
        /// </summary>
        public static string Bundle(string folder, string zipPath, int journals = 2, IEnumerable<string>? extra = null)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(zipPath)!);
            if (File.Exists(zipPath))
                File.Delete(zipPath);
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                foreach (var file in Files(folder).Take(journals).Concat(Files(folder, McpPrefix).Take(1)).Concat(extra ?? Enumerable.Empty<string>()))
                {
                    if (!File.Exists(file))
                        continue;
                    var entry = zip.CreateEntry(System.IO.Path.GetFileName(file));
                    // Files still being written are read shared.
                    using var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var target = entry.Open();
                    source.CopyTo(target);
                }
            }
            return zipPath;
        }

        /// <summary>Deletes all but the newest <paramref name="keep"/> journals of one kind (the game's, or the MCP server's).</summary>
        private static void Prune(string folder, int keep, string? prefix)
        {
            foreach (var old in Files(folder, prefix).Skip(Math.Max(0, keep)))
            {
                try
                {
                    File.Delete(old);
                }
                catch (IOException)
                {
                    // Still open in another process (an earlier game window): kept for next time.
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        private static string Slug(string text)
        {
            var chars = text.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
            var slug = string.Join("-", new string(chars).Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries));
            return slug.Length == 0 ? "table" : slug.Length > 40 ? slug.Substring(0, 40) : slug;
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _writer?.Dispose();
                _writer = null;
            }
        }
    }
}
