using System;
using System.Collections.Generic;
using System.Linq;
using Core.Engine;
using Core.Rules;
using Core.Text;
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
        private DiceTotalsView _diceTotals;
        private SeatJoinView _seatJoins;
        /// <summary>Hot seat, before the start: the seats this machine's players sat at (the first seat to begin with).</summary>
        private readonly HashSet<string> _joined = new HashSet<string>();
        private object _joinedFor;
        private ShowcaseView _showcase;
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
        /// <summary>This player's hidden role and what it knows (ADR 0012), above the log.</summary>
        private Label _gameRole;
        /// <summary>The dark screen of a player whose eyes are closed (ADR 0012), with the phase's words.</summary>
        private VisualElement _sleepOverlay;
        private Label _sleepText;
        /// <summary>Optional roles ticked for the next start, by table (a new table starts with none).</summary>
        private readonly HashSet<string> _optionalRoles = new HashSet<string>();
        private object _optionalRolesFor;
        /// <summary>The language the host picked for the next start (ADR 0011); null: the game's own.</summary>
        private string _startLanguage;
        /// <summary>Lines of the game log already shown, and the session they belong to.</summary>
        private int _logShown;
        private object _logSession;
        /// <summary>The game panel shows every line the latest change wrote, up to this many (a reveal writes why each piece went back).</summary>
        public const int MaxLogLines = 4;
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
            // The UXML's own texts are read before any code fills a label, so each keeps its English to translate.
            _localizer = new UiLocalizer(_root);
            Ui.Changed += OnLanguageChanged;
            _turnLabel = _root.Q<Label>("turnLabel");
            _seatList = _root.Q("seatList");
            _selectionPanel = _root.Q("selectionPanel");
            _selectionTitle = _root.Q<Label>("selectionTitle");
            _selectionDetail = _root.Q<Label>("selectionDetail");
            _actions = _root.Q("actions");
            _status = _root.Q<Label>("statusLabel");
            _gamePanel = _root.Q("gamePanel");
            _gameLog = _root.Q<Label>("gameLog");
            _gameRole = _root.Q<Label>("gameRole");
            _sleepOverlay = _root.Q("sleepOverlay");
            _sleepText = _root.Q<Label>("sleepText");
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
                _emoteWheel = new EmoteWheel(_root, emote =>
                {
                    _session?.Emote(emote.ToString());
                    Gesture(emote.ToString());
                });
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
            _diceTotals = new DiceTotalsView(_root, _table);
            _seatJoins = new SeatJoinView(_root, _table, () => _table.Game is { State: { HasRules: true }, Session: null },
                SitterLabel, CanClickSeat, ClickSeat);
            if (_root.Q<Button>("calculatorButton") is { } calculator)
                calculator.clicked += AddCalculator;
            _showcase = new ShowcaseView(_root);
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

        private UiLocalizer _localizer;

        /// <summary>The interface's language changed: the UXML's texts, the game bar and the settings page are drawn again.</summary>
        private void OnLanguageChanged()
        {
            _localizer?.Apply();
            _shownGameKey = null;
            _escape?.Redraw();
        }

        private void ApplySettings(GameSettings settings)
        {
            Ui.SetLanguage(settings.Language);
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
            Ui.Changed -= OnLanguageChanged;
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
            _diceTotals?.Update(Camera.main);
            _seatJoins?.Update(Camera.main);
            // A game's bottom panels stand above the hand while it shows cards, never over them.
            _root.EnableInClassList("hand-showing", _handBar != null && _handBar.Showing);
            _showcase?.Update(_table.Assets);
            FaceLocalSeat();
            RefreshBlindRound();
            RefreshSelection();
            _overlay?.Update();
            _handBar?.Update();
            _escape?.PollKeyCapture();
            if (_agent != null && _agent.Visible)
                _agent.Update();
            MenuKeys();
            // While a calculator is selected, Enter is its = key, not the chat.
            _chat?.Update(TypingInUi() || (_interaction != null && (_interaction.IgnoreDevices || _interaction.Selected?.Model is Calculator)));
            _players?.Update();
            _notes?.Update();
            _reader?.Update();
            _emoteWheel?.Update(TypingInUi() || (_interaction != null && _interaction.IgnoreDevices));
            UpdateTalking();
            if (_saves?.Tick(_escape.Settings.AutosaveMinutes) is { } autosaved)
                ShowStatus(Ui.T("Autosaved ({autosaved})", ("autosaved", autosaved)), error: false);
            UpdatePresenceLabels();
            if (_netStatus != null && _session != null)
                _netStatus.text = _session.Status == "offline" ? "" : _session.Status;
            // K: a wink at the face this player looks at, seen by whoever looks back (ADR 0012 §6).
            if (UnityEngine.InputSystem.Keyboard.current is { } keys && !TypingInUi() && KeyBindings.Pressed(keys, GameAction.Wink))
            {
                _session?.Emote(nameof(TableWorld.View.Chalkers.ChalkerEmote.Wink));
                Gesture("wink");
            }
            if (Time.unscaledTime >= _nextGameRefresh)
            {
                _nextGameRefresh = Time.unscaledTime + 0.25f;
                RefreshSleep();
                TickVotes();
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
                ShowStatus(Ui.Refusal(result.Error), error: true);
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
            _blindButton.text = blind == null ? Ui.T("Blind round") : Ui.T("Reveal ({count})", ("count", blind.MovedBy.Count));
            _blindButton.tooltip = blind == null
                ? Ui.T("Start a blind round: until the reveal, what each player moves stays hidden from the others.")
                : Ui.T("Reveal everything moved in this blind round.");
        }

        /// <summary>
        /// Start: one button that plays the seats people sat at (the "+" over each seat on the table), so the player
        /// count is who sat down. A table keeps all its seats; the empty ones sit the game out.
        /// </summary>
        private void AddStartButtons(TableState state)
        {
            AddOptionalRoles(state);
            AddLanguages(state);
            var seated = SeatedSeats();
            var min = Mathf.Max(1, state.Game?.MinPlayers ?? 1);
            var max = state.Game?.MaxPlayers > 0 ? state.Game.MaxPlayers : state.Players.Seats.Count;
            var button = new Button(() =>
            {
                var roles = state.Roles?.List.Where(r => r.Optional && _optionalRoles.Contains(r.Id)).Select(r => r.Id).ToList() ?? new List<string>();
                var result = _table.Execute(new StartGameCommand { Seats = seated, Roles = roles, Language = _startLanguage ?? (Translations.Languages(state).Count == 0 ? Ui.Language : null) });
                if (!result.Ok)
                    ShowStatus(Ui.Refusal(result.Error), error: true);
            }) { text = seated.Count == 1 ? Ui.T("Start game (1 player)") : Ui.T("Start game ({count} players)", ("count", seated.Count)) };
            button.AddToClassList("hud-button");
            var fits = seated.Count >= min && seated.Count <= max;
            button.SetEnabled(fits);
            var takes = min == max ? $"{min}" : Ui.T("{min} to {max}", ("min", min), ("max", max));
            button.tooltip = fits ? Ui.T("Sit at more seats with the + over each seat on the table")
                : Ui.T("This game takes {takes} players: sit at the seats with the + over each one on the table", ("takes", takes));
            _gameActions.Add(button);
        }

        /// <summary>
        /// A game in several languages (ADR 0011): one toggle per language before the start, each named in its own
        /// language; the host picks the one everyone reads. The game's own language is picked until then.
        /// </summary>
        private void AddLanguages(TableState state)
        {
            var languages = Translations.Languages(state);
            if (languages.Count < 2)
            {
                _startLanguage = null;
                return;
            }
            if (_startLanguage == null || !languages.Contains(_startLanguage))
                _startLanguage = languages[0];
            foreach (var language in languages)
            {
                var code = language;
                var toggle = new Button(() =>
                {
                    _startLanguage = code;
                    _shownGameKey = null;
                }) { text = Strings.For(code).Raw("language.name") is { } own && Strings.BuiltIn().Contains(code) ? own : code };
                toggle.AddToClassList("hud-button");
                toggle.EnableInClassList("hud-button--picked", code == _startLanguage);
                toggle.tooltip = Ui.T("The language everyone reads this game in.");
                _gameActions.Add(toggle);
            }
        }

        /// <summary>
        /// One toggle per optional role (Avalon's Percival, Morgana) before the start: the host picks which join the deal.
        /// </summary>
        private void AddOptionalRoles(TableState state)
        {
            if (_optionalRolesFor != _table.Engine)
            {
                _optionalRolesFor = _table.Engine;
                _optionalRoles.Clear();
            }
            foreach (var role in state.Roles?.List.Where(r => r.Optional) ?? Enumerable.Empty<RoleDef>())
            {
                var id = role.Id;
                var on = _optionalRoles.Contains(id);
                var toggle = new Button(() =>
                {
                    if (!_optionalRoles.Remove(id))
                        _optionalRoles.Add(id);
                    _shownGameKey = null;
                }) { text = GameEngine.RoleName(state, state.Roles, id) };
                toggle.AddToClassList("hud-button");
                toggle.EnableInClassList("hud-button--picked", on);
                toggle.tooltip = (on ? Ui.T("In the deal: click to leave it out.") : Ui.T("Left out: click to deal it too.")) +
                                 (role.Description is { Length: > 0 } about ? "\n" + about : "");
                _gameActions.Add(toggle);
            }
        }

        /// <summary>
        /// A vote in progress (ADR 0012): how many have voted, and this player's own vote, until the count shows the result.
        /// </summary>
        private List<string> VoteLines(GameEngine game)
        {
            var lines = new List<string>();
            var session = game.Session;
            if (session is not { Started: true, Over: false })
                return lines;
            foreach (var action in game.State.Actions.Where(a => a.Vote != null))
            {
                var cast = session.Secrets.Where(s => s.Action == action.Id).ToList();
                if (cast.Count == 0)
                    continue;
                var voters = game.VotersOf(action).Count;
                // A private vote shows nobody how many have voted (this machine only ever sees its own ballot).
                var actionName = Names.Action(_table.Engine.State, action);
                var line = action.Vote.Private ? Ui.T("{actionName}: voted", ("actionName", actionName)) : Ui.T("{actionName}: {count} of {voters} voted", ("actionName", actionName), ("count", cast.Count), ("voters", voters));
                if (cast.FirstOrDefault(s => s.Seat == _table.LocalSeat) is { } mine && mine.Params.TryGetValue(GameEngine.VoteParam, out var choice))
                    line += Ui.T(" (you: {optionLabel})", ("optionLabel", game.OptionLabel(action.Id, GameEngine.VoteParam, choice)));
                if (_voteLeft.TryGetValue(action.Id, out var left))
                    line += Ui.T(" · {left} s left", ("left", left));
                lines.Add(line);
            }
            return lines;
        }

        /// <summary>When each vote with a time limit got its first ballot here (unscaled seconds), and its whole seconds left.</summary>
        private readonly Dictionary<string, float> _voteOpened = new Dictionary<string, float>();
        private readonly Dictionary<string, int> _voteLeft = new Dictionary<string, int>();
        private readonly HashSet<string> _voteClosing = new HashSet<string>();

        /// <summary>
        /// Votes with a time limit (<c>vote.seconds</c>): the clock starts at the first ballot this machine sees. The host
        /// (or a table played alone) closes the vote when it runs out; everyone else only shows the time left.
        /// </summary>
        private void TickVotes()
        {
            var game = _table.Game;
            var session = game?.Session;
            foreach (var action in game?.State.Actions.Where(a => a.Vote?.Seconds != null) ?? Enumerable.Empty<ActionDef>())
            {
                var open = session is { Started: true, Over: false } && session.Secrets.Any(s => s.Action == action.Id);
                if (!open)
                {
                    _voteOpened.Remove(action.Id);
                    _voteLeft.Remove(action.Id);
                    _voteClosing.Remove(action.Id);
                    continue;
                }
                if (!_voteOpened.TryGetValue(action.Id, out var opened))
                    _voteOpened[action.Id] = opened = Time.unscaledTime;
                var left = Mathf.Max(0, Mathf.CeilToInt(action.Vote.Seconds.Value - (Time.unscaledTime - opened)));
                _voteLeft[action.Id] = left;
                if (left == 0 && _table.HostsTable && _voteClosing.Add(action.Id))
                    _table.Execute(new CloseVoteCommand { Action = action.Id });
            }
        }

        /// <summary>What the role line shows changes with: this seat's role and how many seats it knows about.</summary>
        private string RoleKey(Session session) =>
            session == null || _table.LocalSeat == null || !session.Roles.TryGetValue(_table.LocalSeat, out var mine)
                ? ""
                : mine + "/" + (session.Knows.TryGetValue(_table.LocalSeat, out var sights) ? sights.Count : 0);

        // ---- sitting down before the start: the + over each seat ------------------------------------------------

        /// <summary>The seats people sat at, in seat order: online, where each person sits; hot seat, the seats taken here.</summary>
        /// <summary>Seats this window sat at at a hot-seat table (each shows a chalker); none online, where each player brings their own.</summary>
        public IEnumerable<string> HotSeats() => _table.Networked ? Enumerable.Empty<string>() : SeatedSeats();

        public List<string> SeatedSeats()
        {
            var seats = _table.Engine?.State.Players.Seats ?? new List<Seat>();
            if (_table.Networked && _session != null)
                return seats.Where(s => _session.Occupants.ContainsKey(s.Id)).Select(s => s.Id).ToList();
            if (_joinedFor != _table.Engine)
            {
                // A new table: whoever plays here sits at the first seat; the others are joined with their +.
                _joinedFor = _table.Engine;
                _joined.Clear();
                if (seats.FirstOrDefault() is { } first)
                    _joined.Add(first.Id);
            }
            return seats.Where(s => _joined.Contains(s.Id)).Select(s => s.Id).ToList();
        }

        /// <summary>Who sits at a seat as its marker says it ("Red (you)", a player's name), or null when it is free.</summary>
        private string SitterLabel(Seat seat)
        {
            var name = Ui.SeatName(_table.Engine.State, seat);
            if (_table.Networked && _session != null)
            {
                if (seat.Id == _table.LocalSeat)
                    return Ui.T("{name} (you)", ("name", name));
                return _session.Occupants.TryGetValue(seat.Id, out var who) ? _session.NameOf(who) ?? name : null;
            }
            return SeatedSeats().Contains(seat.Id) ? name : null;
        }

        /// <summary>A free seat can be sat at; at a hot-seat table a taken one can also be left.</summary>
        private bool CanClickSeat(Seat seat)
        {
            if (_table.Networked && _session != null)
                return !_session.Occupants.ContainsKey(seat.Id);
            return true;
        }

        /// <summary>
        /// The + over a seat: online it moves you there (the host decides); at a hot-seat table it adds the seat to the
        /// players, or takes it away again.
        /// </summary>
        public void ClickSeat(string seat)
        {
            if (_table.Networked && _session != null)
            {
                SitAt(seat);
                return;
            }
            SeatedSeats();
            if (!_joined.Remove(seat))
                _joined.Add(seat);
            _shownGameKey = null;
        }

        /// <summary>
        /// The tool bar's Calculator: a calculator put on the table in front of your seat, facing you (press its keys, or
        /// type while it is selected). Everyone sees its display.
        /// </summary>
        private void AddCalculator()
        {
            var state = _table.Engine.State;
            var seat = state.Players.Seats.FirstOrDefault(s => s.Id == _table.LocalSeat);
            var hand = seat == null ? null : state.Zones.FirstOrDefault(z => z.Id == seat.HandZone);
            var place = new SeatLayout.Place { Position = hand?.Transform.Position ?? new Vec3(0f, 0f, -0.3f), Yaw = hand?.Transform.Rotation.Y ?? 0f };
            var at = hand != null ? SeatLayout.InFrontOf(place, along: 0.24f, inward: 0.2f) : new Vec3(0.3f, 0f, -0.15f);
            var result = _table.Execute(new AddCalculatorCommand { Position = at, Yaw = place.Yaw + CalculatorYawOffset, Actor = _table.LocalSeat });
            if (!result.Ok)
                ShowStatus(Ui.Refusal(result.Error), error: true);
            else if (_table.ViewOf(result.CreatedId) is { } placed)
                _interaction?.Select(placed);
        }

        /// <summary>Turn that makes a calculator's face read upright for the seat whose hand has yaw 0 (south).</summary>
        public const float CalculatorYawOffset = 0f;

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
        /// <summary>
        /// Eyes closed (ADR 0012): while the phase keeps this player asleep, the table is dark behind the phase's words. A
        /// spectator, and a host without a seat, keep watching.
        /// </summary>
        private void RefreshSleep()
        {
            var game = _table.Game;
            var seat = _table.Spectating ? null : _table.LocalSeat;
            var asleep = game != null && seat != null && game.EyesClosedPhase && !game.IsAwake(seat, seat);
            _sleepOverlay?.EnableInClassList(Hidden, !asleep);
            if (asleep && _sleepText != null)
                _sleepText.text = game.SleepText;
        }

        /// <summary>
        /// A gesture the rules may react to (ADR 0012 §6): aimed at the face this player looks at. Only sent when the game
        /// has an onGesture event, so ordinary emotes stay presence only.
        /// </summary>
        public void Gesture(string gesture)
        {
            var game = _table.Game;
            if (game?.Session is not { Started: true, Over: false } || !game.State.Events.ContainsKey(GameEvents.OnGesture) || _table.LocalSeat == null)
                return;
            var chalkers = FindAnyObjectByType<TableWorld.View.Chalkers.ChalkersView>();
            var target = chalkers != null ? chalkers.LookedAt(Camera.main) : null;
            var result = _table.Execute(new GestureCommand { Actor = _table.LocalSeat, Gesture = gesture.ToLowerInvariant(), Target = target });
            if (!result.Ok)
                ShowStatus(Ui.Refusal(result.Error), error: true);
        }

        /// <summary>Who won, as players say it: a team's name, a seat's, or nobody.</summary>
        private static string WinnerName(TableState state, Session session) =>
            session.WinnerTeam is { } team && state.Roles != null ? GameEngine.TeamName(state, state.Roles, team)
            : session.Winner != null ? Names.Seat(state, session.Winner) : "nobody";

        /// <summary>A vote with up to this many options shows one button per option.</summary>
        private const int MaxVoteButtons = 4;

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
            // Another table loaded (even one that looks the same) redraws: its toggles and buttons are its own.
            var key = $"{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(game)}|{state.HasRules}|{session?.Turn}|{session?.Phase}|{session?.CurrentSeat}|{session?.Over}|{session?.Log.Count}|" +
                      $"{turns?.Current}|{turns?.Count}|{turns?.Reverse}|{_table.LocalSeat}|{string.Join(",", SeatedSeats())}|" +
                      $"{RoleKey(session)}|{string.Join(",", _optionalRoles)}|{_startLanguage}|{session?.Secrets.Count}|{string.Join(",", _voteLeft.Values)}|" +
                      string.Join(",", legal.Select(l => $"{l.Seat}/{l.Action.ActionId}/{l.Action.Choices.Count}"));
            if (key == _shownGameKey)
                return;
            _shownGameKey = key;

            AnnounceTurn();
            _turnLabel.text = !state.HasRules ? (turns is { Enabled: true } ? FreeTurnText(turns) : Ui.T("Free play"))
                : session == null ? Ui.T("{game}: not started", ("game", state.Game != null ? Names.Game(state) : Ui.T("Game")))
                : session.Over ? (session.WinnerTeam != null ? Ui.T("Game over: {winner} win", ("winner", WinnerName(state, session))) : Ui.T("Game over: {winner} wins", ("winner", WinnerName(state, session))))
                : Ui.T("Turn {turn} · {phaseName} · {currentSeat}", ("turn", session.Turn), ("phaseName", PhaseName(state, session.Phase)), ("currentSeat", Names.Seat(state, session.CurrentSeat)));
            // A long turn line is cut short in the bar; the whole of it is on hover.
            _turnLabel.tooltip = _turnLabel.text;

            _gameActions.Clear();
            if (!state.HasRules)
            {
                // Free-play turns (G22): the seat whose turn it is ends it (any seat at a hot-seat table).
                var canEnd = turns is { Enabled: true } && (!_table.Networked || turns.Current == _table.LocalSeat);
                _gamePanel.EnableInClassList(Hidden, !canEnd);
                _gameLog.AddToClassList(Hidden);
                if (canEnd)
                    AddGameButton(Ui.T("End turn ({current})", ("current", turns.Current)), () => _table.Execute(new EndTurnCommand { Actor = _table.Networked ? _table.LocalSeat : null }));
                return;
            }
            _gamePanel.RemoveFromClassList(Hidden);
            var role = _table.LocalSeat == null ? new List<string>() : GameEngine.RoleLines(state, _table.LocalSeat);
            role.AddRange(VoteLines(game));
            _gameRole.text = string.Join("\n", role);
            _gameRole.EnableInClassList(Hidden, role.Count == 0);
            _gameLog.text = LatestLogLines(session);
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
                var name = state.Actions.FirstOrDefault(a => a.Id == action.ActionId) is { } def ? Names.Action(state, def) : action.Name ?? Names.Humanize(action.ActionId);
                var actor = seat;
                var id = action.ActionId;
                void Perform(Dictionary<string, string> parameters)
                {
                    var result = _table.Execute(new PerformActionCommand { Actor = actor, ActionId = id, Params = new Dictionary<string, string>(parameters) });
                    if (!result.Ok)
                        ShowStatus(Ui.Refusal(result.Error), error: true);
                }
                if (action.Choices.Count == 1)
                {
                    var only = action.Choices[0];
                    AddGameButton(only.Count == 0 ? name : $"{name} · {ChoiceLabel(id, only)}", () => Perform(only));
                    continue;
                }
                // A vote's few options are one click each (approve, reject), not a picker.
                if (state.Actions.FirstOrDefault(a => a.Id == id)?.Vote != null && action.Choices.Count <= MaxVoteButtons)
                {
                    foreach (var option in action.Choices)
                    {
                        var picked = option;
                        AddGameButton($"{name} · {ChoiceLabel(id, picked)}", () => Perform(picked));
                    }
                    continue;
                }
                var choices = action.Choices;
                AddGameButton($"{name} ({choices.Count})", () => _picker.Show(name, choices.Select(c => new ActionPicker.Choice
                {
                    Label = ChoiceLabel(id, c), Image = ChoiceImage(c), Pick = () => Perform(c),
                }).ToList()));
            }
        }

        /// <summary>What players read for a choice: the options' labels, or the chosen objects' names.</summary>
        private string ChoiceLabel(string actionId, Dictionary<string, string> choice) =>
            string.Join(", ", choice.Select(p => _table.Game.OptionLabel(actionId, p.Key, p.Value)));

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
            string NameOf(string id) => _table.Engine.Find(id) is { } o ? (o.Name != null ? Names.Of(_table.Engine.State, o) : o.Type) : id;
            return command switch
            {
                SetCounterCommand c => $"{NameOf(c.ObjectId)}: {(_table.Engine.Find(c.ObjectId) as Counter)?.Value}",
                SetClockCommand c => _table.Engine.Find(c.ObjectId) is Clock clock
                    ? $"{NameOf(c.ObjectId)}: {(clock.Mode == ClockMode.Clock ? "clock" : clock.Running ? "running" : clock.Mode == ClockMode.Timer && clock.Seconds <= 0f ? "time's up" : "stopped")}"
                    : null,
                SetGridCommand { Grid: null } => Ui.T("Grid: none"),
                SetGridCommand c => Ui.T(c.Grid.Shape == GridShape.Hex ? "Grid: hexes of {size} cm" : "Grid: squares of {size} cm", ("size", string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.#}", c.Grid.SizeX * 100f))) +
                                     (c.Grid.Snapping != GridSnapping.Off ? Ui.T(", snapping to {snap}", ("snap", Ui.T(c.Grid.Snapping.ToString().ToLowerInvariant()))) : "") +
                                     (c.Grid.Lines ? "" : Ui.T(", lines hidden")),
                SetTurnOrderCommand { Enabled: false } => Ui.T("Turns: off"),
                SetTurnOrderCommand c => Ui.T("Turns: on") + (c.Reverse ? Ui.T(", reversed") : "") + (c.SkipEmptyHands ? Ui.T(", skipping empty hands") : "")
                                         + " · " + Ui.T("{seat}'s turn", ("seat", Names.Seat(_table.Engine.State, turns?.Current))),
                EndTurnCommand _ => Ui.T("{seat}'s turn", ("seat", Names.Seat(_table.Engine.State, turns?.Current))),
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
            (turns.Current == _table.LocalSeat && _table.Networked ? Ui.T("Your turn") : Ui.T("{current}'s turn", ("current", turns.Current))) +
            Ui.T(" · turn {value}", ("value", turns.Count + 1)) + (turns.Reverse ? Ui.T(" · reversed") : "");

        /// <summary>Online, a player whose turn just came is told so (TTS flashes "your turn").</summary>
        private void AnnounceTurn()
        {
            var current = CurrentTurnSeat();
            if (current == _announcedTurn)
                return;
            _announcedTurn = current;
            if (current != null && _table.Networked && current == _table.LocalSeat)
                ShowStatus(Ui.T("Your turn"), error: false);
        }

        private static string PhaseName(TableState state, string phase) => Names.Phase(state, phase);

        /// <summary>The lines the latest change added to the game log (the last <see cref="MaxLogLines"/>), or the last line.</summary>
        private string LatestLogLines(Session session)
        {
            if (session == null)
                return "";
            if (_logSession != session || session.Log.Count < _logShown)
            {
                _logSession = session;
                _logShown = session.Log.Count;
                return session.Log.LastOrDefault() ?? "";
            }
            if (session.Log.Count == _logShown)
                return _gameLog.text;
            var added = session.Log.Skip(_logShown).ToList();
            _logShown = session.Log.Count;
            return string.Join("\n", added.Skip(Math.Max(0, added.Count - MaxLogLines)));
        }

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
                var suffix = seat.Id == local ? " " + Ui.T("(you)") : sitter == TableWorld.View.Net.NetworkTableSession.HostIdForUi ? " " + Ui.T("(host)") : takenByOther && who == null ? " " + Ui.T("(taken)") : "";
                var name = new Label((who ?? Ui.SeatName(_table.Engine.State, seat)) + suffix) { pickingMode = PickingMode.Ignore, enableRichText = false };
                name.AddToClassList("seat-name");
                chip.Add(swatch);
                chip.Add(name);
                var seatName = Ui.SeatName(_table.Engine.State, seat);
                chip.tooltip = takenByOther ? Ui.T("{value} sits at {seatName}", ("value", who ?? Ui.T("Someone")), ("seatName", seatName)) : seat.Id == local ? Ui.T("Your seat ({seatName})", ("seatName", seatName)) : Ui.T("Click to sit at {seatName}", ("seatName", seatName));
                var id = seat.Id;
                if (!takenByOther && seat.Id != local)
                    chip.RegisterCallback<ClickEvent>(_ => SitAt(id));
                _seatList.Add(chip);
            }
            if (online)
            {
                var watch = new VisualElement { tooltip = Ui.T("Leave your seat and watch") };
                watch.AddToClassList("seat-chip");
                watch.AddToClassList(_table.Spectating ? "seat-chip--local" : "seat-chip--free");
                var label = new Label(_table.Spectating ? Ui.T("Watching (you)") : Ui.T("Watch")) { pickingMode = PickingMode.Ignore };
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
            _selectionTitle.text = selected.HideFace ? Ui.T("A card in another player's hand") : TableOverlay.DisplayName(model);
            _selectionDetail.text = selected.HideFace ? "" : Describe(model);

            AddAction(model.Locked ? Ui.T("Unlock (L)") : Ui.T("Lock (L)"), () => _interaction.ToggleLock(selected));
            if (model.Locked)
            {
                AddReadoutActions(selected);
                return;
            }
            AddAction(Ui.T("Flip (F)"), () => _interaction.Flip(selected));
            switch (model)
            {
                case Die _:
                    AddAction(Ui.T("Roll (R)"), () => _interaction.RollOrShuffle(selected));
                    break;
                case Deck deck when deck.Cards.Count > 0:
                    AddAction(Ui.T("Shuffle (R)"), () => _interaction.RollOrShuffle(selected));
                    AddAction(Ui.T("Deal 1 to me"), () => _interaction.DealToLocalSeat(selected, 1));
                    AddAction(Ui.T("Deal 5 to me"), () => _interaction.DealToLocalSeat(selected, 5));
                    AddAction(Ui.T("Deal 1 to all"), () => _table.Execute(new DealCommand { DeckId = selected.Id, Count = 1, Actor = _table.LocalSeat }));
                    break;
                case Bag bag when !(bag is InfiniteBag):
                    AddAction(Ui.T("Shuffle (R)"), () => _interaction.RollOrShuffle(selected));
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
                    AddAction(Ui.T("Reset"), () => _table.Execute(new SetCounterCommand { ObjectId = selected.Id, Value = 0, Actor = _table.LocalSeat }));
                    break;
                case Clock { Mode: not ClockMode.Clock } clock:
                    AddAction(clock.Running ? Ui.T("Pause") : Ui.T("Start"), () => _interaction.StartOrPause(selected));
                    AddAction(Ui.T("Reset"), () => _interaction.ResetClock(selected));
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
            Deck deck => Ui.T("Deck · {count} cards · {facing}", ("count", deck.Cards.Count), ("facing", deck.IsFaceUp ? Ui.T("face up") : Ui.T("face down"))),
            Card card => Ui.T("Card · {facing}", ("facing", card.IsFaceUp ? Ui.T("face up") : Ui.T("face down"))),
            Die die => Ui.T("d{faces} · {shown}", ("faces", die.Faces), ("shown", die.Value is { } value ? Ui.T("shows {face}", ("face", die.Face(value))) : Ui.T("not rolled"))),
            Bag bag => Ui.T("{type} · {contentsOf} inside", ("type", model.Type), ("contentsOf", _table.Engine.ContentsOf(bag.Id).Count())),
            _ => model.Type,
        } + (model.Locked ? Ui.T(" · locked") : "");

        private void AddAction(string text, Action action)
        {
            var button = new Button(action) { text = text };
            button.AddToClassList("hud-button");
            _actions.Add(button);
        }

        private void OnCommand(Command command, CommandResult result)
        {
            // A card shown to everyone (by a player, a rule or a script) comes up big on every screen.
            if (result.Ok && result.Events.OfType<CardShown>().LastOrDefault() is { } shown)
                _showcase?.Show(shown.Card, Names.Message(_table.Engine.State, "shows", ("seat", Names.Seat(_table.Engine.State, shown.Seat)), ("card", Names.Of(_table.Engine.State, shown.Card))));
            // What table scripts do is theirs to tell (chat, their own UI); their refusals are in the log.
            if (command.ByScript)
                return;
            // Moves and strokes are too frequent for the status line (strokes are in the game log).
            if (result.Ok && (command is MoveCommand || command is PressCalculatorKeyCommand || command is AddDrawingCommand || command is EraseDrawingCommand ||
                              command is DropCommand || command is PickUpCommand || command is PeekCommand || command is BlindfoldCommand ||
                              command is SetButtonsCommand || command is ClickButtonCommand || command is SetScriptUiCommand ||
                              command is SetUiAttributesCommand || command is UiEventCommand || command is SetInputsCommand ||
                              command is TypeInputCommand || command is SpawnCommand))
                return;
            // In words, never the type's name: "SetLightCommand" reads "Set light".
            var words = System.Text.RegularExpressions.Regex.Replace(command.GetType().Name.Replace("Command", ""), "(?<=[a-z])(?=[A-Z])", " ").ToLowerInvariant();
            var name = char.ToUpperInvariant(words[0]) + words.Substring(1);
            _status.text = result.Ok ? (command is RollCommand ? RollText(result) : command is SetPhysicsCommand physics ? Ui.T("Physics: {physicsName}", ("physicsName", PhysicsName(physics.Mode))) : TurnText(command) ?? Ui.T("{name}: done", ("name", name)))
                : Ui.T("{name} rejected: {message} {suggestion}", ("name", name), ("message", result.Error.Message), ("suggestion", result.Error.Suggestion));
            _status.EnableInClassList("status-label--error", !result.Ok);
            _status.RemoveFromClassList(Hidden);
            _shownSelectionHash = null;
        }

        public static string PhysicsName(PhysicsMode mode) => mode switch
        {
            PhysicsMode.SemiLock => Ui.T("semi-lock (nothing tips over)"),
            PhysicsMode.Locked => Ui.T("locked (objects stay where they are put)"),
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
                return Ui.T("Rolled");
            if (_rolls.Count == 1)
                return Ui.T("Rolled {value} (d{value2})", ("value", _rolls[0].Face), ("value2", _rolls[0].Faces));
            var list = Ui.T("Rolled {faces}", ("faces", string.Join(", ", _rolls.Select(r => $"{r.Face} (d{r.Faces})"))));
            // Labelled faces (G21) do not add up.
            return _rolls.All(r => r.Numeric) ? Ui.T("{list} · total {value}", ("list", list), ("value", _rolls.Sum(r => r.Value))) : list;
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
            _gamesPanel.Q<Label>("gamesHint").text = Ui.T("Your own games: put a .json file, or a folder with table.json and its images, in {folder}", ("folder", GameLibrary.UserFolder));
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
                list.Add(ImportAction(Ui.T("Download its {count} missing files", ("count", missing.Count)), () => DownloadMissing(entry.Name, folder, missing)));
            if (Core.Tts.TtsPackage.Outdated(folder) && Core.Tts.TtsPackage.SourceOf(folder, Core.Tts.TtsPackage.DefaultFolder) is { } source)
                list.Add(ImportAction(Ui.T("Re-import it (made by an older importer)"), () => Reimport(entry.Name, source, folder)));
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
            var progress = new System.Progress<(int Done, int Total)>(p => ShowStatus(Ui.T("Downloading {name}: {done} of {total} files", ("name", name), ("done", p.Done), ("total", p.Total)), error: false));
            try
            {
                var result = await new Core.Tts.AssetDownloader().Download(files, progress);
                var failed = result.Failed.Count > 0 ? Ui.T("; {count} could not be fetched (listed in the log)", ("count", result.Failed.Count)) : "";
                foreach (var (url, reason) in result.Failed.Take(50))
                    Debug.LogWarning($"[Import] {url}: {reason}");
                ShowStatus(Ui.T("{name}: {downloaded} files downloaded ({value} MB){failed}. Open it from Games to see them.", ("name", name), ("downloaded", result.Downloaded), ("value", result.Bytes / (1024 * 1024)), ("failed", failed)), error: false);
            }
            catch (System.Exception e)
            {
                ShowStatus(Ui.T("Downloading {name} stopped: {message}", ("name", name), ("message", e.Message)), error: true);
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
            var title = new Label(Ui.T("Import from Tabletop Simulator"));
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
            ShowStatus(Ui.T("Importing {name}…", ("name", save.Name)), error: false);
            var target = System.IO.Path.Combine(GameLibrary.UserFolder, save.FolderName);
            try
            {
                // Big saves and their files take seconds: off the main thread, back here to report.
                var report = await System.Threading.Tasks.Task.Run(() => Core.Tts.TtsPackage.Write(save.File, ttsFolder, target));

                var missing = report.Missing.Count > 0 ? Ui.T("; {count} files not in the TTS cache (they show as placeholders)", ("count", report.Missing.Count)) : "";
                ShowStatus(Ui.T("Imported {name}: {objects} objects, {copied} files{missing}. It is in Games.", ("name", save.Name), ("objects", report.Objects), ("copied", report.Copied), ("missing", missing)), error: false);
                if (!_gamesPanel.ClassListContains(Hidden))
                {
                    _gamesPanel.AddToClassList(Hidden);
                    ToggleGames();
                }
            }
            catch (System.Exception e)
            {
                ShowStatus(Ui.T("Could not import {name}: {message}", ("name", save.Name), ("message", e.Message)), error: true);
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
            ShowStatus(Ui.T("Re-importing {name}…", ("name", name)), error: false);
            try
            {
                var report = await System.Threading.Tasks.Task.Run(() => Core.Tts.TtsPackage.Write(source, Core.Tts.TtsPackage.DefaultFolder, folder));
                var missing = report.Missing.Count > 0 ? Ui.T("; {count} files still missing (Games offers to download them)", ("count", report.Missing.Count)) : "";
                ShowStatus(Ui.T("Re-imported {name}: {objects} objects{missing}.", ("name", name), ("objects", report.Objects), ("missing", missing)), error: false);
            }
            catch (System.Exception e)
            {
                ShowStatus(Ui.T("Could not re-import {name}: {message}", ("name", name), ("message", e.Message)), error: true);
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
                ShowStatus(Ui.T("Only the host can open a game"), error: true);
                return;
            }
            string json;
            try
            {
                json = System.IO.File.ReadAllText(entry.File);
            }
            catch (System.IO.IOException e)
            {
                ShowStatus(Ui.T("Could not read {file}: {message}", ("file", entry.File), ("message", e.Message)), error: true);
                return;
            }
            _gamesPanel.AddToClassList(Hidden);
            StartCoroutine(Open());

            System.Collections.IEnumerator Open()
            {
                yield return _table.LoadInSteps(json, entry.PackageRoot, entry.Name);
                var ok = _table.LastLoadOk;
                ShowStatus(ok ? Ui.T("Opened {name}", ("name", entry.Name)) : Ui.T("{name} is not a valid game (details in the log)", ("name", entry.Name)), error: !ok);
                if (!ok)
                    _gamesPanel.RemoveFromClassList(Hidden);
            }
        }

        private void Save()
        {
            var hash = _table.Save(TableView.SavePath(TableView.QuickSaveName));
            ShowStatus(Ui.T("Saved quicksave ({substring})", ("substring", hash.Substring(7, 8))), error: false);
        }

        private void Load() =>
            _table.LoadFileInSteps(TableView.SavePath(TableView.QuickSaveName), Ui.T("Quicksave"),
                ok => ShowStatus(ok ? Ui.T("Loaded quicksave") : Ui.T("No quicksave yet"), error: !ok));

        private void ShowStatus(string text, bool error)
        {
            _status.text = text;
            _status.EnableInClassList("status-label--error", error);
            _status.RemoveFromClassList(Hidden);
        }
    }
}
