using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace Core.Net
{
    /// <summary>What a chat line is: something a player wrote, a dice roll, a table notice, or a line of the game log.</summary>
    public enum ChatKind
    {
        /// <summary>A player's message.</summary>
        Say,
        /// <summary>The result of <c>/roll</c>, rolled by the host.</summary>
        Roll,
        /// <summary>Joins, leaves, seat changes, turns and rules messages.</summary>
        Notice,
        /// <summary>What someone did at the table (shuffled, dealt, flipped). Kept in the log, not in the chat.</summary>
        Action,
    }

    /// <summary>One line of the chat or game log (G11). <see cref="Seat"/> is the sender's seat, null for a spectator or the table.</summary>
    public sealed class ChatEntry
    {
        public long Id { get; set; }
        public ChatKind Kind { get; set; }
        public string? Seat { get; set; }
        /// <summary>Who it is from, as shown: a seat, "Spectator", or empty for the table itself.</summary>
        public string From { get; set; } = "";
        public string Text { get; set; } = "";
        /// <summary>Only for <see cref="ChatKind.Roll"/>: each die, in order.</summary>
        public List<int> Dice { get; set; } = new List<int>();
        /// <summary>A team message (<c>/t</c>, G23): only that team receives it. <see cref="Team.None"/> for everyone.</summary>
        public Team Team { get; set; }

        private static readonly string[] KindNames = { "say", "roll", "notice", "action" };

        public JObject ToJson()
        {
            var json = new JObject { ["id"] = Id, ["type"] = KindNames[(int)Kind], ["from"] = From, ["text"] = Text };
            if (Seat != null)
                json["seat"] = Seat;
            if (Dice.Count > 0)
                json["dice"] = new JArray(Dice);
            if (Team != Team.None)
                json["team"] = Team.ToString();
            return json;
        }

        /// <summary>The entry in <paramref name="json"/>, or null when it is malformed (a client never trusts the wire blindly).</summary>
        public static ChatEntry? FromJson(JToken? json)
        {
            if (!(json is JObject o) || !(o["text"] is JValue { Type: JTokenType.String } text))
                return null;
            var kind = Array.IndexOf(KindNames, (string?)o["type"]);
            if (kind < 0)
                return null;
            var entry = new ChatEntry
            {
                Id = o["id"]?.Type == JTokenType.Integer ? (long)o["id"]! : 0,
                Kind = (ChatKind)kind,
                Seat = o["seat"]?.Type == JTokenType.String ? (string?)o["seat"] : null,
                From = o["from"]?.Type == JTokenType.String ? (string)o["from"]! : "",
                Text = ChatRoom.Clean((string)text!) ?? "",
                Team = System.Enum.TryParse<Team>((string?)o["team"], out var team) ? team : Team.None,
            };
            if (o["dice"] is JArray dice)
                entry.Dice.AddRange(dice.Where(d => d.Type == JTokenType.Integer).Take(DiceRoll.MaxDice).Select(d => (int)d));
            return entry;
        }
    }

    /// <summary>The lines a machine has seen, oldest first, at most <see cref="Capacity"/> (older ones scroll away).</summary>
    public sealed class ChatLog
    {
        public const int Capacity = 500;

        private readonly List<ChatEntry> _entries = new List<ChatEntry>();
        private readonly HashSet<long> _ids = new HashSet<long>();

        public IReadOnlyList<ChatEntry> Entries => _entries;

        /// <summary>Increases with every entry added; the HUD redraws when it changes.</summary>
        public long Version { get; private set; }

        public event Action<ChatEntry>? Added;

        /// <summary>Adds an entry; one the host already sent (same non-zero id, e.g. history after a join) is skipped.</summary>
        public void Add(ChatEntry entry)
        {
            if (entry.Id != 0 && !_ids.Add(entry.Id))
                return;
            _entries.Add(entry);
            if (_entries.Count > Capacity)
            {
                _ids.Remove(_entries[0].Id);
                _entries.RemoveAt(0);
            }
            Version++;
            Added?.Invoke(entry);
        }

        public void Clear()
        {
            _entries.Clear();
            _ids.Clear();
            Version++;
        }
    }

    /// <summary>
    /// The host's side of the chat (G11, G24): it stamps who sent each line (a client cannot speak for another seat),
    /// cleans the text, limits how fast one player can write, runs chat commands (<c>/roll</c>, <c>/help</c>) and keeps
    /// recent history for players who join later. Wire: a client sends <c>{ kind: "say", text }</c>; everyone receives
    /// <c>{ kind: "chat", entries: [...] }</c>.
    /// </summary>
    public sealed class ChatRoom
    {
        public const int MaxLength = 500;
        /// <summary>Lines sent to a player who joins.</summary>
        public const int HistorySize = 100;
        /// <summary>At most this many lines per player within <see cref="BurstSeconds"/>.</summary>
        public const int BurstLines = 6;
        public const double BurstSeconds = 4.0;

        public const string HelpText = "Commands: /roll 2d6+1 rolls dice for everyone to see (also /r), /t message talks to your team, /help shows this.";

        private readonly Func<int, int> _random;
        private readonly Dictionary<string, Queue<double>> _sent = new Dictionary<string, Queue<double>>();
        private long _nextId = 1;

        /// <summary>Every line the host has seen, the history players get on joining.</summary>
        public ChatLog Log { get; } = new ChatLog();

        /// <param name="random">Returns a number in [0, max): the dice for <c>/roll</c>.</param>
        public ChatRoom(Func<int, int> random)
        {
            _random = random;
        }

        /// <summary>A line typed by <paramref name="participant"/> at <paramref name="seat"/>, shown under the seat's name.</summary>
        public ChatEntry? Say(string participant, string? seat, string text, double now, out string? reply) =>
            Say(new Speaker { Participant = participant, Seat = seat, Name = seat ?? "Spectator" }, text, now, out reply);

        /// <summary>
        /// A line <paramref name="who"/> typed. Returns the entry to send (to everyone, or to <see cref="ChatEntry.Team"/>
        /// only), or null with a <paramref name="reply"/> only the sender sees (help, a bad command, writing too fast,
        /// muted); both null for an empty line.
        /// </summary>
        public ChatEntry? Say(Speaker who, string text, double now, out string? reply)
        {
            reply = null;
            var participant = who.Participant;
            var seat = who.Seat;
            var clean = Clean(text);
            if (string.IsNullOrEmpty(clean))
                return null;
            if (who.Muted)
            {
                reply = "The host has muted you.";
                return null;
            }
            if (!_sent.TryGetValue(participant, out var times))
                _sent[participant] = times = new Queue<double>();
            while (times.Count > 0 && now - times.Peek() > BurstSeconds)
                times.Dequeue();
            if (times.Count >= BurstLines)
            {
                reply = "You are sending messages too fast. Wait a moment.";
                return null;
            }
            times.Enqueue(now);

            var from = string.IsNullOrEmpty(who.Name) ? seat ?? "Spectator" : who.Name;
            if (!clean!.StartsWith("/"))
                return Add(new ChatEntry { Kind = ChatKind.Say, Seat = seat, From = from, Text = clean });

            var space = clean.IndexOf(' ');
            var command = (space < 0 ? clean : clean.Substring(0, space)).ToLowerInvariant();
            var argument = space < 0 ? "" : clean.Substring(space + 1).Trim();
            switch (command)
            {
                case "/roll":
                case "/r":
                    if (!DiceRoll.TryParse(argument.Length == 0 ? "1d6" : argument, out var roll, out var error))
                    {
                        reply = error;
                        return null;
                    }
                    var dice = roll!.Roll(_random);
                    return Add(new ChatEntry { Kind = ChatKind.Roll, Seat = seat, From = from, Text = roll.Describe(dice), Dice = dice });
                case "/t":
                case "/team":
                    if (who.Team == Team.None)
                    {
                        reply = "You are not on a team. The host puts players on teams.";
                        return null;
                    }
                    if (argument.Length == 0)
                        return null;
                    return Add(new ChatEntry { Kind = ChatKind.Say, Seat = seat, From = from, Text = argument, Team = who.Team });
                case "/help":
                case "/?":
                    reply = HelpText;
                    return null;
                default:
                    reply = $"Unknown command {command}. {HelpText}";
                    return null;
            }
        }

        /// <summary>A line from the table itself (someone joined, sat down, the turn passed).</summary>
        public ChatEntry Notice(string text) => Add(new ChatEntry { Kind = ChatKind.Notice, Text = Clean(text) ?? "" });

        /// <summary>A player left: forget their rate limit.</summary>
        public void Forget(string participant) => _sent.Remove(participant);

        private ChatEntry Add(ChatEntry entry)
        {
            entry.Id = _nextId++;
            Log.Add(entry);
            return entry;
        }

        /// <summary>The broadcast carrying <paramref name="entries"/>.</summary>
        public static JObject Message(IEnumerable<ChatEntry> entries) =>
            new JObject { ["kind"] = "chat", ["entries"] = new JArray(entries.Select(e => e.ToJson())) };

        /// <summary>What a player who just joined receives: the last <see cref="HistorySize"/> lines, team lines of their team only.</summary>
        public JObject History(Team team = Team.None)
        {
            var visible = Log.Entries.Where(e => e.Team == Team.None || e.Team == team).ToList();
            return Message(visible.Skip(Math.Max(0, visible.Count - HistorySize)));
        }

        /// <summary>A client's request to say something.</summary>
        public static JObject SayMessage(string text) => new JObject { ["kind"] = "say", ["text"] = text };

        /// <summary>The entries of a <see cref="Message"/>; malformed ones are skipped.</summary>
        public static List<ChatEntry> Read(JObject message) =>
            (message["entries"] as JArray)?.Select(ChatEntry.FromJson).Where(e => e != null).Select(e => e!).ToList() ?? new List<ChatEntry>();

        /// <summary>
        /// The text as it may appear in chat: one line, no control characters, trimmed, at most <see cref="MaxLength"/>
        /// characters (never splitting a surrogate pair). Null when nothing is left.
        /// </summary>
        public static string? Clean(string? text)
        {
            if (text == null)
                return null;
            var builder = new StringBuilder(Math.Min(text.Length, MaxLength));
            foreach (var c in text)
            {
                if (builder.Length >= MaxLength)
                    break;
                builder.Append(char.IsControl(c) ? ' ' : c);
            }
            if (builder.Length > 0 && char.IsHighSurrogate(builder[builder.Length - 1]))
                builder.Length--;
            var clean = builder.ToString().Trim();
            return clean.Length == 0 ? null : clean;
        }
    }

    /// <summary>Who is talking: the host fills this in from its seating and roster, never from what a client claims.</summary>
    public sealed class Speaker
    {
        public string Participant { get; set; } = "";
        public string? Seat { get; set; }
        public string Name { get; set; } = "";
        public Team Team { get; set; }
        public bool Muted { get; set; }
    }

    /// <summary>A dice expression for <c>/roll</c>: <c>NdM</c> with an optional <c>+K</c> or <c>-K</c> (<c>d20</c>, <c>3d6+2</c>).</summary>
    public sealed class DiceRoll
    {
        public const int MaxDice = 100;
        public const int MaxFaces = 1000;
        public const int MaxModifier = 1000;

        private static readonly Regex Pattern = new Regex(@"^\s*(\d*)\s*[dD]\s*(\d+)\s*(?:([+-])\s*(\d+))?\s*$");

        public int Count { get; }
        public int Faces { get; }
        public int Modifier { get; }

        private DiceRoll(int count, int faces, int modifier)
        {
            Count = count;
            Faces = faces;
            Modifier = modifier;
        }

        public static bool TryParse(string text, out DiceRoll? roll, out string? error)
        {
            roll = null;
            error = null;
            var match = Pattern.Match(text);
            if (!match.Success)
            {
                error = $"'{text}' is not a roll. Try /roll d20 or /roll 3d6+2.";
                return false;
            }
            int Number(Group g, int fallback) => g.Success && g.Value.Length > 0 ? (int.TryParse(g.Value, out var n) ? n : int.MaxValue) : fallback;
            var count = Number(match.Groups[1], 1);
            var faces = Number(match.Groups[2], 6);
            var modifier = Number(match.Groups[4], 0);
            if (count < 1 || count > MaxDice)
                error = $"Roll between 1 and {MaxDice} dice.";
            else if (faces < 2 || faces > MaxFaces)
                error = $"Dice have between 2 and {MaxFaces} faces.";
            else if (modifier > MaxModifier)
                error = $"Add at most {MaxModifier}.";
            if (error != null)
                return false;
            roll = new DiceRoll(count, faces, match.Groups[3].Value == "-" ? -modifier : modifier);
            return true;
        }

        public List<int> Roll(Func<int, int> random) =>
            Enumerable.Range(0, Count).Select(_ => random(Faces) + 1).ToList();

        /// <summary>"rolled 3d6+2: 4 + 1 + 6 + 2 = 13".</summary>
        public string Describe(IReadOnlyList<int> dice)
        {
            var expression = $"{Count}d{Faces}" + (Modifier > 0 ? $"+{Modifier}" : Modifier < 0 ? $"−{-Modifier}" : "");
            var total = dice.Sum() + Modifier;
            if (dice.Count == 1 && Modifier == 0)
                return $"rolled {expression}: {total}";
            var terms = string.Join(" + ", dice) + (Modifier > 0 ? $" + {Modifier}" : Modifier < 0 ? $" − {-Modifier}" : "");
            return $"rolled {expression}: {terms} = {total}";
        }
    }
}
