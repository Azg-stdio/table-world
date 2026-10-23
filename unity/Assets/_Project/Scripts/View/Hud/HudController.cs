using System;
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
                _interaction.PointerBlocked = null;
        }

        private void Update()
        {
            if (_table == null || _table.Engine == null)
                return;
            if (_shownEngine != _table.Engine)
            {
                _shownEngine = _table.Engine;
                RebuildSeats();
                _shownSelection = null;
            }
            RefreshSelection();
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
            _turnLabel.text = "Free play";
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
