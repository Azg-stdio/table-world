using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Core.Engine;
using Core.Rules;
using Core.Scripting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TableWorld.Mcp
{
    /// <summary>
    /// The game being built and played through MCP. While authoring, edits are applied to the document's JSON and read
    /// back strictly: shape errors reject the edit; reference problems are reported but kept, because a document is
    /// incomplete until the agent finishes it. Once a game starts, a <see cref="GameEngine"/> runs it.
    /// </summary>
    public sealed class GameSession
    {
        private static readonly string[] Suits = { "clubs", "diamonds", "hearts", "spades" };
        private static readonly string[] Ranks = { "ace", "2", "3", "4", "5", "6", "7", "8", "9", "10", "jack", "queen", "king" };

        private readonly object _lock = new object();

        public TableState Document { get; private set; } = new TableState();
        public GameEngine? Game { get; private set; }

        /// <summary>The Lua of a game running here (headless): the global script and object scripts, as the game's host runs them.</summary>
        private ScriptHost? _scripts;
        /// <summary>What the scripts printed or broadcast since the last reply.</summary>
        private readonly List<string> _scriptLines = new List<string>();

        /// <summary>Starts the table's scripts for <paramref name="game"/>; rules lua effects call them.</summary>
        private void StartScripts(GameEngine game)
        {
            var host = new ScriptHost(() => Game?.Table, command =>
            {
                var result = Game!.Execute(command);
                _scripts?.OnCommand(command, result, null);
                return result;
            });
            host.RulesEngine = () => Game;
            host.Log += line => _scriptLines.Add("[Lua] " + line);
            host.Broadcast += line => _scriptLines.Add(line);
            game.Scripts = host;
            _scripts = host;
            host.Load(0);
        }

        /// <summary>
        /// Asks the game window for a picture of the table (<c>view</c>: seat, top, object, current). Null with the reason
        /// when there is no window or it did not answer.
        /// </summary>
        public (byte[]? Jpeg, int Width, int Height, string? Error) Screenshot(string view, string? seat, string? target, int width)
        {
            if (!Remote)
                return (null, 0, 0, "No game window is connected, so there is nothing to look at. Start Table World (in the Editor, press Play) and it connects within a few seconds.");
            var reply = Bridge!.Request(new JObject { ["type"] = "screenshot", ["view"] = view, ["seat"] = seat, ["target"] = target, ["width"] = width })
                .GetAwaiter().GetResult();
            if (reply == null)
                return (null, 0, 0, "The game window did not answer within 10 s.");
            if ((bool?)reply["ok"] != true)
                return (null, 0, 0, (string?)reply["error"] ?? "The game window could not take the picture.");
            return (Convert.FromBase64String((string)reply["image"]!), (int?)reply["width"] ?? 0, (int?)reply["height"] ?? 0, null);
        }

        /// <summary>The scripts' lines since the last call.</summary>
        public List<string> DrainScriptLines()
        {
            var lines = new List<string>(_scriptLines);
            _scriptLines.Clear();
            return lines;
        }

        /// <summary>
        /// Runs <paramref name="code"/> against a copy of the table as it is now (the game in play, or the setup), with its
        /// scripts loaded: nothing changes in the real game. Returns the error or null, what the code returned, what it
        /// printed, and the copy afterwards.
        /// </summary>
        public (string? Error, string? Value, List<string> Lines, TableState After) TryLua(string code)
        {
            var copy = TableJson.Parse(TableJson.Serialize(Current)).State!;
            var game = new GameEngine(copy, Seed);
            var lines = new List<string>();
            var host = new ScriptHost(() => game.Table, command =>
            {
                var result = game.Execute(command);
                if (!result.Ok)
                    lines.Add($"[refused] {command.GetType().Name.Replace("Command", "")}: {result.Error!.Message}");
                return result;
            });
            host.RulesEngine = () => game;
            host.Log += line => lines.Add(line);
            host.Broadcast += line => lines.Add(line);
            game.Scripts = host;
            host.Load(0);
            var error = host.RunChunk(code, out var value);
            return (error, value, lines, game.State);
        }
        public string? FilePath { get; private set; }
        public ulong Seed { get; set; } = (ulong)DateTime.UtcNow.Ticks;

        /// <summary>Raised after every change, with the current document (the Unity bridge pushes it).</summary>
        public event Action<TableState>? Changed;

        /// <summary>
        /// Where games are saved when the server was started without <c>--file</c>: each <c>new_game</c> starts its own
        /// file here (the player's Games folder, which the game window lists). Null: only <c>--file</c> or
        /// <c>save_game</c> save.
        /// </summary>
        public string? GamesFolder { get; init; }

        /// <summary>The file a new game goes to in <see cref="GamesFolder"/>: its id, never someone else's earlier game.</summary>
        private string NewGameFile(string id)
        {
            var name = new string(id.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '-').ToArray()).Trim('-');
            if (name.Length == 0)
                name = "game";
            var path = Path.GetFullPath(Path.Combine(GamesFolder!, name + ".json"));
            // Starting the same game again in this session rewrites its file; a file from another session is kept.
            for (var n = 2; File.Exists(path) && !string.Equals(path, FilePath, StringComparison.OrdinalIgnoreCase); n++)
                path = Path.GetFullPath(Path.Combine(GamesFolder!, $"{name}-{n}.json"));
            Directory.CreateDirectory(GamesFolder!);
            return path;
        }

        public GameSession(string? filePath)
        {
            FilePath = filePath;
            if (filePath != null && File.Exists(filePath))
                Load(filePath);
        }

        /// <summary>Set when the game window is connected: play then runs in the game (the authoritative host).</summary>
        public UnityBridge? Bridge { get; set; }

        public bool Remote => Bridge?.Connected == true;

        /// <summary>
        /// Latest state the game reported on its own (a human played in the window). Parked here by the bridge's
        /// receive thread, applied at the next tool call: that thread must never wait for the session lock, because a
        /// tool holding it may be waiting for a reply on the same thread.
        /// </summary>
        private volatile JObject? _reported;

        public void Report(JObject document) => _reported = document;

        /// <summary>The game window's table changed since the session last read it (set on the bridge's receive thread).</summary>
        private volatile bool _stale;

        public void MarkChanged() => _stale = true;

        public T Locked<T>(Func<T> body)
        {
            lock (_lock)
            {
                if (_stale && Remote)
                {
                    _stale = false;
                    if (Bridge!.Request(new JObject { ["type"] = "get_state" }).GetAwaiter().GetResult()?["document"] is JObject current)
                        _reported = current;
                }
                if (_reported is { } reported)
                {
                    _reported = null;
                    Mirror(reported);
                }
                return body();
            }
        }

        /// <summary>Sends the table to the game window, which loads it (resuming the session if there is one).</summary>
        public void PushToGame()
        {
            if (Remote)
                Bridge!.Send(new JObject
                {
                    ["type"] = "load",
                    ["document"] = JObject.Parse(TableJson.Serialize(Current)),
                    // Image paths are relative to the game file's folder.
                    ["root"] = FilePath != null ? Path.GetDirectoryName(FilePath) : null,
                }).GetAwaiter().GetResult();
        }

        private void Mirror(JObject document)
        {
            var parsed = TableJson.Parse(document.ToString());
            if (parsed.State == null || parsed.Structural)
                return;
            Game = parsed.State.Session is { Started: true } ? new GameEngine(parsed.State, Seed) : null;
            if (Game == null)
                Document = parsed.State;
        }

        private PlayResult RemoteCommand(JObject command)
        {
            var reply = Bridge!.Request(new JObject { ["type"] = "command", ["command"] = command }).GetAwaiter().GetResult();
            if (reply == null)
                return PlayResult.Refused(new ValidationError("", "game_unreachable", "The game window did not answer within 10 s.",
                    "Check that the game is running, or close it to play headless."));
            if (reply["document"] is JObject document)
                Mirror(document);
            var errors = (reply["errors"] as JArray ?? new JArray()).Select(e => new ValidationError(
                (string?)e["path"] ?? "", (string?)e["code"] ?? "rule_failed", (string?)e["message"] ?? "", (string?)e["suggestion"])).ToArray();
            var messages = (reply["messages"] as JArray ?? new JArray()).Select(m => (string)m!).ToList();
            return errors.Length > 0 ? PlayResult.Refused(errors) : PlayResult.Remote(messages);
        }

        // ---- authoring ----------------------------------------------------------------------------------------------

        /// <summary>A new empty game: standard deck assets, seats around the table, one hand zone per seat.</summary>
        public EditResult NewGame(string id, string name, int players, int minPlayers, int maxPlayers, string? description)
        {
            if (players < 1 || players > SeatLayout.MaxSeats)
                return EditResult.Rejected(new ValidationError("players", ErrorCodes.InvalidValue, $"players must be 1 to {SeatLayout.MaxSeats}.",
                    "Use the number of seats at the table; more people can still join a session and watch."));
            if (GamesFolder != null)
                FilePath = NewGameFile(id);
            var doc = new TableState
            {
                Game = new GameInfo { Id = id, Name = name, MinPlayers = minPlayers, MaxPlayers = maxPlayers, Description = description },
                Assets =
                {
                    new Asset { Id = "std_deck", Type = AssetType.Image, Path = "builtin:standard_deck", Columns = 10, Rows = 7 },
                    new Asset { Id = "std_back", Type = AssetType.Image, Path = "builtin:standard_back" },
                },
            };
            // Each hand faces its seat; five seats or more need a bigger table, which the environment carries.
            var (tableSize, places) = SeatLayout.Arrange(players);
            if (tableSize != TableLookDef.DefaultSize)
                doc.Environment = new EnvironmentDef { Table = { Size = tableSize } };
            foreach (var place in places)
            {
                var hand = new HandZone { Id = place.HandId, Owner = place.SeatId, Size = new Vec3(place.Width, 0.1f, 0.12f) };
                hand.Transform.Position = place.Position;
                hand.Transform.Rotation = new Vec3(0, place.Yaw, 0);
                doc.Zones.Add(hand);
                doc.Players.Seats.Add(new Seat { Id = place.SeatId, Color = place.Color, HandZone = hand.Id });
            }
            Game = null;
            Document = doc;
            return Commit(Array.Empty<ValidationError>());
        }

        /// <summary>File types the game loads, by extension, and the asset type each one is.</summary>
        public static readonly IReadOnlyDictionary<string, string> AssetExtensions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".png"] = "image", [".jpg"] = "image", [".jpeg"] = "image",
            [".obj"] = "model",
            [".mp3"] = "audio", [".ogg"] = "audio", [".wav"] = "audio",
            [".pdf"] = "pdf",
            [".unity3d"] = "assetBundle",
        };

        /// <summary>
        /// Brings a file into the game and returns its path relative to the game file. A file already in the game file's
        /// folder is used where it is; any other is copied into <c>&lt;game&gt;-assets/</c> next to the game file, so the game
        /// keeps working when the original moves and the host can send it to players who join.
        /// </summary>
        public string ImportAssetFile(string source)
        {
            if (FilePath == null)
                throw new EditException(new ValidationError("path", ErrorCodes.MissingField, "The game has no file yet, so there is nowhere to keep its images.",
                    "Call save_game with a path first, e.g. games/my-game.json."));
            var full = Path.GetFullPath(source);
            if (!File.Exists(full))
                throw new EditException(new ValidationError("path", ErrorCodes.InvalidPath, $"No file at {full}.", "Pass the full path of an existing file."));
            if (!AssetExtensions.ContainsKey(Path.GetExtension(full)))
                throw new EditException(new ValidationError("path", ErrorCodes.InvalidValue, $"The game cannot load {Path.GetExtension(full)} files.",
                    "Use " + string.Join(", ", AssetExtensions.Keys) + "; convert other images to PNG or JPEG."));
            var root = Path.GetDirectoryName(FilePath)!;
            var relative = Path.GetRelativePath(root, full).Replace('\\', '/');
            if (Asset.IsSafePath(relative))
                return relative;

            var folder = Path.GetFileNameWithoutExtension(FilePath) + "-assets";
            Directory.CreateDirectory(Path.Combine(root, folder));
            var name = Path.GetFileNameWithoutExtension(full);
            var extension = Path.GetExtension(full).ToLowerInvariant();
            var target = Path.Combine(root, folder, name + extension);
            // Another file of the same name is kept: this one gets a number. The same file again is not copied twice.
            for (var n = 2; File.Exists(target) && !SameBytes(target, full); n++)
                target = Path.Combine(root, folder, $"{name}-{n}{extension}");
            if (!File.Exists(target))
                File.Copy(full, target);
            return Path.GetRelativePath(root, target).Replace('\\', '/');
        }

        private static bool SameBytes(string a, string b)
        {
            var ia = new FileInfo(a);
            var ib = new FileInfo(b);
            return ia.Length == ib.Length && File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));
        }

        /// <summary>Width and height of a PNG or JPEG from its header, or null.</summary>
        public static (int Width, int Height)? ImageSize(string file)
        {
            try
            {
                using var stream = File.OpenRead(file);
                var head = new byte[32];
                if (stream.Read(head, 0, head.Length) < 24)
                    return null;
                if (head[0] == 0x89 && head[1] == (byte)'P' && head[2] == (byte)'N' && head[3] == (byte)'G')
                    return (BigEndian(head, 16), BigEndian(head, 20));
                if (head[0] != 0xFF || head[1] != 0xD8)
                    return null;
                // JPEG: walk the segments to the frame header (SOF0–SOF15 except DHT, JPG and DAC).
                stream.Position = 2;
                var marker = new byte[4];
                while (stream.Read(marker, 0, 4) == 4 && marker[0] == 0xFF)
                {
                    var type = marker[1];
                    var length = (marker[2] << 8) | marker[3];
                    if (type >= 0xC0 && type <= 0xCF && type != 0xC4 && type != 0xC8 && type != 0xCC)
                    {
                        var frame = new byte[5];
                        if (stream.Read(frame, 0, 5) < 5)
                            return null;
                        return ((frame[3] << 8) | frame[4], (frame[1] << 8) | frame[2]);
                    }
                    stream.Position += length - 2;
                }
                return null;
            }
            catch (IOException)
            {
                return null;
            }
        }

        private static int BigEndian(byte[] b, int at) => (b[at] << 24) | (b[at + 1] << 16) | (b[at + 2] << 8) | b[at + 3];

        /// <summary>Applies a change to the document's JSON and keeps it unless the result is malformed.</summary>
        public EditResult Edit(Action<JObject> change)
        {
            if (Game?.Session is { Started: true, Over: false })
                return EditResult.Rejected(new ValidationError("", ErrorCodes.GameAlreadyStarted, "A game is in progress; the rules cannot change now.",
                    "Call reset_game to go back to the setup."));
            var json = JObject.Parse(TableJson.Serialize(Document));
            try
            {
                change(json);
            }
            catch (EditException e)
            {
                return EditResult.Rejected(e.Error);
            }
            var parsed = TableJson.Parse(json.ToString());
            if (parsed.Structural || parsed.State == null)
                return EditResult.Rejected(parsed.Errors.ToArray());
            Game = null;
            Document = parsed.State;
            return Commit(parsed.Errors);
        }

        /// <summary>A card id from a title: lowercase letters and digits joined by underscores ("Escudo Pesado" → escudo_pesado).</summary>
        public static string Slug(string text)
        {
            var normalized = text.Normalize(System.Text.NormalizationForm.FormD);
            var chars = new System.Text.StringBuilder();
            foreach (var c in normalized)
            {
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark)
                    continue;
                chars.Append(char.IsLetterOrDigit(c) && c < 128 ? char.ToLowerInvariant(c) : '_');
            }
            var slug = System.Text.RegularExpressions.Regex.Replace(chars.ToString(), "_+", "_").Trim('_');
            return slug.Length == 0 ? "card" : slug;
        }

        public static JObject ParseObject(string json, string what)
        {
            try
            {
                return JObject.Parse(json);
            }
            catch (JsonReaderException e)
            {
                throw new EditException(new ValidationError(what, ErrorCodes.InvalidJson, $"{what} is not valid JSON: {e.Message}"));
            }
        }

        public static JArray ParseArray(string json, string what)
        {
            try
            {
                return JArray.Parse(json);
            }
            catch (JsonReaderException e)
            {
                throw new EditException(new ValidationError(what, ErrorCodes.InvalidJson, $"{what} is not a valid JSON array: {e.Message}"));
            }
        }

        /// <summary>The 52 (or 54 with jokers) cards of the builtin standard deck; meta rank 2–14 (aces high), suit.</summary>
        public static JArray StandardCards(bool jokers)
        {
            var cards = new JArray();
            for (var i = 0; i < 52; i++)
            {
                var suit = Suits[i / 13];
                var rank = i % 13;
                cards.Add(new JObject
                {
                    ["id"] = $"{Ranks[rank]}_{suit}",
                    ["faceIndex"] = i,
                    ["name"] = $"{char.ToUpperInvariant(Ranks[rank][0])}{Ranks[rank].Substring(1)} of {suit}",
                    ["meta"] = new JObject { ["rank"] = rank == 0 ? 14 : rank + 1, ["suit"] = suit, ["color"] = suit == "hearts" || suit == "diamonds" ? "red" : "black" },
                });
            }
            if (jokers)
            {
                cards.Add(new JObject { ["id"] = "red_joker", ["faceIndex"] = 52, ["name"] = "Red joker", ["tags"] = new JArray("joker") });
                cards.Add(new JObject { ["id"] = "black_joker", ["faceIndex"] = 53, ["name"] = "Black joker", ["tags"] = new JArray("joker") });
            }
            return cards;
        }

        public IReadOnlyList<ValidationError> Validate() => TableValidator.Validate(Document);

        // ---- play ---------------------------------------------------------------------------------------------------

        public PlayResult Start()
        {
            var problems = Validate();
            if (problems.Count > 0)
                return PlayResult.Refused(problems.ToArray());
            if (Remote)
            {
                PushToGame();
                return RemoteCommand(new JObject { ["kind"] = "start", ["seed"] = (long)Seed });
            }
            var copy = TableJson.Parse(TableJson.Serialize(Document)).State!;
            Game = new GameEngine(copy, Seed);
            StartScripts(Game);
            var result = Game.Execute(new StartGameCommand());
            if (!result.Ok)
                Game = null;
            Changed?.Invoke(Current);
            return PlayResult.From(result, DrainScriptLines());
        }

        public PlayResult Execute(Command command)
        {
            if (Remote)
            {
                switch (command)
                {
                    case PerformActionCommand perform:
                        return RemoteCommand(new JObject
                        {
                            ["kind"] = "perform", ["seat"] = perform.Actor, ["action"] = perform.ActionId,
                            ["params"] = JObject.FromObject(perform.Params),
                        });
                    case DealCommand deal:
                        return RemoteCommand(new JObject { ["kind"] = "deal", ["deck"] = deal.DeckId, ["count"] = deal.Count, ["seats"] = new JArray(deal.Seats) });
                }
            }
            if (Game == null)
                return PlayResult.Refused(new ValidationError("", ErrorCodes.GameNotStarted, "No game is running.", "Call start_game first."));
            var result = Game.Execute(command);
            if (result.Ok)
                _scripts?.OnCommand(command, result, null);
            if (result.Ok || result.StateRestored)
                Changed?.Invoke(Current);
            return PlayResult.From(result, DrainScriptLines());
        }

        /// <summary>
        /// A physical table command (flip, cut, move...): on the game window when connected, on the running game, or on
        /// the authored setup (saved like any edit).
        /// </summary>
        public TableResult ExecuteTable(Command command)
        {
            if (Remote)
            {
                var payload = new JObject { ["kind"] = "table", ["type"] = command.GetType().Name, ["command"] = JObject.FromObject(command) };
                var reply = Bridge!.Request(new JObject { ["type"] = "command", ["command"] = payload }).GetAwaiter().GetResult();
                if (reply == null)
                    return TableResult.Failed(new ValidationError("", "game_unreachable", "The game window did not answer within 10 s."));
                if (reply["document"] is JObject document)
                    Mirror(document);
                var errors = (reply["errors"] as JArray ?? new JArray()).Select(e => new ValidationError(
                    (string?)e["path"] ?? "", (string?)e["code"] ?? "rule_failed", (string?)e["message"] ?? "", (string?)e["suggestion"])).ToArray();
                var created = (reply["created"] as JArray ?? new JArray()).Select(c => (string)c!).ToList();
                return errors.Length > 0 ? TableResult.Failed(errors) : TableResult.Done(created);
            }
            CommandResult result;
            if (Game != null)
            {
                result = Game.Execute(command);
            }
            else
            {
                result = new TableEngine(Document, Seed).Execute(command);
                if (result.Ok)
                    Commit(Array.Empty<ValidationError>());
            }
            if (!result.Ok)
                return TableResult.Failed(result.Error!);
            Changed?.Invoke(Current);
            return TableResult.Done(result.CreatedIds?.ToList() ?? (result.CreatedId != null ? new List<string> { result.CreatedId } : new List<string>()));
        }

        /// <summary>Drops the running game and returns to the authored setup.</summary>
        public void Reset()
        {
            Game = null;
            Changed?.Invoke(Current);
            PushToGame();
        }

        /// <summary>The table as it is now: the running game's state, or the document being authored.</summary>
        public TableState Current => Game?.State ?? Document;

        // ---- files --------------------------------------------------------------------------------------------------

        public string Save(string? path)
        {
            path ??= FilePath ?? throw new EditException(new ValidationError("path", ErrorCodes.MissingField, "No file to save to.", "Pass a path, e.g. games/my-game.json."));
            var full = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, TableJson.Serialize(Current));
            FilePath = full;
            return full;
        }

        public TableParseResult Load(string path)
        {
            var parsed = TableJson.Parse(File.ReadAllText(path));
            if (parsed.State == null || parsed.Structural)
                return parsed;
            FilePath = Path.GetFullPath(path);
            Document = parsed.State;
            Game = null;
            if (Document.Session is { Started: true })
            {
                // A saved game in progress: resume it as is.
                Game = new GameEngine(Document, Seed);
                StartScripts(Game);
                Document = TableJson.Parse(TableJson.Serialize(Document)).State!;
                Document.Session = null;
            }
            Changed?.Invoke(Current);
            PushToGame();
            return parsed;
        }

        private EditResult Commit(IReadOnlyList<ValidationError> problems)
        {
            if (FilePath != null)
                File.WriteAllText(FilePath, TableJson.Serialize(Document));
            Changed?.Invoke(Current);
            PushToGame();
            return EditResult.Applied(problems);
        }

    }

    public sealed class TableResult
    {
        public bool Ok => Errors.Count == 0;
        public IReadOnlyList<ValidationError> Errors { get; }
        public IReadOnlyList<string> CreatedIds { get; }

        private TableResult(IReadOnlyList<ValidationError> errors, IReadOnlyList<string> created)
        {
            Errors = errors;
            CreatedIds = created;
        }

        public static TableResult Done(IReadOnlyList<string> created) => new TableResult(Array.Empty<ValidationError>(), created);
        public static TableResult Failed(params ValidationError[] errors) => new TableResult(errors, Array.Empty<string>());
    }

    /// <summary>Outcome of a play command: the engine's result, or a refusal before the engine ran.</summary>
    public sealed class PlayResult
    {
        public bool Ok => Errors.Count == 0;
        public IReadOnlyList<ValidationError> Errors { get; }
        public CommandResult? Result { get; }
        public IReadOnlyList<string> Messages { get; }

        private PlayResult(CommandResult? result, IReadOnlyList<ValidationError> errors, IReadOnlyList<string> messages)
        {
            Result = result;
            Errors = errors;
            Messages = messages;
        }

        public static PlayResult From(CommandResult result, IEnumerable<string>? scriptLines = null) => new PlayResult(result,
            result.Ok ? Array.Empty<ValidationError>() : new[] { result.Error! },
            result.Events.OfType<GameMessage>().Select(m => m.Text).Concat(scriptLines ?? Enumerable.Empty<string>()).ToList());

        /// <summary>A command the game window executed.</summary>
        public static PlayResult Remote(IReadOnlyList<string> messages) => new PlayResult(null, Array.Empty<ValidationError>(), messages);

        public static PlayResult Refused(params ValidationError[] errors) => new PlayResult(null, errors, Array.Empty<string>());
    }

    public sealed class EditResult
    {
        public bool Ok { get; }
        public IReadOnlyList<ValidationError> Errors { get; }

        private EditResult(bool ok, IReadOnlyList<ValidationError> errors)
        {
            Ok = ok;
            Errors = errors;
        }

        public static EditResult Applied(IReadOnlyList<ValidationError> problems) => new EditResult(true, problems);
        public static EditResult Rejected(params ValidationError[] errors) => new EditResult(false, errors);
    }

    public sealed class EditException : Exception
    {
        public ValidationError Error { get; }

        public EditException(ValidationError error) : base(error.Message)
        {
            Error = error;
        }
    }
}
