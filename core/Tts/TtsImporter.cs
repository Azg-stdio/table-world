using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Core.Rules;
using Newtonsoft.Json.Linq;

namespace Core.Tts
{
    /// <summary>An asset an imported table needs: where TTS got it, its TTS cache name, and where the package keeps it.</summary>
    public sealed class ImportedAsset
    {
        public string Id = "";
        public string Url = "";
        public AssetType Type;
        /// <summary>TTS's file name for the URL in its own cache (<c>Mods/Images</c>, <c>Mods/Models</c>, <c>Mods/Assetbundles</c>), without extension.</summary>
        public string CacheKey = "";
        /// <summary>The package-relative path the table refers to.</summary>
        public string Path = "";
    }

    public sealed class ImportResult
    {
        public TableState State = new TableState();
        public List<ImportedAsset> Assets = new List<ImportedAsset>();
        /// <summary>What did not map exactly (kept as placeholders or dropped), one line each, for the person importing.</summary>
        public List<string> Notes = new List<string>();
        /// <summary>Objects by TTS type name, and how many became placeholders.</summary>
        public Dictionary<string, int> Types = new Dictionary<string, int>();
        public int Placeholders;
    }

    /// <summary>
    /// Reads a Tabletop Simulator save into our table model (M7 T8.1, <c>docs/tts-mapping.md</c>): objects with their
    /// states, contents, snap points, scripts and UI; zones; hands and seats; global script, UI and notebook. Runs on the
    /// player's own files; nothing is downloaded here (the asset list says what the package needs). Ids are the TTS GUIDs,
    /// so scripts that name GUIDs keep working; a repeated GUID gets a suffix. Anything without a counterpart becomes a
    /// placeholder that keeps its id, name, notes, container, position and script.
    /// </summary>
    public sealed class TtsImporter
    {
        private readonly ImportResult _result = new ImportResult();
        private readonly HashSet<string> _ids = new HashSet<string>();
        private readonly Dictionary<string, ImportedAsset> _assetsByKey = new Dictionary<string, ImportedAsset>();
        private TableState State => _result.State;

        public static ImportResult Import(string saveJson) => new TtsImporter().Run(JObject.Parse(saveJson));

        /// <summary>TTS's cache name for a URL: the URL with everything but letters and digits removed.</summary>
        public static string CacheKey(string url) => Regex.Replace(url ?? "", "[^A-Za-z0-9]", "");

