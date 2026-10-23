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
    public sealed class TableEngine
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
                    case HiddenZone hidden when !(seat == null ? hidden.VisibleTo.Contains("host") : hidden.VisibleTo.Contains(seat)):
                        return true;
                }
            }
            return false;
        }

        /// <summary>Objects inside a bag, top first.</summary>
        public IEnumerable<TableObject> ContentsOf(string bagId) => State.Objects.Where(o => o.Container == bagId);

        /// <summary>Runs the command, then appends zone enter/leave events for every object it touched.</summary>
        public CommandResult Execute(Command command)
        {
            var result = Dispatch(command);
            return result.Ok ? result.WithEvents(WithZoneEvents(result.Events)) : result;
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
            return all;
        }

        private List<string> ComputeZones(TableObject obj)
        {
            if (obj.Container != null)
                return NoZones;
            var zones = new List<string>();
            foreach (var zone in State.Zones)
                if (Geometry.Contains(zone, obj.Transform.Position))
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
            DealCommand c => Deal(c),
            LockCommand c => Lock(c),
            GroupCommand c => Group(c),
            RollCommand c => Roll(c),
            _ => CommandResult.Fail("", ErrorCodes.UnknownCommand, $"Unknown command '{command.GetType().Name}'."),
        };

        private CommandResult Move(MoveCommand c)
        {
            if (RequireOnTable(c.ObjectId, "objectId", out var obj) is { } fail)
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
                    var result = CommandResult.Success(new List<StateEvent> { new ObjectUpdated(deck.Id), new ObjectAdded(card.Id) });
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
                        events.Add(new ObjectAdded(picked.Id));
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
                default:
                    return CommandResult.Fail("containerId", ErrorCodes.WrongObjectType,
                        $"'{container.Id}' is a {container.Type}; only decks and bags can be taken from.");
            }
        }

        private CommandResult Insert(InsertCommand c)
        {
            if (RequireOnTable(c.ObjectId, "objectId", out var obj) is { } fail)
                return fail;
            if (c.IntoId == c.ObjectId)
                return CommandResult.Fail("intoId", ErrorCodes.InvalidTarget, "An object cannot be inserted into itself.");
            if (RequireOnTable(c.IntoId, "intoId", out var target, allowLocked: true) is { } targetFail)
                return targetFail;

            var events = new List<StateEvent>();
            switch (target)
            {
                case InfiniteBag infinite when ContentsOf(infinite.Id).Any():
                    return CommandResult.Fail("intoId", ErrorCodes.InvalidTarget,
                        $"Infinite bag '{infinite.Id}' already holds its template; it takes no other objects.");
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
                        $"A {obj.Type} cannot go into a {target.Type}. Bags take anything; decks and cards take cards and decks.");
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
                    card.Transform.Rotation = new Vec3(0f, zone.Transform.Rotation.Y, 0f);
                    AddObject(card);
                    events.Add(new ObjectAdded(card.Id));
                    dealt[seatId].Add(card.Id);
                    hands[h] = (seatId, zone, slot + 1);
                }
            }
            events.Add(new ObjectUpdated(deck.Id));
            var result = CommandResult.Success(events);
            result.Dealt = dealt;
            return result;
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
        internal static Vec3 HandSlot(HandZone zone, int slot, Vec2 cardSize)
        {
            var halfWidth = zone.Size.X * zone.Transform.Scale.X / 2;
            var first = -halfWidth + cardSize.X / 2;
            var last = Math.Max(first, halfWidth - cardSize.X / 2);
            var x = Math.Min(first + slot * cardSize.X * 0.6f, last);
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
            var card = new Card
            {
                Id = entry.Id ?? Mint(deck.Id),
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
            };
            return card;
        }

        /// <summary>Card → deck entry. Asset fields equal to the deck's defaults are left null so they keep following the deck.</summary>
        private static DeckCard EntryFromCard(Deck deck, Card card) => new DeckCard
        {
            Id = card.Id,
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
        };

        /// <summary>Re-expresses an entry of <paramref name="from"/> relative to the defaults of <paramref name="to"/>.</summary>
        private static DeckCard Rebase(DeckCard entry, Deck from, Deck to)
        {
            var face = entry.FaceAsset ?? from.FaceAsset;
            var back = entry.BackAsset ?? from.BackAsset;
            var backIndex = entry.BackIndex ?? from.BackIndex;
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
