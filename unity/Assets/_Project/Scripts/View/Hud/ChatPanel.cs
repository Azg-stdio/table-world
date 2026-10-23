using System.Collections.Generic;
using System.Linq;
using Core.Net;
using UnityEngine;
using UnityEngine.UIElements;

namespace TableWorld.View.Hud
{
    /// <summary>
    /// Chat and game log (G11), bottom left like TTS. Closed, it shows only the last few lines for a while, then fades
    /// to a hint; Enter (or a click) opens it with the whole scrollback and the input, Enter sends and closes, Escape
    /// closes. Two tabs: Chat (messages, rolls, notices, rules messages) and Log (that plus every action). Player text
    /// is shown as typed: rich text is off, so a message cannot restyle the HUD. Styling lives in Hud.uss (chat-*).
    /// </summary>
    public sealed class ChatPanel
    {
        private const string Hidden = "hidden";
        /// <summary>A closed chat shows lines this recent.</summary>
        public const float RecentSeconds = 12f;
        public const int RecentLines = 6;
        /// <summary>Lines drawn when open; older ones stay in the log but are not drawn (the list stays fast).</summary>
        public const int DrawnLines = 200;

        private readonly TableView _table;
        private readonly TableWorld.View.Net.NetworkTableSession _session;
        private readonly VisualElement _panel;
        private readonly ScrollView _list;
        private readonly TextField _input;
        private readonly Label _hint;
        private readonly Button _chatTab;
        private readonly Button _logTab;
        private readonly Dictionary<ChatEntry, float> _arrived = new Dictionary<ChatEntry, float>();
        private string _shownKey;
        private int _toggledFrame = -10;
        private bool _logTabOpen;

        /// <summary>True while the chat is open (scrollback and input).</summary>
        public bool Open { get; private set; }

        /// <summary>The Log tab is showing (every action), not the Chat tab.</summary>
        public bool ShowingLog => _logTabOpen;

        /// <summary>Lines drawn right now, as text (tests and autoplay reports read it).</summary>
        public IEnumerable<string> DrawnText => _list.Query<Label>(className: "chat-text").ToList().Select(l => l.text);

        public ChatPanel(VisualElement root, TableView table, TableWorld.View.Net.NetworkTableSession session)
        {
            _table = table;
            _session = session;
            _panel = root.Q("chatPanel");
            _list = root.Q<ScrollView>("chatList");
            _input = root.Q<TextField>("chatInput");
            _hint = root.Q<Label>("chatHint");
            _chatTab = root.Q<Button>("chatTab");
            _logTab = root.Q<Button>("logTab");
            _input.maxLength = ChatRoom.MaxLength;
            _chatTab.clicked += () => ShowTab(log: false);
            _logTab.clicked += () => ShowTab(log: true);
            _input.RegisterCallback<KeyDownEvent>(OnInputKey, TrickleDown.TrickleDown);
            _panel.RegisterCallback<FocusOutEvent>(_ => _panel.schedule.Execute(CloseIfFocusLeft));
            _panel.RegisterCallback<PointerDownEvent>(_ =>
            {
                if (!Open)
                    Show();
            });
            _panel.EnableInClassList(Hidden, session == null);
            if (session != null)
                session.Chat.Added += entry => _arrived[entry] = Time.unscaledTime;
        }

        /// <summary>Opens the chat and puts the cursor in the input.</summary>
        public void Show()
        {
            if (_session == null || Time.frameCount - _toggledFrame <= 1)
                return;
            _toggledFrame = Time.frameCount;
            Open = true;
            _shownKey = null;
            Refresh();
            // The input was hidden until this frame's styles resolve; a hidden field cannot take focus.
            _input.schedule.Execute(FocusInput);
            _list.schedule.Execute(ScrollToEnd);
        }

        /// <summary>Closes the chat (what was typed and not sent stays in the input).</summary>
        public void Hide()
        {
            if (!Open)
                return;
            _toggledFrame = Time.frameCount;
            Open = false;
            _input.Blur();
            _shownKey = null;
            Refresh();
        }

        /// <summary>Sends <paramref name="text"/> as this player (the input's Enter, and tests).</summary>
        public void Send(string text)
        {
            if (!string.IsNullOrWhiteSpace(text))
                _session?.Say(text);
        }

        public void ShowTab(bool log)
        {
            _logTabOpen = log;
            _shownKey = null;
            Refresh();
            if (Open)
                _input.schedule.Execute(FocusInput);
        }

        private void FocusInput()
        {
            if (Open)
                _input.Focus();
        }