        /// <summary>The asset id of a TTS custom UI image (XmlUI names images by their display name).</summary>
        public static string UiAssetId(string name) => "ui_" + Regex.Replace((name ?? "").ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_');

        private ImportResult Run(JObject save)
        {
            if ((string?)save["SaveName"] is { Length: > 0 } saveName)
                State.Game = new GameInfo { Id = "tts", Name = saveName };
            if ((string?)save["LuaScript"] is { Length: > 0 } global)
                State.Scripts["global"] = global;
            if ((string?)save["LuaScriptState"] is { Length: > 0 } globalState)
                State.GlobalScriptState = globalState;
            foreach (var ui in save["CustomUIAssets"] as JArray ?? new JArray())
                if ((string?)ui["Name"] is { Length: > 0 } name && (string?)ui["URL"] is { Length: > 0 } url)
                    Asset(url, AssetType.Image, idOverride: UiAssetId(name));
            State.ScriptUi = Ui((string?)save["XmlUI"], "global UI");
            Hands(save["Hands"] as JObject);
            foreach (var snap in save["SnapPoints"] as JArray ?? new JArray())
                if (snap is JObject point)
                    State.Zones.Add(Snap(point, parent: null));
            Notebook(save["TabStates"] as JObject);
            foreach (var o in save["ObjectStates"] as JArray ?? new JArray())
                if (o is JObject obj)
                    Convert(obj, container: null);
            foreach (var seat in State.Players.Seats)
                seat.HandZone = State.Zones.OfType<HandZone>().FirstOrDefault(z => z.Owner == seat.Id)?.Id;
            Table((string?)save["Table"]);
            return _result;
        }

        /// <summary>
        /// The table the game needs: big enough for its layout (TTS tables are far larger than our default, and games
        /// spread across them), or none for <c>Table_None</c>, where the game's own models are the surface.
        /// </summary>
        private void Table(string? ttsTable)
        {
            var onTable = State.Objects.Where(o => o.Container == null).Select(o => o.Transform.Position)
                .Concat(State.Zones.OfType<HandZone>().Select(z => z.Transform.Position)).ToList();
            var look = new TableLookDef();
            if (onTable.Count > 0)
            {
                // Most of the layout: the outermost few pieces are often parked far away on purpose.
                float Extent(Func<Vec3, float> axis)
                {
                    var values = onTable.Select(p => Math.Abs(axis(p))).OrderBy(v => v).ToList();
                    return values[(int)((values.Count - 1) * 0.98)];
                }
                const float margin = 0.15f;
                var width = Math.Max(TableLookDef.DefaultSize.X, 2f * Extent(p => p.X) + margin);
                var depth = Math.Max(TableLookDef.DefaultSize.Y, 2f * Extent(p => p.Z) + margin);
                if (width > TableLookDef.DefaultSize.X || depth > TableLookDef.DefaultSize.Y)
                    look.Size = new Vec2(Math.Min(30f, width), Math.Min(30f, depth));
            }
            if (ttsTable == "Table_None")
            {
                // No TTS table, so no 0.96 to measure from: the level most pieces rest at becomes our table height.
                look.Hidden = true;
                var shift = onTable.Count > 0 ? onTable.Select(p => p.Y).OrderBy(y => y).ElementAt(onTable.Count / 2) : 0f;
                Lower(shift);
                var lowest = onTable.Count > 0 ? onTable.Min(p => p.Y) - shift : 0f;
                look.LowestY = Math.Min(-0.05f, lowest - 0.3f);
            }
            if (look.Size != null || look.Hidden)
                State.Environment = new EnvironmentDef { Table = look };
        }

        /// <summary>Moves the whole table down by <paramref name="by"/> metres (objects and zones on the table; attached ones follow their parent).</summary>
        private void Lower(float by)
        {
            if (Math.Abs(by) < 1e-6f)
                return;
            foreach (var obj in State.Objects.Where(o => o.Container == null))
                obj.Transform.Position = new Vec3(obj.Transform.Position.X, obj.Transform.Position.Y - by, obj.Transform.Position.Z);
            foreach (var zone in State.Zones.Where(z => z.Parent == null))
                zone.Transform.Position = new Vec3(zone.Transform.Position.X, zone.Transform.Position.Y - by, zone.Transform.Position.Z);
        }

        // ---- ids and assets -----------------------------------------------------------------------------------------

        private string Claim(string? guid, string fallback)
        {
            var id = Regex.Replace((guid ?? "").ToLowerInvariant(), "[^a-z0-9_-]", "");
            if (id.Length == 0)
                id = fallback;
            var unique = id;
            for (var n = 2; !_ids.Add(unique); n++)
                unique = $"{id}_{n}";
            return unique;
        }

        /// <summary>The asset for a URL (one per URL and grid), registered in the table and the download list.</summary>
        private string? Asset(string? url, AssetType type, int columns = 1, int rows = 1, string? idOverride = null)
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;
            url = url!.Trim();
            var key = CacheKey(url);
            var lookup = $"{type}:{key}:{columns}x{rows}:{idOverride}";
            if (_assetsByKey.TryGetValue(lookup, out var known))
                return known.Id;
            var extension = type switch
            {
                AssetType.AssetBundle => ".unity3d",
                AssetType.Model => ".obj",
                _ => Regex.IsMatch(url, @"\.jpe?g($|\?)", RegexOptions.IgnoreCase) ? ".jpg" : ".png",
            };
            var prefix = type switch { AssetType.AssetBundle => "bundle", AssetType.Model => "mesh", _ => "img" };
            var id = idOverride ?? $"{prefix}_{ShortHash(key)}" + (columns * rows > 1 ? $"_{columns}x{rows}" : "");
            for (var n = 2; State.Assets.Any(a => a.Id == id); n++)
                id = (idOverride ?? $"{prefix}_{ShortHash(key)}") + "_" + n;
            var folder = type switch { AssetType.AssetBundle => "bundles", AssetType.Model => "models", _ => "images" };
            var asset = new ImportedAsset { Id = id, Url = url, Type = type, CacheKey = key, Path = $"{folder}/{key}{extension}" };
            _assetsByKey[lookup] = asset;
            _result.Assets.Add(asset);
            State.Assets.Add(new Asset
            {
                Id = id, Type = type, Path = asset.Path, Columns = Math.Max(1, columns), Rows = Math.Max(1, rows),
                // TTS meshes are in TTS units.
                MeshScale = type == AssetType.Model ? TtsUnits.TtsUnitsToMeters : 1f,
            });
            return id;
        }

