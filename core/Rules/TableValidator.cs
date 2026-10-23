using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Core.Rules
{
    /// <summary>
    /// Semantic checks on a structurally valid <see cref="TableState"/>. Returns every problem found, never throws.
    /// Object, deck-card and zone ids share one namespace because effects may target either; assets and seats have their own.
    /// </summary>
    public static class TableValidator
    {
        private static readonly Regex IdPattern = new Regex("^[a-z0-9_-]+$", RegexOptions.CultureInvariant);

        public static List<ValidationError> Validate(TableState state)
        {
            var v = new Run(state);
            v.Execute();
            return v.Errors;
        }

        private sealed class Run
        {
            public readonly List<ValidationError> Errors = new List<ValidationError>();

            private readonly TableState _state;
            private readonly Dictionary<string, Asset> _assets = new Dictionary<string, Asset>();
            private readonly Dictionary<string, TableObject> _objects = new Dictionary<string, TableObject>();
            private readonly HashSet<string> _tableIds = new HashSet<string>();
            private readonly Dictionary<string, Seat> _seats = new Dictionary<string, Seat>();
            private readonly Dictionary<string, Zone> _zones = new Dictionary<string, Zone>();

            public Run(TableState state)
            {
                _state = state;
            }

            public void Execute()
            {
                if (_state.SchemaVersion != TableState.CurrentSchemaVersion)
                    Add("schemaVersion", ErrorCodes.UnsupportedSchemaVersion, $"Schema version '{_state.SchemaVersion}' is not supported.",
                        $"Use \"{TableState.CurrentSchemaVersion}\".");

                CollectIds();
                for (var i = 0; i < _state.Assets.Count; i++)
                    CheckAsset(_state.Assets[i], $"assets[{i}]");
                for (var i = 0; i < _state.Objects.Count; i++)
                    CheckObject(_state.Objects[i], $"objects[{i}]", isState: false);
                CheckContainerCycles();
                for (var i = 0; i < _state.Zones.Count; i++)
                    CheckZone(_state.Zones[i], $"zones[{i}]");
                for (var i = 0; i < _state.Players.Seats.Count; i++)
                    CheckSeat(_state.Players.Seats[i], $"players.seats[{i}]");
                RulesValidator.Validate(_state, Errors);
            }

            private void CollectIds()
            {
                for (var i = 0; i < _state.Assets.Count; i++)
                {
                    var asset = _state.Assets[i];
                    if (CheckIdFormat(asset.Id, $"assets[{i}].id", "asset") && !_assets.ContainsKey(asset.Id))
                        _assets.Add(asset.Id, asset);
                    else if (asset.Id.Length > 0 && IdPattern.IsMatch(asset.Id))
                        Add($"assets[{i}].id", ErrorCodes.DuplicateId, $"Asset id '{asset.Id}' is used more than once.", "Give each asset a unique id.");
                }

                for (var i = 0; i < _state.Objects.Count; i++)
                {
                    var obj = _state.Objects[i];
                    if (ClaimTableId(obj.Id, $"objects[{i}].id", "object"))
                        _objects[obj.Id] = obj;
                    foreach (var pair in obj.States)
                        ClaimTableId(pair.Value.Id, $"objects[{i}].states.{pair.Key}.id", "object");
                    if (obj is Deck deck)
                        for (var j = 0; j < deck.Cards.Count; j++)
                            if (deck.Cards[j].Id != null)
                                ClaimTableId(deck.Cards[j].Id!, $"objects[{i}].cards[{j}].id", "card");
                }

                for (var i = 0; i < _state.Zones.Count; i++)
                {
                    var zone = _state.Zones[i];
                    if (ClaimTableId(zone.Id, $"zones[{i}].id", "zone"))
                        _zones[zone.Id] = zone;
                }

                for (var i = 0; i < _state.Players.Seats.Count; i++)
                {
                    var seat = _state.Players.Seats[i];
                    if (!CheckIdFormat(seat.Id, $"players.seats[{i}].id", "seat"))
                        continue;
                    if (_seats.ContainsKey(seat.Id))
                        Add($"players.seats[{i}].id", ErrorCodes.DuplicateId, $"Seat id '{seat.Id}' is used more than once.", "Give each seat a unique id.");
                    else
                        _seats.Add(seat.Id, seat);
                }
            }

            private bool ClaimTableId(string id, string path, string noun)
            {
                if (!CheckIdFormat(id, path, noun))
                    return false;
                if (_tableIds.Add(id))
                    return true;
                Add(path, ErrorCodes.DuplicateId, $"Id '{id}' is already used by another object, card or zone.",
                    "Objects, deck cards and zones share one id namespace; pick a unique id.");
                return false;
            }

            private bool CheckIdFormat(string id, string path, string noun)
            {
                if (string.IsNullOrEmpty(id))
                {
                    Add(path, ErrorCodes.MissingField, $"This {noun} has no id.", "Add an id made of a-z, 0-9, '_' or '-'.");
                    return false;
                }
                if (!IdPattern.IsMatch(id))
                {
                    var fixedId = Regex.Replace(id.ToLowerInvariant(), "[^a-z0-9_-]", "_");
                    Add(path, ErrorCodes.InvalidId, $"Id '{id}' has characters outside a-z, 0-9, '_' and '-'.", $"Use '{fixedId}'.");
                    return false;
                }
                return true;
            }

            private void CheckAsset(Asset asset, string path)
            {
                if (string.IsNullOrWhiteSpace(asset.Path))
                    Add($"{path}.path", ErrorCodes.MissingField, $"Asset '{asset.Id}' has no path.",
                        "Use a path relative to the game package, or builtin:<name> for generated assets.");
                if (asset.Columns < 1)
                    Add($"{path}.columns", ErrorCodes.InvalidValue, "Columns must be at least 1.");
                if (asset.Rows < 1)
                    Add($"{path}.rows", ErrorCodes.InvalidValue, "Rows must be at least 1.");
            }

            private void CheckObject(TableObject obj, string path, bool isState)
            {
                CheckTransform(obj.Transform, $"{path}.transform");
                CheckTint(obj.Tint, $"{path}.tint");

                if (obj.Container != null)
                {
                    if (isState)
                        Add($"{path}.container", ErrorCodes.InvalidContainer, "A state cannot have a container; the active object's container applies.",
                            "Remove 'container' from the state.");
                    else if (obj.Container == obj.Id)
                        Add($"{path}.container", ErrorCodes.ContainerCycle, $"Object '{obj.Id}' cannot contain itself.");
                    else if (!_objects.TryGetValue(obj.Container, out var container))
                        Add($"{path}.container", ErrorCodes.UnknownContainer, $"Container '{obj.Container}' does not exist.",
                            Suggest.OneOf(obj.Container, _objects.Values.OfType<Bag>().Select(b => b.Id), "a Bag or InfiniteBag id"));
                    else if (!(container is Bag))
                        Add($"{path}.container", ErrorCodes.InvalidContainer, $"'{obj.Container}' is a {container.Type}; only Bag and InfiniteBag hold objects.",
                            obj is Card ? "To put a card in a deck, add it to the deck's 'cards' list instead." : null);
                }

                foreach (var pair in obj.States)
                {
                    var statePath = $"{path}.states.{pair.Key}";
                    if (!int.TryParse(pair.Key, out var stateNumber) || stateNumber < 1)
                        Add(statePath, ErrorCodes.InvalidValue, $"State key '{pair.Key}' must be a positive integer.");
                    if (pair.Value.States.Count > 0)
                        Add($"{statePath}.states", ErrorCodes.InvalidValue, "States cannot be nested.", "Move every state to the top-level object's 'states'.");
                    CheckObject(pair.Value, statePath, isState: true);
                }

                switch (obj)
                {
                    case Card card:
                        CheckSheetCell(card.FaceAsset, card.FaceIndex, $"{path}.faceAsset", $"{path}.faceIndex");
                        CheckSheetCell(card.BackAsset, card.BackIndex, $"{path}.backAsset", $"{path}.backIndex");
                        CheckSize(card.Size, $"{path}.size");
                        break;
                    case Deck deck:
                        CheckSize(deck.Size, $"{path}.size");
                        if (deck.FaceAsset.Length > 0)
                            CheckAssetRef(deck.FaceAsset, $"{path}.faceAsset", AssetType.Image);
                        if (deck.BackAsset.Length > 0)
                            CheckSheetCell(deck.BackAsset, deck.BackIndex, $"{path}.backAsset", $"{path}.backIndex");
                        for (var j = 0; j < deck.Cards.Count; j++)
                            CheckDeckCard(deck, deck.Cards[j], $"{path}.cards[{j}]");
                        break;
                    case Die die:
                        if (!Die.SupportedFaces.Contains(die.Faces))
                            Add($"{path}.faces", ErrorCodes.InvalidDieFaces, $"A die cannot have {die.Faces} faces.",
                                $"Use one of: {string.Join(", ", Die.SupportedFaces)}.");
                        if (die.Labels != null && die.Labels.Count != die.Faces)
                            Add($"{path}.labels", ErrorCodes.InvalidValue, $"There are {die.Labels.Count} labels for {die.Faces} faces.",
                                "Give exactly one label per face, or remove 'labels'.");
                        if (die.Value.HasValue && (die.Value < 1 || die.Value > die.Faces))
                            Add($"{path}.value", ErrorCodes.InvalidValue, $"Value {die.Value} is outside 1..{die.Faces}.");
                        CheckOptionalAsset(die.Asset, $"{path}.asset", AssetType.Image);
                        break;
                    case Model model:
                        if (model.Asset.Length == 0)
                            Add($"{path}.asset", ErrorCodes.MissingField, $"Model '{model.Id}' has no asset.", "Reference a model or assetBundle asset.");
                        else
                            CheckAssetRef(model.Asset, $"{path}.asset", AssetType.Model, AssetType.AssetBundle);
                        CheckOptionalAsset(model.DiffuseAsset, $"{path}.diffuseAsset", AssetType.Image);
                        break;
                    case Token token:
                        CheckOptionalAsset(token.Asset, $"{path}.asset", AssetType.Image);
                        CheckPositive(token.Thickness, $"{path}.thickness");
                        break;
                    case Tile tile:
                        CheckOptionalAsset(tile.Asset, $"{path}.asset", AssetType.Image);
                        CheckPositive(tile.Thickness, $"{path}.thickness");
                        break;
                    case Bag bag:
                        CheckOptionalAsset(bag.Asset, $"{path}.asset", AssetType.Model, AssetType.AssetBundle);
                        break;
                    case Board board:
                        CheckOptionalAsset(board.Asset, $"{path}.asset", AssetType.Image);
                        CheckSize(board.Size, $"{path}.size");
                        break;
                    case Figure figure:
                        CheckOptionalAsset(figure.Asset, $"{path}.asset", AssetType.Image);
                        CheckPositive(figure.Height, $"{path}.height");
                        break;
                }
            }

            private void CheckDeckCard(Deck deck, DeckCard card, string path)
            {
                // Deck-level asset problems are reported once on the deck, not again for every card that inherits them.
                var face = card.FaceAsset ?? deck.FaceAsset;
                if (face.Length == 0)
                    Add($"{path}.faceAsset", ErrorCodes.MissingField, "This card has no face asset.", "Set 'faceAsset' on the deck or on the card.");
                else if (card.FaceAsset != null)
                    CheckSheetCell(face, card.FaceIndex, $"{path}.faceAsset", $"{path}.faceIndex");
                else if (_assets.TryGetValue(face, out var deckFace) && deckFace.Type == AssetType.Image)
                    CheckCellIndex(deckFace, card.FaceIndex, $"{path}.faceIndex");

                var back = card.BackAsset ?? deck.BackAsset;
                if (back.Length == 0)
                    Add($"{path}.backAsset", ErrorCodes.MissingField, "This card has no back asset.", "Set 'backAsset' on the deck or on the card.");
                else if (card.BackAsset != null)
                    CheckSheetCell(back, card.BackIndex ?? deck.BackIndex, $"{path}.backAsset", $"{path}.backIndex");
                else if (card.BackIndex != null && _assets.TryGetValue(back, out var deckBack) && deckBack.Type == AssetType.Image)
                    CheckCellIndex(deckBack, card.BackIndex.Value, $"{path}.backIndex");
            }

            private void CheckSheetCell(string assetId, int index, string assetPath, string indexPath)
            {
                if (CheckAssetRef(assetId, assetPath, AssetType.Image))
                    CheckCellIndex(_assets[assetId], index, indexPath);
            }

            private void CheckCellIndex(Asset asset, int index, string indexPath)
            {
                var assetId = asset.Id;
                var cells = asset.Columns * asset.Rows;
                if (index < 0 || index >= cells)
                    Add(indexPath, ErrorCodes.IndexOutOfRange, $"Index {index} is outside the {asset.Columns}×{asset.Rows} sheet '{assetId}'.",
                        $"Use 0..{cells - 1} (row-major, 0 is top-left).");
            }

            private void CheckOptionalAsset(string? assetId, string path, params AssetType[] allowed)
            {
                if (assetId != null)
                    CheckAssetRef(assetId, path, allowed);
            }

            private bool CheckAssetRef(string assetId, string path, params AssetType[] allowed)
            {
                if (assetId.Length == 0)
                {
                    Add(path, ErrorCodes.MissingField, "No asset is set.", $"Reference an asset of type {string.Join(" or ", allowed)}.");
                    return false;
                }
                if (!_assets.TryGetValue(assetId, out var asset))
                {
                    Add(path, ErrorCodes.UnknownAsset, $"Asset '{assetId}' does not exist.",
                        Suggest.OneOf(assetId, _assets.Values.Where(a => allowed.Contains(a.Type)).Select(a => a.Id), "an asset id")
                        ?? "Add it to 'assets' first.");
                    return false;
                }
                if (!allowed.Contains(asset.Type))
                {
                    Add(path, ErrorCodes.WrongAssetType, $"Asset '{assetId}' is a {asset.Type}, expected {string.Join(" or ", allowed)}.");
                    return false;
                }
                return true;
            }

            private void CheckTransform(TableTransform transform, string path)
            {
                var s = transform.Scale;
                if (s.X <= 0 || s.Y <= 0 || s.Z <= 0)
                    Add($"{path}.scale", ErrorCodes.InvalidValue, $"Scale {s} must be positive on every axis.");
            }

            private void CheckTint(float[]? tint, string path)
            {
                if (tint == null)
                    return;
                if (tint.Length != 3 && tint.Length != 4)
                    Add(path, ErrorCodes.InvalidValue, $"Tint has {tint.Length} values.", "Use [r, g, b] or [r, g, b, a] with values 0–1.");
                else if (tint.Any(c => c < 0 || c > 1))
                    Add(path, ErrorCodes.InvalidValue, "Tint values must be between 0 and 1.");
            }

            private void CheckSize(Vec2? size, string path)
            {
                if (size.HasValue && (size.Value.X <= 0 || size.Value.Y <= 0))
                    Add(path, ErrorCodes.InvalidValue, $"Size {size.Value} must be positive.");
            }

            private void CheckPositive(float? value, string path)
            {
                if (value.HasValue && value.Value <= 0)
                    Add(path, ErrorCodes.InvalidValue, $"Value {value.Value} must be positive.");
            }

            private void CheckContainerCycles()
            {
                for (var i = 0; i < _state.Objects.Count; i++)
                {
                    var start = _state.Objects[i];
                    var seen = new HashSet<string> { start.Id };
                    var current = start;
                    while (current.Container != null && _objects.TryGetValue(current.Container, out var parent) && parent.Id != current.Id)
                    {
                        if (!seen.Add(parent.Id))
                        {
                            if (parent.Id == start.Id)
                                Add($"objects[{i}].container", ErrorCodes.ContainerCycle,
                                    $"Containers loop: {string.Join(" → ", seen)} → {start.Id}.", "An object cannot end up inside itself; break the loop.");
                            break;
                        }
                        current = parent;
                    }
                }
            }

            private void CheckZone(Zone zone, string path)
            {
                CheckTransform(zone.Transform, $"{path}.transform");
                if (!(zone is SnapPoint) && (zone.Size.X <= 0 || zone.Size.Y <= 0 || zone.Size.Z <= 0))
                    Add($"{path}.size", ErrorCodes.InvalidValue, $"Size {zone.Size} must be positive on every axis.");
                if (zone.Parent != null && !_objects.ContainsKey(zone.Parent))
                    Add($"{path}.parent", ErrorCodes.UnknownObject, $"Parent object '{zone.Parent}' does not exist.",
                        Suggest.OneOf(zone.Parent, _objects.Keys, "an object id"));

                switch (zone)
                {
                    case HandZone hand:
                        if (hand.Owner.Length == 0)
                            Add($"{path}.owner", ErrorCodes.MissingField, "A HandZone needs an owner seat.", SeatSuggestion(""));
                        else if (!_seats.ContainsKey(hand.Owner))
                            Add($"{path}.owner", ErrorCodes.UnknownSeat, $"Seat '{hand.Owner}' does not exist.", SeatSuggestion(hand.Owner));
                        break;
                    case HiddenZone hidden:
                        for (var j = 0; j < hidden.VisibleTo.Count; j++)
                        {
                            var who = hidden.VisibleTo[j];
                            if (who != "host" && !_seats.ContainsKey(who))
                                Add($"{path}.visibleTo[{j}]", ErrorCodes.UnknownSeat, $"'{who}' is neither a seat nor 'host'.",
                                    Suggest.OneOf(who, _seats.Keys.Append("host"), "a seat id or host"));
                        }
                        break;
                }
            }

            private void CheckSeat(Seat seat, string path)
            {
                if (string.IsNullOrWhiteSpace(seat.Color))
                    Add($"{path}.color", ErrorCodes.MissingField, $"Seat '{seat.Id}' has no color.", "Use a player color such as red, blue, green, yellow.");
                if (seat.HandZone == null)
                    return;
                if (!_zones.TryGetValue(seat.HandZone, out var zone))
                    Add($"{path}.handZone", ErrorCodes.UnknownZone, $"Zone '{seat.HandZone}' does not exist.",
                        Suggest.OneOf(seat.HandZone, _zones.Values.OfType<HandZone>().Select(z => z.Id), "a HandZone id") ?? "Define a HandZone for this seat first.");
                else if (!(zone is HandZone hand))
                    Add($"{path}.handZone", ErrorCodes.UnknownZone, $"Zone '{seat.HandZone}' is a {zone.Type}, not a HandZone.");
                else if (hand.Owner != seat.Id)
                    Add($"{path}.handZone", ErrorCodes.InvalidValue, $"HandZone '{hand.Id}' belongs to '{hand.Owner}', not '{seat.Id}'.",
                        $"Set the zone's owner to '{seat.Id}' or pick this seat's own hand zone.");
            }

            private string? SeatSuggestion(string actual) =>
                _seats.Count == 0 ? "Add the seat to players.seats first." : Suggest.OneOf(actual, _seats.Keys, "a seat id");

            private void Add(string path, string code, string message, string? suggestion = null) =>
                Errors.Add(new ValidationError(path, code, message, suggestion));
        }
    }
}