        /// <summary>Enter opens the chat when nothing else takes the keyboard; called every frame by the HUD.</summary>
        public void Update(bool typingElsewhere)
        {
            if (_session == null)
                return;
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (!Open && !typingElsewhere && keyboard != null && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame))
                Show();
            else if (Open && keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                Hide();
            else if (Open && ClickedOutside())
                Hide();
            Refresh();
        }

        private void OnInputKey(KeyDownEvent e)
        {
            if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.character == '\n')
            {
                e.StopPropagation();
                if (e.keyCode == KeyCode.None)
                    return;
                Send(_input.value);
                _input.value = "";
                Hide();
            }
            else if (e.keyCode == KeyCode.Escape)
            {
                e.StopPropagation();
                Hide();
            }
        }

        /// <summary>A click anywhere but the open chat (the table, another panel) closes it, as leaving the input does.</summary>
        private bool ClickedOutside()
        {
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null || _panel.panel == null || !(mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame))
                return false;
            var screen = mouse.position.ReadValue();
            var point = RuntimePanelUtils.ScreenToPanel(_panel.panel, new Vector2(screen.x, Screen.height - screen.y));
            return !_panel.worldBound.Contains(point);
        }

        private void CloseIfFocusLeft()
        {
            var focused = _panel.panel?.focusController?.focusedElement as VisualElement;
            var inside = focused != null && (focused == _panel || _panel.Contains(focused));
            if (Open && !inside)
                Hide();
        }

        private bool Visible(ChatEntry entry) => _logTabOpen || entry.Kind != ChatKind.Action;

        /// <summary>Redraws when a line arrives, the tab changes, or (closed) a line grows old enough to fade.</summary>
        private void Refresh()
        {
            if (_session == null)
                return;
            var log = _session.Chat;
            var now = Time.unscaledTime;
            List<ChatEntry> lines;
            if (Open)
                lines = log.Entries.Where(Visible).ToList();
            else
                lines = log.Entries.Where(e => e.Kind != ChatKind.Action && _arrived.TryGetValue(e, out var at) && now - at < RecentSeconds).ToList();
            lines = lines.Skip(Mathf.Max(0, lines.Count - (Open ? DrawnLines : RecentLines))).ToList();
            var key = $"{Open}|{_logTabOpen}|{log.Version}|{lines.Count}|{(lines.Count > 0 ? lines[0].GetHashCode() : 0)}";
            if (key == _shownKey)
                return;
            var wasAtEnd = _list.verticalScroller.value >= _list.verticalScroller.highValue - 4f;
            _shownKey = key;

            _panel.EnableInClassList("chat-panel--open", Open);
            _chatTab.EnableInClassList("chat-tab--active", !_logTabOpen);
            _logTab.EnableInClassList("chat-tab--active", _logTabOpen);
            _hint.EnableInClassList(Hidden, Open || lines.Count > 0);
            _list.EnableInClassList(Hidden, !Open && lines.Count == 0);
            _list.Clear();
            foreach (var entry in lines)
                _list.Add(Row(entry));
            foreach (var gone in _arrived.Keys.Where(e => !log.Entries.Contains(e)).ToList())
                _arrived.Remove(gone);
            if (!Open || wasAtEnd)
                _list.schedule.Execute(ScrollToEnd);
        }

        private void ScrollToEnd() => _list.scrollOffset = new Vector2(0f, float.MaxValue);

        private VisualElement Row(ChatEntry entry)
        {
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList("chat-row");
            if (entry.Kind == ChatKind.Say || entry.Kind == ChatKind.Roll)
            {
                var from = new Label(entry.From) { pickingMode = PickingMode.Ignore, enableRichText = false };
                from.AddToClassList("chat-from");
                // The seat colour is game data (any colour a table picks), so it is set from code like the pointer labels.
                var seat = entry.Seat == null ? null : _table.Engine?.State.Players.Seats.FirstOrDefault(s => s.Id == entry.Seat);
                if (seat != null)
                    from.style.color = Readable(ZoneViewFactory.SeatColor(seat.Color));
                row.Add(from);
            }
            var text = new Label(entry.Text) { pickingMode = PickingMode.Ignore, enableRichText = false };
            text.AddToClassList("chat-text");
            text.AddToClassList("chat-text--" + entry.Kind.ToString().ToLowerInvariant());
            row.Add(text);
            return row;
        }

        /// <summary>Dark seat colours are lifted toward white so names read on the dark panel.</summary>
        private static Color Readable(Color color)
        {
            var luminance = 0.2126f * color.r + 0.7152f * color.g + 0.0722f * color.b;
            return luminance < 0.35f ? Color.Lerp(color, Color.white, 0.45f) : color;
        }
    }
}