        private static string ShortHash(string text)
        {
            unchecked
            {
                var h = 2166136261u;
                foreach (var c in text)
                    h = (h ^ c) * 16777619u;
                return h.ToString("x8");
            }
        }

        // ---- objects --------------------------------------------------------------------------------------------------

        private TableObject? Convert(JObject o, string? container, bool isState = false)
        {
            var name = (string?)o["Name"] ?? "";
            _result.Types.TryGetValue(name, out var seen);
            _result.Types[name] = seen + 1;

            if (Zone(o, name) is { } zone)
            {
                if (container != null)
                    _result.Notes.Add($"{name} {o["GUID"]} was inside a container; zones cannot be, so it lies on the table.");
                State.Zones.Add(zone);
                return null;
            }
            var obj = Object(o, name);
            obj.Id = Claim((string?)o["GUID"], name.ToLowerInvariant());
            Common(o, obj);
            if (!isState)
            {
                obj.Container = container;
                State.Objects.Add(obj);
            }
            foreach (var pair in o["States"] as JObject ?? new JObject())
                if (pair.Value is JObject stateObject && Convert(stateObject, null, isState: true) is { } state)
                    obj.States[pair.Key] = state;
            // A state's snap points would need the state on the table; they come back when it is (not yet).
            if (isState && o["AttachedSnapPoints"] is JArray { Count: > 0 })
                _result.Notes.Add($"State {obj.Id} of {name} has snap points; they are dropped until the state is shown.");
            else
                foreach (var snap in o["AttachedSnapPoints"] as JArray ?? new JArray())
                    if (snap is JObject point)
                        State.Zones.Add(Snap(point, obj.Id));
            if (!(obj is Deck) && !isState)
                foreach (var child in o["ContainedObjects"] as JArray ?? new JArray())
                    if (child is JObject inner)
                        Convert(inner, obj.Id);
            return obj;
        }

        private void Common(JObject o, TableObject obj)
        {
            obj.Name = NullIfEmpty((string?)o["Nickname"]);
            obj.Description = NullIfEmpty((string?)o["Description"]);
            obj.GmNotes = NullIfEmpty((string?)o["GMNotes"]);
            obj.Tags = (o["Tags"] as JArray)?.Select(t => (string?)t).Where(t => !string.IsNullOrEmpty(t)).Select(t => t!).ToList() ?? new List<string>();
            obj.Locked = (bool?)o["Locked"] ?? false;
            obj.Transform = Transform(o["Transform"] as JObject);
            obj.Snap = (bool?)o["Snap"] ?? true;
            obj.Tooltip = (bool?)o["Tooltip"] ?? true;
            obj.Hands = (bool?)o["Hands"] ?? true;
            obj.Grid = (bool?)o["Grid"] ?? true;
            var hide = (bool?)o["HideWhenFaceDown"];
            if (hide.HasValue && hide.Value != obj is Card)
                obj.HideWhenFaceDown = hide;
            obj.Script = NullIfEmpty((string?)o["LuaScript"]);
            obj.ScriptState = NullIfEmpty((string?)o["LuaScriptState"]);
            if ((string?)o["XmlUI"] is { Length: > 0 } xml)
                obj.ScriptUi = Ui(xml, $"{o["Name"]} {o["GUID"]}");
        }

        private static string? NullIfEmpty(string? text) => string.IsNullOrEmpty(text) ? null : text;

        private static TableTransform Transform(JObject? t)
        {
            float F(string key, float fallback = 0f) => t?[key] is JToken v && v.Type != JTokenType.Null ? (float)v : fallback;
            return new TableTransform
            {
                Position = new Vec3(F("posX") * TtsUnits.TtsUnitsToMeters, (F("posY") - TtsUnits.TableSurfaceY) * TtsUnits.TtsUnitsToMeters, F("posZ") * TtsUnits.TtsUnitsToMeters),
                Rotation = new Vec3(F("rotX"), F("rotY"), F("rotZ")),
                Scale = new Vec3(F("scaleX", 1f), F("scaleY", 1f), F("scaleZ", 1f)),
            };
        }

