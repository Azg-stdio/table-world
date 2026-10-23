using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Core.Engine;
using Core.Rules;
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
        public static readonly string[] DefaultColors = { "red", "blue", "green", "yellow" };
        private static readonly string[] Suits = { "clubs", "diamonds", "hearts", "spades" };
        private static readonly string[] Ranks = { "ace", "2", "3", "4", "5", "6", "7", "8", "9", "10", "jack", "queen", "king" };

        private readonly object _lock = new object();

        public TableState Document { get; private set; } = new TableState();
        public GameEngine? Game { get; private set; }
        public string? FilePath { get; private set; }
        public ulong Seed { get; set; } = (ulong)DateTime.UtcNow.Ticks;

        /// <summary>Raised after every change, with the current document (the Unity bridge pushes it).</summary>
        public event Action<TableState>? Changed;

        public GameSession(string? filePath)
        {
            FilePath = filePath;
            if (filePath != null && File.Exists(filePath))
                Load(filePath);
        }

        public T Locked<T>(Func<T> body)
        {
            lock (_lock)
                return body();
        }

        // ---- authoring ----------------------------------------------------------------------------------------------

        /// <summary>A new empty game: standard deck assets, seats around the table, one hand zone per seat.</summary>
        public EditResult NewGame(string id, string name, int players, int minPlayers, int maxPlayers, string? description)
        {
            if (players < 1 || players > 4)
                return EditResult.Rejected(new ValidationError("players", ErrorCodes.InvalidValue, "players must be 1 to 4 in v0.", "Use 2 for a two-player game."));
            var doc = new TableState
            {
                Game = new GameInfo { Id = id, Name = name, MinPlayers = minPlayers, MaxPlayers = maxPlayers, Description = description },
                Assets =
                {
                    new Asset { Id = "std_deck", Type = AssetType.Image, Path = "builtin:standard_deck", Columns = 10, Rows = 7 },
                    new Asset { Id = "std_back", Type = AssetType.Image, Path = "builtin:standard_back" },
                },
            };
            // South, north, east, west. Each hand faces its seat; y = 0.05 keeps cards inside the zone box.
            var seats = new[] { ("south", 0f, -0.42f, 0f, 0.6f), ("north", 0f, 0.42f, 180f, 0.6f), ("east", 0.68f, 0f, 270f, 0.5f), ("west", -0.68f, 0f, 90f, 0.5f) };
            for (var i = 0; i < players; i++)
            {
                var (where, x, z, yaw, width) = seats[i];
                var seatId = $"seat_{where}";
                var hand = new HandZone { Id = $"hand_{where}", Owner = seatId, Size = new Vec3(width, 0.1f, 0.12f) };
                hand.Transform.Position = new Vec3(x, 0.05f, z);
                hand.Transform.Rotation = new Vec3(0, yaw, 0);
                doc.Zones.Add(hand);
                doc.Players.Seats.Add(new Seat { Id = seatId, Color = DefaultColors[i], HandZone = hand.Id });
            }
            Game = null;
            Document = doc;
            return Commit(Array.Empty<ValidationError>());
        }

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
            var copy = TableJson.Parse(TableJson.Serialize(Document)).State!;
            Game = new GameEngine(copy, Seed);
            var result = Game.Execute(new StartGameCommand());
            if (!result.Ok)
                Game = null;
            Changed?.Invoke(Current);
            return PlayResult.From(result);
        }

        public PlayResult Execute(Command command)
        {
            if (Game == null)
                return PlayResult.Refused(new ValidationError("", ErrorCodes.GameNotStarted, "No game is running.", "Call start_game first."));
            var result = Game.Execute(command);
            if (result.Ok || result.StateRestored)
                Changed?.Invoke(Current);
            return PlayResult.From(result);
        }

        /// <summary>Drops the running game and returns to the authored setup.</summary>
        public void Reset()
        {
            Game = null;
            Changed?.Invoke(Current);
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
                Document = TableJson.Parse(TableJson.Serialize(Document)).State!;
                Document.Session = null;
            }
            Changed?.Invoke(Current);
            return parsed;
        }

        private EditResult Commit(IReadOnlyList<ValidationError> problems)
        {
            if (FilePath != null)
                File.WriteAllText(FilePath, TableJson.Serialize(Document));
            Changed?.Invoke(Current);
            return EditResult.Applied(problems);
        }

    }

    /// <summary>Outcome of a play command: the engine's result, or a refusal before the engine ran.</summary>
    public sealed class PlayResult
    {
        public bool Ok => Errors.Count == 0;
        public IReadOnlyList<ValidationError> Errors { get; }
        public CommandResult? Result { get; }

        private PlayResult(CommandResult? result, IReadOnlyList<ValidationError> errors)
        {
            Result = result;
            Errors = errors;
        }

        public static PlayResult From(CommandResult result) =>
            new PlayResult(result, result.Ok ? Array.Empty<ValidationError>() : new[] { result.Error! });

        public static PlayResult Refused(params ValidationError[] errors) => new PlayResult(null, errors);
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
