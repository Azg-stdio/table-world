using System;
using System.Linq;
using Core.Net;
using UnityEngine;
using UnityEngine.UIElements;

namespace TableWorld.View.Hud
{
    /// <summary>
    /// Everyone at a hosted table (G23): name, seat and what the host gave them. The host gets buttons on each row to
    /// promote, make Game Master, change team, mute and remove. Opened with the Players button. Styling lives in Hud.uss
    /// (player-*).
    /// </summary>
    public sealed class PlayersPanel
    {
        private const string Hidden = "hidden";
        private static readonly Team[] Teams = (Team[])Enum.GetValues(typeof(Team));

        private readonly VisualElement _panel;
        private readonly ScrollView _list;
        private readonly Label _hint;
        private readonly Button _button;
        private readonly TableView _table;
        private readonly TableWorld.View.Net.NetworkTableSession _session;
        private string _shownKey;

        public bool Visible => !_panel.ClassListContains(Hidden);

        public PlayersPanel(VisualElement root, TableView table, TableWorld.View.Net.NetworkTableSession session)
        {
            _table = table;
            _session = session;
            _panel = root.Q("playersPanel");
            _list = root.Q<ScrollView>("playersList");
            _hint = root.Q<Label>("playersHint");
            _button = root.Q<Button>("playersButton");
            _button.clicked += Toggle;
            root.Q<Button>("playersClose").clicked += Hide;
        }

        public void Toggle()
        {
            if (Visible)
                Hide();
            else
            {
                _shownKey = null;
                _panel.RemoveFromClassList(Hidden);
            }
        }

        public void Hide() => _panel.AddToClassList(Hidden);

        /// <summary>Shows the button only online; redraws the list when anyone changes.</summary>
        public void Update()
        {
            var online = _session != null && (_session.IsHost || _session.IsClient);
            _button.EnableInClassList(Hidden, !online);
            if (!online)
            {
                Hide();
                return;
            }
            if (!Visible)
                return;
            var players = _session.Players;
            var occupants = _session.Occupants;
            var key = string.Join("|", players.Select(p => $"{p.Key}:{p.Value.ToJson()}")) + "#" + string.Join(",", occupants.Select(o => o.Key + "=" + o.Value));
            if (key == _shownKey)
                return;
            _shownKey = key;
            _list.Clear();
            var hosting = _session.IsHost;
            foreach (var (participant, info) in players.OrderBy(p => p.Key == SessionHost.HostId ? 0 : 1).ThenBy(p => p.Value.Name))
                _list.Add(Row(participant, info, occupants.FirstOrDefault(o => o.Value == participant).Key, hosting));
            _hint.text = hosting
                ? "Promoted players may change table settings. The Game Master sees every hand and hidden area. Teams chat with /t."
                : "Only the host changes these. Teams chat with /t.";
        }

        private VisualElement Row(string participant, PlayerInfo info, string seat, bool hosting)
        {
            var row = new VisualElement();
            row.AddToClassList("player-row");
            var swatch = new VisualElement();
            swatch.AddToClassList("seat-swatch");
            var seatModel = seat == null ? null : _table.Engine?.State.Players.Seats.FirstOrDefault(s => s.Id == seat);
            if (seatModel != null)
                swatch.AddToClassList("seat-swatch--" + seatModel.Color.ToLowerInvariant());
            else
                swatch.AddToClassList("seat-swatch--none");
            row.Add(swatch);

            var you = participant == _session.LocalParticipant;
            var name = new Label(info.Name + (you ? " (you)" : "")) { enableRichText = false };
            name.AddToClassList("player-name");
            row.Add(name);
            var badges = new[]
            {
                participant == SessionHost.HostId ? "host" : null,
                seat ?? "watching",
                info.Promoted ? "promoted" : null,
                info.GameMaster ? "Game Master" : null,
                info.Team != Team.None ? info.Team.ToString() : null,
                info.Muted ? "muted" : null,
            }.Where(b => b != null);
            var detail = new Label(string.Join(" · ", badges));
            detail.AddToClassList("player-badges");
            row.Add(detail);

            if (hosting)
            {
                var actions = new VisualElement();
                actions.AddToClassList("player-actions");
                var isHost = participant == SessionHost.HostId;
                if (!isHost)
                    Action(actions, info.Promoted ? "Demote" : "Promote", () => _session.SetPromoted(participant, !info.Promoted));
                Action(actions, info.GameMaster ? "Not GM" : "Make GM", () => _session.SetGameMaster(participant, !info.GameMaster));
                var next = Teams[(Array.IndexOf(Teams, info.Team) + 1) % Teams.Length];
                Action(actions, info.Team == Team.None ? "Team" : $"Team: {info.Team}", () => _session.SetTeam(participant, next));
                if (!isHost)
                {
                    Action(actions, info.Muted ? "Unmute" : "Mute", () => _session.SetMuted(participant, !info.Muted));
                    Action(actions, "Remove", () => _session.Kick(participant), "player-danger");
                }
                row.Add(actions);
            }
            return row;
        }

        private static void Action(VisualElement into, string text, System.Action action, string extraClass = null)
        {
            var button = new Button(action) { text = text };
            button.AddToClassList("hud-button");
            button.AddToClassList("player-action");
            if (extraClass != null)
                button.AddToClassList(extraClass);
            into.Add(button);
        }
    }
}
