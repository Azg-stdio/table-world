namespace Core.Rules
{
    /// <summary>The built-in rooms. A game can also bring its own room model (<see cref="EnvironmentDef.RoomAsset"/>).</summary>
    public enum RoomPreset
    {
        /// <summary>A small dim room: warm lamp over the table, dark walls, wooden floor. The default.</summary>
        GameNight,
        /// <summary>No room: the table over a plain background colour (<see cref="EnvironmentDef.WallColor"/>).</summary>
        None,
        /// <summary>
        /// A karaoke room (M11): the game night room with sofas round the table instead of chairs, a big screen on the far
        /// wall and a stage light on whoever sings.
        /// </summary>
        Karaoke,
        /// <summary>
        /// A medieval tavern (M12): stone walls over a wooden wainscot, beams, a plank floor, a chandelier of candles over
        /// the table, candles on the walls and a fire in the hearth, all flickering; dark red cloth on the table.
        /// </summary>
        Tavern,
    }

    /// <summary>How the game's panels look (M12): the default dark glass, or parchment and wood for a medieval game.</summary>
    /// <summary>The table's outline seen from above.</summary>
    public enum TableShape
    {
        /// <summary>A rectangle as wide and deep as the table's size. The default.</summary>
        Rectangle,
        /// <summary>
        /// A disc as wide as the larger of the table's two sizes (TTS <c>Table_Circular</c>): everyone round it sees
        /// everyone else, as in a party game for many.
        /// </summary>
        Round,
    }

    public enum HudTheme
    {
        Default,
        /// <summary>Parchment panels, ink text, gold titles and wooden buttons, with a serif for headings.</summary>
        Parchment,
    }

    /// <summary>
    /// Light in the room. Every value is optional; unset values keep the room preset's. The lamp and ambient set the
    /// room's mood; game pieces and the felt are lit only by the table light, neutral white by default, so card art keeps
    /// its true colours whatever mood a game picks.
    /// </summary>
    public sealed class LightingDef
    {
        /// <summary>Light that reaches everything, from the room (0 = none). Colour 0–1.</summary>
        public float? AmbientIntensity { get; set; }
        public float[]? AmbientColor { get; set; }
        /// <summary>The lamp over the table (0 = off).</summary>
        public float? LampIntensity { get; set; }
        public float[]? LampColor { get; set; }
        /// <summary>The light on game pieces and the felt only (0 = off). Neutral white unless a game wants otherwise.</summary>
        public float? TableLightIntensity { get; set; }
        public float[]? TableLightColor { get; set; }

        /// <summary>
        /// Multiplies the lamp and the table light: TTS's <c>light_intensity</c> over its default (0.54), so a game that
        /// dims or brightens TTS's light does the same here. Null is 1.
        /// </summary>
        public float? LightScale { get; set; }
        /// <summary>Multiplies the ambient light: TTS's <c>ambient_intensity</c> over its default (1.3). Null is 1.</summary>
        public float? AmbientScale { get; set; }
        /// <summary>Tints the lamp and the table light: TTS's <c>light_color</c> over its default warm white. Null is none.</summary>
        public float[]? LightTint { get; set; }

        /// <summary>
        /// The room's lights alone (the lamp and its warm fill: the walls, the floor, the table's frame), changed while
        /// playing (<c>SetLightCommand</c>): a multiplier (1 normal, 0 dark) and a tint (0–2 per channel). Night in the
        /// room leaves the cards readable. Null is normal.
        /// </summary>
        public float? RoomScale { get; set; }
        public float[]? RoomTint { get; set; }
        /// <summary>The table light alone (the felt and the pieces on it), changed while playing. Null is normal.</summary>
        public float? TableScale { get; set; }
        public float[]? TableTint { get; set; }

        /// <summary>Seconds a light change while playing takes on screen, when it does not say.</summary>
        public const float DefaultFade = 1.5f;
        /// <summary>
        /// How long the last change while playing (<c>SetLightCommand</c>) takes on screen, in seconds; null is
        /// <see cref="DefaultFade"/>. A table that loads shows its light at once.
        /// </summary>
        public float? Fade { get; set; }
    }

    /// <summary>TTS's lighting, as saves and scripts give it, against the multipliers of <see cref="LightingDef"/>.</summary>
    public static class TtsLighting
    {
        public const float DefaultLightIntensity = 0.54f;
        public const float DefaultAmbientIntensity = 1.3f;
        public static readonly float[] DefaultLightColor = { 1f, 0.9804f, 0.8902f };

        /// <summary>The tint a TTS light colour means here (each channel over TTS's default), or null for the default.</summary>
        public static float[]? Tint(float r, float g, float b)
        {
            var tint = new[] { r / DefaultLightColor[0], g / DefaultLightColor[1], b / DefaultLightColor[2] };
            for (var i = 0; i < 3; i++)
                tint[i] = tint[i] < 0f ? 0f : tint[i] > 2f ? 2f : tint[i];
            return tint[0] > 0.98f && tint[0] < 1.02f && tint[1] > 0.98f && tint[1] < 1.02f && tint[2] > 0.98f && tint[2] < 1.02f ? null : tint;
        }
    }

    /// <summary>
    /// The room's post-processing (M12), each 0–1 and null for the room preset's: <see cref="Fog"/> greys what is far
    /// (the walls, never the table), <see cref="Vignette"/> darkens the picture's edges, <see cref="Bloom"/> makes flames
    /// and lamps glow (not cards: only what is far brighter than white). Ambient occlusion is always on.
    /// </summary>
    public sealed class LookDef
    {
        public float? Fog { get; set; }
        public float[]? FogColor { get; set; }
        public float? Vignette { get; set; }
        public float? Bloom { get; set; }
    }

    /// <summary>How the table itself looks.</summary>
    public sealed class TableLookDef
    {
        /// <summary>Default playing surface, metres (width × depth).</summary>
        public static readonly Vec2 DefaultSize = new Vec2(1.6f, 1.0f);

        /// <summary>
        /// Playing surface in metres; null is <see cref="DefaultSize"/>. TTS games lay out far more than our default
        /// (Pokémon spans ±2.3 m, Gloomhaven ±4.4 m), so imported tables say how much room they need.
        /// </summary>
        public Vec2? Size { get; set; }
        /// <summary>The table's outline: a rectangle, or a disc as wide as the larger of <see cref="Size"/>'s values.</summary>
        public TableShape Shape { get; set; }

        /// <summary>The playing surface's width × depth, metres: a round table is as deep as it is wide.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public Vec2 SurfaceSize
        {
            get
            {
                var size = Size ?? DefaultSize;
                if (Shape != TableShape.Round)
                    return size;
                var diameter = System.Math.Max(size.X, size.Y);
                return new Vec2(diameter, diameter);
            }
        }

        /// <summary>No table at all (TTS <c>Table_None</c>): the game brings its own surface as objects.</summary>
        public bool Hidden { get; set; }
        /// <summary>
        /// With no table, how far below 0 (metres) a piece may sit before it counts as fallen off and is put back.
        /// Null: a little below the surface, as with a table.
        /// </summary>
        public float? LowestY { get; set; }
        public float[]? FeltColor { get; set; }
        /// <summary>Image asset stretched over the playing surface (TTS <c>Table_Custom</c> + <c>TableURL</c>).</summary>
        public string? FeltAsset { get; set; }
        public float[]? WoodColor { get; set; }
    }

    /// <summary>
    /// The room around the table: what a game (a mod) sets so the table sits in its own place. The TTS importer maps
    /// <c>Sky</c>, <c>Table</c>/<c>TableURL</c> and <c>Lighting</c> here. Player avatars belong here too once physics
    /// characters exist (M6).
    /// </summary>
    public sealed class EnvironmentDef
    {
        public RoomPreset Room { get; set; }
        /// <summary>The look of the game's panels while this table is loaded.</summary>
        public HudTheme Theme { get; set; }
        /// <summary>Model asset that replaces the built-in room (loaded once models load, M4).</summary>
        public string? RoomAsset { get; set; }
        /// <summary>
        /// A 360° image drawn all around the table instead of a room (TTS's custom sky, <c>SkyURL</c>): an
        /// equirectangular panorama, twice as wide as high.
        /// </summary>
        public string? SkyAsset { get; set; }
        public float[]? WallColor { get; set; }
        public float[]? FloorColor { get; set; }
        public LightingDef Lighting { get; set; } = new LightingDef();
        public LookDef Look { get; set; } = new LookDef();
        public TableLookDef Table { get; set; } = new TableLookDef();
    }
}
