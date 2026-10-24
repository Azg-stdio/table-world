using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Core.Rules
{
    /// <summary>Position in meters (table surface is Y = 0), rotation in Euler degrees (Unity order), scale as a multiplier.</summary>
    public sealed class TableTransform
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
        /// <summary>
        /// Seat colours that do not see this object at all (TTS <c>setInvisibleTo</c>). Like hidden zones, honest play:
        /// every machine has the object.
        /// </summary>
        public List<string> InvisibleTo { get; set; } = new List<string>();
        public bool ShouldSerializeInvisibleTo() => InvisibleTo.Count > 0;
        public bool Locked { get; set; }
        public TableTransform Transform { get; set; } = new TableTransform();
        public bool ShouldSerializeTransform() => !Transform.IsDefault;

        /// <summary>Id of the Bag/InfiniteBag holding this object, or null when it is on the table.</summary>
        public string? Container { get; set; }

        /// <summary>False: the object ignores snap points when dropped (TTS <c>Snap</c>, G16).</summary>
        [System.ComponentModel.DefaultValue(true)]
        public bool Snap { get; set; } = true;

        /// <summary>False: hovering the object shows no name or description (TTS <c>Tooltip</c>, G16).</summary>
        [System.ComponentModel.DefaultValue(true)]
        public bool Tooltip { get; set; } = true;

        /// <summary>False: the object never joins a hand; dropped in a hand zone it just lies there (TTS <c>Hands</c>, G39).</summary>
        [System.ComponentModel.DefaultValue(true)]
        public bool Hands { get; set; } = true;

        /// <summary>
        /// Whether the name and description stay secret while the object lies face down (TTS <c>HideWhenFaceDown</c>, G39).
        /// Null: cards hide, other objects do not (a face-down deck keeps its deck name, as in TTS).
        /// </summary>
        public bool? HideWhenFaceDown { get; set; }

        /// <summary>True when its name must not be shown now: face down and hiding when face down.</summary>
        [JsonIgnore]
        public bool NameHidden => !IsFaceUp && (HideWhenFaceDown ?? this is Card);

        /// <summary>False: the object ignores the table grid's snapping (TTS <c>Grid</c> flag, G28).</summary>
        // Table JSON skips default values: [DefaultValue] names the real default, so false is written and true is not.
        [System.ComponentModel.DefaultValue(true)]
        public bool Grid { get; set; } = true;

        /// <summary>RGB or RGBA multiplier, 0–1 (TTS <c>ColorDiffuse</c>).</summary>
        public float[]? Tint { get; set; }

        /// <summary>Alternate states keyed by state number, TTS-style. The object itself is the active state.</summary>
        public Dictionary<string, TableObject> States { get; set; } = new Dictionary<string, TableObject>();

        /// <summary>Optional per-object Lua (M5).</summary>
        public string? Script { get; set; }

        /// <summary>What the object's script saved with <c>onSave</c> (TTS <c>script_state</c>); given back to its <c>onLoad</c>.</summary>
        public string? ScriptState { get; set; }

        /// <summary>Buttons scripts put on this object (M5, TTS <c>createButton</c>).</summary>
        public List<ButtonDef> Buttons { get; set; } = new List<ButtonDef>();
        public bool ShouldSerializeButtons() => Buttons.Count > 0;

        /// <summary>Text fields scripts put on this object (M5, TTS <c>createInput</c>).</summary>
        public List<InputDef> Inputs { get; set; } = new List<InputDef>();
        public bool ShouldSerializeInputs() => Inputs.Count > 0;

        /// <summary>UI its script drew on it (M5, TTS object <c>UI.*</c>; ADR 0004).</summary>
        public List<UiNode> ScriptUi { get; set; } = new List<UiNode>();
        public bool ShouldSerializeScriptUi() => ScriptUi.Count > 0;

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
        /// <summary>Identical pieces dropped on each other form one counted stack (TTS <c>Stackable</c>).</summary>
        public bool Stackable { get; set; }

        /// <summary>
        /// How many identical pieces this object is (G14, TTS <c>Number</c>): a stack of chips is one object. More than 1
        /// only when <see cref="Stackable"/>.
        /// </summary>
        public int Count { get; set; } = 1;
        public bool ShouldSerializeCount() => Count != 1;
    }

    public sealed class Tile : TableObject
    {
        public string? Asset { get; set; }
        public TileShape Shape { get; set; }
        public float? Thickness { get; set; }
        /// <summary>Identical pieces dropped on each other form one counted stack (TTS <c>Stackable</c>).</summary>
        public bool Stackable { get; set; }

        /// <summary>
        /// How many identical pieces this object is (G14, TTS <c>Number</c>): a stack of chips is one object. More than 1
        /// only when <see cref="Stackable"/>.
        /// </summary>
        public int Count { get; set; } = 1;
        public bool ShouldSerializeCount() => Count != 1;
    }

    public sealed class Die : TableObject
    {
        public static readonly IReadOnlyList<int> SupportedFaces = new[] { 4, 6, 8, 10, 12, 20 };

        public int Faces { get; set; } = 6;
        /// <summary>
        /// Optional label per face, value 1 first; length must equal <see cref="Faces"/> (TTS <c>RotationValues</c>, e.g.
        /// "Extra Turn"). A labelled die shows and announces its label instead of the number (G21).
        /// </summary>
        public List<string>? Labels { get; set; }
        /// <summary>Face showing up, 1..Faces; null before the first roll.</summary>
        public int? Value { get; set; }
        /// <summary>
        /// Optional image for custom dice (TTS <c>Custom_Dice</c>). A d6 image follows the TTS template: a square whose
        /// lower two thirds hold the faces in two rows of three (see docs/tts-mapping.md).
        /// </summary>
        public string? Asset { get; set; }

        /// <summary>What face <paramref name="value"/> says: its label, or the number.</summary>
        public string Face(int value) =>
            Labels != null && value >= 1 && value <= Labels.Count && !string.IsNullOrWhiteSpace(Labels[value - 1]) ? Labels[value - 1] : value.ToString();

        /// <summary>True when every face is a plain number (rolls can be added up).</summary>
        [Newtonsoft.Json.JsonIgnore]
        public bool Numeric => Labels == null || Labels.Select((l, i) => (l, i)).All(p => string.IsNullOrWhiteSpace(p.l) || p.l == (p.i + 1).ToString());
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

    /// <summary>A number on the table that players count up and down (TTS <c>Counter</c>, G27).</summary>
    public sealed class Counter : TableObject
    {
        public const int MinValue = -99999;
        public const int MaxValue = 999999;

        public int Value { get; set; }
    }

    public enum ClockMode
    {
        /// <summary>The time of day, on each player's own clock.</summary>
        Clock,
        /// <summary>Counts up from <see cref="Clock.Seconds"/>.</summary>
        Stopwatch,
        /// <summary>Counts down from <see cref="Clock.Seconds"/> to zero, then stops and rings.</summary>
        Timer,
    }

    /// <summary>
    /// A digital clock (TTS <c>Digital_Clock</c>, G27). <see cref="Seconds"/> is the reading when it last started or
    /// stopped; while <see cref="Running"/>, each machine adds the time since it saw the start.
    /// </summary>
    public sealed class Clock : TableObject
    {
        public const float MaxSeconds = 100 * 3600;

        public ClockMode Mode { get; set; }
        public float Seconds { get; set; }
        /// <summary>What a timer was set to: Reset goes back to it.</summary>
        public float Duration { get; set; }
        public bool Running { get; set; }
    }

    /// <summary>Registry of the JSON <c>type</c> values for objects.</summary>
    /// <summary>
    /// A note lying on the table (TTS <c>Notecard</c>, G27): <see cref="TableObject.Name"/> is its title and
    /// <see cref="TableObject.Description"/> its text, both printed on it. Players edit them like any name and description.
    /// </summary>
    public sealed class Notecard : TableObject
    {
        public static readonly Vec2 Size = new Vec2(0.11f, 0.075f);
    }

    public static class TableObjectTypes
    {
        public static readonly IReadOnlyDictionary<string, Type> ByName = Build(
            typeof(Card), typeof(Deck), typeof(Token), typeof(Tile), typeof(Die),
            typeof(Model), typeof(Bag), typeof(InfiniteBag), typeof(Board), typeof(Figure), typeof(Counter), typeof(Clock), typeof(Notecard));

        internal static IReadOnlyDictionary<string, Type> Build(params Type[] types)
        {
            var map = new Dictionary<string, Type>(StringComparer.Ordinal);
            foreach (var t in types)
                map.Add(t.Name, t);
            return map;
        }
    }
}
