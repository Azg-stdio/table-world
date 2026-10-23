using System.Collections.Generic;

namespace Core.Rules
{
    public enum AssetType { Image, Model, AssetBundle, Audio }

    /// <summary>
    /// A file the table needs. Paths are relative to the game package; <c>builtin:&lt;name&gt;</c> names an asset the
    /// client generates itself (the MVP's procedural deck, dice, table).
    /// </summary>
    public sealed class Asset
    {
        public string Id { get; set; } = "";
        public AssetType Type { get; set; }
        public string Path { get; set; } = "";
        /// <summary>Filled by tooling, not by the agent: <c>sha256:&lt;hex&gt;</c>.</summary>
        public string? Hash { get; set; }
        /// <summary>Sprite-sheet grid for images: cells are row-major, index 0 top-left (TTS <c>NumWidth</c> × <c>NumHeight</c>).</summary>
        public int Columns { get; set; } = 1;
        public int Rows { get; set; } = 1;
    }

    public sealed class Seat
    {
        public string Id { get; set; } = "";
        public string Color { get; set; } = "";
        public string? HandZone { get; set; }
    }

    public sealed class Players
    {
        public List<Seat> Seats { get; set; } = new List<Seat>();
    }

    /// <summary>
    /// The physical table: assets, objects, zones and seats. Its JSON is the subset of the game document
    /// (docs/rules-schema.md) that describes the table, and it is also the save-file format.
    /// </summary>
    public sealed class TableState
    {
        public const string CurrentSchemaVersion = "0.1";

        public string SchemaVersion { get; set; } = CurrentSchemaVersion;
        public List<Asset> Assets { get; set; } = new List<Asset>();
        /// <summary>All objects, including those inside bags (their <c>container</c> is set). Order inside a bag is list order.</summary>
        public List<TableObject> Objects { get; set; } = new List<TableObject>();
        public List<Zone> Zones { get; set; } = new List<Zone>();
        public Players Players { get; set; } = new Players();
    }
}
