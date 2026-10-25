using System.Collections.Generic;
using System.Linq;
using Core.Rules;

namespace Core.Engine
{
    /// <summary>
    /// Game log lines (G11): what a command did, in words, as one viewer may read it. A card is named only when its face
    /// is up and the viewer may see it; anything in a hand or hidden zone the viewer cannot see is "a card" or
    /// "something". Moves are not logged (there are hundreds); rules messages come back as their own lines.
    /// </summary>
    public static class ActionLog
    {
        /// <summary>
        /// The lines for <paramref name="command"/>, already executed on <paramref name="engine"/>. <paramref name="actor"/>
        /// is who did it as shown ("north", "Spectator"), or null when unknown; <paramref name="viewer"/> is the reading seat.
        /// Returns (is a rules message, text) pairs; empty when there is nothing worth logging.
        /// </summary>
        public static List<(bool Rules, string Text)> Describe(TableEngine engine, Command command, CommandResult result, string? actor, string? viewer)
        {
            var lines = new List<(bool, string)>();
            if (!result.Ok)
                return lines;
            var line = Line(engine, command, result, viewer);
            if (line != null)
                lines.Add((false, actor == null ? Capitalise(line) : $"{actor} {line}"));
            lines.AddRange(result.Events.OfType<GameMessage>().Select(m => (true, m.Text)));
            return lines;
        }

