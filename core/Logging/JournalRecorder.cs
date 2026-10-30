using System.Linq;
using Core.Engine;
using Core.Rules;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Core.Logging
{
    /// <summary>
    /// Writes what each command did into a <see cref="Journal"/>: the command (<c>command</c>, or <c>refused</c> with the
    /// reason), the rules' lines (<c>rules</c>; <c>private</c> for a line only some players read), moves the rules made
    /// for a player (<c>auto</c>), cards shown (<c>shown</c>), and where the game stands when that changed (<c>turn</c>).
    /// It writes what the machine it runs on knows: a client's journal never holds another player's secrets, because they
    /// never reach it. <paramref name="viewer"/> is the local seat: private lines and shown cards for others are left out
    /// (a host's machine holds them all).
    /// </summary>
    public sealed class JournalRecorder
    {
        private static readonly JsonSerializer Serializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Ignore,
        });

        private readonly Journal _journal;
        private string? _turnKey;

        public JournalRecorder(Journal journal)
        {
            _journal = journal;
        }

        public Journal Journal => _journal;

        /// <summary>A table was loaded or started afresh: what it is, as the first lines of its story.</summary>
        public void Table(TableState state, string? source, string? role = null, string? seat = null)
        {
            _turnKey = null;
            _journal.Write("table", new JObject
            {
                ["game"] = state.Game?.Name,
                ["id"] = state.Game?.Id,
                ["source"] = source,
                ["rules"] = state.HasRules,
                ["role"] = role,
                ["version"] = Core.Version.Get(),
            }, seat);
            Turn(state);
        }

        /// <summary>The lines for <paramref name="command"/>, already executed with <paramref name="result"/>.</summary>
        public void Record(TableState state, Command command, CommandResult result, string? viewer)
        {
            var data = new JObject { ["command"] = command.GetType().Name.Replace("Command", "") };
            var body = JObject.FromObject(command, Serializer);
            body.Remove("Actor");
            Round(body);
            if (body.Count > 0)
                data["args"] = body;
            if (command is PerformActionCommand perform && state.Actions.FirstOrDefault(a => a.Id == perform.ActionId) is { } action)
                data["action"] = Names.Action(state, action);
            if (result.Ok)
                _journal.Write("command", data, command.Actor);
            else
            {
                data["code"] = result.Error!.Code;
                data["reason"] = result.Error.Message;
                if (result.Error.Path.Length > 0)
                    data["path"] = result.Error.Path;
                _journal.Write("refused", data, command.Actor);
            }
            foreach (var e in result.Events)
            {
                switch (e)
                {
                    case GameMessage message:
                        _journal.Text("rules", message.Text);
                        break;
                    case PrivateMessage message when viewer == null || message.Seat == viewer:
                        _journal.Text("private", message.Text, message.Seat);
                        break;
                    case AutoMoved auto:
                        _journal.Write("auto", new JObject
                        {
                            ["action"] = state.Actions.FirstOrDefault(a => a.Id == auto.Action) is { } done ? Names.Action(state, done) : auto.Action,
                            ["args"] = JObject.FromObject(auto.Params),
                        }, auto.Seat);
                        break;
                    case CardShown shown when shown.For(viewer) || viewer == null:
                        _journal.Write("shown", new JObject
                        {
                            ["card"] = Names.Of(state, shown.Card),
                            ["to"] = shown.Seats == null ? null : new JArray(shown.Seats),
                        }, shown.Seat);
                        break;
                }
            }
            if (result.Ok)
                Turn(state);
        }

        /// <summary>Positions and turns to a tenth of a millimetre or degree: physics leaves noise like -3.4E-07 behind.</summary>
        private static void Round(JObject token)
        {
            foreach (var value in token.DescendantsAndSelf().OfType<JValue>().Where(v => v.Type == JTokenType.Float).ToList())
                value.Value = System.Math.Round(System.Convert.ToDouble(value.Value, System.Globalization.CultureInfo.InvariantCulture), 4);
        }

        /// <summary>A <c>turn</c> line when the turn, the phase, who is out or the end changed since the last one.</summary>
        private void Turn(TableState state)
        {
            var s = state.Session;
            var key = s == null ? "none" : $"{s.Started}|{s.Turn}|{s.CurrentSeat}|{s.Phase}|{string.Join(",", s.Eliminated)}|{s.Over}|{s.Winner}|{s.WinnerTeam}";
            if (key == _turnKey)
                return;
            _turnKey = key;
            if (s == null || !s.Started)
                return;
            _journal.Write("turn", new JObject
            {
                ["turn"] = s.Turn,
                ["phase"] = s.Phase,
                ["current"] = s.CurrentSeat,
                ["out"] = s.Eliminated.Count > 0 ? new JArray(s.Eliminated) : null,
                ["over"] = s.Over ? true : (bool?)null,
                ["winner"] = s.Winner ?? s.WinnerTeam,
            });
        }
    }
}
