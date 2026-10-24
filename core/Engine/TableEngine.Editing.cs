using System;
using System.Collections.Generic;
using System.Linq;
using Core.Rules;

namespace Core.Engine
{
    /// <summary>The TTS editing and deck-handling commands (T2.11a): scale, delete, clone, states, cut, split, spread, die value, properties.</summary>
    public sealed partial class TableEngine
    {
        public const float MinScale = 0.2f;
        public const float MaxScale = 5f;

        private CommandResult Scale(ScaleCommand c)
        {
            if (RequireOnTable(c.ObjectId, "objectId", out var obj) is { } fail)
                return fail;
            if (!(c.Factor > 0))
                return CommandResult.Fail("factor", ErrorCodes.InvalidValue, $"Factor must be positive, got {c.Factor}.");
            var s = obj.Transform.Scale;
            float Clamp(float v) => Math.Max(MinScale, Math.Min(MaxScale, v * c.Factor));
            obj.Transform.Scale = new Vec3(Clamp(s.X), Clamp(s.Y), Clamp(s.Z));
            return CommandResult.Success(new List<StateEvent> { new ObjectUpdated(obj.Id) });
        }

        private CommandResult Delete(DeleteCommand c)
        {
            if (RequireOnTable(c.ObjectId, "objectId", out var obj) is { } fail)
                return fail;
            var events = new List<StateEvent>();
            DeleteTree(obj, events);
            return CommandResult.Success(events);
        }

        private void DeleteTree(TableObject obj, List<StateEvent> events)
        {
            foreach (var child in ContentsOf(obj.Id).ToList())
                DeleteTree(child, events);
            RemoveObject(obj);
            events.Add(new ObjectRemoved(obj.Id));
        }

        private CommandResult Spawn(SpawnCommand c)
        {
            // Read and check the object inside a table made of this table's assets and it alone.
            var assets = State.Assets.Concat(c.Assets.Where(a => State.Assets.All(x => x.Id != a.Id))).ToList();
            Newtonsoft.Json.Linq.JObject obj;
            try
            {
                obj = Newtonsoft.Json.Linq.JObject.Parse(c.ObjectJson);
            }
            catch (Newtonsoft.Json.JsonException e)
            {
                return CommandResult.Fail("objectJson", ErrorCodes.InvalidJson, e.Message);
            }
            obj.Remove("container");
            obj["id"] = "spawned";
            var document = Newtonsoft.Json.Linq.JObject.Parse(TableJson.Serialize(new TableState { Assets = assets }, indented: false));
            document["objects"] = new Newtonsoft.Json.Linq.JArray(obj);
            var parsed = TableJson.Parse(document.ToString());
            if (!parsed.Ok)
            {
                var first = parsed.Errors.First();
                return CommandResult.Fail(first.Path, first.Code, first.Message);
            }
            foreach (var asset in c.Assets.Where(a => State.Assets.All(x => x.Id != a.Id)))
                State.Assets.Add(asset);
            var spawned = parsed.State!.Objects.Single();
            spawned.Id = Mint(spawned.Type.ToLowerInvariant());
            foreach (var state in spawned.States.Values)
                state.Id = Mint(state.Id);
            AddObject(spawned);
            var result = CommandResult.Success(new List<StateEvent> { new ObjectAdded(spawned.Id) });
            result.CreatedId = spawned.Id;
            return result;
        }

        private CommandResult SetCustom(SetCustomCommand c)
        {
            if (RequireOnTable(c.ObjectId, "objectId", out var obj, allowLocked: true) is { } fail)
                return fail;
            var events = new List<StateEvent>();
            foreach (var asset in c.Assets.Where(a => State.Assets.All(x => x.Id != a.Id)))
            {
                if (!Asset.IsSafePath(asset.Path) || (asset.Url != null && !Asset.IsWebAddress(asset.Url)) || string.IsNullOrEmpty(asset.Id))
                    return CommandResult.Fail("assets", ErrorCodes.InvalidValue, $"Asset '{asset.Id}' needs a path inside the package and an http(s) url.");
                State.Assets.Add(asset);
                events.Add(new AssetAdded(asset.Id));
            }
            bool Known(string? id) => id == null || State.Assets.Any(a => a.Id == id);
            if (!Known(c.Image) || !Known(c.Face) || !Known(c.Back) || !Known(c.Mesh) || !Known(c.Diffuse))
                return CommandResult.Fail("assets", ErrorCodes.InvalidValue, "Every file named must be one of the table's assets.");
            switch (obj)
            {
                case Card card:
                    if (c.Face != null) { card.FaceAsset = c.Face; card.FaceIndex = 0; }
                    if (c.Back != null) { card.BackAsset = c.Back; card.BackIndex = 0; }
                    break;
                case Deck deck:
                    if (c.Face != null) deck.FaceAsset = c.Face;
                    if (c.Back != null) { deck.BackAsset = c.Back; deck.BackIndex = 0; }
                    break;
                case Model model:
                    if (c.Mesh != null) model.Asset = c.Mesh;
                    if (c.Diffuse != null) model.DiffuseAsset = c.Diffuse;
                    break;
                case Bag bag:
                    if (c.Mesh != null) bag.Asset = c.Mesh;
                    break;
                case Token token when c.Image != null: token.Asset = c.Image; break;
                case Tile tile when c.Image != null: tile.Asset = c.Image; break;
                case Board board when c.Image != null: board.Asset = c.Image; break;
                case Figure figure when c.Image != null: figure.Asset = c.Image; break;
                case Die die when c.Image != null: die.Asset = c.Image; break;
            }
            events.Add(new ObjectUpdated(obj.Id));
            return CommandResult.Success(events);
        }

