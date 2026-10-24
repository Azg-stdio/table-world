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
        /// <summary>
        /// Models: metres per unit of the file. Our models are in metres (1); TTS meshes are in TTS units (0.03), so the
        /// importer sets that.
        /// </summary>
        [System.ComponentModel.DefaultValue(1f)]
        public float MeshScale { get; set; } = 1f;

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
    /// Free-play turns (TTS "Turns", G22): whose turn it is, without a rules file. A game with rules runs its own turns
    /// (<see cref="TableState.Turn"/>) and leaves this off.
    /// </summary>
    public sealed class FreeTurns
    {
        public bool Enabled { get; set; }
        /// <summary>Seats in turn order; empty means the order of <see cref="Players.Seats"/>.</summary>
        public List<string> Seats { get; set; } = new List<string>();
        /// <summary>The seat whose turn it is.</summary>
        public string? Current { get; set; }
        /// <summary>Play goes backwards through the order.</summary>
        public bool Reverse { get; set; }
        /// <summary>Seats with a hand zone holding nothing are passed over (a seat without a hand zone never is).</summary>
        public bool SkipEmptyHands { get; set; }
        /// <summary>Counts turns ended since turns were switched on.</summary>
        public int Count { get; set; }
    }

    /// <summary>How the table simulates objects (TTS physics modes, chosen by the host).</summary>
    public enum PhysicsMode
    {
        /// <summary>Everything falls, slides, tumbles and can be knocked over.</summary>
        Full,
        /// <summary>Objects fall and slide but cannot be knocked over; dice still tumble.</summary>
        SemiLock,
        /// <summary>Nothing moves on its own: a dropped object is set down where it was released.</summary>
        Locked,
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
        /// <summary>The room around the table; null means the default room.</summary>
        public EnvironmentDef? Environment { get; set; }
        /// <summary>Free-play turns (G22); null means off.</summary>
        public FreeTurns? FreeTurns { get; set; }
        /// <summary>How objects are simulated (G19). Saved with the table; only the host changes it.</summary>
        public PhysicsMode Physics { get; set; }
        /// <summary>The table grid (G28): lines and snapping; null means none. Only the host changes it.</summary>
        public GridDef? Grid { get; set; }
        /// <summary>The notebook's pages (G25).</summary>
        public List<NoteTab> Notebook { get; set; } = new List<NoteTab>();
        public bool ShouldSerializeNotebook() => Notebook.Count > 0;

        /// <summary>The global script's screen UI (M5, TTS <c>UI.*</c>; ADR 0004).</summary>
        public List<UiNode> ScriptUi { get; set; } = new List<UiNode>();
        public bool ShouldSerializeScriptUi() => ScriptUi.Count > 0;
        /// <summary>Lines and arrows players drew on the table (G28).</summary>
        public List<DrawingDef> Drawings { get; set; } = new List<DrawingDef>();
        public bool ShouldSerializeDrawings() => Drawings.Count > 0;

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
        /// <summary>What the global script saved with <c>onSave</c> (TTS <c>LuaScriptState</c>), given back to its <c>onLoad</c>.</summary>
        public string? GlobalScriptState { get; set; }
        /// <summary>Lua sources by name (M5); carried as-is.</summary>
        public Dictionary<string, string> Scripts { get; set; } = new Dictionary<string, string>();

        /// <summary>Progress of a game in play. Written by the engine.</summary>
        public Session? Session { get; set; }

        [Newtonsoft.Json.JsonIgnore]
        public bool HasRules => Turn != null && Phases.Count > 0;
    }
}
