using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Core.Rules
{
    public enum AssetType { Image, Model, AssetBundle, Audio, Pdf }

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

        /// <summary>
        /// Where the file comes from on the web (TTS games name their files by URL). Each machine fetches it itself when
        /// the package does not hold <see cref="Path"/>, as TTS does: http or https only.
        /// </summary>
        public string? Url { get; set; }

        /// <summary>
        /// The file to use in another language (ADR 0011): images with words in them, e.g. <c>{ "en": "cards_en.png" }</c>.
        /// <see cref="Path"/> is the game's own language and the fallback.
        /// </summary>
        public Dictionary<string, string> Localized { get; set; } = new Dictionary<string, string>();

        public static bool IsWebAddress(string? url) =>
            url != null && System.Uri.TryCreate(url, System.UriKind.Absolute, out var uri) && (uri.Scheme == System.Uri.UriSchemeHttp || uri.Scheme == System.Uri.UriSchemeHttps);

        /// <summary>
        /// Where to fetch <paramref name="url"/> from today. Steam retired the host most TTS saves name
        /// (<c>cloud-3.steamusercontent.com</c>, 86 % of their addresses): it answers 403, and the same path is served by
        /// <c>steamusercontent-a.akamaihd.net</c>, which TTS rewrites to as well. The original address stays the asset's
        /// name (the TTS cache and ours are keyed by it); only the download goes elsewhere.
        /// </summary>
        public static string FetchAddress(string url)
        {
            var match = System.Text.RegularExpressions.Regex.Match(url ?? "",
                @"^https?://(cloud-3\.steamusercontent\.com|steamusercontent-a\.akamaihd\.net)/(.*)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return match.Success ? "https://steamusercontent-a.akamaihd.net/" + match.Groups[2].Value : url ?? "";
        }

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
        /// <summary>What players call the seat ("North", a character); null shows its colour ("Red"), as TTS names players.</summary>
        public string? Name { get; set; }
    }

    public sealed class Players
    {
        public List<Seat> Seats { get; set; } = new List<Seat>();
    }

    /// <summary>
    /// Free-play turns (TTS "Turns", G22): whose turn it is, without a rules file. A game with rules runs its own turns
    /// (<see cref="TableState.Turn"/>) and leaves this off.
    /// </summary>
    /// <summary>
    /// A blind round: until the reveal, whatever a seat moves, flips, turns or takes is invisible to the other seats, so
    /// everyone commits a move without seeing the others' (place your worker, bid with chips, then show). The reveal
    /// gives every piece back the visibility it had before the round.
    /// </summary>
    public sealed class BlindRound
    {
        /// <summary>Object id → the seat that last moved it in this round.</summary>
        public Dictionary<string, string> MovedBy { get; set; } = new Dictionary<string, string>();
        /// <summary>Object id → the colours it was invisible to before the round.</summary>
        public Dictionary<string, List<string>> Before { get; set; } = new Dictionary<string, List<string>>();
    }

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
        /// <summary>A blind round in progress (<see cref="BlindRound"/>); null when none.</summary>
        public BlindRound? Blind { get; set; }
        /// <summary>The table grid (G28): lines and snapping; null means none. Only the host changes it.</summary>
        public GridDef? Grid { get; set; }
        /// <summary>The table's music (G27, TTS Music Player); null means none. Only the host changes it.</summary>
        public MusicState? Music { get; set; }
        /// <summary>The karaoke room's big screen (ADR 0013, T12.11); null when nothing is on it.</summary>
        public ScreenState? Screen { get; set; }
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
        /// <summary>
        /// Variables only the host's rules read (ADR 0012): the werewolves' victim before dawn. Their values never reach the
        /// players' machines, and a command that reads or sets one runs on the host.
        /// </summary>
        public List<string> SecretVariables { get; set; } = new List<string>();
        public bool ShouldSerializeSecretVariables() => SecretVariables.Count > 0;
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

        /// <summary>Hidden roles dealt at the start (ADR 0012).</summary>
        public RolesDef? Roles { get; set; }

        /// <summary>
        /// The game in other languages (ADR 0011): language → path key → text, e.g.
        /// <c>{ "en": { "actions.elegir.name": "Choose a space", "strings.noche": "It is night." } }</c>. Path keys name the
        /// text they replace (<see cref="Core.Text.Translations.Texts"/>); <c>strings.&lt;key&gt;</c> is the game's own free
        /// text, read where a text says <c>@key</c>, and the game's own language holds those too.
        /// </summary>
        public Dictionary<string, Dictionary<string, string>> Translations { get; set; } = new Dictionary<string, Dictionary<string, string>>();

        /// <summary>Progress of a game in play. Written by the engine.</summary>
        public Session? Session { get; set; }

        [Newtonsoft.Json.JsonIgnore]
        public bool HasRules => Turn != null && Phases.Count > 0;

        /// <summary>
        /// Cards are secret on the wire too (ADR 0012, T11.16): a table with rules or roles. A free-play table (a TTS
        /// import, a sandbox) keeps TTS's honest play, where every machine holds everything and the view hides it.
        /// </summary>
        [Newtonsoft.Json.JsonIgnore]
        public bool SecretCards => HasRules || Roles != null;

        /// <summary>A seat's hidden role as this state holds it (on a peer, only its own); null without one.</summary>
        public string? RoleOf(string seat) => Session != null && Session.Roles.TryGetValue(seat, out var role) ? role : null;

        /// <summary>A seat's team: the one <c>setTeam</c> moved it to, else its role's; null when not known here.</summary>
        public string? TeamOf(string seat)
        {
            if (Session == null)
                return null;
            if (Session.Teams.TryGetValue(seat, out var moved))
                return moved;
            return RoleOf(seat) is { } role ? Roles?.List.FirstOrDefault(r => r.Id == role)?.Team : null;
        }

        /// <summary>
        /// Whether <paramref name="seat"/> is among <paramref name="who"/>: seat ids, <c>role:&lt;id&gt;</c>, <c>team:&lt;id&gt;</c>
        /// (hidden zones' <c>visibleTo</c>, notebook pages' <c>for</c>). Roles are read as this state knows them.
        /// </summary>
        public bool Names(IEnumerable<string> who, string? seat)
        {
            if (seat == null)
                return false;
            foreach (var entry in who)
            {
                if (entry == seat)
                    return true;
                if (entry.StartsWith("role:") && RoleOf(seat) == entry.Substring(5))
                    return true;
                if (entry.StartsWith("team:") && TeamOf(seat) == entry.Substring(5))
                    return true;
            }
            return false;
        }
    }
}