        private static string? Line(TableEngine engine, Command command, CommandResult result, string? viewer)
        {
            string Name(string id) => NameFor(engine, id, viewer);
            switch (command)
            {
                case MoveCommand _:
                    return null;
                case FlipCommand c:
                    return $"flipped {Name(c.ObjectId)}";
                case ShuffleCommand c:
                    return $"shuffled {Name(c.ObjectId)}";
                case PeekCommand c:
                    return $"peeked at {Name(c.ObjectId)}";
                case BlindfoldCommand c:
                    return c.On ? "put on a blindfold" : "took off the blindfold";
                case SearchCommand c:
                    return $"searched {Name(c.ContainerId)}";
                case RollCommand _:
                {
                    var rolls = result.Events.OfType<DieRolled>().ToList();
                    if (rolls.Count == 0)
                        return null;
                    string Face(DieRolled r) => engine.Find(r.ObjectId) is Die die ? die.Face(r.Value) : r.Value.ToString();
                    var numeric = rolls.All(r => !(engine.Find(r.ObjectId) is Die { Numeric: false }));
                    return rolls.Count == 1
                        ? $"rolled {Name(rolls[0].ObjectId)}: {Face(rolls[0])}"
                        : $"rolled {string.Join(", ", rolls.Select(Face))}" + (numeric ? $" · total {rolls.Sum(r => r.Value)}" : "");
                }
                case DealCommand c:
                {
                    var to = c.Seats.Count == 0 ? "everyone" : string.Join(", ", c.Seats);
                    return $"dealt {Plural(c.Count, "card")} from {Name(c.DeckId)} to {to}";
                }
                case TakeCommand c when engine.Find(c.ContainerId) is { } stack && Stacks.IsStackable(stack):
                    return $"took {c.Count ?? 1} from {Name(c.ContainerId)} ({Stacks.CountOf(stack)} left)";
                case InsertCommand c when engine.Find(c.IntoId) is { } stack && Stacks.IsStackable(stack):
                    return $"stacked {Name(c.IntoId)} ({Stacks.CountOf(stack)})";
                case TakeCommand c:
                {
                    var taken = result.Events.OfType<ContainerChanged>().FirstOrDefault(e => e.From == c.ContainerId)?.ObjectId
                                ?? result.Events.OfType<ObjectAdded>().FirstOrDefault()?.ObjectId;
                    return taken == null ? $"took from {Name(c.ContainerId)}" : $"took {Name(taken)} from {Name(c.ContainerId)}";
                }
                case InsertCommand c:
                    return $"put {Name(c.ObjectId)} into {Name(c.IntoId)}";
                case LockCommand c:
                    return $"{(c.Locked ? "locked" : "unlocked")} {Name(c.ObjectId)}";
                case GroupCommand c:
                    return $"grouped {Plural(c.ObjectIds.Count, "object")}";
                case ScaleCommand c:
                    return $"resized {Name(c.ObjectId)}";
                case DeleteCommand c:
                    return $"deleted {Name(c.ObjectId)}";
                case CloneCommand c:
                    return $"copied {Name(c.ObjectId)}";
                case SetStateCommand c:
                    return $"changed {Name(c.ObjectId)} to state {c.State}";
                case CutCommand c:
                    return $"cut {Name(c.DeckId)}";
                case SplitCommand c:
                    return $"split {Name(c.DeckId)} into {c.Piles} piles";
                case SpreadCommand c:
                    return $"spread {Name(c.DeckId)}";
                case SetDieValueCommand c:
                    return $"set {Name(c.ObjectId)} to {c.Value}";
                case SetPageCommand c:
                    return engine.Find(c.ObjectId) is Pdf pdf ? $"turned {Name(c.ObjectId)} to page {pdf.Page + 1 + pdf.PageOffset}" : null;
                case SetCounterCommand c:
                    return engine.Find(c.ObjectId) is Counter counter ? $"set {Name(c.ObjectId)} to {counter.Value}" : null;
                case SetClockCommand c:
                    return engine.Find(c.ObjectId) is Clock clock
                        ? $"{(clock.Mode == ClockMode.Clock ? "set" : clock.Running ? "started" : "stopped")} {Name(c.ObjectId)}"
                        : null;
                case SetPropertiesCommand c:
                    return $"edited {Name(c.ObjectId)}";
                case SetTurnOrderCommand { Enabled: false }:
                    return "turned turns off";
                case SetTurnOrderCommand _:
                    return $"turned turns on · {engine.State.FreeTurns?.Current}'s turn";
                case EndTurnCommand _:
                    return $"ended the turn · {engine.State.FreeTurns?.Current}'s turn";
                case SetPhysicsCommand c:
                    return $"set physics to {c.Mode switch { PhysicsMode.SemiLock => "semi-lock", PhysicsMode.Locked => "locked", _ => "full" }}";
                case ArrangeZoneCommand c:
                    return $"laid out {ZoneName(engine, c.ZoneId)}";
                case RandomizeZoneCommand c:
                    return $"randomized {ZoneName(engine, c.ZoneId)}";
                case SetButtonsCommand _:
                case SpawnCommand _:
                case SetCustomCommand _:
                case SetInputsCommand _:
                case TypeInputCommand _:
                case SetScriptUiCommand _:
                case SetUiAttributesCommand _:
                case UiEventCommand _:
                case ClickButtonCommand _:
                case SetJointCommand _:
                case ScriptingKeyCommand _:
                case TypeNumberCommand _:
                    return null;
                case SetNoteTabCommand c:
                    return c.Tab.Color == null ? $"wrote in the notebook ({c.Tab.Title})" : "wrote private notes";
                case DeleteNoteTabCommand _:
                    return "deleted a notebook page";
                case AddDrawingCommand c:
                    return c.Drawing.Arrow ? "drew an arrow" : "drew on the table";
                case EraseDrawingCommand _:
                    return "erased a drawing";
                case ClearDrawingsCommand c:
                    return c.Seat == null ? "cleared every drawing" : $"cleared {c.Seat}'s drawings";
                case SetGridCommand { Grid: null }:
                    return "removed the grid";
                case SetGridCommand c:
                    return $"set a {c.Grid!.Shape.ToString().ToLowerInvariant()} grid of {c.Grid.SizeX * 100f:0.#} cm" +
                           (c.Grid.Snapping != GridSnapping.Off ? $", snapping to {c.Grid.Snapping.ToString().ToLowerInvariant()}" : "");
                case StartGameCommand _:
                    return "started the game";
                case PerformActionCommand c:
                    return $"did {engine.State.Actions.FirstOrDefault(a => a.Id == c.ActionId)?.Name ?? c.ActionId}";
                default:
                    return null;
            }
        }

        /// <summary>How <paramref name="viewer"/> may name an object: its name, or a word for its kind when its face is hidden.</summary>
        public static string NameFor(TableEngine engine, string id, string? viewer)
        {
            var o = engine.Find(id);
            if (o == null)
                return "something";
            var hidden = engine.IsHiddenFrom(id, viewer);
            if (hidden && engine.HiddenZoneHiding(id, viewer, host: viewer == null) != null)
                return "something";
            // In another hand or a deck, a card is never named; face down, only when it hides its name (G39, the default).
            if (o is Card && (hidden || o.Container != null))
                return "a card";
            if (!string.IsNullOrWhiteSpace(o.Name) && !o.NameHidden)
                return o.Name!;
            return o switch
            {
                Card _ => "a card",
                Deck _ => "a deck",
                Die die => $"a d{die.Faces}",
                InfiniteBag _ => "an infinite bag",
                _ => "a " + o.Type.ToLowerInvariant(),
            };
        }

        private static string ZoneName(TableEngine engine, string id) =>
            engine.State.Zones.FirstOrDefault(z => z.Id == id)?.Name is { Length: > 0 } name ? name : "a zone";

        private static string Plural(int count, string word) => count == 1 ? $"1 {word}" : $"{count} {word}s";

        private static string Capitalise(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);
    }
}