        private static float[]? Tint(JObject o)
        {
            if (!(o["ColorDiffuse"] is JObject c))
                return null;
            var tint = new[] { (float?)c["r"] ?? 1f, (float?)c["g"] ?? 1f, (float?)c["b"] ?? 1f };
            return tint.Select(v => Math.Max(0f, Math.Min(1f, v))).ToArray();
        }

        private TableObject Object(JObject o, string name)
        {
            switch (name)
            {
                case "Card":
                case "CardCustom":
                    return Card(o);
                case "Deck":
                case "DeckCustom":
                    return Deck(o);
                case "Custom_Token":
                {
                    var image = o["CustomImage"] as JObject;
                    var token = new Token
                    {
                        Asset = Asset((string?)image?["ImageURL"], AssetType.Image),
                        Shape = TokenShape.Image,
                        Stackable = (bool?)image?["CustomToken"]?["Stackable"] ?? false,
                        Count = Math.Max(1, (int?)o["Number"] ?? 1),
                    };
                    if ((float?)image?["CustomToken"]?["Thickness"] is { } thickness && thickness > 0f)
                        token.Thickness = thickness * TtsUnits.TtsUnitsToMeters;
                    return token;
                }
                case "Custom_Tile":
                {
                    var image = o["CustomImage"] as JObject;
                    var tile = image?["CustomTile"] as JObject;
                    var result = new Tile
                    {
                        Asset = Asset((string?)image?["ImageURL"], AssetType.Image),
                        Shape = ((int?)tile?["Type"] ?? 0) switch { 1 => TileShape.Hex, 2 => TileShape.Circle, 3 => TileShape.Rounded, _ => TileShape.Square },
                        Stackable = (bool?)tile?["Stackable"] ?? false,
                        Count = Math.Max(1, (int?)o["Number"] ?? 1),
                    };
                    if ((float?)tile?["Thickness"] is { } thickness && thickness > 0f)
                        result.Thickness = thickness * TtsUnits.TtsUnitsToMeters;
                    return result;
                }
                case "Custom_Dice":
                {
                    var image = o["CustomImage"] as JObject;
                    var faces = ((int?)image?["CustomDice"]?["Type"] ?? 1) switch { 0 => 4, 2 => 8, 3 => 10, 4 => 12, 5 => 20, _ => 6 };
                    return new Die { Faces = faces, Asset = Asset((string?)image?["ImageURL"], AssetType.Image) };
                }
                case "Custom_Board":
                    return new Board { Asset = Asset((string?)o["CustomImage"]?["ImageURL"], AssetType.Image) };
                case "Figurine_Custom":
                    return new Figure { Asset = Asset((string?)o["CustomImage"]?["ImageURL"], AssetType.Image) };
                case "Custom_Model":
                    return Model(o);
                case "Custom_Model_Bag":
                case "Custom_Model_Infinite_Bag":
                {
                    var bag = name.Contains("Infinite") ? new InfiniteBag() : new Bag();
                    bag.Asset = Asset((string?)o["CustomMesh"]?["MeshURL"], AssetType.Model);
                    bag.Tint = Tint(o);
                    return bag;
                }
                case "Custom_Assetbundle":
                    return BundleModel(o);
                case "Custom_Assetbundle_Bag":
                case "Custom_Assetbundle_Infinite_Bag":
                {
                    var bag = name.Contains("Infinite") ? new InfiniteBag() : new Bag();
                    bag.Asset = Asset((string?)o["CustomAssetbundle"]?["AssetbundleURL"], AssetType.AssetBundle);
                    return bag;
                }
                case "Bag":
                    return new Bag { Tint = Tint(o) };
                case "Infinite_Bag":
                    return new InfiniteBag { Tint = Tint(o) };
                case "Notecard":
                    return new Notecard();
                case "Counter":
                    return new Counter { Value = (int?)o["Counter"]?["value"] ?? 0 };
                case "Digital_Clock":
                {
                    var clock = o["Clock"] as JObject;
                    return new Clock
                    {
                        Mode = ((string?)clock?["ClockMode"]) switch { "Stopwatch" => ClockMode.Stopwatch, "Timer" => ClockMode.Timer, _ => ClockMode.Clock },
                        Seconds = (float?)clock?["SecondsPassed"] ?? 0f,
                        Running = !((bool?)clock?["Paused"] ?? true),
                    };
                }
                case "BlockSquare":
                    return Builtin("block_square", o);
                case "BlockRectangle":
                    return Builtin("block_rectangle", o);
                case "PlayerPawn":
                case "Chess_Pawn":
                    return Builtin("pawn", o);
            }
            if (name.StartsWith("Die_", StringComparison.Ordinal) && int.TryParse(new string(name.Substring(4).TakeWhile(char.IsDigit).ToArray()), out var n) && Die.SupportedFaces.Contains(n))
                return new Die { Faces = n, Tint = Tint(o) };
            if (name.StartsWith("Checker_", StringComparison.Ordinal))
                return Builtin("checker", o);
            // Anything else keeps its place and data as a grey box until it gets a counterpart.
            _result.Placeholders++;
            _result.Notes.Add($"{name} {o["GUID"]} ('{o["Nickname"]}') has no counterpart yet: kept as a placeholder.");
            var placeholder = new Model { Asset = BuiltinAsset("placeholder") };
            placeholder.TtsExtra = new JObject { ["ttsName"] = name };
            return placeholder;
        }

