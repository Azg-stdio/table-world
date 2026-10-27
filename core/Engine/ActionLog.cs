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
            // The rules' lines in order, a private one only for the seat it is meant for (its role, what it sees).
            foreach (var e in result.Events)
                if (e is GameMessage m)
                    lines.Add((true, m.Text));
                else if (e is PrivateMessage p && p.Seat == viewer)
                    lines.Add((true, p.Text));
            return lines;
        }

        private static string? Line(TableEngine engine, Command command, CommandResult result, string? viewer)
        {
            string Name(string id) => NameFor(engine, id, viewer);
            string T(string k, params (string Name, object? Value)[] fills) => Text(engine.State, k, null, fills);
            string N(string k, int count, params (string Name, object? Value)[] fills) => Text(engine.State, k, count, fills);
            switch (command)
            {
                case MoveCommand _:
                    return null;
                case FlipCommand c:
                    return T("flipped", ("thing", Name(c.ObjectId)));
                case ShuffleCommand c:
                    return T("shuffled", ("thing", Name(c.ObjectId)));
                case PeekCommand c:
                    return T("peeked", ("thing", Name(c.ObjectId)));
                case BlindfoldCommand c:
                    return T(c.On ? "blindfoldOn" : "blindfoldOff");
                case SearchCommand c:
                    return T("searched", ("thing", Name(c.ContainerId)));
                case RollCommand _:
                {
                    var rolls = result.Events.OfType<DieRolled>().ToList();
                    if (rolls.Count == 0)
                        return null;
                    string Face(DieRolled r) => engine.Find(r.ObjectId) is Die die ? die.Face(r.Value) : r.Value.ToString();
                    var numeric = rolls.All(r => !(engine.Find(r.ObjectId) is Die { Numeric: false }));
                    return rolls.Count == 1
                        ? T("rolled", ("thing", Name(rolls[0].ObjectId)), ("face", Face(rolls[0])))
                        : numeric
                            ? T("rolledTotal", ("faces", string.Join(", ", rolls.Select(Face))), ("total", rolls.Sum(r => r.Value)))
                            : T("rolledMany", ("faces", string.Join(", ", rolls.Select(Face))));
                }
                case DealCommand c:
                {
                    var to = c.Seats.Count == 0 ? T("everyone") : string.Join(", ", c.Seats.Select(seat => Names.Seat(engine.State, seat)));
                    return N("dealt", c.Count, ("count", c.Count), ("thing", Name(c.DeckId)), ("seats", to));
                }
                case TakeCommand c when engine.Find(c.ContainerId) is { } stack && Stacks.IsStackable(stack):
                    return T("tookCount", ("count", c.Count ?? 1), ("thing", Name(c.ContainerId)), ("left", Stacks.CountOf(stack)));
                case InsertCommand c when engine.Find(c.IntoId) is { } stack && Stacks.IsStackable(stack):
                    return T("stacked", ("thing", Name(c.IntoId)), ("count", Stacks.CountOf(stack)));
                case TakeCommand c:
                {
                    var taken = result.Events.OfType<ContainerChanged>().FirstOrDefault(e => e.From == c.ContainerId)?.ObjectId
                                ?? result.Events.OfType<ObjectAdded>().FirstOrDefault()?.ObjectId;
                    return taken == null ? T("tookFrom", ("from", Name(c.ContainerId))) : T("took", ("thing", Name(taken)), ("from", Name(c.ContainerId)));
                }
                case InsertCommand c:
                    return T("put", ("thing", Name(c.ObjectId)), ("into", Name(c.IntoId)));
                case LockCommand c:
                    return T(c.Locked ? "locked" : "unlocked", ("thing", Name(c.ObjectId)));
                case GroupCommand c:
                    return N("grouped", c.ObjectIds.Count, ("count", c.ObjectIds.Count));
                case ScaleCommand c:
                    return T("resized", ("thing", Name(c.ObjectId)));
                case DeleteCommand c:
                    return T("deleted", ("thing", Name(c.ObjectId)));
                case CloneCommand c:
                    return T("copied", ("thing", Name(c.ObjectId)));
                case SetStateCommand c:
                    return T("changedState", ("thing", Name(c.ObjectId)), ("state", c.State));
                case CutCommand c:
                    return T("cut", ("thing", Name(c.DeckId)));
                case SplitCommand c:
                    return T("split", ("thing", Name(c.DeckId)), ("piles", c.Piles));
                case SpreadCommand c:
                    return T("spread", ("thing", Name(c.DeckId)));
                case SetDieValueCommand c:
                    return T("setValue", ("thing", Name(c.ObjectId)), ("value", c.Value));
                case SetPageCommand c:
                    return engine.Find(c.ObjectId) is Pdf pdf ? T("turnedPage", ("thing", Name(c.ObjectId)), ("page", pdf.Page + 1 + pdf.PageOffset)) : null;
                case SetCounterCommand c:
                    return engine.Find(c.ObjectId) is Counter counter ? T("setValue", ("thing", Name(c.ObjectId)), ("value", counter.Value)) : null;
                case SetClockCommand c:
                    return engine.Find(c.ObjectId) is Clock clock
                        ? T(clock.Mode == ClockMode.Clock ? "setClock" : clock.Running ? "startedClock" : "stoppedClock", ("thing", Name(c.ObjectId)))
                        : null;
                case SetPropertiesCommand c:
                    return T("edited", ("thing", Name(c.ObjectId)));
                case SetTurnOrderCommand { Enabled: false }:
                    return T("turnsOff");
                case SetTurnOrderCommand _:
                    return T("turnsOn", ("seat", Names.Seat(engine.State, engine.State.FreeTurns?.Current)));
                case EndTurnCommand _:
                    return T("endedTurn", ("seat", Names.Seat(engine.State, engine.State.FreeTurns?.Current)));
                case SetPhysicsCommand c:
                    return T(c.Mode switch { PhysicsMode.SemiLock => "physicsSemiLock", PhysicsMode.Locked => "physicsLocked", _ => "physicsFull" });
                case ArrangeZoneCommand c:
                    return T("laidOut", ("zone", ZoneName(engine, c.ZoneId)));
                case RandomizeZoneCommand c:
                    return T("randomized", ("zone", ZoneName(engine, c.ZoneId)));
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
                case PlayEffectCommand _:
                case SetLightingCommand _:
                case SetLightCommand _:
                case ScriptingKeyCommand _:
                case TypeNumberCommand _:
                    return null;
                case SetNoteTabCommand c:
                    return c.Tab.Color == null ? T("wroteNotebook", ("page", c.Tab.Title)) : T("wroteNotes");
                case DeleteNoteTabCommand _:
                    return T("deletedPage");
                case AddDrawingCommand c:
                    return T(c.Drawing.Arrow ? "drewArrow" : "drew");
                case EraseDrawingCommand _:
                    return T("erased");
                case ClearDrawingsCommand c:
                    return c.Seat == null ? T("clearedAll") : T("cleared", ("seat", Names.Seat(engine.State, c.Seat)));
                case SetGridCommand { Grid: null }:
                    return T("gridOff");
                case SetGridCommand c:
                    return T(c.Grid!.Snapping != GridSnapping.Off ? "gridSnapping" : "grid",
                        ("shape", T("shape." + c.Grid.Shape.ToString().ToLowerInvariant())), ("size", (c.Grid.SizeX * 100f).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)),
                        ("snap", T("snap." + c.Grid.Snapping.ToString().ToLowerInvariant())));
                case StartGameCommand _:
                    return T("started");
                case PerformActionCommand c when engine.State.Actions.FirstOrDefault(a => a.Id == c.ActionId) is { Secret: true }:
                    return T("choseSecret");
                // The rules log "Red voted."; the choice stays secret until the count.
                case PerformActionCommand c when engine.State.Actions.FirstOrDefault(a => a.Id == c.ActionId) is { Vote: { } }:
                    return null;
                case PerformActionCommand c:
                    return T("did", ("action", engine.State.Actions.FirstOrDefault(a => a.Id == c.ActionId) is { } done ? Names.Action(engine.State, done) : Names.Humanize(c.ActionId)));
                case BlindRoundCommand { On: true }:
                    return T("blindOn");
                case BlindRoundCommand _:
                    return T("blindOff");
                default:
                    return null;
            }
        }

        /// <summary>How <paramref name="viewer"/> may name an object: its name, or a word for its kind when its face is hidden.</summary>
        public static string NameFor(TableEngine engine, string id, string? viewer)
        {
            var state = engine.State;
            var o = engine.Find(id);
            if (o == null)
                return Text(state, "something");
            var hidden = engine.IsHiddenFrom(id, viewer);
            if (hidden && engine.HiddenZoneHiding(id, viewer, host: viewer == null) != null)
                return Text(state, "something");
            // In another hand or a deck, a card is never named; face down, only when it hides its name (G39, the default).
            if (o is Card && (hidden || o.Container != null))
                return Text(state, "aCard");
            if (!o.NameHidden && (!string.IsNullOrWhiteSpace(o.Name) || o is Card { Text: { } }))
                return Names.Of(state, o);
            return o switch
            {
                Card _ => Text(state, "aCard"),
                Deck _ => Text(state, "aDeck"),
                Die die => Text(state, "aDie", null, ("faces", die.Faces)),
                InfiniteBag _ => Text(state, "anInfiniteBag"),
                _ => Text(state, "aThing", null, ("type", o.Type.ToLowerInvariant())),
            };
        }

        private static string ZoneName(TableEngine engine, string id) =>
            engine.State.Zones.FirstOrDefault(z => z.Id == id)?.Name is { Length: > 0 } name ? name : Text(engine.State, "aZone");

        /// <summary>A log phrase (<c>log.&lt;key&gt;</c>) in the session's language; <c>.one</c> first when <paramref name="count"/> is one.</summary>
        private static string Text(TableState state, string key, int? count = null, params (string Name, object? Value)[] fills)
        {
            var table = Names.Table(state);
            var text = (count == 1 ? table.Raw("log." + key + ".one") : null) ?? table.Raw("log." + key) ?? key;
            return Core.Text.Strings.Fill(text, fills);
        }

        private static string Capitalise(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);
    }
}
