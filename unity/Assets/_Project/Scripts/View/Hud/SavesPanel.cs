using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace TableWorld.View.Hud
{
    /// <summary>
    /// Save and load (G30), opened from the Escape menu: type a name and Save, or Load / Delete a listed save (Delete asks
    /// again). Autosaves are listed too. Styling lives in Hud.uss (saves-*).
    /// </summary>
    public sealed class SavesPanel
    {
        private const string Hidden = "hidden";

        private readonly VisualElement _panel;
        private readonly ScrollView _list;
        private readonly TextField _name;
        private readonly Label _hint;
        private readonly SaveLibrary _library;
        private readonly Func<int> _autosaveMinutes;
        private readonly Action<string, bool> _tell;
        private string _confirmDelete;

        public bool Visible => !_panel.ClassListContains(Hidden);

        public SavesPanel(VisualElement root, SaveLibrary library, Func<int> autosaveMinutes, Action<string, bool> tell, Action back)
        {
            _library = library;
            _autosaveMinutes = autosaveMinutes;
            _tell = tell;
            _panel = root.Q("savesPanel");
            _list = root.Q<ScrollView>("savesList");
            _name = root.Q<TextField>("saveName");
            _hint = root.Q<Label>("savesHint");
            _name.maxLength = Core.Rules.SaveNames.MaxLength;
            _name.textEdition.placeholder = Ui.T("Name this save");
            root.Q<Button>("saveNamed").clicked += SaveNamed;
            root.Q<Button>("savesBack").clicked += back;
        }

        public void Show()
        {
            _confirmDelete = null;
            _name.value = "";
            Rebuild();
            _panel.RemoveFromClassList(Hidden);
        }

        public void Hide() => _panel.AddToClassList(Hidden);

        /// <summary>Saves under the typed name (the Save button, and tests).</summary>
        public void SaveNamed()
        {
            var entry = _library.Save(_name.value, out var error);
            _tell(entry != null ? Ui.T("Saved {name}", ("name", entry.Name)) : error, entry == null);
            if (entry != null)
                _name.value = "";
            Rebuild();
        }

        private void Rebuild()
        {
            _list.Clear();
            var entries = _library.List();
            foreach (var entry in entries)
                _list.Add(Row(entry));
            var minutes = _autosaveMinutes();
            _hint.text = (_library.CanSave ? "" : Ui.T("Only the host saves and loads. ")) +
                         (minutes > 0 ? Ui.T("Autosave every {minutes} minutes while the table changes (Settings).", ("minutes", minutes)) : Ui.T("Autosave is off (Settings).")) +
                         (entries.Count == 0 ? Ui.T(" No saves yet.") : "");
        }

        private VisualElement Row(SaveLibrary.Entry entry)
        {
            var row = new VisualElement();
            row.AddToClassList("saves-row");
            var text = new VisualElement();
            text.AddToClassList("saves-text");
            var name = new Label(entry.Name) { enableRichText = false };
            name.AddToClassList("saves-name");
            var detail = new Label(string.Join(" · ", new[] { entry.Game, entry.SavedAt.ToString("d MMM HH:mm"), entry.Autosave ? Ui.T("autosave") : null }
                .Where(s => !string.IsNullOrEmpty(s)))) { enableRichText = false };
            detail.AddToClassList("saves-detail");
            text.Add(name);
            text.Add(detail);
            row.Add(text);

            var load = new Button(() => _library.LoadInSteps(entry, (ok, error) => _tell(ok ? Ui.T("Loaded {name}", ("name", entry.Name)) : error, !ok)))
                { text = Ui.T("Load") };
            load.AddToClassList("hud-button");
            load.SetEnabled(_library.CanSave);
            row.Add(load);

            var confirming = _confirmDelete == entry.File;
            var delete = new Button(() =>
            {
                if (_confirmDelete == entry.File)
                {
                    _library.Delete(entry);
                    _confirmDelete = null;
                    _tell(Ui.T("Deleted {name}", ("name", entry.Name)), false);
                }
                else
                    _confirmDelete = entry.File;
                Rebuild();
            }) { text = confirming ? Ui.T("Really delete?") : Ui.T("Delete") };
            delete.AddToClassList("hud-button");
            if (confirming)
                delete.AddToClassList("escape-danger");
            row.Add(delete);
            return row;
        }
    }
}