        private Model Builtin(string piece, JObject o) => new Model { Asset = BuiltinAsset(piece), Tint = Tint(o), Kind = piece == "checker" ? ModelKind.Chip : ModelKind.Generic };

        private string BuiltinAsset(string piece)
        {
            var id = "builtin_" + piece;
            if (State.Assets.All(a => a.Id != id))
                State.Assets.Add(new Asset { Id = id, Type = AssetType.Model, Path = Rules.Asset.BuiltinPrefix + piece });
            return id;
        }

        private Model Model(JObject o)
        {
            var mesh = o["CustomMesh"] as JObject;
            var model = new Model
            {
                Asset = Asset((string?)mesh?["MeshURL"], AssetType.Model) ?? BuiltinAsset("placeholder"),
                DiffuseAsset = Asset((string?)mesh?["DiffuseURL"], AssetType.Image),
                Material = ((int?)mesh?["MaterialIndex"] ?? 0) switch { 1 => ModelMaterial.Wood, 2 => ModelMaterial.Metal, 3 => ModelMaterial.Cardboard, 4 => ModelMaterial.Glass, _ => ModelMaterial.Plastic },
                Kind = ((int?)mesh?["TypeIndex"] ?? 0) switch { 1 => ModelKind.Figurine, 2 => ModelKind.Dice, 3 => ModelKind.Coin, 4 => ModelKind.Board, 5 => ModelKind.Chip, _ => ModelKind.Generic },
            };
            if (model.DiffuseAsset == null)
                model.Tint = Tint(o);
            return model;
        }

        private Model BundleModel(JObject o)
        {
            var bundle = o["CustomAssetbundle"] as JObject;
            var model = new Model { Asset = Asset((string?)bundle?["AssetbundleURL"], AssetType.AssetBundle) ?? BuiltinAsset("placeholder") };
            if ((string?)bundle?["AssetbundleSecondaryURL"] is { Length: > 0 } secondary)
                model.TtsExtra = new JObject { ["secondaryBundle"] = Asset(secondary, AssetType.AssetBundle) };
            return model;
        }

        // ---- cards and decks ------------------------------------------------------------------------------------------

        private (string Face, int FaceIndex, string Back, int BackIndex, bool Sideways) CardLook(JObject o)
        {
            var cardId = (int?)o["CardID"] ?? 0;
            var key = (cardId / 100).ToString(CultureInfo.InvariantCulture);
            var index = cardId % 100;
            var decks = o["CustomDeck"] as JObject;
            var deck = decks?[key] as JObject ?? decks?.Properties().FirstOrDefault()?.Value as JObject;
            var columns = Math.Max(1, (int?)deck?["NumWidth"] ?? 10);
            var rows = Math.Max(1, (int?)deck?["NumHeight"] ?? 7);
            var face = Asset((string?)deck?["FaceURL"], AssetType.Image, columns, rows) ?? BuiltinImage("standard_deck", 10, 7);
            var uniqueBack = (bool?)deck?["UniqueBack"] ?? false;
            var back = uniqueBack
                ? Asset((string?)deck?["BackURL"], AssetType.Image, columns, rows)
                : Asset((string?)deck?["BackURL"], AssetType.Image);
            return (face, Math.Min(index, columns * rows - 1), back ?? BuiltinImage("standard_back", 1, 1), uniqueBack ? index : 0, (bool?)o["SidewaysCard"] ?? false);
        }

