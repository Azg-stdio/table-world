using System.Collections.Generic;
using Newtonsoft.Json.Linq;

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

        public const string BuiltinPrefix = "builtin:";

        [Newtonsoft.Json.JsonIgnore]
        public bool IsBuiltin => Path.StartsWith(BuiltinPrefix, System.StringComparison.Ordinal);

        /// <summary>
        /// True for <c>builtin:</c> names and relative paths that stay inside the package: the host reads these files
        /// and sends them to every player, so a path must never reach outside the package folder.
        /// </summary>
        public static bool IsSafePath(string path)
        {
            if (path.StartsWith(BuiltinPrefix, System.StringComparison.Ordinal))
                return true;
            if (path.Length == 0 || path.IndexOf(':') >= 0 || path.IndexOf('\0') >= 0 || path[0] == '/' || path[0] == '\\')
                return false;
            foreach (var part in path.Split('/', '\\'))
                if (part == ".." || part.Length == 0)
                    return false;
            return true;
        }
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

        // ---- rules (docs/rules-schema.md); all optional: a table without them is free play ----

        public GameInfo? Game { get; set; }
        public List<ResourceDef> Resources { get; set; } = new List<ResourceDef>();
        /// <summary>Declared variables and their initial values.</summary>
        public Dictionary<string, JToken> Variables { get; set; } = new Dictionary<string, JToken>();
        public TurnDef? Turn { get; set; }
        public List<PhaseDef> Phases { get; set; } = new List<PhaseDef>();
        public List<ActionDef> Actions { get; set; } = new List<ActionDef>();
        /// <summary>Event name (<see cref="GameEvents"/>) to effects.</summary>
        public Dictionary<string, List<Effect>> Events { get; set; } = new Dictionary<string, List<Effect>>();
        public List<WinCondition> WinConditions { get; set; } = new List<WinCondition>();
        /// <summary>Declarative UI panels (T4.6).</summary>
        public UiDef? Ui { get; set; }
        /// <summary>Lua sources by name (M5); carried as-is.</summary>
        public Dictionary<string, string> Scripts { get; set; } = new Dictionary<string, string>();

        /// <summary>Progress of a game in play. Written by the engine.</summary>
        public Session? Session { get; set; }

        [Newtonsoft.Json.JsonIgnore]
        public bool HasRules => Turn != null && Phases.Count > 0;
    }
}
