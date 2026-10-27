using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace TableWorld.View.Hud
{
    /// <summary>
    /// The choices of one game action, opened from its button: a card choice shows the card itself (a character to pick,
    /// a card to buy), anything else its name. One button per action keeps the HUD short however many choices there are.
    /// </summary>
    public sealed class ActionPicker
    {
        public sealed class Choice
        {
            public string Label;
            /// <summary>The card's face, or null for a choice that is not a card.</summary>
            public Sprite Image;
            public Action Pick;
        }

        private readonly VisualElement _panel;
        private readonly Label _title;
        private readonly VisualElement _grid;

        public ActionPicker(VisualElement root)
        {
            _panel = new VisualElement { name = "actionPicker" };
            _panel.AddToClassList("action-picker");
            _panel.AddToClassList("hidden");
            var header = new VisualElement();
            header.AddToClassList("action-picker-header");
            _title = new Label();
            _title.AddToClassList("action-picker-title");
            var close = new Button(Hide) { text = Ui.T("Cancel") };
            close.AddToClassList("hud-button");
            header.Add(_title);
            header.Add(close);
            _grid = new VisualElement();
            _grid.AddToClassList("action-picker-grid");
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("action-picker-scroll");
            scroll.Add(_grid);
            _panel.Add(header);
            _panel.Add(scroll);
            root.Add(_panel);
        }

        public bool Visible => !_panel.ClassListContains("hidden");

        public void Show(string title, IReadOnlyList<Choice> choices)
        {
            _title.text = title;
            _grid.Clear();
            foreach (var choice in choices)
            {
                var pick = choice.Pick;
                var button = new Button(() =>
                {
                    Hide();
                    pick();
                }) { tooltip = choice.Label };
                button.AddToClassList("action-choice");
                if (choice.Image != null)
                {
                    button.AddToClassList("action-choice--card");
                    var face = new VisualElement { pickingMode = PickingMode.Ignore };
                    face.AddToClassList("action-choice-image");
                    // A runtime card image (a cell of a sprite sheet) can only be set from code.
                    face.style.backgroundImage = new StyleBackground(choice.Image);
                    button.Add(face);
                }
                var label = new Label(choice.Label) { pickingMode = PickingMode.Ignore, enableRichText = false };
                label.AddToClassList("action-choice-label");
                button.Add(label);
                _grid.Add(button);
            }
            _panel.RemoveFromClassList("hidden");
        }

        public void Hide() => _panel.AddToClassList("hidden");
    }
}