        private string BuiltinImage(string name, int columns, int rows)
        {
            var id = "builtin_" + name;
            if (State.Assets.All(a => a.Id != id))
                State.Assets.Add(new Asset { Id = id, Type = AssetType.Image, Path = Rules.Asset.BuiltinPrefix + name, Columns = columns, Rows = rows });
            return id;
        }

        private Card Card(JObject o)
        {
            var look = CardLook(o);
            return new Card
            {
                FaceAsset = look.Face, FaceIndex = look.FaceIndex, BackAsset = look.Back, BackIndex = look.BackIndex,
                Size = look.Sideways ? new Vec2(Rules.Card.DefaultSize.Y, Rules.Card.DefaultSize.X) : (Vec2?)null,
            };
        }

        private Deck Deck(JObject o)
        {
            var deck = new Deck();
            // DeckIDs[0] is the top card, as our cards[0]; the contained cards carry their own data.
            foreach (var c in o["ContainedObjects"] as JArray ?? new JArray())
            {
                if (!(c is JObject card))
                    continue;
                var look = CardLook(card);
                if (deck.Cards.Count == 0)
                {
                    deck.FaceAsset = look.Face;
                    deck.BackAsset = look.Back;
                    deck.BackIndex = look.BackIndex;
                    if (look.Sideways)
                        deck.Size = new Vec2(Rules.Card.DefaultSize.Y, Rules.Card.DefaultSize.X);
                }
                deck.Cards.Add(new DeckCard
                {
                    Id = Claim((string?)card["GUID"], "card"),
                    Name = NullIfEmpty((string?)card["Nickname"]),
                    Description = NullIfEmpty((string?)card["Description"]),
                    GmNotes = NullIfEmpty((string?)card["GMNotes"]),
                    Tags = (card["Tags"] as JArray)?.Select(t => (string?)t).Where(t => !string.IsNullOrEmpty(t)).Select(t => t!).ToList() ?? new List<string>(),
                    FaceAsset = look.Face == deck.FaceAsset ? null : look.Face,
                    FaceIndex = look.FaceIndex,
                    BackAsset = look.Back == deck.BackAsset ? null : look.Back,
                    BackIndex = look.BackIndex == deck.BackIndex ? (int?)null : look.BackIndex,
                    Script = NullIfEmpty((string?)card["LuaScript"]),
                });
            }
            if (deck.Cards.Count == 0)
            {
                deck.FaceAsset = BuiltinImage("standard_deck", 10, 7);
                deck.BackAsset = BuiltinImage("standard_back", 1, 1);
            }
            return deck;
        }

        // ---- zones ----------------------------------------------------------------------------------------------------

        private Zone? Zone(JObject o, string name)
        {
            Zone? zone = name switch
            {
                "ScriptingTrigger" => new ScriptingZone(),
                "HandTrigger" => new HandZone { Owner = Seat((string?)o["FogColor"]) },
                "FogOfWarTrigger" => new HiddenZone { VisibleTo = (string?)o["FogColor"] is { Length: > 0 } fog ? new List<string> { Seat(fog) } : new List<string>() },
                "RandomizeTrigger" => new RandomizeZone(),
                "LayoutZone" => new LayoutZone { ManualOnly = (bool?)o["LayoutZone"]?["Options"]?["ManualOnly"] ?? false },
                _ => null,
            };
            if (zone == null)
                return null;
            zone.Id = Claim((string?)o["GUID"], "zone");
            zone.Name = NullIfEmpty((string?)o["Nickname"]);
            zone.Tags = (o["Tags"] as JArray)?.Select(t => (string?)t).Where(t => !string.IsNullOrEmpty(t)).Select(t => t!).ToList() ?? new List<string>();
            var t = Transform(o["Transform"] as JObject);
            // A trigger's scale is its box, in TTS units.
            zone.Size = new Vec3(Math.Abs(t.Scale.X) * TtsUnits.TtsUnitsToMeters, Math.Abs(t.Scale.Y) * TtsUnits.TtsUnitsToMeters, Math.Abs(t.Scale.Z) * TtsUnits.TtsUnitsToMeters);
            zone.Transform = new TableTransform { Position = t.Position, Rotation = t.Rotation };
            zone.Script = NullIfEmpty((string?)o["LuaScript"]);
            zone.ScriptState = NullIfEmpty((string?)o["LuaScriptState"]);
            if ((string?)o["XmlUI"] is { Length: > 0 } xml)
                zone.ScriptUi = Ui(xml, $"{name} {o["GUID"]}");
            return zone;
        }

