namespace Core.Rules
{
    /// <summary>The built-in rooms. A game can also bring its own room model (<see cref="EnvironmentDef.RoomAsset"/>).</summary>
    public enum RoomPreset
    {
        /// <summary>A small dim room: warm lamp over the table, dark walls, wooden floor. The default.</summary>
        GameNight,
        /// <summary>No room: the table over a plain background colour (<see cref="EnvironmentDef.WallColor"/>).</summary>
        None,
    }

    /// <summary>Light in the room. Every value is optional; unset values keep the room preset's.</summary>
    public sealed class LightingDef
    {
        /// <summary>Light that reaches everything, from the room (0 = none). Colour 0–1.</summary>
        public float? AmbientIntensity { get; set; }
        public float[]? AmbientColor { get; set; }
        /// <summary>The lamp over the table (0 = off).</summary>
        public float? LampIntensity { get; set; }
        public float[]? LampColor { get; set; }
    }

    /// <summary>How the table itself looks.</summary>
    public sealed class TableLookDef
    {
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
        /// <summary>Model asset that replaces the built-in room (loaded once models load, M4).</summary>
        public string? RoomAsset { get; set; }
        public float[]? WallColor { get; set; }
        public float[]? FloorColor { get; set; }
        public LightingDef Lighting { get; set; } = new LightingDef();
        public TableLookDef Table { get; set; } = new TableLookDef();
    }
}
