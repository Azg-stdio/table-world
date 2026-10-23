using System.ComponentModel;
using System.Linq;
using Core.Engine;
using Core.Rules;
using ModelContextProtocol.Server;

namespace TableWorld.Mcp
{
    /// <summary>
    /// Physical table actions for agents (the TTS interactions of docs/tts-interactions.md as commands): what a player
    /// can do with their hands, outside the rules. Works on the authored setup before a game starts, and during play.
    /// </summary>
    [McpServerToolType]
    public static class TableTools
    {
        public static readonly string[] Actions =
        {
            "flip", "rotate", "move", "lock", "unlock", "shuffle", "roll", "set_die_value", "cut", "split", "spread",
            "take", "insert", "group", "delete", "clone", "set_state", "scale", "rename",
        };

        [McpServerTool(Name = "table_action"), Description(
            "Do something physical on the table, like a player's hands (outside the rules): " +
            "flip, rotate (value = degrees about the vertical), move (x, z), lock, unlock, shuffle (deck or bag), roll (die), " +
            "set_die_value (value), cut (value = cards, default half), split (value = piles), spread (deck into a row), " +
            "take (from a deck or bag; value = index from the top, or other = card id; x, z = where), insert (other = deck/bag/card to put it into; value 1 = at the bottom), " +
            "group (other = comma-separated ids to gather with it into a deck), delete, clone (x, z), set_state (other = state key), " +
            "scale (value = factor), rename (other = new name, text = description).")]
        public static string TableAction(GameSession session,
            [Description("One of: " + "flip, rotate, move, lock, unlock, shuffle, roll, set_die_value, cut, split, spread, take, insert, group, delete, clone, set_state, scale, rename")] string action,
            [Description("The object (or deck/bag) id.")] string target,
            [Description("Number argument (degrees, cards, piles, die value, index, factor) as described for the action.")] double? value = null,
            [Description("Text argument (another id, ids, state key or name) as described for the action.")] string? other = null,
            [Description("Position x in metres (table centre is 0, the table is 1.6 x 1.0).")] float? x = null,
            [Description("Position z in metres (south seat at z = -0.5).")] float? z = null,
            [Description("Description, for rename.")] string? text = null) =>
            session.Locked(() =>
            {
                Vec3? At() => x.HasValue && z.HasValue ? new Vec3(x.Value, 0f, z.Value) : (Vec3?)null;
                var obj = session.Current.Objects.FirstOrDefault(o => o.Id == target);
                Command? command = action switch
                {
                    "flip" => new FlipCommand { ObjectId = target },
                    "rotate" when obj != null => new MoveCommand
                    {
                        ObjectId = target,
                        Position = obj.Transform.Position,
                        Rotation = new Vec3(obj.Transform.Rotation.X, Geometry.NormalizeAngle(obj.Transform.Rotation.Y + (float)(value ?? 90)), obj.Transform.Rotation.Z),
                    },
                    "move" when obj != null => new MoveCommand { ObjectId = target, Position = At() ?? obj.Transform.Position, Rotation = obj.Transform.Rotation },
                    "lock" => new LockCommand { ObjectId = target, Locked = true },
                    "unlock" => new LockCommand { ObjectId = target, Locked = false },
                    "shuffle" => new ShuffleCommand { ObjectId = target },
                    "roll" => new RollCommand { ObjectId = target },
                    "set_die_value" => new SetDieValueCommand { ObjectId = target, Value = (int)(value ?? 1) },
                    "cut" => new CutCommand { DeckId = target, Count = value.HasValue ? (int)value.Value : (int?)null },
                    "split" => new SplitCommand { DeckId = target, Piles = (int)(value ?? 2) },
                    "spread" => new SpreadCommand { DeckId = target },
                    "take" => new TakeCommand { ContainerId = target, Index = (int)(value ?? 0), EntryId = other, Position = At() },
                    "insert" => new InsertCommand { ObjectId = target, IntoId = other ?? "", AtBottom = value == 1 },
                    "group" => new GroupCommand { ObjectIds = new[] { target }.Concat((other ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0)).ToList() },
                    "delete" => new DeleteCommand { ObjectId = target },
                    "clone" => new CloneCommand { ObjectId = target, Position = At() },
                    "set_state" => new SetStateCommand { ObjectId = target, State = other ?? "" },
                    "scale" => new ScaleCommand { ObjectId = target, Factor = (float)(value ?? 1.1) },
                    "rename" => new SetPropertiesCommand { ObjectId = target, Name = other, Description = text },
                    _ => null,
                };
                if (command == null)
                {
                    var error = obj == null && (action == "rotate" || action == "move")
                        ? new ValidationError("target", ErrorCodes.UnknownObject, $"Object '{target}' does not exist.", null)
                        : new ValidationError("action", ErrorCodes.UnknownCommand, $"Unknown action '{action}'.", $"Use one of: {string.Join(", ", Actions)}.");
                    return GameTools.Json(new { ok = false, errors = new[] { new { path = error.Path, code = error.Code, message = error.Message, suggestion = error.Suggestion } } });
                }
                var result = session.ExecuteTable(command);
                return result.Ok
                    ? GameTools.Json(new { ok = true, created = result.CreatedIds, state = GameTools.Summary(session) })
                    : GameTools.Json(new { ok = false, errors = result.Errors.Select(e => new { path = e.Path, code = e.Code, message = e.Message, suggestion = e.Suggestion }) });
            });
    }
}