        /// <summary>The seat of a TTS player colour, made the first time a colour appears.</summary>
        private string Seat(string? color)
        {
            var id = (color ?? "white").ToLowerInvariant();
            if (State.Players.Seats.All(s => s.Id != id))
                State.Players.Seats.Add(new Seat { Id = id, Color = id });
            return id;
        }

        private void Hands(JObject? hands)
        {
            foreach (var hand in hands?["HandTransforms"] as JArray ?? new JArray())
            {
                if (!(hand is JObject h))
                    continue;
                var t = Transform(h["Transform"] as JObject);
                State.Zones.Add(new HandZone
                {
                    Id = Claim("hand_" + ((string?)h["Color"] ?? "").ToLowerInvariant(), "hand"),
                    Owner = Seat((string?)h["Color"]),
                    Size = new Vec3(Math.Abs(t.Scale.X) * TtsUnits.TtsUnitsToMeters, Math.Abs(t.Scale.Y) * TtsUnits.TtsUnitsToMeters, Math.Abs(t.Scale.Z) * TtsUnits.TtsUnitsToMeters),
                    Transform = new TableTransform { Position = t.Position, Rotation = t.Rotation },
                });
            }
        }

        private SnapPoint Snap(JObject point, string? parent)
        {
            var p = point["Position"] as JObject;
            var r = point["Rotation"] as JObject;
            float F(JObject? v, string key) => (float?)v?[key] ?? 0f;
            // Global snap points sit on the table; attached ones are in their object's own frame.
            var y = parent == null ? F(p, "y") - TtsUnits.TableSurfaceY : F(p, "y");
            return new SnapPoint
            {
                Id = Claim(null, parent == null ? "snap" : parent + "_snap"),
                Parent = parent,
                RotationSnap = r != null,
                Transform = new TableTransform
                {
                    Position = new Vec3(F(p, "x") * TtsUnits.TtsUnitsToMeters, y * TtsUnits.TtsUnitsToMeters, F(p, "z") * TtsUnits.TtsUnitsToMeters),
                    Rotation = new Vec3(F(r, "x"), F(r, "y"), F(r, "z")),
                },
            };
        }

        private void Notebook(JObject? tabs)
        {
            foreach (var pair in tabs ?? new JObject())
            {
                if (!(pair.Value is JObject tab))
                    continue;
                var title = (string?)tab["title"] ?? "";
                var body = (string?)tab["body"] ?? "";
                var color = (string?)tab["color"] ?? "Grey";
                if (body.Length == 0 && color != "Grey")
                    continue;
                State.Notebook.Add(new NoteTab
                {
                    Id = Claim("note_" + pair.Key, "note"),
                    Title = title.Length > NoteTab.MaxTitle ? title.Substring(0, NoteTab.MaxTitle) : title,
                    Body = body.Length > NoteTab.MaxBody ? body.Substring(0, NoteTab.MaxBody) : body,
                    Color = color == "Grey" ? null : Seat(color),
                });
            }
        }

        private List<UiNode> Ui(string? xml, string where)
        {
            if (string.IsNullOrWhiteSpace(xml))
                return new List<UiNode>();
            try
            {
                return XmlUi.Parse(xml!);
            }
            catch (System.Xml.XmlException e)
            {
                _result.Notes.Add($"The XmlUI of {where} does not read ({e.Message}); left out.");
                return new List<UiNode>();
            }
        }
    }
}
