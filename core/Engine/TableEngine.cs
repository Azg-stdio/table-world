using System;
using System.Collections.Generic;
using System.Linq;
using Core.Rules;

namespace Core.Engine
{
    /// <summary>
    /// Executes <see cref="Command"/>s against a <see cref="TableState"/>. Each command is checked completely before
    /// anything changes, so a failed command leaves the state untouched. All randomness comes from a seeded
    /// <see cref="DeterministicRandom"/>: same state + same seed + same commands → same result, on every machine.
    /// The state passed in is assumed valid (<see cref="TableJson.Parse"/>); the engine keeps it valid.
    /// </summary>
    public sealed partial class TableEngine
    {
        /// <summary>How far beside a container a taken object lands when the command gives no position.</summary>
        public const float BesideOffset = 0.1f;

        private readonly Dictionary<string, TableObject> _objects = new Dictionary<string, TableObject>();
        private readonly HashSet<string> _usedIds = new HashSet<string>();
        private readonly Dictionary<string, List<string>> _zonesOf = new Dictionary<string, List<string>>();
        private static readonly List<string> NoZones = new List<string>();

        public TableState State { get; }
        public DeterministicRandom Random { get; }

        public TableEngine(TableState state, ulong seed)
        {
            State = state;
            Random = new DeterministicRandom(seed);
            Reindex();
        }

        /// <summary>Rebuilds the id and zone indexes after the state's lists were replaced (rollback of a failed rule action).</summary>
        internal void Reindex()
        {
            _objects.Clear();
            _usedIds.Clear();
            _zonesOf.Clear();
            foreach (var obj in State.Objects)
            {
                _objects[obj.Id] = obj;
                ClaimIds(obj);
            }
            foreach (var zone in State.Zones)
                _usedIds.Add(zone.Id);
            foreach (var obj in State.Objects)
                _zonesOf[obj.Id] = ComputeZones(obj);
        }

        public TableObject? Find(string id) => _objects.TryGetValue(id, out var obj) ? obj : null;

        /// <summary>
        /// The id rules and scripts know an object by: a card drawn on a table with secret cards travels under a fresh id
        /// (<see cref="Card.EntryId"/> keeps its own), but the game's rules and scripts run on the host and still call it
        /// by its own id (Tornaris checks which character card was dealt).
        /// </summary>
        public static string OwnId(TableObject obj) => obj is Card { EntryId: { Length: > 0 } own } ? own : obj.Id;

        /// <summary><see cref="Find"/>, or the card on the table whose own id (<see cref="OwnId"/>) this is. For rules and scripts only: commands from other machines name objects by their table id.</summary>
        public TableObject? FindByOwnId(string id) => Find(id) ?? State.Objects.FirstOrDefault(o => o is Card { EntryId: { } own } && own == id);

        /// <summary>Where the next object placed into <paramref name="zone"/> goes: a hand slot, or stacked at the zone's centre.</summary>
        internal Vec3 SlotFor(Zone zone)
        {
            var count = ObjectsIn(zone.Id).Count();
            if (zone is HandZone hand)
                return HandSlot(hand, count, Card.DefaultSize);
            var p = zone.Transform.Position;
            var floor = Math.Max(0f, p.Y - zone.Size.Y * zone.Transform.Scale.Y / 2f);
            return new Vec3(p.X, floor + 0.001f * (count + 1), p.Z);
        }

        /// <summary>Ids of the zones (not snap points) whose box holds the object's position. Empty inside a container.</summary>
        public IReadOnlyList<string> ZonesOf(string objectId) => _zonesOf.TryGetValue(objectId, out var zones) ? zones : NoZones;

        /// <summary>Objects on the table inside the zone, in state order.</summary>
        public IEnumerable<TableObject> ObjectsIn(string zoneId) =>
            State.Objects.Where(o => _zonesOf.TryGetValue(o.Id, out var zones) && zones.Contains(zoneId));

        /// <summary>The tag naming a die's (or any piece's) owner: <c>owner:&lt;seatId&gt;</c>.</summary>
        public const string OwnerTag = "owner:";

        /// <summary>
        /// The dice lying in a zone: their total, how many there are, and each owner's total (by <see cref="OwnerTag"/>;
        /// dice without an owner count only in the total). A die not rolled yet counts 0.
        /// </summary>
        public (int Total, int Dice, Dictionary<string, int> BySeat) DiceIn(string zoneId)
        {
            var bySeat = new Dictionary<string, int>();
            var total = 0;
            var count = 0;
            foreach (var die in ObjectsIn(zoneId).OfType<Die>())
            {
                var value = die.Value ?? 0;
                total += value;
                count++;
                if (die.Tags.FirstOrDefault(t => t.StartsWith(OwnerTag, System.StringComparison.Ordinal)) is { } tag)
                {
                    var seat = tag.Substring(OwnerTag.Length);
                    bySeat[seat] = (bySeat.TryGetValue(seat, out var sum) ? sum : 0) + value;
                }
            }
            return (total, count, bySeat);
        }