        private CommandResult Clone(CloneCommand c)
        {
            if (RequireOnTable(c.ObjectId, "objectId", out var obj, allowLocked: true) is { } fail)
                return fail;
            var events = new List<StateEvent>();
            var copy = CloneTree(obj, null, events);
            copy.Transform.Position = c.Position ?? Beside(obj);
            var result = CommandResult.Success(events);
            result.CreatedId = copy.Id;
            return result;
        }

        private TableObject CloneTree(TableObject source, string? container, List<StateEvent> events)
        {
            var copy = TableJson.CloneObject(source);
            copy.Id = Mint(source.Id);
            copy.Container = container;
            foreach (var state in copy.States.Values)
                state.Id = Mint(state.Id);
            if (copy is Deck deck)
                foreach (var entry in deck.Cards.Where(e => e.Id != null))
                    entry.Id = Mint(entry.Id!);
            AddObject(copy);
            events.Add(container == null ? new ObjectAdded(copy.Id) : (StateEvent)new ContainerChanged(copy.Id, null, container));
            foreach (var child in ContentsOf(source.Id).ToList())
                CloneTree(child, copy.Id, events);
            return copy;
        }

        /// <summary>
        /// The object itself is the active state; its number is the smallest positive integer not used as a key in
        /// <c>states</c>. Switching swaps the active object with the chosen one, keeping pose and container.
        /// </summary>
        private CommandResult SetState(SetStateCommand c)
        {
            if (RequireOnTable(c.ObjectId, "objectId", out var obj) is { } fail)
                return fail;
            if (!obj.States.TryGetValue(c.State, out var next))
                return CommandResult.Fail("state", ErrorCodes.InvalidValue,
                    obj.States.Count == 0 ? $"'{obj.Id}' has no other states." : $"'{obj.Id}' has no state '{c.State}'.",
                    obj.States.Count == 0 ? null : $"Use one of: {string.Join(", ", obj.States.Keys)}.");

            var activeKey = 1;
            while (obj.States.ContainsKey(activeKey.ToString()))
                activeKey++;
            var others = obj.States.Where(p => p.Key != c.State).ToDictionary(p => p.Key, p => p.Value);
            obj.States = new Dictionary<string, TableObject>();
            others[activeKey.ToString()] = obj;

            next.States = others;
            next.Transform = new TableTransform { Position = obj.Transform.Position, Rotation = obj.Transform.Rotation, Scale = next.Transform.Scale };
            next.Container = obj.Container;

            var index = State.Objects.IndexOf(obj);
            State.Objects[index] = next;
            _objects.Remove(obj.Id);
            _objects[next.Id] = next;
            foreach (var zone in State.Zones.Where(z => z.Parent == obj.Id))
                zone.Parent = next.Id;
            return CommandResult.Success(new List<StateEvent> { new ObjectRemoved(obj.Id), new ObjectAdded(next.Id) });
        }

        private CommandResult Cut(CutCommand c)
        {
            if (RequireDeck(c.DeckId, "deckId", out var deck) is { } fail)
                return fail;
            var count = c.Count ?? deck.Cards.Count / 2;
            if (count < 1 || count >= deck.Cards.Count)
                return CommandResult.Fail("count", ErrorCodes.InvalidValue, $"Cut takes 1..{deck.Cards.Count - 1} cards from a deck of {deck.Cards.Count}.");
            var events = new List<StateEvent>();
            var pile = SplitOff(deck, 0, count, c.Position ?? Beside(deck), events);
            events.Add(new ObjectUpdated(deck.Id));
            var result = CommandResult.Success(events);
            result.CreatedId = pile.Id;
            return result;
        }

