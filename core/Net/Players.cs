using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Core.Net
{
    /// <summary>TTS teams: players on one team can chat among themselves (<c>/t</c>).</summary>
    public enum Team { None, Clubs, Diamonds, Hearts, Spades, Jokers }

    /// <summary>
    /// Who a participant is at a hosted table (G23): their name, and what the host allows them. A promoted player may
    /// change host-only table settings; a Game Master (TTS's Black seat) sees every hand and hidden zone; a muted player
    /// cannot chat.
    /// </summary>
    public sealed class PlayerInfo
    {
        public const int MaxNameLength = 24;
        public const int MaxLookLength = 24;

        public string Name { get; set; } = "";
        public bool Promoted { get; set; }
        public bool GameMaster { get; set; }
        public Team Team { get; set; }
        public bool Muted { get; set; }
        /// <summary>The chalker personality the player picked (T7.3), a name like "Grumpy"; empty means the default.</summary>
        public string Look { get; set; } = "";

        public JObject ToJson()
        {
            var json = new JObject { ["name"] = Name };
            if (Promoted) json["promoted"] = true;
            if (GameMaster) json["gm"] = true;
            if (Team != Team.None) json["team"] = Team.ToString();
            if (Muted) json["muted"] = true;
            if (Look.Length > 0) json["look"] = Look;
            return json;
        }

        public static PlayerInfo FromJson(JToken? json)
        {
            var o = json as JObject;
            return new PlayerInfo
            {
                Name = o?["name"]?.Type == JTokenType.String ? CleanName((string?)o["name"]) ?? "" : "",
                Promoted = o?["promoted"]?.Type == JTokenType.Boolean && (bool)o["promoted"]!,
                GameMaster = o?["gm"]?.Type == JTokenType.Boolean && (bool)o["gm"]!,
                Team = System.Enum.TryParse<Team>((string?)o?["team"], out var team) ? team : Team.None,
                Muted = o?["muted"]?.Type == JTokenType.Boolean && (bool)o["muted"]!,
                Look = CleanLook(o?["look"]?.Type == JTokenType.String ? (string?)o["look"] : null),
            };
        }

        /// <summary>A personality or emote name: letters only, at most <see cref="MaxLookLength"/>; anything else is empty.</summary>
        public static string CleanLook(string? look) =>
            look != null && look.Length <= MaxLookLength && look.All(c => c < 128 && char.IsLetter(c)) ? look : "";

        /// <summary>A name as players may see it: one line, trimmed, at most <see cref="MaxNameLength"/> characters; null when empty.</summary>
        public static string? CleanName(string? name)
        {
            var clean = ChatRoom.Clean(name);
            if (clean == null)
                return null;
            if (clean.Length > MaxNameLength)
                clean = clean.Substring(0, char.IsHighSurrogate(clean[MaxNameLength - 1]) ? MaxNameLength - 1 : MaxNameLength).TrimEnd();
            return clean.Length == 0 ? null : clean;
        }
    }

    /// <summary>The players at a hosted table, kept by the host next to <see cref="Seating"/> and sent with it.</summary>
    public sealed class Roster
    {
        private readonly Dictionary<string, PlayerInfo> _players = new Dictionary<string, PlayerInfo>();
        private int _joined;

        public IReadOnlyDictionary<string, PlayerInfo> Players => _players;

        /// <summary>The participant's info, created with a placeholder name ("Player 2") the first time.</summary>
        public PlayerInfo Of(string participant)
        {
            if (!_players.TryGetValue(participant, out var info))
                _players[participant] = info = new PlayerInfo { Name = $"Player {++_joined}" };
            return info;
        }

        /// <summary>Sets the name a participant asked for; false (keeping the old one) when nothing is left of it.</summary>
        public bool Rename(string participant, string? name)
        {
            var clean = PlayerInfo.CleanName(name);
            if (clean == null)
                return false;
            Of(participant).Name = clean;
            return true;
        }

        /// <summary>Sets the chalker personality a participant picked (anything not a plain name clears it).</summary>
        public void SetLook(string participant, string? look) => Of(participant).Look = PlayerInfo.CleanLook(look);

        public void Leave(string participant) => _players.Remove(participant);

        /// <summary>Only the host and promoted players may change host-only settings.</summary>
        public bool MayHost(string participant) => participant == SessionHost.HostId || (_players.TryGetValue(participant, out var p) && p.Promoted);

        public bool SeesEverything(string participant) => _players.TryGetValue(participant, out var p) && p.GameMaster;

        /// <summary>Participants on <paramref name="team"/> (none for <see cref="Team.None"/>).</summary>
        public IEnumerable<string> OnTeam(Team team) =>
            team == Team.None ? Enumerable.Empty<string>() : _players.Where(p => p.Value.Team == team).Select(p => p.Key);

        public JObject ToJson()
        {
            var json = new JObject();
            foreach (var pair in _players.OrderBy(p => p.Key, System.StringComparer.Ordinal))
                json[pair.Key] = pair.Value.ToJson();
            return json;
        }

        /// <summary>Reads <see cref="ToJson"/> on a client.</summary>
        public static Dictionary<string, PlayerInfo> Read(JToken? json) =>
            (json as JObject)?.Properties().ToDictionary(p => p.Name, p => PlayerInfo.FromJson(p.Value)) ?? new Dictionary<string, PlayerInfo>();
    }
}
