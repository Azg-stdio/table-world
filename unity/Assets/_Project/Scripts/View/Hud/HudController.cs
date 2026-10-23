using System;
using System.Collections.Generic;
using System.Linq;
using Core.Engine;
using Core.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace TableWorld.View.Hud
{
    /// <summary>
    /// HUD v0 (UI/Hud/Hud.uxml): turn line, seats, save/load, the selected object's actions and the last command's
    /// outcome. Every button issues a command through <see cref="TableInteraction"/> or <see cref="TableView"/>.
    /// Styling lives in Hud.uss; this class only toggles classes.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class HudController : MonoBehaviour
    {
        private const string Hidden = "hidden";

        [SerializeField] private TableView _table;
        [SerializeField] private TableInteraction _interaction;

        private VisualElement _root;
        private Label _turnLabel;
        private VisualElement _seatList;
        private VisualElement _selectionPanel;
        private Label _selectionTitle;
        private Label _selectionDetail;
        private VisualElement _actions;
        private Label _status;
        private VisualElement _gamePanel;
        private Label _gameLog;
        private VisualElement _gameActions;
        private string _shownGameKey;
        private GameUiRenderer _gameUi;
        private TableOverlay _overlay;
        private LookCamera _look;

        public TableOverlay Overlay => _overlay;
        private float _nextGameRefresh;

        /// <summary>At most this many action buttons; a game offering more should use parameters with fewer choices.</summary>
        public const int MaxActionButtons = 24;
        private ObjectView _shownSelection;
        private string _shownSelectionHash;
        private TableEngine _shownEngine;

        public void Configure(TableView table, TableInteraction interaction)
        {
            _table = table;
            _interaction = interaction;
        }

        private void OnEnable()
        {
            _root = GetComponent<UIDocument>().rootVisualElement;
            _turnLabel = _root.Q<Label>("turnLabel");
            _seatList = _root.Q("seatList");
            _selectionPanel = _root.Q("selectionPanel");
            _selectionTitle = _root.Q<Label>("selectionTitle");
            _selectionDetail = _root.Q<Label>("selectionDetail");
            _actions = _root.Q("actions");
            _status = _root.Q<Label>("statusLabel");
            _gamePanel = _root.Q("gamePanel");
            _gameLog = _root.Q<Label>("gameLog");
            _gameActions = _root.Q("gameActions");
            _gameUi = new GameUiRenderer(_root.Q("gameUi"), _table);
            if (_interaction != null)
            {
                _look = _look != null ? _look : LookCamera.Create(transform);
                var camera = Camera.main != null ? Camera.main.GetComponent<TableCamera>() : null;
                _overlay = new TableOverlay(_root, _table, _interaction, camera, _look);
                _interaction.Pinged += OnPing;
            }

            _root.Q<Button>("saveButton").clicked += Save;
            _root.Q<Button>("loadButton").clicked += Load;

            if (_table != null)
                _table.CommandExecuted += OnCommand;
            if (_interaction != null)
                _interaction.PointerBlocked = IsOverUi;
        }

        private void OnDisable()
        {
            if (_table != null)
                _table.CommandExecuted -= OnCommand;
            if (_interaction != null)
            {
                _interaction.PointerBlocked = null;
                _interaction.Pinged -= OnPing;
            }
        }

        private void OnPing(Vector3 point)
        {
            var seat = _table.Engine?.State.Players.Seats.FirstOrDefault(s => s.Id == _table.LocalSeat);
            PingMarker.Spawn(point, ZoneViewFactory.SeatColor(seat?.Color ?? "white"));
        }

        private void Update()
        {
            if (_table == null || _table.Engine == null)
                return;
            if (_shownEngine != _table.Engine)
            {
                _shownEngine = _table.Engine;
                RebuildSeats();
                _gameUi.Build();
                _shownSelection = null;
            }
            RefreshSelection();
            _overlay?.Update();
            if (Time.unscaledTime >= _nextGameRefresh)
            {
                _nextGameRefresh = Time.unscaledTime + 0.25f;
                RefreshGame();
                _gameUi.Refresh();
            }
        }

        /// <summary>Turn line, Start button and one button per legal action (hot seat: every seat that can act).</summary>
        private void RefreshGame()
        {
            var game = _table.Game;
            var state = game.State;
            var session = game.Session;
            var legal = session is { Started: true, Over: false }
                ? state.Players.Seats.SelectMany(s => game.LegalActions(s.Id).Select(a => (Seat: s.Id, Action: a))).ToList()
                : new List<(string Seat, LegalAction Action)>();
            var key = $"{state.HasRules}|{session?.Turn}|{session?.Phase}|{session?.CurrentSeat}|{session?.Over}|{session?.Log.Count}|" +
                      string.Join(",", legal.Select(l => $"{l.Seat}/{l.Action.ActionId}/{l.Action.Choices.Count}"));
            if (key == _shownGameKey)
                return;
            _shownGameKey = key;

            _turnLabel.text = !state.HasRules ? "Free play"
                : session == null ? $"{state.Game?.Name ?? "Game"}: not started"
                : session.Over ? $"Game over: {session.Winner ?? "nobody"} wins"
                : $"Turn {session.Turn} · {PhaseName(state, session.Phase)} · {session.CurrentSeat}";

            _gameActions.Clear();
            _gamePanel.EnableInClassList(Hidden, !state.HasRules);
            if (!state.HasRules)
                return;
            _gameLog.text = session?.Log.LastOrDefault() ?? "";
            _gameLog.EnableInClassList(Hidden, string.IsNullOrEmpty(_gameLog.text));
            if (session == null)
            {
                AddGameButton("Start game", () => _table.Execute(new StartGameCommand()));
                return;
            }
            var count = 0;
            foreach (var (seat, action) in legal)
            {
                foreach (var choice in action.Choices)
                {
                    if (count++ >= MaxActionButtons)
                        return;
                    var label = $"{seat}: {action.Name ?? action.ActionId}";
                    if (choice.Count > 0)
                        label += " · " + string.Join(", ", choice.Values.Select(v => _table.Engine.Find(v)?.Name ?? v));
                    var parameters = choice;
                    AddGameButton(label, () => _table.Execute(new PerformActionCommand { Actor = seat, ActionId = action.ActionId, Params = new Dictionary<string, string>(parameters) }));
                }
            }
        }

        private static string PhaseName(TableState state, string phase) => state.Phases.FirstOrDefault(p => p.Id == phase)?.Name ?? phase;

        private void AddGameButton(string text, Action action)
        {
            var button = new Button(action) { text = text };
            button.AddToClassList("hud-button");
            _gameActions.Add(button);
        }

        /// <summary>True when the screen point is over an opaque HUD element, so the table must ignore the click.</summary>
        private bool IsOverUi(Vector2 screenPosition)
        {
            if (_root?.panel == null)
                return false;
            var panelPosition = RuntimePanelUtils.ScreenToPanel(_root.panel, new Vector2(screenPosition.x, Screen.height - screenPosition.y));
            var picked = _root.panel.Pick(panelPosition);
            return picked != null && picked != _root;
        }

        private void RebuildSeats()
        {
            _seatList.Clear();
            var local = _table.LocalSeat;
            foreach (var seat in _table.Engine.State.Players.Seats)
            {
                var chip = new VisualElement { pickingMode = PickingMode.Ignore };
                chip.AddToClassList("seat-chip");
                if (seat.Id == local)
                    chip.AddToClassList("seat-chip--local");
                var swatch = new VisualElement { pickingMode = PickingMode.Ignore };
                swatch.AddToClassList("seat-swatch");
                swatch.AddToClassList("seat-swatch--" + seat.Color.ToLowerInvariant());
                var name = new Label(seat.Id == local ? $"{seat.Id} (you)" : seat.Id) { pickingMode = PickingMode.Ignore };
                name.AddToClassList("seat-name");
                chip.Add(swatch);
                chip.Add(name);
                _seatList.Add(chip);
            }
        }

        private void RefreshSelection()
        {
            var selected = _interaction != null ? _interaction.Selected : null;
            var hash = selected == null ? null : SelectionKey(selected.Model);
            if (selected == _shownSelection && hash == _shownSelectionHash)
                return;
            _shownSelection = selected;
            _shownSelectionHash = hash;

            _selectionPanel.EnableInClassList(Hidden, selected == null);
            _actions.Clear();
            if (selected == null)
                return;

            var model = selected.Model;
            _selectionTitle.text = string.IsNullOrEmpty(model.Name) ? $"{model.Type} {model.Id}" : model.Name;
            _selectionDetail.text = Describe(model);

            AddAction(model.Locked ? "Unlock (L)" : "Lock (L)", () => _interaction.ToggleLock(selected));
            if (model.Locked)
                return;
            AddAction("Flip (F)", () => _interaction.Flip(selected));
            switch (model)
            {
                case Die _:
                    AddAction("Roll (R)", () => _interaction.RollOrShuffle(selected));
                    break;
                case Deck deck when deck.Cards.Count > 0:
                    AddAction("Shuffle (R)", () => _interaction.RollOrShuffle(selected));
                    AddAction("Deal 1 to me", () => _interaction.DealToLocalSeat(selected, 1));
                    AddAction("Deal 5 to me", () => _interaction.DealToLocalSeat(selected, 5));
                    AddAction("Deal 1 to all", () => _table.Execute(new DealCommand { DeckId = selected.Id, Count = 1, Actor = _table.LocalSeat }));
                    break;
                case Bag bag when !(bag is InfiniteBag):
                    AddAction("Shuffle (R)", () => _interaction.RollOrShuffle(selected));
                    break;
            }
        }

        private static string SelectionKey(TableObject model) => model switch
        {
            Deck deck => $"{model.Locked}:{deck.Cards.Count}",
            Die die => $"{model.Locked}:{die.Value}",
            _ => $"{model.Locked}",
        };

        private string Describe(TableObject model) => model switch
        {
            Deck deck => $"Deck · {deck.Cards.Count} cards · {(deck.IsFaceUp ? "face up" : "face down")}",
            Card card => $"Card · {(card.IsFaceUp ? "face up" : "face down")}",
            Die die => $"d{die.Faces} · {(die.Value.HasValue ? $"shows {die.Value}" : "not rolled")}",
            Bag bag => $"{model.Type} · {_table.Engine.ContentsOf(bag.Id).Count()} inside",
            _ => model.Type,
        } + (model.Locked ? " · locked" : "");

        private void AddAction(string text, Action action)
        {
            var button = new Button(action) { text = text };
            button.AddToClassList("hud-button");
            _actions.Add(button);
        }

        private void OnCommand(Command command, CommandResult result)
        {
            if (result.Ok && command is MoveCommand)
                return;
            var name = command.GetType().Name.Replace("Command", "");
            _status.text = result.Ok ? $"{name}: done" : $"{name} rejected: {result.Error.Message} {result.Error.Suggestion}";
            _status.EnableInClassList("status-label--error", !result.Ok);
            _status.RemoveFromClassList(Hidden);
            _shownSelectionHash = null;
        }

        private void Save()
        {
            var hash = _table.Save(TableView.SavePath(TableView.QuickSaveName));
            ShowStatus($"Saved quicksave ({hash.Substring(7, 8)})", error: false);
        }

        private void Load()
        {
            var ok = _table.LoadFile(TableView.SavePath(TableView.QuickSaveName));
            ShowStatus(ok ? "Loaded quicksave" : "No quicksave yet", error: !ok);
        }

        private void ShowStatus(string text, bool error)
        {
            _status.text = text;
            _status.EnableInClassList("status-label--error", error);
            _status.RemoveFromClassList(Hidden);
        }
    }
}