        private CommandResult Split(SplitCommand c)
        {
            if (RequireDeck(c.DeckId, "deckId", out var deck) is { } fail)
                return fail;
            if (c.Piles < 2 || c.Piles > deck.Cards.Count)
                return CommandResult.Fail("piles", ErrorCodes.InvalidValue, $"A deck of {deck.Cards.Count} splits into 2..{deck.Cards.Count} piles.");

            var size = (deck.Cards.Count + c.Piles - 1) / c.Piles;
            var width = (deck.Size ?? Card.DefaultSize).X;
            var events = new List<StateEvent>();
            var created = new List<string>();
            // The original keeps the first `size` cards; the rest go, in order, to new decks to its right.
            var rest = deck.Cards.Count - size;
            for (var pile = 1; rest > 0; pile++)
            {
                var take = Math.Min(size, rest);
                var offset = Geometry.Rotate(new Vec3(0f, deck.Transform.Rotation.Y, 0f), new Vec3(pile * (width + 0.02f), 0f, 0f));
                created.Add(SplitOff(deck, size, take, Geometry.Add(deck.Transform.Position, offset), events).Id);
                rest -= take;
            }
            events.Add(new ObjectUpdated(deck.Id));
            var result = CommandResult.Success(events);
            result.CreatedIds = created;
            result.CreatedId = created.FirstOrDefault();
            return result;
        }

        /// <summary>Moves <paramref name="count"/> cards starting at <paramref name="start"/> into a new deck at <paramref name="position"/>.</summary>
        private Deck SplitOff(Deck deck, int start, int count, Vec3 position, List<StateEvent> events)
        {
            var pile = new Deck
            {
                Id = Mint(deck.Id),
                Name = deck.Name,
                FaceAsset = deck.FaceAsset,
                BackAsset = deck.BackAsset,
                BackIndex = deck.BackIndex,
                Size = deck.Size,
                Cards = deck.Cards.GetRange(start, count),
            };
            deck.Cards.RemoveRange(start, count);
            pile.Transform.Position = position;
            pile.Transform.Rotation = deck.Transform.Rotation;
            AddObject(pile);
            events.Add(new ObjectAdded(pile.Id));
            return pile;
        }

        private CommandResult Spread(SpreadCommand c)
        {
            if (RequireDeck(c.DeckId, "deckId", out var deck) is { } fail)
                return fail;
            if (deck.Cards.Count == 0)
                return CommandResult.Fail("deckId", ErrorCodes.ContainerEmpty, $"'{deck.Id}' is empty.");
            if (!(c.Spacing > 0))
                return CommandResult.Fail("spacing", ErrorCodes.InvalidValue, "Spacing must be positive.");

            var events = new List<StateEvent>();
            var created = new List<string>();
            var yaw = new Vec3(0f, deck.Transform.Rotation.Y, 0f);
            var i = 0;
            while (deck.Cards.Count > 0)
            {
                var card = CardFromEntry(deck, deck.Cards[0]);
                deck.Cards.RemoveAt(0);
                card.Transform.Position = Geometry.Add(deck.Transform.Position, Geometry.Rotate(yaw, new Vec3(BesideOffset + i * c.Spacing, 0f, 0f)));
                card.Transform.Rotation = deck.Transform.Rotation;
                AddObject(card);
                events.Add(new ObjectAdded(card.Id));
                created.Add(card.Id);
                i++;
            }
            events.Add(new ObjectUpdated(deck.Id));
            var result = CommandResult.Success(events);
            result.CreatedIds = created;
            return result;
        }

        private CommandResult SetDieValue(SetDieValueCommand c)
        {
            if (RequireOnTable(c.ObjectId, "objectId", out var obj) is { } fail)
                return fail;
            if (!(obj is Die die))
                return CommandResult.Fail("objectId", ErrorCodes.WrongObjectType, $"'{obj.Id}' is a {obj.Type}; only dice have a value.");
            if (c.Value < 1 || c.Value > die.Faces)
                return CommandResult.Fail("value", ErrorCodes.InvalidValue, $"Value {c.Value} is outside 1..{die.Faces}.");
            die.Value = c.Value;
            return CommandResult.Success(new List<StateEvent> { new ObjectUpdated(die.Id) });
        }

        private CommandResult SetCounter(SetCounterCommand c)
        {
            if (RequireOnTable(c.ObjectId, "objectId", out var obj, allowLocked: true) is { } fail)
                return fail;
            if (!(obj is Counter counter))
                return CommandResult.Fail("objectId", ErrorCodes.WrongObjectType, $"'{obj.Id}' is a {obj.Type}, not a counter.");
            if (c.Value == null && c.Add == null)
                return CommandResult.Fail("value", ErrorCodes.MissingField, "Give a value or an amount to add.");
            var value = (long)(c.Value ?? counter.Value) + (c.Add ?? 0);
            counter.Value = (int)Math.Max(Counter.MinValue, Math.Min(Counter.MaxValue, value));
            return CommandResult.Success(new List<StateEvent> { new ObjectUpdated(counter.Id) });
        }

