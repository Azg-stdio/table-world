using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Core.Rules
{
    /// <summary>Position in meters (table surface is Y = 0), rotation in Euler degrees (Unity order), scale as a multiplier.</summary>
    public sealed class Transform
    {
        public Vec3 Position { get; set; }
        public Vec3 Rotation { get; set; }
        public Vec3 Scale { get; set; } = Vec3.One;

        [JsonIgnore]
        public bool IsDefault => Position == Vec3.Zero && Rotation == Vec3.Zero && Scale == Vec3.One;

        public bool ShouldSerializeScale() => Scale != Vec3.One;
    }

    /// <summary>
    /// Base of every physical object on the table. The JSON <c>type</c> is the concrete class name
    /// (see <see cref="TableObjectTypes"/>), so renaming a subclass is a format change.
    /// </summary>
    public abstract class TableObject
    {
        [JsonProperty(Order = -3)]
        public string Id { get; set; } = "";

        [JsonProperty(Order = -2)]
        public string Type => GetType().Name;

        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? GmNotes { get; set; }
        public List<string> Tags { get; set; } = new List<string>();
        public bool Locked { get; set; }
        public Transform Transform { get; set; } = new Transform();
        public bool ShouldSerializeTransform() => !Transform.IsDefault;

        /// <summary>Id of the Bag/InfiniteBag holding this object, or null when it is on the table.</summary>
        public string? Container { get; set; }

        /// <summary>RGB or RGBA multiplier, 0–1 (TTS <c>ColorDiffuse</c>).</summary>
        public float[]? Tint { get; set; }

        /// <summary>Alternate states keyed by state number, TTS-style. The object itself is the active state.</summary>
        public Dictionary<string, TableObject> States { get; set; } = new Dictionary<string, TableObject>();

        /// <summary>Optional per-object Lua (M5).</summary>
        public string? Script { get; set; }

        /// <summary>TTS fields we carry but do not interpret yet (interaction flags, AltLookAngle, ...). Free-form.</summary>
        public JObject? TtsExtra { get; set; }

        /// <summary>
        /// True when the object's local up axis points up. Unity applies Euler Z, then X, then Y, so up.y = cos(x)·cos(z).
        /// A card or deck at rotation [0, 0, 0] is face up; [0, 0, 180] is face down (same as TTS).
        /// </summary>
        [JsonIgnore]
        public bool IsFaceUp
        {
            get
            {
                const double degToRad = Math.PI / 180.0;
                var r = Transform.Rotation;
                return Math.Cos(r.X * degToRad) * Math.Cos(r.Z * degToRad) > 0;
            }
        }
    }

    public enum TokenShape { Round, Square, Image }
    public enum TileShape { Square, Hex, Circle, Rounded }
    public enum ColliderKind { Convex, Box, Mesh }
    public enum ModelKind { Generic, Figurine, Dice, Coin, Board, Chip }
    public enum ModelMaterial { Plastic, Wood, Metal, Cardboard, Glass }

    /// <summary>A single card. Face and back are cells of image assets whose grid is defined on the asset.</summary>
    public sealed class Card : TableObject
    {
        public static readonly Vec2 DefaultSize = new Vec2(0.0635f, 0.0889f);

        public string FaceAsset { get; set; } = "";
        public int FaceIndex { get; set; }
        public string BackAsset { get; set; } = "";
        public int BackIndex { get; set; }

        /// <summary>Width × height in meters; null means <see cref="DefaultSize"/>.</summary>
        public Vec2? Size { get; set; }

        /// <summary>Free-form card data for rule conditions, e.g. <c>{ "rank": 11, "suit": "hearts" }</c>.</summary>
        public JObject? Meta { get; set; }
    }

    /// <summary>One card inside a <see cref="Deck"/>. Unset asset fields fall back to the deck's defaults.</summary>
    public sealed class DeckCard
    {
        /// <summary>Optional stable id. The engine mints <c>&lt;deckId&gt;-&lt;n&gt;</c> when a card without id leaves the deck.</summary>
        [JsonProperty(Order = -2)]
        public string? Id { get; set; }

        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? GmNotes { get; set; }
        public List<string> Tags { get; set; } = new List<string>();
        public string? FaceAsset { get; set; }
        public int FaceIndex { get; set; }
        public string? BackAsset { get; set; }
        public int? BackIndex { get; set; }
        public JObject? Meta { get; set; }
        public string? Script { get; set; }
        public JObject? TtsExtra { get; set; }
    }

    /// <summary>An ordered stack of cards. <c>Cards[0]</c> is the top (the card a draw takes), same as TTS <c>DeckIDs[0]</c>.</summary>
    public sealed class Deck : TableObject
    {
        public string FaceAsset { get; set; } = "";
        public string BackAsset { get; set; } = "";
        public int BackIndex { get; set; }
        public Vec2? Size { get; set; }
        public List<DeckCard> Cards { get; set; } = new List<DeckCard>();
    }

    public sealed class Token : TableObject
    {
        public string? Asset { get; set; }
        public TokenShape Shape { get; set; }
        /// <summary>Meters; null means the view's default.</summary>
        public float? Thickness { get; set; }
        public bool Stackable { get; set; }
    }

    public sealed class Tile : TableObject
    {
        public string? Asset { get; set; }
        public TileShape Shape { get; set; }
        public float? Thickness { get; set; }
        public bool Stackable { get; set; }
    }

    public sealed class Die : TableObject
    {
        public static readonly IReadOnlyList<int> SupportedFaces = new[] { 4, 6, 8, 10, 12, 20 };

        public int Faces { get; set; } = 6;
        /// <summary>Optional label per face; length must equal <see cref="Faces"/>.</summary>
        public List<string>? Labels { get; set; }
        /// <summary>Face showing up, 1..Faces; null before the first roll.</summary>
        public int? Value { get; set; }
        /// <summary>Optional image asset for custom dice (TTS <c>Custom_Dice</c>).</summary>
        public string? Asset { get; set; }
    }

    public sealed class Model : TableObject
    {
        /// <summary>glTF model or AssetBundle asset.</summary>
        public string Asset { get; set; } = "";
        public string? DiffuseAsset { get; set; }
        public ColliderKind Collider { get; set; }
        public ModelKind Kind { get; set; }
        public ModelMaterial Material { get; set; }
    }

    /// <summary>A container. Its contents are the objects whose <c>container</c> is this id, in list order (first = top).</summary>
    public class Bag : TableObject
    {
        /// <summary>Optional model asset for the bag's look; null means the default bag.</summary>
        public string? Asset { get; set; }
    }

    /// <summary>A bag that clones its single template object on every take.</summary>
    public sealed class InfiniteBag : Bag
    {
    }

    public sealed class Board : TableObject
    {
        public string? Asset { get; set; }
        public Vec2? Size { get; set; }
    }

    /// <summary>A standee (TTS <c>Figurine_Custom</c>).</summary>
    public sealed class Figure : TableObject
    {
        public string? Asset { get; set; }
        /// <summary>Meters; null means the view's default.</summary>
        public float? Height { get; set; }
    }

    /// <summary>Registry of the JSON <c>type</c> values for objects.</summary>
    public static class TableObjectTypes
    {
        public static readonly IReadOnlyDictionary<string, Type> ByName = Build(
            typeof(Card), typeof(Deck), typeof(Token), typeof(Tile), typeof(Die),
            typeof(Model), typeof(Bag), typeof(InfiniteBag), typeof(Board), typeof(Figure));

        internal static IReadOnlyDictionary<string, Type> Build(params Type[] types)
        {
            var map = new Dictionary<string, Type>(StringComparer.Ordinal);
            foreach (var t in types)
                map.Add(t.Name, t);
            return map;
        }
    }
}