        /// <summary>
        /// True when <paramref name="seat"/> may not see the object's face: it lies in another seat's hand zone, or in a
        /// hidden zone that does not list the seat. Null seat means the host, who sees everything except other hands.
        /// </summary>
        public bool IsHiddenFrom(string objectId, string? seat)
        {
            foreach (var zoneId in ZonesOf(objectId))
            {
                switch (State.Zones.FirstOrDefault(z => z.Id == zoneId))
                {
                    case HandZone hand when hand.Owner != seat:
                        return true;
                    case HiddenZone hidden when !hidden.Reveals(seat, host: seat == null, State):
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The hidden zone that keeps the object out of sight of a viewer at <paramref name="seat"/> (hosting when
        /// <paramref name="host"/>), or null. Hand zones are not considered: the view shows their backs instead.
        /// </summary>
        public HiddenZone? HiddenZoneHiding(string objectId, string? seat, bool host)
        {
            foreach (var zoneId in ZonesOf(objectId))
                if (State.Zones.FirstOrDefault(z => z.Id == zoneId) is HiddenZone hidden && !hidden.Reveals(seat, host, State))
                    return hidden;
            return null;
        }

        /// <summary>Objects inside a bag, top first.</summary>
        public IEnumerable<TableObject> ContentsOf(string bagId) => State.Objects.Where(o => o.Container == bagId);

        /// <summary>Runs the command, then appends zone enter/leave events for every object it touched.</summary>
        public CommandResult Execute(Command command)
        {
            var result = Dispatch(command);
            if (!result.Ok)
                return result;
            var events = WithZoneEvents(result.Events);
            if (State.Blind != null && command.Actor != null && command is MoveCommand or TakeCommand or FlipCommand or DropCommand or DealCommand)
                HideBlindMoves(command.Actor, events);
            return result.WithEvents(events);
        }

        private CommandResult Show(ShowCommand c)
        {
            if (RequireOnTable(c.ObjectId, "objectId", out var obj, allowLocked: true) is { } fail)
                return fail;
            var card = obj switch
            {
                Card single => single,
                Deck { Cards: { Count: > 0 } } deck => CardFromEntry(deck, deck.Cards[0]),
                _ => null,
            };
            if (card == null)
                return CommandResult.Fail("objectId", ErrorCodes.InvalidValue, $"'{c.ObjectId}' is not a card or a deck with cards.", "Show a card, or a deck to show its top card.");
            // Shown to some players only: what it is stays theirs (the rules say who looked).
            if (c.Seats != null)
                return CommandResult.Success(new List<StateEvent> { new CardShown(obj.Id, card, c.Actor, c.Seats) });
            var name = Names.Of(State, card);
            return CommandResult.Success(new List<StateEvent>
            {
                new CardShown(obj.Id, card, c.Actor),
                new GameMessage(Names.Message(State, "shows", ("seat", Names.Seat(State, c.Actor)), ("card", name))),
            });
        }

        private CommandResult SetBlindRound(BlindRoundCommand c)
        {
            var events = new List<StateEvent> { new TableSettingsChanged() };
            if (c.On)
            {
                // Starting again while one runs keeps what is hidden.
                State.Blind ??= new BlindRound();
                events.Add(new GameMessage(Names.Message(State, "blindRound", ("seat", Names.Seat(State, c.Actor)))));
                return CommandResult.Success(events);
            }
            if (State.Blind is { } blind)
            {
                foreach (var pair in blind.Before)
                    if (_objects.TryGetValue(pair.Key, out var obj))
                    {
                        obj.InvisibleTo = pair.Value;
                        events.Add(new ObjectUpdated(obj.Id));
                    }
                events.Add(new GameMessage(Names.Message(State, "reveal", blind.Before.Count,
                    ("seat", Names.Seat(State, c.Actor)), ("count", blind.Before.Count.ToString()))));
            }
            State.Blind = null;
            return CommandResult.Success(events);
        }

        /// <summary>In a blind round: what <paramref name="seat"/> just moved is seen only by that seat until the reveal.</summary>
        private void HideBlindMoves(string seat, List<StateEvent> events)
        {
            var blind = State.Blind!;
            var mine = State.Players.Seats.FirstOrDefault(s => s.Id == seat)?.Color?.ToLowerInvariant();
            if (mine == null)
                return;
            var others = State.Players.Seats.Select(s => s.Color.ToLowerInvariant()).Where(c => c != mine).Distinct().ToList();
            var touched = events.Where(e => e is ObjectUpdated or ObjectAdded or ContainerChanged).Select(e => e.ObjectId)
                .Where(id => id.Length > 0).Distinct().ToList();
            foreach (var id in touched)
            {
                // Cards in a hand are hidden by the hand already; pieces inside a bag are not on the table.
                if (!_objects.TryGetValue(id, out var obj) || obj.Container != null || ZonesOf(id).Any(z => State.Zones.FirstOrDefault(x => x.Id == z) is HandZone))
                    continue;
                if (!blind.Before.ContainsKey(id))
                    blind.Before[id] = new List<string>(obj.InvisibleTo);
                blind.MovedBy[id] = seat;
                obj.InvisibleTo = blind.Before[id].Concat(others).Distinct().ToList();
                events.Add(new ObjectUpdated(id));
            }
        }

        private List<StateEvent> WithZoneEvents(IReadOnlyList<StateEvent> events)
        {
            var all = new List<StateEvent>(events);
            var touched = new List<string>();
            foreach (var e in events)
                if (e.ObjectId.Length > 0 && !touched.Contains(e.ObjectId))
                    touched.Add(e.ObjectId);
            foreach (var id in touched)
            {
                var before = ZonesOf(id);
                var after = _objects.TryGetValue(id, out var obj) ? ComputeZones(obj) : NoZones;
                foreach (var zone in before)
                    if (!after.Contains(zone))
                        all.Add(new ZoneLeft(id, zone));
                foreach (var zone in after)
                    if (!before.Contains(zone))
                        all.Add(new ZoneEntered(id, zone));
                if (obj != null)
                    _zonesOf[id] = after;
                else
                    _zonesOf.Remove(id);
            }

            // TTS arranges hands by itself: whenever a hand gains or loses a card, lay it out again, keeping the
            // player's left-to-right order.
            var hands = all.OfType<ZoneEntered>().Select(e => e.ZoneId).Concat(all.OfType<ZoneLeft>().Select(e => e.ZoneId))
                .Distinct().Select(id => State.Zones.FirstOrDefault(z => z.Id == id)).OfType<HandZone>().ToList();
            foreach (var hand in hands)
                LayoutHand(hand, all);
            // A layout zone arranges what is dropped into it, unless it only lays out on demand (G18).
            var layouts = all.OfType<ZoneEntered>().Select(e => e.ZoneId).Distinct()
                .Select(id => State.Zones.FirstOrDefault(z => z.Id == id)).OfType<LayoutZone>().Where(z => !z.ManualOnly).ToList();
            foreach (var layout in layouts)
                Arrange(layout, all);
            return all;
        }

        /// <summary>Spreads the objects in a hand evenly from its left edge, in their current left-to-right order.</summary>
        private void LayoutHand(HandZone hand, List<StateEvent> events)
        {
            var members = ObjectsIn(hand.Id)
                .OrderBy(o => Geometry.InverseRotate(hand.Transform.Rotation, Geometry.Subtract(o.Transform.Position, hand.Transform.Position)).X)
                .ThenBy(o => o.Id, StringComparer.Ordinal)
                .ToList();
            for (var i = 0; i < members.Count; i++)
            {
                var obj = members[i];
                var size = (obj as Card)?.Size ?? Card.DefaultSize;
                var slot = HandSlot(hand, i, size, members.Count);
                var p = obj.Transform.Position;
                if (Math.Abs(p.X - slot.X) < 0.001f && Math.Abs(p.Z - slot.Z) < 0.001f)
                    continue;
                obj.Transform.Position = new Vec3(slot.X, p.Y, slot.Z);
                if (!events.Any(e => e is ObjectUpdated && e.ObjectId == obj.Id))
                    events.Add(new ObjectUpdated(obj.Id));
            }
        }

        private List<string> ComputeZones(TableObject obj)
        {
            if (obj.Container != null)
                return NoZones;
            var zones = new List<string>();
            foreach (var zone in State.Zones)
                // An object that never joins hands (G39) is not in a hand zone even when it lies inside one.
                if (Geometry.Contains(zone, obj.Transform.Position, Geometry.HalfHeight(obj)) && (obj.Hands || !(zone is HandZone)))
                    zones.Add(zone.Id);
            return zones;
        }

        private CommandResult Dispatch(Command command) => command switch
        {
            MoveCommand c => Move(c),
            TakeCommand c => Take(c),
            InsertCommand c => Insert(c),
            FlipCommand c => Flip(c),
            ShuffleCommand c => Shuffle(c),
            SearchCommand c => Search(c),
            PeekCommand c => RequireOnTable(c.ObjectId, "objectId", out _, allowLocked: true) ?? CommandResult.Success(new List<StateEvent>()),
            ShowCommand c => Show(c),
            BlindfoldCommand _ => CommandResult.Success(new List<StateEvent>()),
            DropCommand c => RequireOnTable(c.ObjectId, "objectId", out _, allowLocked: true) ?? CommandResult.Success(new List<StateEvent>()),
            PickUpCommand c => RequireOnTable(c.ObjectId, "objectId", out _, allowLocked: true) ?? CommandResult.Success(new List<StateEvent>()),
            SetButtonsCommand c => SetButtons(c),
            SetInputsCommand c => SetInputs(c),
            TypeInputCommand c => TypeInput(c),
            SetScriptUiCommand c => SetScriptUi(c),
            SpawnCommand c => Spawn(c),
            SetCustomCommand c => SetCustom(c),
            SetUiAttributesCommand c => SetUiAttributes(c),
            UiEventCommand c => UiEvent(c),
            ClickButtonCommand c => ClickButton(c),
            ScriptingKeyCommand c => c.Index >= 1 && c.Index <= ScriptingKeyCommand.MaxIndex
                ? CommandResult.Success(new List<StateEvent>())
                : CommandResult.Fail("index", ErrorCodes.InvalidValue, $"Scripting keys are 1 to {ScriptingKeyCommand.MaxIndex}."),
            TypeNumberCommand c => RequireOnTable(c.ObjectId, "objectId", out _, allowLocked: true) ?? (c.Number < 0
                ? CommandResult.Fail("number", ErrorCodes.InvalidValue, "A typed number is 0 or more.")
                : CommandResult.Success(new List<StateEvent>())),
            DealCommand c => Deal(c),
            LockCommand c => Lock(c),
            GroupCommand c => Group(c),
            RollCommand c => Roll(c),
            SetPhysicsCommand c => SetPhysics(c),
            BlindRoundCommand c => SetBlindRound(c),
            SetGridCommand c => SetGrid(c),
            SetLightingCommand c => SetLighting(c),
            SetLightCommand c => SetLight(c),
            PlaySoundCommand c => PlaySound(c),
            MoveZoneCommand c => MoveZone(c),
            SetMusicCommand c => SetMusic(c),
            SetScreenCommand c => SetScreen(c),
            QueueSongCommand c => QueueSong(c),
            RemoveSongCommand c => RemoveSong(c),
            PlayNextSongCommand c => PlayNextSong(c),
            AddDrawingCommand c => AddDrawing(c),
            SetNoteTabCommand c => SetNoteTab(c),
            DeleteNoteTabCommand c => DeleteNoteTab(c),
            EraseDrawingCommand c => EraseDrawing(c),
            ClearDrawingsCommand c => ClearDrawings(c),
            SetTurnOrderCommand c => SetTurnOrder(c),
            EndTurnCommand c => EndTurn(c),
            ScaleCommand c => Scale(c),
            DeleteCommand c => Delete(c),
            SetPageCommand c => SetPage(c),
            SetJointCommand c => SetJoint(c),
            PlayEffectCommand c => PlayEffect(c),
            CloneCommand c => Clone(c),
            SetStateCommand c => SetState(c),
            CutCommand c => Cut(c),
            SplitCommand c => Split(c),
            SpreadCommand c => Spread(c),
            SetDieValueCommand c => SetDieValue(c),
            SetCounterCommand c => SetCounter(c),
            PressCalculatorKeyCommand c => PressCalculatorKey(c),
            AddCalculatorCommand c => AddCalculator(c),
            SetClockCommand c => SetClock(c),
            SetPropertiesCommand c => SetProperties(c),
            ArrangeZoneCommand c => ArrangeZone(c),
            RandomizeZoneCommand c => RandomizeZone(c),
            _ => CommandResult.Fail("", ErrorCodes.UnknownCommand, $"Unknown command '{command.GetType().Name}'."),
        };

        private CommandResult Move(MoveCommand c)
        {
            if (RequireOnTable(c.ObjectId, "objectId", out var obj, allowLocked: c.EvenLocked) is { } fail)
                return fail;
            if (c.DieValue.HasValue)
            {
                if (!(obj is Die die))
                    return CommandResult.Fail("dieValue", ErrorCodes.WrongObjectType, $"'{obj.Id}' is a {obj.Type}; only dice have a value.");
                if (c.DieValue < 1 || c.DieValue > die.Faces)
                    return CommandResult.Fail("dieValue", ErrorCodes.InvalidValue, $"Value {c.DieValue} is outside 1..{die.Faces}.");
                die.Value = c.DieValue;
            }

            obj.Transform.Position = c.Position;
            obj.Transform.Rotation = c.Rotation;
            return CommandResult.Success(new List<StateEvent> { new ObjectUpdated(obj.Id) });
        }

        private CommandResult Take(TakeCommand c)
        {
            if (RequireOnTable(c.ContainerId, "containerId", out var container, allowLocked: true) is { } fail)
                return fail;

            switch (container)
            {
                case Deck deck:
                {
                    if (PickIndex(deck.Cards.Count, c, i => deck.Cards[i].Id, "card", deck.Id, out var index) is { } pickFail)
                        return pickFail;

                    var entry = deck.Cards[index];
                    deck.Cards.RemoveAt(index);
                    var card = CardFromEntry(deck, entry);
                    card.Transform.Position = c.Position ?? Beside(deck);
                    card.Transform.Rotation = c.Rotation ?? deck.Transform.Rotation;
                    AddObject(card);
                    var result = CommandResult.Success(new List<StateEvent> { new ObjectUpdated(deck.Id), new ObjectAdded(card.Id, deck.Id) });
                    result.CreatedId = card.Id;
                    return result;
                }
                case Bag bag:
                {
                    var contents = ContentsOf(bag.Id).ToList();
                    if (PickIndex(contents.Count, c, i => contents[i].Id, "object", bag.Id, out var index) is { } pickFail)
                        return pickFail;

                    var picked = contents[index];
                    var events = new List<StateEvent>();
                    if (bag is InfiniteBag)
                    {
                        picked = TableJson.CloneObject(picked);
                        picked.Id = Mint(picked.Id);
                        foreach (var state in picked.States.Values)
                            state.Id = Mint(state.Id);
                        picked.Container = null;
                        AddObject(picked);
                        events.Add(new ObjectAdded(picked.Id, bag.Id));
                    }
                    else
                    {
                        picked.Container = null;
                        events.Add(new ContainerChanged(picked.Id, bag.Id, null));
                    }
                    picked.Transform.Position = c.Position ?? Beside(bag);
                    if (c.Rotation.HasValue)
                        picked.Transform.Rotation = c.Rotation.Value;
                    var result = CommandResult.Success(events);
                    result.CreatedId = picked.Id;
                    return result;
                }
                case var stack when Stacks.CountOf(stack) > 1:
                {
                    var have = Stacks.CountOf(stack);
                    var count = c.Count ?? 1;
                    if (count < 1 || count >= have)
                        return CommandResult.Fail("count", ErrorCodes.InvalidValue, $"Take 1 to {have - 1} of the {have} in '{stack.Id}'.",
                            "To move the whole stack, move it.");
                    var piece = TableJson.CloneObject(stack);
                    piece.Id = Mint(stack.Id);
                    piece.Locked = false;
                    Stacks.SetCount(piece, count);
                    Stacks.SetCount(stack, have - count);
                    piece.Transform.Position = c.Position ?? Beside(stack);
                    if (c.Rotation.HasValue)
                        piece.Transform.Rotation = c.Rotation.Value;
                    AddObject(piece);
                    var result = CommandResult.Success(new List<StateEvent> { new ObjectUpdated(stack.Id), new ObjectAdded(piece.Id, stack.Id) });
                    result.CreatedId = piece.Id;
                    return result;
                }
                default:
                    return CommandResult.Fail("containerId", ErrorCodes.WrongObjectType,
                        $"'{container.Id}' is a {container.Type}; only decks, bags and stacks can be taken from.");
            }
        }

        private CommandResult Insert(InsertCommand c)
        {
            if (RequireOnTable(c.ObjectId, "objectId", out var obj, allowLocked: c.EvenLocked) is { } fail)
                return fail;
            if (c.IntoId == c.ObjectId)
                return CommandResult.Fail("intoId", ErrorCodes.InvalidTarget, "An object cannot be inserted into itself.");
            if (RequireOnTable(c.IntoId, "intoId", out var target, allowLocked: true) is { } targetFail)
                return targetFail;

            var events = new List<StateEvent>();
            switch (target)
            {
                case InfiniteBag infinite when ContentsOf(infinite.Id).FirstOrDefault() is { } template:
                {
                    // TTS: a piece like the bag's own goes back in (a coin into the bank), which takes it off the table.
                    if (!Stacks.SamePiece(obj, template))
                        return CommandResult.Fail("intoId", ErrorCodes.InvalidTarget,
                            $"{Names.Of(infinite)} only takes back {Names.Of(template)}.");
                    RemoveObject(obj);
                    events.Add(new ObjectRemoved(obj.Id));
                    break;
                }
                case Bag bag:
                    PutInBag(obj, bag, c.AtBottom);
                    events.Add(new ContainerChanged(obj.Id, null, bag.Id));
                    break;
                case Deck deck when obj is Card || obj is Deck:
                    AddToDeck(deck, obj, c.AtBottom, events);
                    events.Add(new ObjectUpdated(deck.Id));
                    break;
                case Card card when obj is Card:
                {
                    var deck = NewDeckAt(card, events);
                    AddToDeck(deck, card, atBottom: true, events);
                    AddToDeck(deck, obj, c.AtBottom, events);
                    var result = CommandResult.Success(events);
                    result.CreatedId = deck.Id;
                    return result;
                }
                case var stack when Stacks.SameKind(obj, stack):
                {
                    var total = Stacks.CountOf(stack) + Stacks.CountOf(obj);
                    if (total > Stacks.MaxCount)
                        return CommandResult.Fail("intoId", ErrorCodes.LimitReached, $"A stack holds at most {Stacks.MaxCount}.", "Start another stack.");
                    Stacks.SetCount(stack, total);
                    RemoveObject(obj);
                    events.Add(new ObjectRemoved(obj.Id));
                    events.Add(new ObjectUpdated(stack.Id));
                    break;
                }
                case Card card when obj is Deck deck:
                {
                    // The deck lands on the card: the card ends up at the bottom (or top) and the deck takes its place.
                    deck.Transform.Position = card.Transform.Position;
                    AddToDeck(deck, card, atBottom: !c.AtBottom, events);
                    events.Add(new ObjectUpdated(deck.Id));
                    break;
                }
                default:
                    return CommandResult.Fail("intoId", ErrorCodes.InvalidTarget,
                        $"A {obj.Type} cannot go into a {target.Type}. Bags take anything; decks and cards take cards and decks; a stack takes the same piece.");
            }
            return CommandResult.Success(events);
        }

        private CommandResult Flip(FlipCommand c)
        {
            if (RequireOnTable(c.ObjectId, "objectId", out var obj) is { } fail)
                return fail;

            var r = obj.Transform.Rotation;
            obj.Transform.Rotation = new Vec3(r.X, r.Y, Geometry.NormalizeAngle(r.Z + 180f));
            if (obj is Deck deck)
                deck.Cards.Reverse();
            return CommandResult.Success(new List<StateEvent> { new ObjectUpdated(obj.Id) });
        }

        private CommandResult Shuffle(ShuffleCommand c)
        {
            if (RequireOnTable(c.ObjectId, "objectId", out var obj, allowLocked: true) is { } fail)
                return fail;

            switch (obj)
            {
                case Deck deck:
                    ShuffleInPlace(deck.Cards);
                    break;
                case Bag bag when !(bag is InfiniteBag):
                {
                    var slots = new List<int>();
                    for (var i = 0; i < State.Objects.Count; i++)
                        if (State.Objects[i].Container == bag.Id)
                            slots.Add(i);
                    var contents = slots.Select(i => State.Objects[i]).ToList();
                    ShuffleInPlace(contents);
                    for (var i = 0; i < slots.Count; i++)
                        State.Objects[slots[i]] = contents[i];
                    break;
                }
                default:
                    return CommandResult.Fail("objectId", ErrorCodes.WrongObjectType, $"'{obj.Id}' is a {obj.Type}; only decks and bags can be shuffled.");
            }
            return CommandResult.Success(new List<StateEvent> { new Shuffled(obj.Id), new ObjectUpdated(obj.Id) });
        }

        private CommandResult Search(SearchCommand c)
        {
            if (RequireOnTable(c.ContainerId, "containerId", out var obj, allowLocked: true) is { } fail)
                return fail;

            var result = CommandResult.Success(new List<StateEvent> { new Searched(obj.Id, c.Actor) });
            switch (obj)
            {
                case Deck deck:
                    result.DeckContents = deck.Cards.ToList();
                    return result;
                case Bag bag:
                    result.BagContents = ContentsOf(bag.Id).Select(o => o.Id).ToList();
                    return result;
                default:
                    return CommandResult.Fail("containerId", ErrorCodes.WrongObjectType, $"'{obj.Id}' is a {obj.Type}; only decks and bags can be searched.");
            }
        }

        private CommandResult Deal(DealCommand c)
        {
            if (RequireOnTable(c.DeckId, "deckId", out var obj, allowLocked: true) is { } fail)
                return fail;
            if (!(obj is Deck deck))
                return CommandResult.Fail("deckId", ErrorCodes.WrongObjectType, $"'{obj.Id}' is a {obj.Type}; only decks can be dealt from.");
            if (c.Count < 1)
                return CommandResult.Fail("count", ErrorCodes.InvalidValue, $"Count must be at least 1, got {c.Count}.");

            var seatIds = c.Seats.Count > 0 ? c.Seats : State.Players.Seats.Select(s => s.Id).ToList();
            if (seatIds.Count == 0)
                return CommandResult.Fail("seats", ErrorCodes.UnknownSeat, "The table has no seats to deal to.", "Add seats to players.seats first.");

            var hands = new List<(string Seat, HandZone Zone, int Slot)>();
            for (var i = 0; i < seatIds.Count; i++)
            {
                var seat = State.Players.Seats.FirstOrDefault(s => s.Id == seatIds[i]);
                if (seat == null)
                    return CommandResult.Fail($"seats[{i}]", ErrorCodes.UnknownSeat, $"Seat '{seatIds[i]}' does not exist.",
                        Suggest.OneOf(seatIds[i], State.Players.Seats.Select(s => s.Id), "a seat id"));
                if (!(State.Zones.FirstOrDefault(z => z.Id == seat.HandZone) is HandZone zone))
                    return CommandResult.Fail($"seats[{i}]", ErrorCodes.NoHandZone, $"Seat '{seat.Id}' has no hand zone.", "Set the seat's handZone to a HandZone.");
                hands.Add((seat.Id, zone, State.Objects.Count(o => o.Container == null && Geometry.Contains(zone, o.Transform.Position))));
            }

            var events = new List<StateEvent>();
            var dealt = hands.ToDictionary(h => h.Seat, _ => new List<string>());
            for (var round = 0; round < c.Count && deck.Cards.Count > 0; round++)
            {
                for (var h = 0; h < hands.Count && deck.Cards.Count > 0; h++)
                {
                    var (seatId, zone, slot) = hands[h];
                    var card = CardFromEntry(deck, deck.Cards[0]);
                    deck.Cards.RemoveAt(0);
                    card.Transform.Position = HandSlot(zone, slot, card.Size ?? Card.DefaultSize);
                    card.Transform.Rotation = new Vec3(0f, Geometry.FacingYaw(zone.Transform.Rotation.Y), 0f);
                    AddObject(card);
                    events.Add(new ObjectAdded(card.Id, deck.Id));
                    dealt[seatId].Add(card.Id);
                    hands[h] = (seatId, zone, slot + 1);
                }
            }
            events.Add(new ObjectUpdated(deck.Id));
            var result = CommandResult.Success(events);
            result.Dealt = dealt;
            return result;
        }

        private CommandResult SetPhysics(SetPhysicsCommand c)
        {
            if (!System.Enum.IsDefined(typeof(PhysicsMode), c.Mode))
                return CommandResult.Fail("mode", ErrorCodes.InvalidValue, $"Unknown physics mode {(int)c.Mode}.", "Use full, semiLock or locked.");
            State.Physics = c.Mode;
            return CommandResult.Success(new List<StateEvent> { new TableSettingsChanged() });
        }

        private CommandResult SetScreen(SetScreenCommand c)
        {
            if (c.VideoId == null)
            {
                State.Screen = null;
                return CommandResult.Success(new List<StateEvent> { new ScreenChanged() });
            }
            if (YouTube.VideoId(c.VideoId) != c.VideoId)
                return CommandResult.Fail("videoId", ErrorCodes.InvalidValue, $"'{c.VideoId}' is not a YouTube video id.", "Use the 11 characters after v= in a YouTube link.");
            if (!(c.Position >= 0 && c.Position < 86_400) || !(c.At >= 0) || double.IsInfinity(c.At))
                return CommandResult.Fail("position", ErrorCodes.InvalidValue, "A position is 0 or more seconds, at a time on the host's clock.");
            State.Screen = new ScreenState { VideoId = c.VideoId, Playing = c.Playing, Position = c.Position, At = c.At };
            return CommandResult.Success(new List<StateEvent> { new ScreenChanged() });
        }

        /// <summary>The most songs the karaoke queue holds.</summary>
        public const int MaxQueuedSongs = 50;

        private CommandResult QueueSong(QueueSongCommand c)
        {
            if (YouTube.VideoId(c.VideoId) != c.VideoId)
                return CommandResult.Fail("videoId", ErrorCodes.InvalidValue, $"'{c.VideoId}' is not a YouTube video id.", "Use the 11 characters after v= in a YouTube link.");
            if (State.SongQueue.Count >= MaxQueuedSongs)
                return CommandResult.Fail("songQueue", ErrorCodes.LimitReached, $"The queue holds at most {MaxQueuedSongs} songs.");
            var title = c.Title?.Trim();
            if (title != null)
            {
                title = new string(title.Where(ch => !char.IsControl(ch)).ToArray());
                if (title.Length > QueuedSong.MaxTitle)
                    title = title.Substring(0, QueuedSong.MaxTitle);
                if (title.Length == 0)
                    title = null;
            }
            State.SongQueue.Add(new QueuedSong { VideoId = c.VideoId, Title = title, Seat = c.Actor });
            return CommandResult.Success(new List<StateEvent> { new ScreenChanged() });
        }

        private CommandResult RemoveSong(RemoveSongCommand c)
        {
            if (c.Index < 0 || c.Index >= State.SongQueue.Count)
                return CommandResult.Fail("index", ErrorCodes.IndexOutOfRange, $"There is no song {c.Index} in a queue of {State.SongQueue.Count}.");
            var song = State.SongQueue[c.Index];
            if (c.Actor != null && song.Seat != null && song.Seat != c.Actor)
                return CommandResult.Fail("index", ErrorCodes.NotYourSeat, "Only who added a song, or the host, takes it out of the queue.");
            State.SongQueue.RemoveAt(c.Index);
            return CommandResult.Success(new List<StateEvent> { new ScreenChanged() });
        }

        private CommandResult PlayNextSong(PlayNextSongCommand c)
        {
            if (!(c.At >= 0) || double.IsInfinity(c.At))
                return CommandResult.Fail("at", ErrorCodes.InvalidValue, "A time on the host's clock is 0 or more milliseconds.");
            if (State.SongQueue.Count == 0)
            {
                State.Screen = null;
                return CommandResult.Success(new List<StateEvent> { new ScreenChanged() });
            }
            var next = State.SongQueue[0];
            State.SongQueue.RemoveAt(0);
            State.Screen = new ScreenState { VideoId = next.VideoId, Playing = true, Position = 0, At = c.At };
            return CommandResult.Success(new List<StateEvent> { new ScreenChanged() });
        }

        private CommandResult SetMusic(SetMusicCommand c)
        {
            if (c.Music is { } music)
            {
                if (music.Index < -1 || music.Index >= music.Playlist.Count)
                    return CommandResult.Fail("music.index", ErrorCodes.IndexOutOfRange, $"Track {music.Index} is not in a playlist of {music.Playlist.Count}.");
                if (music.AllTracks().FirstOrDefault(t => !MusicTrack.Valid(State, t.Url)) is { } bad)
                    return CommandResult.Fail("music", ErrorCodes.InvalidValue, $"'{bad.Url}' is not a track.",
                        "Tracks are http(s) addresses of audio files, audio assets of the table, or library:<pack>/<track>.");
                if (music.Volume is < 0f or > 1f)
                    return CommandResult.Fail("music.volume", ErrorCodes.InvalidValue, "The music's volume goes from 0 to 1.");
                if (music.Crossfade is < 0f or > 20f)
                    return CommandResult.Fail("music.crossfade", ErrorCodes.InvalidValue, "A crossfade takes 0 to 20 seconds.");
                if (music.Start != null && !music.Playlists.ContainsKey(music.Start))
                    return CommandResult.Fail("music.start", ErrorCodes.InvalidValue, $"There is no playlist '{music.Start}'.");
            }
            State.Music = c.Music?.Clone();
            return CommandResult.Success(new List<StateEvent> { new MusicChanged() });
        }

        private CommandResult MoveZone(MoveZoneCommand c)
        {
            if (!(State.Zones.FirstOrDefault(z => z.Id == c.ZoneId) is { } zone))
                return CommandResult.Fail("zoneId", ErrorCodes.UnknownZone, $"No zone '{c.ZoneId}'.");
            if (c.Size is { } size && (size.X <= 0f || size.Y <= 0f || size.Z <= 0f))
                return CommandResult.Fail("size", ErrorCodes.InvalidValue, "A zone's size must be positive.");
            if (c.Position is { } position)
                zone.Transform.Position = position;
            if (c.Rotation is { } rotation)
                zone.Transform.Rotation = rotation;
            if (c.Size is { } newSize)
                zone.Size = newSize;
            var events = new List<StateEvent> { new ZoneChanged(zone.Id) };
            if (zone.ScriptUi.Count > 0)
                events.Add(new ScriptUiChanged(zone.Id));
            return CommandResult.Success(events);
        }

        private CommandResult SetLighting(SetLightingCommand c)
        {
            if (c.LightScale is < 0f or > 8f || c.AmbientScale is < 0f or > 8f)
                return CommandResult.Fail("lightScale", ErrorCodes.InvalidValue, "Light scales go from 0 to 8.");
            if (c.LightTint != null && (c.LightTint.Length != 3 || c.LightTint.Any(v => !(v >= 0f && v <= 2f))))
                return CommandResult.Fail("lightTint", ErrorCodes.InvalidValue, "A tint is three numbers from 0 to 2.");
            State.Environment ??= new EnvironmentDef();
            var lighting = State.Environment.Lighting;
            lighting.LightScale = c.LightScale;
            lighting.AmbientScale = c.AmbientScale;
            lighting.LightTint = c.LightTint;
            return CommandResult.Success(new List<StateEvent> { new LightingChanged() });
        }

        private CommandResult PlaySound(PlaySoundCommand c)
        {
            if (string.IsNullOrWhiteSpace(c.Sound) || !SoundCues.Known(State, c.Sound))
                return CommandResult.Fail("sound", ErrorCodes.InvalidValue, $"'{c.Sound}' is not a sound.",
                    "Use a built-in cue (" + string.Join(", ", SoundCues.Builtin) + "), an audio asset's id, or library:<pack>/<track>.");
            if (c.Volume is < 0f or > 1f)
                return CommandResult.Fail("volume", ErrorCodes.InvalidValue, "A sound's volume goes from 0 to 1.");
            return CommandResult.Success(new List<StateEvent> { new SoundCue(c.Sound, c.Volume ?? 1f, c.Seats) });
        }

        private CommandResult SetLight(SetLightCommand c)
        {
            if (c.Light != SetLightCommand.Room && c.Light != SetLightCommand.TableLight)
                return CommandResult.Fail("light", ErrorCodes.InvalidValue, $"'{c.Light}' is not a light.", "Use room (the lamp and the room) or table (the felt and the pieces).");
            if (c.Scale is < 0f or > 8f)
                return CommandResult.Fail("scale", ErrorCodes.InvalidValue, "A light's scale goes from 0 (off) to 8; 1 is normal.");
            if (c.Tint != null && (c.Tint.Length != 3 || c.Tint.Any(v => !(v >= 0f && v <= 2f))))
                return CommandResult.Fail("tint", ErrorCodes.InvalidValue, "A tint is three numbers from 0 to 2.");
            if (c.Seconds is < 0f or > 30f)
                return CommandResult.Fail("seconds", ErrorCodes.InvalidValue, "A light change takes 0 to 30 seconds.");
            State.Environment ??= new EnvironmentDef();
            var lighting = State.Environment.Lighting;
            lighting.Fade = c.Seconds;
            if (c.Light == SetLightCommand.Room)
            {
                lighting.RoomScale = c.Scale;
                lighting.RoomTint = c.Tint;
            }
            else
            {
                lighting.TableScale = c.Scale;
                lighting.TableTint = c.Tint;
            }
            return CommandResult.Success(new List<StateEvent> { new LightingChanged() });
        }

        private CommandResult SetGrid(SetGridCommand c)
        {
            if (c.Grid != null && TableValidator.CheckGrid(c.Grid, "grid").FirstOrDefault() is { } bad)
                return CommandResult.Fail(bad.Path, bad.Code, bad.Message);
            State.Grid = c.Grid;
            return CommandResult.Success(new List<StateEvent> { new TableSettingsChanged() });
        }

        // ---- script UI (M5, ADR 0004) ------------------------------------------------------------------------------

        /// <summary>
        /// The global UI (no id), an object's, or a zone's; null with <paramref name="fail"/> when neither is there.
        /// <paramref name="setUi"/> replaces that tree.
        /// </summary>
        private List<UiNode>? UiOf(string? objectId, out Action<List<UiNode>>? setUi, out CommandResult? fail)
        {
            fail = null;
            setUi = null;
            if (objectId == null)
            {
                setUi = nodes => State.ScriptUi = nodes;
                return State.ScriptUi;
            }
            if (State.Zones.FirstOrDefault(z => z.Id == objectId) is { } zone)
            {
                setUi = nodes => zone.ScriptUi = nodes;
                return zone.ScriptUi;
            }
            fail = RequireOnTable(objectId, "objectId", out var obj, allowLocked: true);
            if (fail != null)
                return null;
            setUi = nodes => obj!.ScriptUi = nodes;
            return obj!.ScriptUi;
        }

        private CommandResult SetScriptUi(SetScriptUiCommand c)
        {
            if (!(UiOf(c.ObjectId, out var setUi, out var fail) is { }))
                return fail!;
            if (TableValidator.CheckUi(c.Nodes, "nodes").FirstOrDefault() is { } bad)
                return CommandResult.Fail(bad.Path, bad.Code, bad.Message);
            setUi!(c.Nodes);
            return CommandResult.Success(new List<StateEvent> { new ScriptUiChanged(c.ObjectId) });
        }

        private CommandResult SetUiAttributes(SetUiAttributesCommand c)
        {
            if (!(UiOf(c.ObjectId, out _, out var fail) is { } nodes))
                return fail!;
            if (!(UiNode.Find(nodes, c.ElementId) is { } node))
                return CommandResult.Fail("elementId", ErrorCodes.InvalidValue, $"No UI element has the id '{c.ElementId}'.");
            if (c.Attributes.Values.Any(v => v != null && v.Length > UiNode.MaxValueLength) || (c.Value?.Length ?? 0) > UiNode.MaxValueLength)
                return CommandResult.Fail("attributes", ErrorCodes.InvalidValue, $"A UI value has at most {UiNode.MaxValueLength} characters.");
            foreach (var pair in c.Attributes)
            {
                if (pair.Value == null)
                    node.Attributes.Remove(pair.Key);
                else
                    node.Attributes[pair.Key] = UiNode.Normalize(pair.Key, pair.Value);
            }
            if (c.SetValue)
                node.Value = c.Value;
            return CommandResult.Success(new List<StateEvent> { new ScriptUiChanged(c.ObjectId) });
        }

        private CommandResult UiEvent(UiEventCommand c)
        {
            if (!(UiOf(c.ObjectId, out _, out var fail) is { } nodes))
                return fail!;
            return UiAt(nodes, c.Path) is { } node && node.Get(c.Event) != null
                ? CommandResult.Success(new List<StateEvent>())
                : CommandResult.Fail("path", ErrorCodes.InvalidValue, $"No UI element there handles {c.Event}.");
        }

        /// <summary>The element at a path of child indices, or null.</summary>
        public static UiNode? UiAt(List<UiNode> nodes, IReadOnlyList<int> path)
        {
            UiNode? node = null;
            var level = nodes;
            foreach (var i in path)
            {
                if (i < 0 || i >= level.Count)
                    return null;
                node = level[i];
                level = node.Children;
            }
            return node;
        }

        // ---- script buttons (M5) ------------------------------------------------------------------------------------

        private CommandResult SetButtons(SetButtonsCommand c)
        {
            if (RequireOnTable(c.ObjectId, "objectId", out var obj, allowLocked: true) is { } fail)
                return fail;
            if (c.Buttons.Count > ButtonDef.MaxButtons)
                return CommandResult.Fail("buttons", ErrorCodes.LimitReached, $"An object holds at most {ButtonDef.MaxButtons} buttons.");
            for (var i = 0; i < c.Buttons.Count; i++)
                if (TableValidator.CheckButton(c.Buttons[i], $"buttons[{i}]").FirstOrDefault() is { } bad)
                    return CommandResult.Fail(bad.Path, bad.Code, bad.Message);
            obj.Buttons = c.Buttons;
            return CommandResult.Success(new List<StateEvent> { new ButtonsChanged(obj.Id) });
        }

        private CommandResult SetInputs(SetInputsCommand c)
        {
            if (RequireOnTable(c.ObjectId, "objectId", out var obj, allowLocked: true) is { } fail)
                return fail;
            if (c.Inputs.Count > InputDef.MaxInputs)
                return CommandResult.Fail("inputs", ErrorCodes.LimitReached, $"An object holds at most {InputDef.MaxInputs} inputs.");
            for (var i = 0; i < c.Inputs.Count; i++)
                if (TableValidator.CheckInput(c.Inputs[i], $"inputs[{i}]").FirstOrDefault() is { } bad)
                    return CommandResult.Fail(bad.Path, bad.Code, bad.Message);
            obj.Inputs = c.Inputs;
            return CommandResult.Success(new List<StateEvent> { new InputsChanged(obj.Id) });
        }

        private CommandResult TypeInput(TypeInputCommand c)
        {
            if (RequireOnTable(c.ObjectId, "objectId", out var obj, allowLocked: true) is { } fail)
                return fail;
            if (c.Index < 0 || c.Index >= obj.Inputs.Count)
                return CommandResult.Fail("index", ErrorCodes.InvalidValue, $"'{obj.Id}' has no input {c.Index}.");
            if ((c.Value ?? "").Length > InputDef.MaxLength)
                return CommandResult.Fail("value", ErrorCodes.InvalidValue, $"An input holds at most {InputDef.MaxLength} characters.");
            obj.Inputs[c.Index].Value = c.Value ?? "";
            return CommandResult.Success(new List<StateEvent> { new InputsChanged(obj.Id) });
        }

        private CommandResult ClickButton(ClickButtonCommand c)
        {
            if (RequireOnTable(c.ObjectId, "objectId", out var obj, allowLocked: true) is { } fail)
                return fail;
            return c.Index < 0 || c.Index >= obj.Buttons.Count
                ? CommandResult.Fail("index", ErrorCodes.InvalidValue, $"'{obj.Id}' has no button {c.Index}.")
                : CommandResult.Success(new List<StateEvent>());
        }

        // ---- notebook (G25) -----------------------------------------------------------------------------------------

        private CommandResult SetNoteTab(SetNoteTabCommand c)
        {
            var tab = c.Tab;
            if (tab == null)
                return CommandResult.Fail("tab", ErrorCodes.MissingField, "No page.");
            if (TableValidator.CheckNoteTab(tab, "tab").FirstOrDefault() is { } bad)
                return CommandResult.Fail(bad.Path, bad.Code, bad.Message);
            var index = string.IsNullOrEmpty(tab.Id) ? -1 : State.Notebook.FindIndex(t => t.Id == tab.Id);
            if (index >= 0)
                State.Notebook[index] = tab;
            else
            {
                if (State.Notebook.Count >= NoteTab.MaxTabs)
                    return CommandResult.Fail("tab", ErrorCodes.LimitReached, $"The notebook holds at most {NoteTab.MaxTabs} pages.");
                if (string.IsNullOrEmpty(tab.Id))
                    for (var n = State.Notebook.Count + 1; ; n++)
                        if (State.Notebook.All(t => t.Id != $"page-{n}"))
                        {
                            tab.Id = $"page-{n}";
                            break;
                        }
                State.Notebook.Add(tab);
            }
            var result = CommandResult.Success(new List<StateEvent> { new NotebookChanged() });
            result.CreatedId = tab.Id;
            return result;
        }

        private CommandResult DeleteNoteTab(DeleteNoteTabCommand c) =>
            State.Notebook.RemoveAll(t => t.Id == c.TabId) == 0
                ? CommandResult.Fail("tabId", ErrorCodes.UnknownObject, $"There is no page '{c.TabId}'.")
                : CommandResult.Success(new List<StateEvent> { new NotebookChanged() });

        // ---- drawings (G28) -----------------------------------------------------------------------------------------

        public const int MaxDrawings = 500;

        private CommandResult AddDrawing(AddDrawingCommand c)
        {
            var d = c.Drawing;
            if (d == null)
                return CommandResult.Fail("drawing", ErrorCodes.MissingField, "No drawing.");
            if (TableValidator.CheckDrawing(d, "drawing").FirstOrDefault() is { } bad)
                return CommandResult.Fail(bad.Path, bad.Code, bad.Message);
            if (State.Drawings.Count >= MaxDrawings)
                return CommandResult.Fail("drawing", ErrorCodes.LimitReached, $"The table holds at most {MaxDrawings} drawings.", "Erase some first.");
            if (string.IsNullOrEmpty(d.Id) || State.Drawings.Any(x => x.Id == d.Id))
                d.Id = MintDrawingId();
            State.Drawings.Add(d);
            var result = CommandResult.Success(new List<StateEvent> { new DrawingsChanged() });
            result.CreatedId = d.Id;
            return result;
        }

        private string MintDrawingId()
        {
            for (var n = State.Drawings.Count + 1; ; n++)
                if (State.Drawings.All(x => x.Id != $"drawing-{n}"))
                    return $"drawing-{n}";
        }

        private CommandResult EraseDrawing(EraseDrawingCommand c)
        {
            var removed = State.Drawings.RemoveAll(d => d.Id == c.DrawingId);
            return removed == 0
                ? CommandResult.Fail("drawingId", ErrorCodes.UnknownObject, $"There is no drawing '{c.DrawingId}'.")
                : CommandResult.Success(new List<StateEvent> { new DrawingsChanged() });
        }

        private CommandResult ClearDrawings(ClearDrawingsCommand c)
        {
            State.Drawings.RemoveAll(d => c.Seat == null || d.Seat == c.Seat);
            return CommandResult.Success(new List<StateEvent> { new DrawingsChanged() });
        }

        // ---- free-play turns (G22) ---------------------------------------------------------------------------------

        private CommandResult SetTurnOrder(SetTurnOrderCommand c)
        {
            if (!c.Enabled)
            {
                State.FreeTurns = null;
                return CommandResult.Success(new List<StateEvent> { new TableSettingsChanged() });
            }
            if (State.Session is { Started: true, Over: false })
                return CommandResult.Fail("", ErrorCodes.GameAlreadyStarted, "The game's rules run the turns.", "Turns can be set when no rules game is running.");
            for (var i = 0; i < c.Seats.Count; i++)
                if (State.Players.Seats.All(s => s.Id != c.Seats[i]))
                    return CommandResult.Fail($"seats[{i}]", ErrorCodes.UnknownSeat, $"Seat '{c.Seats[i]}' does not exist.");
            if (c.Seats.Distinct().Count() != c.Seats.Count)
                return CommandResult.Fail("seats", ErrorCodes.InvalidValue, "A seat appears twice in the turn order.");
            var order = new FreeTurns { Enabled = true, Seats = new List<string>(c.Seats), Reverse = c.Reverse, SkipEmptyHands = c.SkipEmptyHands };
            var seats = TurnSeats(order);
            if (seats.Count == 0)
                return CommandResult.Fail("seats", ErrorCodes.PlayerCount, "There are no seats to take turns.");
            if (c.First != null && !seats.Contains(c.First))
                return CommandResult.Fail("first", ErrorCodes.UnknownSeat, $"Seat '{c.First}' is not in the turn order.");
            order.Current = c.First ?? (State.FreeTurns?.Current is { } kept && seats.Contains(kept) ? kept : seats[0]);
            State.FreeTurns = order;
            return CommandResult.Success(new List<StateEvent> { new TableSettingsChanged() });
        }

        private CommandResult EndTurn(EndTurnCommand c)
        {
            if (!(State.FreeTurns is { Enabled: true, Current: { } current } order))
                return CommandResult.Fail("", ErrorCodes.InvalidValue, "Turns are off.", "The host can switch them on from the table menu.");
            if (c.Actor != null && c.Actor != current)
                return CommandResult.Fail("actor", ErrorCodes.NotYourTurn, $"It is {current}'s turn.").Text("refusal.notYourTurn", ("seat", Names.Seat(State, current)));
            order.Current = NextTurnSeat(order, current);
            order.Count++;
            return CommandResult.Success(new List<StateEvent> { new TableSettingsChanged() });
        }

        private List<string> TurnSeats(FreeTurns order) =>
            order.Seats.Count > 0 ? order.Seats : State.Players.Seats.Select(s => s.Id).ToList();

        /// <summary>The seat after <paramref name="current"/> in play direction, passing over empty hands when asked (but never all of them).</summary>
        public string NextTurnSeat(FreeTurns order, string current)
        {
            var seats = TurnSeats(order);
            var at = seats.IndexOf(current);
            var step = order.Reverse ? -1 : 1;
            for (var i = 1; i <= seats.Count; i++)
            {
                var next = seats[((at + step * i) % seats.Count + seats.Count) % seats.Count];
                if (!order.SkipEmptyHands || !HasEmptyHand(next))
                    return next;
            }
            return seats[((at + step) % seats.Count + seats.Count) % seats.Count];
        }

        private bool HasEmptyHand(string seatId)
        {
            var hand = State.Players.Seats.FirstOrDefault(s => s.Id == seatId)?.HandZone;
            return hand != null && !ObjectsIn(hand).Any();
        }

        private CommandResult Lock(LockCommand c)
        {
            if (RequireOnTable(c.ObjectId, "objectId", out var obj, allowLocked: true) is { } fail)
                return fail;

            obj.Locked = c.Locked;
            return CommandResult.Success(new List<StateEvent> { new ObjectUpdated(obj.Id) });
        }

        private CommandResult Group(GroupCommand c)
        {
            if (c.ObjectIds.Count < 2)
                return CommandResult.Fail("objectIds", ErrorCodes.InvalidValue, "Group needs at least two objects.");
            if (c.ObjectIds.Distinct().Count() != c.ObjectIds.Count)
                return CommandResult.Fail("objectIds", ErrorCodes.InvalidValue, "Each object can appear only once.");

            var objects = new List<TableObject>();
            for (var i = 0; i < c.ObjectIds.Count; i++)
            {
                if (RequireOnTable(c.ObjectIds[i], $"objectIds[{i}]", out var obj) is { } fail)
                    return fail;
                if (!(obj is Card || obj is Deck))
                    return CommandResult.Fail($"objectIds[{i}]", ErrorCodes.WrongObjectType, $"'{obj.Id}' is a {obj.Type}; only cards and decks can be grouped.");
                objects.Add(obj);
            }

            var events = new List<StateEvent>();
            var deck = objects[0] as Deck ?? NewDeckAt(objects[0], events);
            if (deck != objects[0])
                AddToDeck(deck, objects[0], atBottom: true, events);
            foreach (var obj in objects.Skip(1))
                AddToDeck(deck, obj, atBottom: true, events);
            events.Add(new ObjectUpdated(deck.Id));
            var result = CommandResult.Success(events);
            result.CreatedId = deck.Id;
            return result;
        }

        private CommandResult Roll(RollCommand c)
        {
            if (RequireOnTable(c.ObjectId, "objectId", out var obj) is { } fail)
                return fail;
            if (!(obj is Die die))
                return CommandResult.Fail("objectId", ErrorCodes.WrongObjectType, $"'{obj.Id}' is a {obj.Type}; only dice can be rolled.");

            die.Value = 1 + Random.Next(die.Faces);
            return CommandResult.Success(new List<StateEvent> { new DieRolled(die.Id, die.Value.Value), new ObjectUpdated(die.Id) });
        }

        // ---- helpers ------------------------------------------------------------------------------------------

        private CommandResult? RequireOnTable(string id, string field, out TableObject obj, bool allowLocked = false)
        {
            obj = null!;
            if (!_objects.TryGetValue(id, out var found))
                return CommandResult.Fail(field, ErrorCodes.UnknownObject, $"Object '{id}' does not exist.",
                    Suggest.OneOf(id, _objects.Keys, "an object id"));
            if (found.Container != null)
                return CommandResult.Fail(field, ErrorCodes.NotOnTable, $"'{id}' is inside '{found.Container}'.", $"Take it out of '{found.Container}' first.");
            if (found.Locked && !allowLocked)
                return CommandResult.Fail(field, ErrorCodes.ObjectLocked, $"'{id}' is locked.", "Unlock it first.");
            obj = found;
            return null;
        }

        private static CommandResult? PickIndex(int count, TakeCommand c, Func<int, string?> idAt, string noun, string containerId, out int index)
        {
            index = -1;
            if (count == 0)
                return CommandResult.Fail("containerId", ErrorCodes.ContainerEmpty, $"'{containerId}' is empty.");
            if (c.EntryId != null)
            {
                for (var i = 0; i < count; i++)
                    if (idAt(i) == c.EntryId)
                        index = i;
                return index >= 0 ? null : CommandResult.Fail("entryId", ErrorCodes.UnknownObject, $"There is no {noun} '{c.EntryId}' in '{containerId}'.");
            }
            if (c.Index < 0 || c.Index >= count)
                return CommandResult.Fail("index", ErrorCodes.InvalidValue, $"Index {c.Index} is outside 0..{count - 1}.");
            index = c.FromBottom ? count - 1 - c.Index : c.Index;
            return null;
        }

        private void ShuffleInPlace<T>(List<T> list)
        {
            for (var i = list.Count - 1; i > 0; i--)
            {
                var j = Random.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        private static Vec3 Beside(TableObject container)
        {
            var offset = Geometry.Rotate(new Vec3(0f, container.Transform.Rotation.Y, 0f), new Vec3(BesideOffset, 0f, 0f));
            return Geometry.Add(container.Transform.Position, offset);
        }

        /// <summary>Slot k of a hand, overlapping left to right along the zone's local X, clamped inside the zone.</summary>
        /// <summary>Slot <paramref name="slot"/> of <paramref name="count"/> along the hand's local X: 60 % overlap, squeezed to fit the zone.</summary>
        internal static Vec3 HandSlot(HandZone zone, int slot, Vec2 cardSize, int count = 0)
        {
            var halfWidth = zone.Size.X * zone.Transform.Scale.X / 2;
            var first = -halfWidth + cardSize.X / 2;
            var last = Math.Max(first, halfWidth - cardSize.X / 2);
            var step = cardSize.X * 0.6f;
            if (count > 1)
                step = Math.Min(step, (last - first) / (count - 1));
            var x = Math.Min(first + slot * step, last);
            return Geometry.Add(zone.Transform.Position, Geometry.Rotate(zone.Transform.Rotation, new Vec3(x, 0f, 0f)));
        }

        private Deck NewDeckAt(TableObject at, List<StateEvent> events)
        {
            var card = at as Card;
            var deck = new Deck
            {
                Id = Mint("deck"),
                FaceAsset = card?.FaceAsset ?? "",
                BackAsset = card?.BackAsset ?? "",
                BackIndex = card?.BackIndex ?? 0,
                Size = card?.Size,
                Style = card?.Style?.Clone(),
            };
            deck.Transform.Position = at.Transform.Position;
            deck.Transform.Rotation = at.Transform.Rotation;
            AddObject(deck);
            events.Add(new ObjectAdded(deck.Id));
            return deck;
        }

        /// <summary>Moves a card or a whole deck into <paramref name="deck"/>, removing the source object from the table.</summary>
        private void AddToDeck(Deck deck, TableObject source, bool atBottom, List<StateEvent> events)
        {
            List<DeckCard> entries;
            switch (source)
            {
                case Card card:
                    entries = new List<DeckCard> { EntryFromCard(deck, card) };
                    break;
                case Deck other:
                    entries = other.Cards.Select(e => Rebase(e, other, deck)).ToList();
                    break;
                default:
                    throw new InvalidOperationException($"{source.Type} cannot join a deck.");
            }

            RemoveObject(source);
            events.Add(new ObjectRemoved(source.Id));
            foreach (var entry in entries.Where(e => e.Id != null))
                _usedIds.Add(entry.Id!);
            if (atBottom)
                deck.Cards.AddRange(entries);
            else
                deck.Cards.InsertRange(0, entries);
        }

        private Card CardFromEntry(Deck deck, DeckCard entry)
        {
            // A card's own id would name it to every machine on a table with secret cards: it travels as EntryId instead.
            var opaque = State.SecretCards && entry.Id != null;
            var card = new Card
            {
                Id = entry.Id == null || opaque ? Mint(deck.Id) : entry.Id,
                EntryId = opaque ? entry.Id : null,
                Name = entry.Name,
                Description = entry.Description,
                GmNotes = entry.GmNotes,
                Tags = new List<string>(entry.Tags),
                FaceAsset = entry.FaceAsset ?? deck.FaceAsset,
                FaceIndex = entry.FaceIndex,
                BackAsset = entry.BackAsset ?? deck.BackAsset,
                BackIndex = entry.BackIndex ?? deck.BackIndex,
                Size = deck.Size,
                Meta = entry.Meta,
                Script = entry.Script,
                TtsExtra = entry.TtsExtra,
                Text = entry.Text?.Clone(),
                Style = (entry.Style ?? deck.Style)?.Clone(),
            };
            // A card is the deck's size (TTS mods scale whole decks: Pokémon Sinnoh's cards are 1.5).
            card.Transform.Scale = deck.Transform.Scale;
            return card;
        }

        /// <summary>Card → deck entry. Asset fields equal to the deck's defaults are left null so they keep following the deck.</summary>
        private static DeckCard EntryFromCard(Deck deck, Card card) => new DeckCard
        {
            Id = card.EntryId ?? card.Id,
            Name = card.Name,
            Description = card.Description,
            GmNotes = card.GmNotes,
            Tags = new List<string>(card.Tags),
            FaceAsset = card.FaceAsset == deck.FaceAsset ? null : card.FaceAsset,
            FaceIndex = card.FaceIndex,
            BackAsset = card.BackAsset == deck.BackAsset ? null : card.BackAsset,
            BackIndex = card.BackIndex == deck.BackIndex ? (int?)null : card.BackIndex,
            Meta = card.Meta,
            Script = card.Script,
            TtsExtra = card.TtsExtra,
            Text = card.Text?.Clone(),
            Style = CardStyle.Same(card.Style, deck.Style) ? null : card.Style?.Clone(),
        };

        /// <summary>Re-expresses an entry of <paramref name="from"/> relative to the defaults of <paramref name="to"/>.</summary>
        private static DeckCard Rebase(DeckCard entry, Deck from, Deck to)
        {
            var face = entry.FaceAsset ?? from.FaceAsset;
            var back = entry.BackAsset ?? from.BackAsset;
            var backIndex = entry.BackIndex ?? from.BackIndex;
            var style = entry.Style ?? from.Style;
            return new DeckCard
            {
                Id = entry.Id,
                Name = entry.Name,
                Description = entry.Description,
                GmNotes = entry.GmNotes,
                Tags = entry.Tags,
                FaceAsset = face == to.FaceAsset ? null : face,
                FaceIndex = entry.FaceIndex,
                BackAsset = back == to.BackAsset ? null : back,
                BackIndex = backIndex == to.BackIndex ? (int?)null : backIndex,
                Meta = entry.Meta,
                Script = entry.Script,
                TtsExtra = entry.TtsExtra,
                Text = entry.Text,
                Style = CardStyle.Same(style, to.Style) ? null : style,
            };
        }

        /// <summary>Puts an object into a bag at the top (before the current contents) or the bottom.</summary>
        private void PutInBag(TableObject obj, Bag bag, bool atBottom)
        {
            State.Objects.Remove(obj);
            var first = State.Objects.FindIndex(o => o.Container == bag.Id);
            var last = State.Objects.FindLastIndex(o => o.Container == bag.Id);
            var index = first < 0 ? State.Objects.Count : atBottom ? last + 1 : first;
            State.Objects.Insert(index, obj);
            obj.Container = bag.Id;
        }

        private void AddObject(TableObject obj)
        {
            State.Objects.Add(obj);
            _objects[obj.Id] = obj;
            ClaimIds(obj);
        }

        private void RemoveObject(TableObject obj)
        {
            State.Objects.Remove(obj);
            _objects.Remove(obj.Id);
            _usedIds.Remove(obj.Id);
            foreach (var state in obj.States.Values)
                _usedIds.Remove(state.Id);
            foreach (var zone in State.Zones.Where(z => z.Parent == obj.Id))
                zone.Parent = null;
            // What was held to it is free now.
            foreach (var held in State.Objects.Where(o => o.Joint?.To == obj.Id))
                held.Joint = null;
        }

        private void ClaimIds(TableObject obj)
        {
            _usedIds.Add(obj.Id);
            foreach (var state in obj.States.Values)
                _usedIds.Add(state.Id);
            if (obj is Deck deck)
                foreach (var entry in deck.Cards)
                    if (entry.Id != null)
                        _usedIds.Add(entry.Id);
        }

        /// <summary>First free id of the form <c>&lt;prefix&gt;-&lt;n&gt;</c>, n ≥ 1. Deterministic for a given state.</summary>
        private string Mint(string prefix)
        {
            for (var n = 1; ; n++)
            {
                var id = $"{prefix}-{n}";
                if (!_usedIds.Contains(id))
                {
                    _usedIds.Add(id);
                    return id;
                }
            }
        }
    }
}
