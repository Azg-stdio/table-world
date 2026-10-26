using System;
using System.Globalization;
using UnityEngine.UIElements;

namespace TableWorld.View.Hud
{
    /// <summary>
    /// A pocket calculator for the table (top bar): duel totals, damage, change. Private to each player, like the one on
    /// the phone next to the board. Works as a basic calculator: each operator applies to the running total.
    /// </summary>
    public sealed class CalculatorPanel
    {
        private readonly VisualElement _panel;
        private readonly Label _display;
        private readonly Label _history;
        private double _total;
        private string _entry = "";
        private char _pending;
        private bool _fresh = true;

        public CalculatorPanel(VisualElement root, Button toggle)
        {
            _panel = new VisualElement { name = "calculator" };
            _panel.AddToClassList("calculator");
            _panel.AddToClassList("hidden");
            _history = new Label();
            _history.AddToClassList("calculator-history");
            _display = new Label("0");
            _display.AddToClassList("calculator-display");
            _panel.Add(_history);
            _panel.Add(_display);
            var rows = new[] { "789÷", "456×", "123−", "C0=+" };
            foreach (var row in rows)
            {
                var line = new VisualElement();
                line.AddToClassList("calculator-row");
                foreach (var key in row)
                {
                    var k = key;
                    var button = new Button(() => Press(k)) { text = k.ToString() };
                    button.AddToClassList("calculator-key");
                    if ("÷×−+=".IndexOf(k) >= 0)
                        button.AddToClassList("calculator-key--op");
                    line.Add(button);
                }
                _panel.Add(line);
            }
            root.Add(_panel);
            if (toggle != null)
                toggle.clicked += () => _panel.ToggleInClassList("hidden");
        }

        public bool Visible => !_panel.ClassListContains("hidden");

        /// <summary>A key: a digit, an operator (÷ × − +), = or C.</summary>
        public void Press(char key)
        {
            if (char.IsDigit(key))
            {
                if (_entry.Length < 9)
                    _entry = _entry == "0" ? key.ToString() : _entry + key;
                _display.text = _entry;
                return;
            }
            switch (key)
            {
                case 'C':
                    _total = 0;
                    _entry = "";
                    _pending = '\0';
                    _fresh = true;
                    _history.text = "";
                    _display.text = "0";
                    return;
                case '=':
                    Apply();
                    _history.text = "";
                    _pending = '\0';
                    _fresh = true;
                    break;
                default:
                    Apply();
                    // An operator right after = goes on from the result.
                    _fresh = false;
                    _pending = key;
                    _history.text = Format(_total) + " " + key;
                    break;
            }
            _display.text = Format(_total);
        }

        public string Display => _display.text;

        private void Apply()
        {
            if (_entry.Length == 0)
                return;
            var value = double.Parse(_entry, CultureInfo.InvariantCulture);
            _entry = "";
            if (_fresh || _pending == '\0')
            {
                _total = value;
                _fresh = false;
                return;
            }
            _total = _pending switch
            {
                '+' => _total + value,
                '−' => _total - value,
                '×' => _total * value,
                '÷' => value == 0 ? 0 : _total / value,
                _ => value,
            };
        }

        private static string Format(double value) =>
            Math.Abs(value - Math.Round(value)) < 1e-9 ? Math.Round(value).ToString(CultureInfo.InvariantCulture) : value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
