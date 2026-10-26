using System;
using System.Collections.Generic;
using System.Linq;
using Core.Engine;
using Core.Rules;
using UnityEngine;
using TableWorld.View.Settings;
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
        private LoadingScreen _loading;

        [SerializeField] private TableView _table;
        private TableWorld.View.ScriptUi.ScriptUiView _scriptUi;
        private InputEditor _inputEditor;
        [SerializeField] private TableInteraction _interaction;
        [SerializeField] private TableWorld.View.Net.NetworkTableSession _session;
        private Label _netStatus;
        private Button _blindButton;
        private ActionPicker _picker;
        private readonly CardImages _choiceImages = new CardImages();
        private string _followedTurn;
        private string _shownBlind;

        private VisualElement _root;
        private Label _turnLabel;
        private VisualElement _seatList;
        private readonly Dictionary<string, VisualElement> _seatChips = new Dictionary<string, VisualElement>();
        private EmoteWheel _emoteWheel;
        private PdfReader _reader;
        private TableWorld.View.Voice.VoiceChat _voice;
        private VisualElement _selectionPanel;
        private Label _selectionTitle;
        private Label _selectionDetail;
        private VisualElement _actions;
        private Label _status;
        private VisualElement _gamePanel;
        private Label _gameLog;
        private VisualElement _gameActions;
        private string _shownGameKey;
        private string _shownSeatsKey;
        private string _announcedTurn;
        private GameUiRenderer _gameUi;
        private TableOverlay _overlay;
        private HandBar _handBar;

        /// <summary>The hand at the bottom of the screen.</summary>
        public HandBar HandBar => _handBar;
        private readonly List<Label> _presenceLabels = new List<Label>();
        private TableWorld.View.Net.PresenceView _presence;
        private ControlsHelp _help;

        /// <summary>The controls list (F1, ?, or the Controls button).</summary>
        public ControlsHelp Help => _help;
        private ChatPanel _chat;
        private EscapeMenu _escape;
        private SaveLibrary _saves;
        private GizmoPanel _gizmo;
        private NotebookPanel _notes;

        /// <summary>The table's notebook (G25).</summary>
        public NotebookPanel Notes => _notes;

        /// <summary>Exact position, turn and scale of one object (G28).</summary>
        public GizmoPanel Gizmo => _gizmo;
        private SavesPanel _savesPanel;
        private AgentPanel _agent;

        /// <summary>Named saves and autosaves (G30).</summary>
        public SaveLibrary Saves => _saves;
        public SavesPanel SavesPanel => _savesPanel;
        private PlayersPanel _players;

        /// <summary>Everyone at a hosted table, with the host's controls (G23).</summary>
        public PlayersPanel Players => _players;
        private PanelSettings _sharedPanelSettings;

        /// <summary>The Escape menu and settings (G13, G31).</summary>
        public EscapeMenu Escape => _escape;
        public EmoteWheel Emotes => _emoteWheel;
        public PdfReader Reader => _reader;

        /// <summary>Chat and game log, bottom left (Enter).</summary>
        public ChatPanel Chat => _chat;
        private LookCamera _look;

        public TableOverlay Overlay => _overlay;
        private float _nextGameRefresh;

        /// <summary>At most this many action buttons; a game offering more should use parameters with fewer choices.</summary>
        public const int MaxActionButtons = 24;
        private ObjectView _shownSelection;
        private string _shownSelectionHash;
        private TableEngine _shownEngine;

        public void Configure(TableView table, TableInteraction interaction, TableWorld.View.Net.NetworkTableSession session = null)
        {
            _table = table;
            _interaction = interaction;
            _session = session;
        }

        private void OnEnable()
        {
            var document = GetComponent<UIDocument>();
            // The interface size setting changes the panel's scaling: on a copy, so the shared asset never changes in play.
            if (_sharedPanelSettings == null && document.panelSettings != null)
            {
                _sharedPanelSettings = document.panelSettings;
                document.panelSettings = Instantiate(_sharedPanelSettings);
            }
            _root = document.rootVisualElement;
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
                _reader = new PdfReader(_root, _table);
                _overlay.ReadRequested += _reader.Show;
                _interaction.ReadRequested += _reader.Show;
                _handBar = new HandBar(_root, _table, _interaction);
                _help = new ControlsHelp(_root);
                _interaction.Pinged += OnPing;
                _interaction.BlindfoldChanged += on => _root.Q("blindfold").EnableInClassList(Hidden, !on);
                foreach (var (name, tool) in new[] { ("toolGrab", TableInteraction.Tool.Grab), ("toolDraw", TableInteraction.Tool.Draw),
                             ("toolArrow", TableInteraction.Tool.Arrow), ("toolErase", TableInteraction.Tool.Erase), ("toolGizmo", TableInteraction.Tool.Gizmo) })
                    _root.Q<Button>(name).clicked += () => SetTool(tool);
            }
            if (_table != null)
            {
                _chat = new ChatPanel(_root, _table, _session);
                _players = new PlayersPanel(_root, _table, _session);
                _emoteWheel = new EmoteWheel(_root, emote => _session?.Emote(emote.ToString()));
            }
            _escape = new EscapeMenu(_root, GameSettings.Load(GameSettings.DefaultPath), ApplySettings, _session, () => _help?.Show());
            ApplySettings(_escape.Settings);
            _agent = new AgentPanel(_root, CloseAgent);
            _escape.OpenAgent = _agent.Show;
            if (_table != null)
            {
                _saves = new SaveLibrary(_table);
                _gizmo = new GizmoPanel(_root, _table, ShowStatus);
                _notes = new NotebookPanel(_root, _table);
                if (_interaction != null)
                    _interaction.GizmoRequested += _gizmo.Open;
                if (_overlay != null)
                    _overlay.GizmoRequested = _gizmo.Open;
                _savesPanel = new SavesPanel(_root, _saves, () => _escape.Settings.AutosaveMinutes, ShowStatus, CloseSaves);
                _escape.OpenSaves = _savesPanel.Show;
            }

            _root.Q<Button>("saveButton").clicked += Save;
            _picker = new ActionPicker(_root);
            _blindButton = _root.Q<Button>("blindButton");
            if (_blindButton != null)
                _blindButton.clicked += ToggleBlindRound;
            _netStatus = _root.Q<Label>("netStatus");
            var address = _root.Q<TextField>("netAddress");
            _root.Q<Button>("hostButton").clicked += () => _session?.StartHost(Port(address.value));
            _root.Q<Button>("hostOnlineButton").clicked += () => HostOnline(address);
            // The field takes an address (LAN) or a join code (online).
            _root.Q<Button>("joinButton").clicked += () =>
            {
                if (_session == null)
                    return;
                if (TableWorld.View.Net.RelayLobby.LooksLikeCode(address.value))
                    _ = _session.JoinWithCodeAsync(address.value);
                else
                    _session.StartClient(Host(address.value), Port(address.value));
            };
            _root.Q<Button>("loadButton").clicked += Load;
            _gamesPanel = _root.Q("gamesPanel");
            _root.Q<Button>("gamesButton").clicked += ToggleGames;
            _root.Q<Button>("gamesClose").clicked += () => _gamesPanel.AddToClassList(Hidden);

            if (_table != null)
            {
                _loading = new LoadingScreen(_root, _table);
                _table.CommandExecuted += OnCommand;
                _table.Notice += OnNotice;
                if (_interaction != null)
                {
                    _inputEditor = new InputEditor(_root, _table);
                    _interaction.InputEditRequested += _inputEditor.Show;
                }
                // The scripts' own screen UI (M5), drawn under the HUD.
                if (_scriptUi == null)
                    _scriptUi = TableWorld.View.ScriptUi.ScriptUiView.Create(_sharedPanelSettings, _table);
            }
            if (_interaction != null)
            {
                _interaction.PointerBlocked = IsOverUi;
                _interaction.KeyboardBlocked = () => TypingInUi() || (_loading?.Visible ?? false);
            }
        }

        /// <summary>True while a text field of the HUD (chat, name, address) has the keyboard: keys type, they are not shortcuts.</summary>
        public bool TypingInUi()
        {
            if (_chat != null && _chat.Open)
                return true;
            // Reading a PDF: the arrow and Page keys turn its pages, not the camera.
            if (_reader != null && _reader.Visible)
                return true;
            if (_scriptUi != null && _scriptUi.Typing)
                return true;
            // Moving a key in the settings: the next key pressed is the menu's.
            if (_escape != null && _escape.CapturingKey)
                return true;
            var focused = _root?.panel?.focusController?.focusedElement as VisualElement;
            return focused != null && (focused is TextField || focused.GetFirstAncestorOfType<TextField>() != null);
        }

        private void OnNotice(string text) => ShowStatus(text, error: true);

        /// <summary>Each other player's seat name, beside their pointer, in their colour.</summary>
        private void UpdatePresenceLabels()
        {
            _presence ??= FindAnyObjectByType<TableWorld.View.Net.PresenceView>();
            var layer = _root.Q("presenceLayer");
            var camera = Camera.main;
            if (layer == null || camera == null || _root.panel == null)
                return;
            var pointers = _presence != null ? _presence.Pointers : (IReadOnlyList<(string, Vector3, Color)>)System.Array.Empty<(string, Vector3, Color)>();
            while (_presenceLabels.Count < pointers.Count)
            {
                var label = new Label();
                label.AddToClassList("presence-label");
                label.pickingMode = PickingMode.Ignore;
                layer.Add(label);
                _presenceLabels.Add(label);
            }
            for (var i = 0; i < _presenceLabels.Count; i++)
            {
                var label = _presenceLabels[i];
                var visible = i < pointers.Count;
                label.EnableInClassList(Hidden, !visible);
                if (!visible)
                    continue;
                var (seat, position, color) = pointers[i];
                var screen = camera.WorldToScreenPoint(position + Vector3.up * 0.05f);
                if (screen.z < 0f)
                {
                    label.AddToClassList(Hidden);
                    continue;
                }
                var panel = RuntimePanelUtils.ScreenToPanel(_root.panel, new Vector2(screen.x, Screen.height - screen.y));
                label.text = _session?.NameAtSeat(seat) ?? seat;
                // The seat colour is runtime data (any colour a game picks), so it is set from code: the label is filled
                // with it, and the text is dark or light, whichever reads on that colour.
                var luminance = 0.2126f * color.r + 0.7152f * color.g + 0.0722f * color.b;
                label.style.backgroundColor = new Color(color.r, color.g, color.b, 0.9f);
                label.style.color = luminance > 0.5f ? new Color(0.08f, 0.08f, 0.1f) : Color.white;
                label.style.left = panel.x + 10f;
                label.style.top = panel.y - 22f;
            }
        }

        /// <summary>
        /// F1 or ? (Shift + /) toggles the controls list. Escape closes the topmost thing open (menu, search, games, controls,
        /// settings), and opens the Escape menu when nothing is. The chat handles its own Escape while it has the keyboard.
        /// </summary>
        private void MenuKeys()
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null || (_interaction != null && _interaction.IgnoreDevices) || TypingInUi())
                return;
            if (_help != null && (keyboard.f1Key.wasPressedThisFrame || (keyboard.slashKey.wasPressedThisFrame && keyboard.shiftKey.isPressed)))
                _help.Toggle();
            else if (keyboard.escapeKey.wasPressedThisFrame)
                PressEscape();
        }

        /// <summary>Picks the left button's tool (G28) and lights its button.</summary>
        public void SetTool(TableInteraction.Tool tool)
        {
            if (_interaction == null)
                return;
            _interaction.ActiveTool = tool;
            foreach (var (name, t) in new[] { ("toolGrab", TableInteraction.Tool.Grab), ("toolDraw", TableInteraction.Tool.Draw),
                         ("toolArrow", TableInteraction.Tool.Arrow), ("toolErase", TableInteraction.Tool.Erase), ("toolGizmo", TableInteraction.Tool.Gizmo) })
                _root.Q<Button>(name)?.EnableInClassList("tool-button--active", t == tool);
        }

        /// <summary>What Escape does (also called by tests).</summary>
        public void PressEscape()
        {
            if (_reader != null && _reader.Visible)
                _reader.Hide();
            else if (_gizmo != null && _gizmo.Visible)
                _gizmo.Hide();
            else if (_interaction != null && _interaction.ActiveTool != TableInteraction.Tool.Grab)
                SetTool(TableInteraction.Tool.Grab);
            else if (_overlay != null && _overlay.MenuOpen)
                _overlay.CloseMenu();
            else if (_overlay != null && _overlay.SearchOpen)
                _overlay.CloseSearch();
            else if (_gamesPanel != null && !_gamesPanel.ClassListContains(Hidden))
                _gamesPanel.AddToClassList(Hidden);
            else if (_players != null && _players.Visible)
                _players.Hide();
            else if (_savesPanel != null && _savesPanel.Visible)
                CloseSaves();
            else if (_agent != null && _agent.Visible)
                CloseAgent();
            else if (_notes != null && _notes.Visible)
                _notes.Hide();
            else if (_help != null && _help.Visible)
                _help.Hide();
            else
                _escape?.Back();
        }

        /// <summary>Back from the agent page to the Escape menu.</summary>
        private void CloseAgent()
        {
            _agent.Hide();
            _escape.Open();
        }

        /// <summary>Back from the save page to the Escape menu.</summary>
        private void CloseSaves()
        {
            _savesPanel.Hide();
            _escape.Open();
        }

        private void ApplySettings(GameSettings settings)
        {
            var camera = Camera.main != null ? Camera.main.GetComponent<TableCamera>() : null;
            settings.Apply(camera);
            _session?.SetLocalName(settings.PlayerName);
            _session?.SetLocalLook(settings.Personality);
            var panel = GetComponent<UIDocument>().panelSettings;
            if (panel != null && _sharedPanelSettings != null)
                panel.referenceResolution = new Vector2Int(
                    Mathf.RoundToInt(_sharedPanelSettings.referenceResolution.x / settings.UiScale),
                    Mathf.RoundToInt(_sharedPanelSettings.referenceResolution.y / settings.UiScale));
        }

        private string _facedSeat;

        /// <summary>The camera starts behind your seat: your own seat online, the first seat when playing alone.</summary>
        private void FaceLocalSeat()
        {
            var state = _table.Engine.State;
            var seat = state.Players.Seats.FirstOrDefault(s => s.Id == (_table.SeatOverride ?? state.Players.Seats.FirstOrDefault()?.Id));
            var hand = seat == null ? null : state.Zones.FirstOrDefault(z => z.Id == seat.HandZone);
            var key = hand == null ? null : $"{seat.Id}@{hand.Transform.Position.X},{hand.Transform.Position.Z}";
            if (key == null || key == _facedSeat)
                return;
            _facedSeat = key;
            var camera = Camera.main != null ? Camera.main.GetComponent<TableCamera>() : null;
            if (camera != null)
                camera.FaceFrom(hand.Transform.Position.ToUnity(), hand.Transform.Rotation.Y);
        }

        private void OnDisable()
        {
            if (_table != null)
            {
                _table.CommandExecuted -= OnCommand;
                _table.Notice -= OnNotice;
            }
            if (_interaction != null)
            {
                _interaction.PointerBlocked = null;
                _interaction.KeyboardBlocked = null;
                _interaction.Pinged -= OnPing;
                if (_reader != null)
                    _interaction.ReadRequested -= _reader.Show;
            }
        }

        private void OnPing(Vector3 point)
        {
            var seat = _table.Engine?.State.Players.Seats.FirstOrDefault(s => s.Id == _table.LocalSeat);
            PingMarker.Spawn(point, ZoneViewFactory.SeatColor(seat?.Color ?? "white"));
        }

        private void Update()
        {
            _loading?.Update();
            if (_table == null || _table.Engine == null)
                return;
            var seatsKey = $"{_table.LocalSeat}|{_table.Spectating}|{CurrentTurnSeat()}|{_table.Networked}|" +
                           (_session != null ? string.Join(",", _session.Occupants.Select(o => o.Key + "=" + o.Value + "=" + _session.NameOf(o.Value))) : "");
            if (_shownEngine != _table.Engine || seatsKey != _shownSeatsKey)
            {
                _shownSeatsKey = seatsKey;
                RebuildSeats();
            }
            if (_shownEngine != _table.Engine)
            {
                _shownEngine = _table.Engine;
                _gameUi.Build();
                _shownSelection = null;
            }
            FollowTurnInHotSeat();
            FaceLocalSeat();
            RefreshBlindRound();
            RefreshSelection();
            _overlay?.Update();
            _handBar?.Update();
            _escape?.PollKeyCapture();
            if (_agent != null && _agent.Visible)
                _agent.Update();
            MenuKeys();
            _chat?.Update(TypingInUi() || (_interaction != null && _interaction.IgnoreDevices));
            _players?.Update();
            _notes?.Update();
            _reader?.Update();
            _emoteWheel?.Update(TypingInUi() || (_interaction != null && _interaction.IgnoreDevices));
            UpdateTalking();
            if (_saves?.Tick(_escape.Settings.AutosaveMinutes) is { } autosaved)
                ShowStatus($"Autosaved ({autosaved})", error: false);
            UpdatePresenceLabels();
            if (_netStatus != null && _session != null)
                _netStatus.text = _session.Status == "offline" ? "" : _session.Status;
            if (Time.unscaledTime >= _nextGameRefresh)
            {
                _nextGameRefresh = Time.unscaledTime + 0.25f;
                RefreshGame();
                _gameUi.Refresh();
            }
        }

        /// <summary>
        /// Blind round: until the reveal, what each player moves is hidden from the others (BlindRound). The button starts
        /// one, and while it runs shows how many pieces are hidden and reveals them.
        /// </summary>
        private void ToggleBlindRound()
        {
            var running = _table.Engine.State.Blind != null;
            var result = _table.Execute(new Core.Engine.BlindRoundCommand { On = !running, Actor = _table.LocalSeat });
            if (!result.Ok)
                ShowStatus(result.Error.Message, error: true);
        }

        private void RefreshBlindRound()
        {
            if (_blindButton == null)
                return;
            var blind = _table.Engine.State.Blind;
            var key = blind == null ? "" : blind.MovedBy.Count + "|" + _table.Spectating;
            if (key == _shownBlind && _blindButton.ClassListContains("hidden") == _table.Spectating)
                return;
            _shownBlind = key;
            _blindButton.EnableInClassList("hidden", _table.Spectating);
            _blindButton.EnableInClassList("hud-button--blind", blind != null);
            _blindButton.text = blind == null ? "Blind round" : $"Reveal ({blind.MovedBy.Count})";
            _blindButton.tooltip = blind == null
                ? "Start a blind round: until the reveal, what each player moves stays hidden from the others."
                : "Reveal everything moved in this blind round.";
        }

        /// <summary>
        /// Start: online, with the seats people sit in; alone (hot seat), one button per player count the game takes, the
        /// players spread evenly around the table. A table keeps all its seats; the empty ones sit the game out.
        /// </summary>
        private void AddStartButtons(TableState state)
        {
            var seats = state.Players.Seats.Select(s => s.Id).ToList();
            var min = Mathf.Max(1, state.Game?.MinPlayers ?? seats.Count);
            var max = Mathf.Min(seats.Count, state.Game?.MaxPlayers > 0 ? state.Game.MaxPlayers : seats.Count);
            void Start(List<string> playing)
            {
                var result = _table.Execute(new StartGameCommand { Seats = playing });
                if (!result.Ok)
                    ShowStatus(result.Error.Message, error: true);
            }
            if (_table.Networked && _session != null)
            {
                var seated = seats.Where(s => _session.Occupants.ContainsKey(s)).ToList();
                AddGameButton($"Start game ({seated.Count} {(seated.Count == 1 ? "player" : "players")})", () => Start(seated));
                return;
            }
            if (min >= max)
            {
                AddGameButton("Start game", () => Start(SeatLayout.Spread(seats, max)));
                return;
            }
            for (var n = min; n <= max; n++)
            {
                var count = n;
                AddGameButton($"Start: {n} players", () => Start(SeatLayout.Spread(seats, count)));
            }
        }

        /// <summary>A seat chip glows while its player talks; your own while your microphone sends.</summary>
        private void UpdateTalking()
        {
            _voice ??= FindAnyObjectByType<TableWorld.View.Voice.VoiceChat>();
            if (_voice == null)
                return;
            var local = _table.LocalSeat;
            foreach (var pair in _seatChips)
            {
                var talking = pair.Key == local ? _voice.Talking && (_voice.Online?.Invoke() ?? false) : _voice.LevelOf(pair.Key) > 0.01f;
                pair.Value.EnableInClassList("seat-chip--talking", talking);
            }
        }

        /// <summary>
        /// Playing alone (hot seat), a seat picked from its chip is kept until the turn passes; then the view follows the
        /// seat whose turn it is again, as when players pass the device.
        /// </summary>
        private void FollowTurnInHotSeat()
        {
            if (_table.Networked)
                return;
            var turn = _table.Game?.Session?.CurrentSeat;
            if (turn == _followedTurn)
                return;
            _followedTurn = turn;
            _table.SeatOverride = null;
        }

        /// <summary>
        /// Turn line, Start button and one button per action the local seat may perform now (in hot seat, the seat whose
        /// turn it is, or the one picked from its chip). An action with several choices opens the picker; actions marked
        /// <c>button: false</c> are done with the pieces, by scripts or by agents, and get none.
        /// </summary>
        private void RefreshGame()
        {
            var game = _table.Game;
            var state = game.State;
            var session = game.Session;
            var legal = session is { Started: true, Over: false }
                ? state.Players.Seats.Where(s => s.Id == _table.LocalSeat)
                    .SelectMany(s => game.LegalActions(s.Id).Select(a => (Seat: s.Id, Action: a)))
                    .Where(l => state.Actions.FirstOrDefault(a => a.Id == l.Action.ActionId)?.Button != false).ToList()
                : new List<(string Seat, LegalAction Action)>();
            var turns = state.FreeTurns;
            var key = $"{state.HasRules}|{session?.Turn}|{session?.Phase}|{session?.CurrentSeat}|{session?.Over}|{session?.Log.Count}|" +
                      $"{turns?.Current}|{turns?.Count}|{turns?.Reverse}|{_table.LocalSeat}|" +
                      string.Join(",", legal.Select(l => $"{l.Seat}/{l.Action.ActionId}/{l.Action.Choices.Count}"));
            if (key == _shownGameKey)
                return;
            _shownGameKey = key;

            AnnounceTurn();
            _turnLabel.text = !state.HasRules ? (turns is { Enabled: true } ? FreeTurnText(turns) : "Free play")
                : session == null ? $"{state.Game?.Name ?? "Game"}: not started"
                : session.Over ? $"Game over: {session.Winner ?? "nobody"} wins"
                : $"Turn {session.Turn} · {PhaseName(state, session.Phase)} · {session.CurrentSeat}";

            _gameActions.Clear();
            if (!state.HasRules)
            {
                // Free-play turns (G22): the seat whose turn it is ends it (any seat at a hot-seat table).
                var canEnd = turns is { Enabled: true } && (!_table.Networked || turns.Current == _table.LocalSeat);
                _gamePanel.EnableInClassList(Hidden, !canEnd);
                _gameLog.AddToClassList(Hidden);
                if (canEnd)
                    AddGameButton($"End turn ({turns.Current})", () => _table.Execute(new EndTurnCommand { Actor = _table.Networked ? _table.LocalSeat : null }));
                return;
            }
            _gamePanel.RemoveFromClassList(Hidden);
            _gameLog.text = session?.Log.LastOrDefault() ?? "";
            _gameLog.EnableInClassList(Hidden, string.IsNullOrEmpty(_gameLog.text));
            if (session == null)
            {
                AddStartButtons(state);
                return;
            }
            var count = 0;
            foreach (var (seat, action) in legal)
            {
                if (count++ >= MaxActionButtons)
                    return;
                var name = action.Name ?? action.ActionId;
                var actor = seat;
                var id = action.ActionId;
                void Perform(Dictionary<string, string> parameters)
                {
                    var result = _table.Execute(new PerformActionCommand { Actor = actor, ActionId = id, Params = new Dictionary<string, string>(parameters) });
                    if (!result.Ok)
                        ShowStatus(result.Error.Message, error: true);
                }
                if (action.Choices.Count == 1)
                {
                    var only = action.Choices[0];
                    AddGameButton(only.Count == 0 ? name : $"{name} · {ChoiceLabel(only)}", () => Perform(only));
                    continue;
                }
                var choices = action.Choices;
                AddGameButton($"{name} ({choices.Count})", () => _picker.Show(name, choices.Select(c => new ActionPicker.Choice
                {
                    Label = ChoiceLabel(c), Image = ChoiceImage(c), Pick = () => Perform(c),
                }).ToList()));
            }
        }

        private string ChoiceLabel(Dictionary<string, string> choice) =>
            string.Join(", ", choice.Values.Select(v => _table.Engine.Find(v)?.Name ?? v));

        /// <summary>The card face of a choice that is one card, for the picker.</summary>
        private Sprite ChoiceImage(Dictionary<string, string> choice)
        {
            if (choice.Count != 1 || !(_table.Engine.Find(choice.Values.First()) is Card card))
                return null;
            var (asset, index) = _table.Assets.Face(card);
            return _choiceImages.Face(_table.Assets, asset, index);
        }

        private string TurnText(Command command)
        {
            var turns = _table.Engine.State.FreeTurns;
            string NameOf(string id) => _table.Engine.Find(id) is { } o ? o.Name ?? o.Type : id;
            return command switch
            {
                SetCounterCommand c => $"{NameOf(c.ObjectId)}: {(_table.Engine.Find(c.ObjectId) as Counter)?.Value}",
                SetClockCommand c => _table.Engine.Find(c.ObjectId) is Clock clock
                    ? $"{NameOf(c.ObjectId)}: {(clock.Mode == ClockMode.Clock ? "clock" : clock.Running ? "running" : clock.Mode == ClockMode.Timer && clock.Seconds <= 0f ? "time's up" : "stopped")}"
                    : null,
                SetGridCommand { Grid: null } => "Grid: none",
                SetGridCommand c => $"Grid: {(c.Grid.Shape == GridShape.Hex ? "hexes" : "squares")} of {c.Grid.SizeX * 100f:0.#} cm" +
                                     (c.Grid.Snapping != GridSnapping.Off ? $", snapping to {c.Grid.Snapping.ToString().ToLowerInvariant()}" : "") +
                                     (c.Grid.Lines ? "" : ", lines hidden"),
                SetTurnOrderCommand { Enabled: false } => "Turns: off",
                SetTurnOrderCommand c => $"Turns: on{(c.Reverse ? ", reversed" : "")}{(c.SkipEmptyHands ? ", skipping empty hands" : "")} · {turns?.Current}'s turn",
                EndTurnCommand _ => $"{turns?.Current}'s turn",
                _ => null,
            };
        }

        /// <summary>Whose turn it is: the rules game's, or free play's (G22); null when nobody has a turn.</summary>
        private string CurrentTurnSeat()
        {
            var state = _table.Engine?.State;
            if (state == null)
                return null;
            if (_table.Game?.Session is { Started: true, Over: false } session)
                return session.CurrentSeat;
            return state.FreeTurns is { Enabled: true } turns ? turns.Current : null;
        }

        private string FreeTurnText(FreeTurns turns) =>
            (turns.Current == _table.LocalSeat && _table.Networked ? "Your turn" : $"{turns.Current}'s turn") +
            $" · turn {turns.Count + 1}" + (turns.Reverse ? " · reversed" : "");

        /// <summary>Online, a player whose turn just came is told so (TTS flashes "your turn").</summary>
        private void AnnounceTurn()
        {
            var current = CurrentTurnSeat();
            if (current == _announcedTurn)
                return;
            _announcedTurn = current;
            if (current != null && _table.Networked && current == _table.LocalSeat)
                ShowStatus("Your turn", error: false);
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
            if (_scriptUi != null && _scriptUi.IsOver(screenPosition))
                return true;
            if (_root?.panel == null)
                return false;
            var panelPosition = RuntimePanelUtils.ScreenToPanel(_root.panel, new Vector2(screenPosition.x, Screen.height - screenPosition.y));
            var picked = _root.panel.Pick(panelPosition);
            return picked != null && picked != _root;
        }

        /// <summary>
        /// One chip per seat: colour, name and who sits there. Clicking a free seat sits you there (G10); online, the host
        /// decides. Online there is also a Watch chip to give up your seat.
        /// </summary>
        private void RebuildSeats()
        {
            _seatList.Clear();
            _seatChips.Clear();
            var local = _table.LocalSeat;
            var online = _table.Networked && _session != null;
            var occupants = online ? _session.Occupants : null;
            foreach (var seat in _table.Engine.State.Players.Seats)
            {
                var chip = new VisualElement();
                chip.AddToClassList("seat-chip");
                _seatChips[seat.Id] = chip;
                if (seat.Id == local)
                    chip.AddToClassList("seat-chip--local");
                if (seat.Id == CurrentTurnSeat())
                    chip.AddToClassList("seat-chip--turn");
                // A seat the game started without sits it out.
                if (_table.Game?.Session is { Started: true } && _table.Game.Playing.All(p => p.Id != seat.Id))
                    chip.AddToClassList("seat-chip--out");
                var sitter = occupants != null && occupants.TryGetValue(seat.Id, out var occupant) ? occupant : null;
                var takenByOther = sitter != null && seat.Id != local;
                chip.AddToClassList(takenByOther ? "seat-chip--taken" : "seat-chip--free");
                var swatch = new VisualElement { pickingMode = PickingMode.Ignore };
                swatch.AddToClassList("seat-swatch");
                swatch.AddToClassList("seat-swatch--" + seat.Color.ToLowerInvariant());
                // Online, a seat shows who sits there by name (G23); free seats show the seat.
                var who = sitter != null ? _session.NameOf(sitter) : null;
                var suffix = seat.Id == local ? " (you)" : sitter == TableWorld.View.Net.NetworkTableSession.HostIdForUi ? " (host)" : takenByOther && who == null ? " (taken)" : "";
                var name = new Label((who ?? seat.Id) + suffix) { pickingMode = PickingMode.Ignore, enableRichText = false };
                name.AddToClassList("seat-name");
                chip.Add(swatch);
                chip.Add(name);
                chip.tooltip = takenByOther ? $"{who ?? "Someone"} sits at {seat.Id}" : seat.Id == local ? $"Your seat ({seat.Id})" : $"Click to sit at {seat.Id}";
                var id = seat.Id;
                if (!takenByOther && seat.Id != local)
                    chip.RegisterCallback<ClickEvent>(_ => SitAt(id));
                _seatList.Add(chip);
            }
            if (online)
            {
                var watch = new VisualElement { tooltip = "Leave your seat and watch" };
                watch.AddToClassList("seat-chip");
                watch.AddToClassList(_table.Spectating ? "seat-chip--local" : "seat-chip--free");
                var label = new Label(_table.Spectating ? "Watching (you)" : "Watch") { pickingMode = PickingMode.Ignore };
                label.AddToClassList("seat-name");
                watch.Add(label);
                if (!_table.Spectating)
                    watch.RegisterCallback<ClickEvent>(_ => SitAt(null));
                _seatList.Add(watch);
            }
        }

        /// <summary>Sit at a seat (null: watch). Online the host decides; solo it picks which seat this machine plays.</summary>
        public void SitAt(string seat)
        {
            if (_table.Networked && _session != null)
                _session.RequestSeat(seat);
            else if (seat != null)
                _table.SeatOverride = seat;
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
            _selectionTitle.text = selected.HideFace ? "A card in another player's hand" : TableOverlay.DisplayName(model);
            _selectionDetail.text = selected.HideFace ? "" : Describe(model);

            AddAction(model.Locked ? "Unlock (L)" : "Lock (L)", () => _interaction.ToggleLock(selected));
            if (model.Locked)
            {
                AddReadoutActions(selected);
                return;
            }
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
            AddReadoutActions(selected);
        }

        /// <summary>Counter and clock buttons (G27); they work on locked ones too.</summary>
        private void AddReadoutActions(ObjectView selected)
        {
            switch (selected.Model)
            {
                case Counter _:
                    AddAction("−1", () => _interaction.AddToCounter(selected, -1));
                    AddAction("+1", () => _interaction.AddToCounter(selected, 1));
                    AddAction("Reset", () => _table.Execute(new SetCounterCommand { ObjectId = selected.Id, Value = 0, Actor = _table.LocalSeat }));
                    break;
                case Clock { Mode: not ClockMode.Clock } clock:
                    AddAction(clock.Running ? "Pause" : "Start", () => _interaction.StartOrPause(selected));
                    AddAction("Reset", () => _interaction.ResetClock(selected));
                    break;
            }
        }

        private static string SelectionKey(TableObject model) => $"{model.NameHidden}:{model.Name}:" + model switch
        {
            Deck deck => $"{model.Locked}:{deck.Cards.Count}",
            Die die => $"{model.Locked}:{die.Value}",
            Counter counter => $"{model.Locked}:{counter.Value}",
            Clock clock => $"{model.Locked}:{clock.Mode}:{clock.Running}:{clock.Seconds}",
            _ => $"{model.Locked}",
        };

        private string Describe(TableObject model) => model switch
        {
            Deck deck => $"Deck · {deck.Cards.Count} cards · {(deck.IsFaceUp ? "face up" : "face down")}",
            Card card => $"Card · {(card.IsFaceUp ? "face up" : "face down")}",
            Die die => $"d{die.Faces} · {(die.Value is { } value ? $"shows {die.Face(value)}" : "not rolled")}",
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
            // What table scripts do is theirs to tell (chat, their own UI); their refusals are in the log.
            if (command.ByScript)
                return;
            // Moves and strokes are too frequent for the status line (strokes are in the game log).
            if (result.Ok && (command is MoveCommand || command is AddDrawingCommand || command is EraseDrawingCommand ||
                              command is DropCommand || command is PickUpCommand || command is PeekCommand || command is BlindfoldCommand ||
                              command is SetButtonsCommand || command is ClickButtonCommand || command is SetScriptUiCommand ||
                              command is SetUiAttributesCommand || command is UiEventCommand || command is SetInputsCommand ||
                              command is TypeInputCommand || command is SpawnCommand))
                return;
            var name = command.GetType().Name.Replace("Command", "");
            _status.text = result.Ok ? (command is RollCommand ? RollText(result) : command is SetPhysicsCommand physics ? $"Physics: {PhysicsName(physics.Mode)}" : TurnText(command) ?? $"{name}: done")
                : $"{name} rejected: {result.Error.Message} {result.Error.Suggestion}";
            _status.EnableInClassList("status-label--error", !result.Ok);
            _status.RemoveFromClassList(Hidden);
            _shownSelectionHash = null;
        }

        public static string PhysicsName(PhysicsMode mode) => mode switch
        {
            PhysicsMode.SemiLock => "semi-lock (nothing tips over)",
            PhysicsMode.Locked => "locked (objects stay where they are put)",
            _ => "full",
        };

        private readonly List<(int Faces, int Value, string Face, bool Numeric)> _rolls = new List<(int, int, string, bool)>();
        private int _rollsFrame = -1;

        /// <summary>What was rolled; dice rolled together (G20) are listed together with their total.</summary>
        private string RollText(CommandResult result)
        {
            if (Time.frameCount != _rollsFrame)
            {
                _rolls.Clear();
                _rollsFrame = Time.frameCount;
            }
            foreach (var rolled in result.Events.OfType<DieRolled>())
                if (_table.Engine.Find(rolled.ObjectId) is Die die)
                    _rolls.Add((die.Faces, rolled.Value, die.Face(rolled.Value), die.Numeric));
            if (_rolls.Count == 0)
                return "Rolled";
            if (_rolls.Count == 1)
                return $"Rolled {_rolls[0].Face} (d{_rolls[0].Faces})";
            var list = $"Rolled {string.Join(", ", _rolls.Select(r => $"{r.Face} (d{r.Faces})"))}";
            // Labelled faces (G21) do not add up.
            return _rolls.All(r => r.Numeric) ? $"{list} · total {_rolls.Sum(r => r.Value)}" : list;
        }

        private async void HostOnline(TextField address)
        {
            if (_session == null || !await _session.HostWithCodeAsync())
                return;
            // Show the code where friends' copies would type it, and put it on the clipboard to paste in a chat.
            address.value = _session.JoinCode;
            GUIUtility.systemCopyBuffer = _session.JoinCode;
        }

        private static string Host(string address) => address.Split(':')[0].Trim();

        private static ushort Port(string address) =>
            address.Contains(":") && ushort.TryParse(address.Split(':')[1], out var port) ? port : TableWorld.View.Net.NetworkTableSession.DefaultPort;

        private VisualElement _gamesPanel;

        private void ToggleGames()
        {
            if (!_gamesPanel.ClassListContains(Hidden))
            {
                _gamesPanel.AddToClassList(Hidden);
                return;
            }
            var list = _gamesPanel.Q<ScrollView>("gamesList");
            list.Clear();
            foreach (var entry in GameLibrary.List())
            {
                var button = new Button(() => OpenGame(entry)) { text = entry.Name };
                button.AddToClassList("menu-item");
                list.Add(button);
                ImportActions(list, entry);
            }
            ListTtsSaves(list);
            _gamesPanel.Q<Label>("gamesHint").text = $"Your own games: put a .json file, or a folder with table.json and its images, in {GameLibrary.UserFolder}";
            _gamesPanel.RemoveFromClassList(Hidden);
        }

        private bool _importing;

        /// <summary>
        /// Under an imported game: download the files its import could not find, and re-import it when the importer has
        /// improved since (T8.2). Both read what the import recorded in the game's folder, so they work any time.
        /// </summary>
        private void ImportActions(ScrollView list, GameLibrary.Entry entry)
        {
            var folder = entry.PackageRoot;
            if (_importing || !Core.Tts.TtsPackage.IsImport(folder))
                return;
            var missing = Core.Tts.TtsPackage.ReadInfo(folder)?.MissingNow(folder);
            if (missing != null && missing.Count > 0)
                list.Add(ImportAction($"Download its {missing.Count} missing files", () => DownloadMissing(entry.Name, folder, missing)));
            if (Core.Tts.TtsPackage.Outdated(folder) && Core.Tts.TtsPackage.SourceOf(folder, Core.Tts.TtsPackage.DefaultFolder) is { } source)
                list.Add(ImportAction("Re-import it (made by an older importer)", () => Reimport(entry.Name, source, folder)));
        }

        private static Button ImportAction(string text, System.Action clicked)
        {
            var button = new Button(clicked) { text = text };
            button.AddToClassList("menu-item");
            button.AddToClassList("menu-item--sub");
            return button;
        }

        /// <summary>
        /// Fetches what the TTS cache lacked (T8.2): the files the save names, from where TTS would fetch them, a few at
        /// a time; a stopped download resumes next time. The game shows them when it is opened again.
        /// </summary>
        private async void DownloadMissing(string name, string folder, List<(string Url, string Path)> missing)
        {
            if (_importing)
                return;
            _importing = true;
            CloseGamesPanel();
            var files = missing.Select(m => (m.Url, System.IO.Path.Combine(folder, m.Path))).ToList();
            var progress = new System.Progress<(int Done, int Total)>(p => ShowStatus($"Downloading {name}: {p.Done} of {p.Total} files", error: false));
            try
            {
                var result = await new Core.Tts.AssetDownloader().Download(files, progress);
                var failed = result.Failed.Count > 0 ? $"; {result.Failed.Count} could not be fetched (listed in the log)" : "";
                foreach (var (url, reason) in result.Failed.Take(50))
                    Debug.LogWarning($"[Import] {url}: {reason}");
                ShowStatus($"{name}: {result.Downloaded} files downloaded ({result.Bytes / (1024 * 1024)} MB){failed}. Open it from Games to see them.", error: false);
            }
            catch (System.Exception e)
            {
                ShowStatus($"Downloading {name} stopped: {e.Message}", error: true);
            }
            finally
            {
                _importing = false;
            }
        }

        /// <summary>
        /// Tabletop Simulator saves on this computer (M7): importing one makes it a game in the list above, with the
        /// files the TTS cache holds. Only the player's own files are read; nothing is downloaded or shared.
        /// </summary>
        private void ListTtsSaves(ScrollView list)
        {
            var folder = Core.Tts.TtsPackage.DefaultFolder;
            if (!System.IO.Directory.Exists(folder))
                return;
            var saves = Core.Tts.TtsPackage.Find(folder);
            if (saves.Count == 0)
                return;
            var title = new Label("Import from Tabletop Simulator");
            title.AddToClassList("selection-title");
            list.Add(title);
            foreach (var save in saves)
            {
                var button = new Button(() => ImportTts(save, folder)) { text = save.Label };
                button.AddToClassList("menu-item");
                list.Add(button);
            }
        }

        private async void ImportTts(Core.Tts.TtsPackage.Save save, string ttsFolder)
        {
            if (_importing)
                return;
            _importing = true;
            ShowStatus($"Importing {save.Name}…", error: false);
            var target = System.IO.Path.Combine(GameLibrary.UserFolder, save.FolderName);
            try
            {
                // Big saves and their files take seconds: off the main thread, back here to report.
                var report = await System.Threading.Tasks.Task.Run(() => Core.Tts.TtsPackage.Write(save.File, ttsFolder, target));

                var missing = report.Missing.Count > 0 ? $"; {report.Missing.Count} files not in the TTS cache (they show as placeholders)" : "";
                ShowStatus($"Imported {save.Name}: {report.Objects} objects, {report.Copied} files{missing}. It is in Games.", error: false);
                if (!_gamesPanel.ClassListContains(Hidden))
                {
                    _gamesPanel.AddToClassList(Hidden);
                    ToggleGames();
                }
            }
            catch (System.Exception e)
            {
                ShowStatus($"Could not import {save.Name}: {e.Message}", error: true);
                Debug.LogException(e);
            }
            finally
            {
                _importing = false;
            }
        }

        /// <summary>Imports a game's save again into its own folder, with what the importer does now.</summary>
        private async void Reimport(string name, string source, string folder)
        {
            if (_importing)
                return;
            _importing = true;
            CloseGamesPanel();
            ShowStatus($"Re-importing {name}…", error: false);
            try
            {
                var report = await System.Threading.Tasks.Task.Run(() => Core.Tts.TtsPackage.Write(source, Core.Tts.TtsPackage.DefaultFolder, folder));
                var missing = report.Missing.Count > 0 ? $"; {report.Missing.Count} files still missing (Games offers to download them)" : "";
                ShowStatus($"Re-imported {name}: {report.Objects} objects{missing}.", error: false);
            }
            catch (System.Exception e)
            {
                ShowStatus($"Could not re-import {name}: {e.Message}", error: true);
                Debug.LogException(e);
            }
            finally
            {
                _importing = false;
            }
        }

        private void CloseGamesPanel()
        {
            if (_gamesPanel != null)
                _gamesPanel.AddToClassList(Hidden);
        }

        private void OpenGame(GameLibrary.Entry entry)
        {
            // The host's table is the shared one; a client opening a file would leave the session's table.
            if (_session != null && _session.IsClient)
            {
                ShowStatus("Only the host can open a game", error: true);
                return;
            }
            string json;
            try
            {
                json = System.IO.File.ReadAllText(entry.File);
            }
            catch (System.IO.IOException e)
            {
                ShowStatus($"Could not read {entry.File}: {e.Message}", error: true);
                return;
            }
            _gamesPanel.AddToClassList(Hidden);
            StartCoroutine(Open());

            System.Collections.IEnumerator Open()
            {
                yield return _table.LoadInSteps(json, entry.PackageRoot, entry.Name);
                var ok = _table.LastLoadOk;
                ShowStatus(ok ? $"Opened {entry.Name}" : $"{entry.Name} is not a valid game (details in the log)", error: !ok);
                if (!ok)
                    _gamesPanel.RemoveFromClassList(Hidden);
            }
        }

        private void Save()
        {
            var hash = _table.Save(TableView.SavePath(TableView.QuickSaveName));
            ShowStatus($"Saved quicksave ({hash.Substring(7, 8)})", error: false);
        }

        private void Load() =>
            _table.LoadFileInSteps(TableView.SavePath(TableView.QuickSaveName), "Quicksave",
                ok => ShowStatus(ok ? "Loaded quicksave" : "No quicksave yet", error: !ok));

        private void ShowStatus(string text, bool error)
        {
            _status.text = text;
            _status.EnableInClassList("status-label--error", error);
            _status.RemoveFromClassList(Hidden);
        }
    }
}
