using System.Linq;
using Core.Engine;
using UnityEngine;
using UnityEngine.UIElements;

namespace TableWorld.View.Hud
{
    /// <summary>
    /// Typing into a script's input on an object (M5): a field opens at the pointer with the input's text; every change
    /// is sent (<c>selected</c> true) and Enter, a click elsewhere or Escape finishes (<c>selected</c> false), as TTS's
    /// input_function hears it. Integer and decimal inputs keep only what fits.
    /// </summary>
    public sealed class InputEditor
    {
        private readonly VisualElement _root;
        private readonly TableView _table;
        private readonly TextField _field;
        // The input being edited, as plain data: typing changes the table, which rebuilds the input views.
        private string _objectId;
        private int _index;
        private int _validation;

        public bool Open => _objectId != null;

        public InputEditor(VisualElement root, TableView table)
        {
            _root = root;
            _table = table;
            _field = new TextField { name = "scriptInputEditor" };
            _field.AddToClassList("script-input-editor");
            _field.AddToClassList("hidden");
            _root.Add(_field);
            _field.RegisterValueChangedCallback(OnChanged);
            _field.RegisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
            // Enter and Escape can also arrive as navigation events (Input System UI).
            _field.RegisterCallback<NavigationSubmitEvent>(_ => Close(), TrickleDown.TrickleDown);
            _field.RegisterCallback<NavigationCancelEvent>(_ => Close(), TrickleDown.TrickleDown);
            _field.RegisterCallback<FocusOutEvent>(_ => Finish());
        }

        public void Show(ScriptInputView input, Vector2 screen)
        {
            Finish();
            if (input == null || input.Owner == null)
                return;
            _objectId = input.Owner.Id;
            _index = input.Index;
            _validation = input.Def.Validation;
            _field.multiline = false;
            _field.SetValueWithoutNotify(input.Def.Value ?? "");
            _field.textEdition.placeholder = input.Def.Label ?? "";
            var point = RuntimePanelUtils.ScreenToPanel(_root.panel, new Vector2(screen.x, Screen.height - screen.y));
            _field.style.left = point.x;
            _field.style.top = point.y;
            _field.RemoveFromClassList("hidden");
            _field.schedule.Execute(() =>
            {
                _field.Focus();
                _field.SelectAll();
            });
        }

        private void OnChanged(ChangeEvent<string> change)
        {
            if (_objectId == null)
                return;
            var value = change.newValue ?? "";
            if (!Fits(value, _validation))
            {
                _field.SetValueWithoutNotify(change.previousValue);
                return;
            }
            Send(_objectId, value, selected: true);
        }

        private void OnKey(KeyDownEvent e)
        {
            if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.keyCode == KeyCode.Escape)
            {
                e.StopPropagation();
                Close();
            }
        }

        private void Close()
        {
            _root.Focus();
            Finish();
        }

        private void Finish()
        {
            if (_objectId == null)
                return;
            var objectId = _objectId;
            _objectId = null;
            _field.AddToClassList("hidden");
            Send(objectId, _field.value ?? "", selected: false);
        }

        private void Send(string objectId, string value, bool selected)
        {
            if (_table == null || _table.Engine?.Find(objectId) is not { } obj || _index >= obj.Inputs.Count)
                return;
            _table.Execute(new TypeInputCommand
            {
                ObjectId = objectId, Index = _index, Value = value, Selected = selected, Actor = _table.LocalSeat,
            });
        }

        /// <summary>TTS validation: 2 integer, 3 decimal, 4 letters and digits; the others take anything.</summary>
        private static bool Fits(string value, int validation) => validation switch
        {
            2 => value.Length == 0 || value == "-" || long.TryParse(value, out _),
            3 => value.Length == 0 || value == "-" || value == "." || value == "-." ||
                 double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _),
            4 => value.All(char.IsLetterOrDigit),
            _ => true,
        };
    }
}
