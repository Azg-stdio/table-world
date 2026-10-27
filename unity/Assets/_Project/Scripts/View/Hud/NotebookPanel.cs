using System.Linq;
using Core.Engine;
using Core.Rules;
using Core.Text;
using UnityEngine.UIElements;

namespace TableWorld.View.Hud
{
    /// <summary>
    /// The table's notebook (G25, TTS Notebook): pages listed on the left, the open page's title and text on the right.
    /// Edits are sent when a field loses focus. A page is for everyone or only for your seat's colour (the Game Master
    /// reads all). Opened with the Notes button. Styling lives in Hud.uss (notes-*).
    /// </summary>
    public sealed class NotebookPanel
    {
        private const string Hidden = "hidden";

        private readonly VisualElement _panel;
        private readonly ScrollView _tabs;
        private readonly TextField _title;
        private readonly TextField _text;
        private readonly Button _who;
        private readonly Button _delete;
        private readonly VisualElement _editor;
        private readonly TableView _table;
        private string _open;
        private string _shownKey;
        private bool _confirmDelete;

        public bool Visible => !_panel.ClassListContains(Hidden);

        /// <summary>The page being read or written (tests).</summary>
        public string OpenPage => _open;

        public NotebookPanel(VisualElement root, TableView table)
        {
            _table = table;
            _panel = root.Q("notesPanel");
            _tabs = root.Q<ScrollView>("notesTabs");
            _title = root.Q<TextField>("notesTitle");
            _text = root.Q<TextField>("notesText");
            _who = root.Q<Button>("notesWho");
            _delete = root.Q<Button>("notesDelete");
            _editor = root.Q("notesEditor");
            _title.maxLength = NoteTab.MaxTitle;
            _text.maxLength = NoteTab.MaxBody;
            _text.multiline = true;
            _text.verticalScrollerVisibility = ScrollerVisibility.Auto;
            _title.RegisterCallback<FocusOutEvent>(_ => Commit());
            _text.RegisterCallback<FocusOutEvent>(_ => Commit());
            root.Q<Button>("notesButton").clicked += Toggle;
            root.Q<Button>("notesClose").clicked += Hide;
            root.Q<Button>("notesNew").clicked += NewPage;
            _who.clicked += ToggleWho;
            _delete.clicked += Delete;
        }

        public void Toggle()
        {
            if (Visible)
                Hide();
            else
            {
                _shownKey = null;
                _panel.RemoveFromClassList(Hidden);
                Update();
            }
        }

        public void Hide()
        {
            Commit();
            _panel.AddToClassList(Hidden);
        }

        private string SeatColor => _table.Engine?.State.Players.Seats.FirstOrDefault(s => s.Id == _table.LocalSeat)?.Color;

        private bool CanRead(NoteTab tab) => tab.VisibleTo(SeatColor, _table.SeesEverything, _table.Engine.State, _table.LocalSeat);

        /// <summary>Redraws the page list when the notebook changes (someone else's edit too).</summary>
        public void Update()
        {
            if (!Visible || _table.Engine == null)
                return;
            var pages = _table.Engine.State.Notebook.Where(CanRead).ToList();
            var state = _table.Engine.State;
            var key = string.Join("|", pages.Select(p => $"{p.Id}:{p.Title}:{p.Color}:{p.Body.GetHashCode()}")) + "#" + _open + _confirmDelete + "#" + state.Session?.Language;
            if (key == _shownKey)
                return;
            _shownKey = key;
            if (_open == null || pages.All(p => p.Id != _open))
                _open = pages.FirstOrDefault()?.Id;
            _tabs.Clear();
            foreach (var page in pages)
            {
                var id = page.Id;
                var title = TitleOf(state, page);
                var button = new Button(() => OpenPage_(id)) { text = string.IsNullOrWhiteSpace(title) ? "(untitled)" : title };
                button.AddToClassList("notes-tab");
                if (page.Color != null)
                    button.AddToClassList("notes-tab--private");
                if (id == _open)
                    button.AddToClassList("notes-tab--open");
                _tabs.Add(button);
            }
            var open = pages.FirstOrDefault(p => p.Id == _open);
            _editor.EnableInClassList(Hidden, open == null);
            if (open == null)
                return;
            // A page read in another language than the game's own is read-only: an edit would write the translation over
            // the game's text.
            var translated = Translated(state, open);
            _title.isReadOnly = translated;
            _text.isReadOnly = translated;
            // Someone else's edit must not overwrite what this player is typing.
            if (_title.focusController?.focusedElement != _title || translated)
                _title.SetValueWithoutNotify(TitleOf(state, open));
            if (_text.focusController?.focusedElement != _text || translated)
                _text.SetValueWithoutNotify(BodyOf(state, open));
            _who.text = open.Color == null ? "For everyone" : "Only for you";
            _who.SetEnabled(SeatColor != null);
            _delete.text = _confirmDelete ? "Really delete?" : "Delete page";
            _delete.EnableInClassList("escape-danger", _confirmDelete);
        }

        private void OpenPage_(string id)
        {
            Commit();
            _open = id;
            _confirmDelete = false;
            _shownKey = null;
            Update();
        }

        /// <summary>Opens a page by id (tests).</summary>
        public void Open(string id) => OpenPage_(id);

        /// <summary>Types into the open page and sends it (tests).</summary>
        public void Write(string title, string text)
        {
            _title.SetValueWithoutNotify(title);
            _text.SetValueWithoutNotify(text);
            Commit();
        }

        private static string TitleOf(TableState state, NoteTab page) => Translations.Text(state, $"notebook.{page.Id}.title", page.Title) ?? "";

        private static string BodyOf(TableState state, NoteTab page) => Translations.Text(state, $"notebook.{page.Id}.body", page.Body) ?? "";

        /// <summary>The page reads in a translation (the session's language is not the game's own and translates it).</summary>
        private static bool Translated(TableState state, NoteTab page) => TitleOf(state, page) != page.Title || BodyOf(state, page) != page.Body;

        /// <summary>Sends the open page when its title or text changed.</summary>
        private void Commit()
        {
            var page = Current();
            if (page == null || Translated(_table.Engine.State, page) || (page.Title == _title.value && page.Body == _text.value))
                return;
            Send(new NoteTab { Id = page.Id, Title = _title.value ?? "", Body = _text.value ?? "", Color = page.Color });
        }

        private NoteTab Current() => _open == null ? null : _table.Engine?.State.Notebook.FirstOrDefault(p => p.Id == _open);

        private void NewPage()
        {
            Commit();
            var result = Send(new NoteTab { Title = "New page", Body = "" });
            if (result != null && result.Ok)
                _open = result.CreatedId;
            _shownKey = null;
            Update();
        }

        private void ToggleWho()
        {
            var page = Current();
            if (page == null || SeatColor == null || Translated(_table.Engine.State, page))
                return;
            Send(new NoteTab { Id = page.Id, Title = _title.value ?? page.Title, Body = _text.value ?? page.Body, Color = page.Color == null ? SeatColor : null });
        }

        private void Delete()
        {
            var page = Current();
            if (page == null)
                return;
            if (!_confirmDelete)
                _confirmDelete = true;
            else
            {
                _confirmDelete = false;
                _table.Execute(new DeleteNoteTabCommand { TabId = page.Id, Actor = _table.LocalSeat });
                _open = null;
            }
            _shownKey = null;
            Update();
        }

        private CommandResult Send(NoteTab tab) => _table.Execute(new SetNoteTabCommand { Tab = tab, Actor = _table.LocalSeat });
    }
}