        private CommandResult SetClock(SetClockCommand c)
        {
            if (RequireOnTable(c.ObjectId, "objectId", out var obj, allowLocked: true) is { } fail)
                return fail;
            if (!(obj is Clock clock))
                return CommandResult.Fail("objectId", ErrorCodes.WrongObjectType, $"'{obj.Id}' is a {obj.Type}, not a clock.");
            if (c.Mode is { } mode && !Enum.IsDefined(typeof(ClockMode), mode))
                return CommandResult.Fail("mode", ErrorCodes.InvalidValue, $"Unknown clock mode {(int)mode}.");
            if (c.Seconds is { } seconds && (seconds < 0 || seconds > Clock.MaxSeconds || float.IsNaN(seconds)))
                return CommandResult.Fail("seconds", ErrorCodes.InvalidValue, $"Seconds must be 0..{Clock.MaxSeconds}.");
            if (c.Duration is { } duration && (duration < 0 || duration > Clock.MaxSeconds || float.IsNaN(duration)))
                return CommandResult.Fail("duration", ErrorCodes.InvalidValue, $"Duration must be 0..{Clock.MaxSeconds}.");
            clock.Mode = c.Mode ?? clock.Mode;
            clock.Duration = c.Duration ?? clock.Duration;
            clock.Seconds = c.Seconds ?? clock.Seconds;
            clock.Running = c.Running ?? clock.Running;
            // A timer at zero has nothing left to count.
            if (clock.Mode == ClockMode.Timer && clock.Seconds <= 0f)
                clock.Running = false;
            return CommandResult.Success(new List<StateEvent> { new ObjectUpdated(clock.Id) });
        }

        private CommandResult SetProperties(SetPropertiesCommand c)
        {
            if (RequireOnTable(c.ObjectId, "objectId", out var obj, allowLocked: true) is { } fail)
                return fail;
            if (c.Tint != null && (c.Tint.Length < 3 || c.Tint.Length > 4 || c.Tint.Any(v => v < 0 || v > 1)))
                return CommandResult.Fail("tint", ErrorCodes.InvalidValue, "Tint is [r, g, b] or [r, g, b, a] with values 0–1.");
            if (c.Name != null) obj.Name = c.Name.Length == 0 ? null : c.Name;
            if (c.Description != null) obj.Description = c.Description.Length == 0 ? null : c.Description;
            if (c.Tint != null) obj.Tint = c.Tint;
            if (c.ClearTint) obj.Tint = null;
            if (c.Snap.HasValue) obj.Snap = c.Snap.Value;
            if (c.Grid.HasValue) obj.Grid = c.Grid.Value;
            if (c.Tooltip.HasValue) obj.Tooltip = c.Tooltip.Value;
            if (c.MeshHidden.HasValue) obj.MeshHidden = c.MeshHidden.Value;
            if (c.HideWhenFaceDown.HasValue) obj.HideWhenFaceDown = c.HideWhenFaceDown.Value;
            if (c.Tags != null && (c.Tags.Count > 200 || c.Tags.Any(t => string.IsNullOrEmpty(t) || t.Length > 200)))
                return CommandResult.Fail("tags", ErrorCodes.InvalidValue, "Tags are up to 200 words of up to 200 characters.");
            if (c.Script != null) obj.Script = c.Script.Length == 0 ? null : c.Script;
            if (c.Tags != null) obj.Tags = c.Tags.Distinct().ToList();
            if (c.InvisibleTo != null) obj.InvisibleTo = c.InvisibleTo.Select(x => x.ToLowerInvariant()).Distinct().ToList();
            var events = new List<StateEvent> { new ObjectUpdated(obj.Id) };
            if (c.Hands.HasValue && c.Hands.Value != obj.Hands)
            {
                obj.Hands = c.Hands.Value;
                // Joining or leaving a hand changes which zones the object counts as in.
                RefreshZones(new[] { obj }, events);
            }
            return CommandResult.Success(events);
        }

        private CommandResult? RequireDeck(string id, string field, out Deck deck)
        {
            deck = null!;
            if (RequireOnTable(id, field, out var obj) is { } fail)
                return fail;
            if (!(obj is Deck d))
                return CommandResult.Fail(field, ErrorCodes.WrongObjectType, $"'{obj.Id}' is a {obj.Type}; this needs a deck.");
            deck = d;
            return null;
        }
    }
}
