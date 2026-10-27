using System.Collections.Generic;
using UnityEngine.UIElements;

namespace TableWorld.View.Hud
{
    /// <summary>
    /// The texts a UXML file writes (button captions, labels, tooltips, field labels), in the interface's language
    /// (<see cref="Ui"/>, T10.2). Each one's English is read once from the tree; a change of language rewrites those that
    /// still show what was put there, so a label the code has since filled keeps the code's text.
    /// </summary>
    public sealed class UiLocalizer
    {
        private readonly List<Entry> _entries = new List<Entry>();

        private sealed class Entry
        {
            public VisualElement Element;
            public string Text, Tooltip, Label;
            public string ShownText, ShownTooltip, ShownLabel;
        }

        public UiLocalizer(VisualElement root)
        {
            root.Query<VisualElement>().ForEach(element =>
            {
                var text = element is TextElement t && !string.IsNullOrWhiteSpace(t.text) ? t.text : null;
                var tooltip = string.IsNullOrWhiteSpace(element.tooltip) ? null : element.tooltip;
                var label = element is BaseField<string> field && !string.IsNullOrWhiteSpace(field.label) ? field.label : null;
                if (text != null || tooltip != null || label != null)
                    _entries.Add(new Entry { Element = element, Text = text, Tooltip = tooltip, Label = label, ShownText = text, ShownTooltip = tooltip, ShownLabel = label });
            });
            Apply();
        }

        public int Count => _entries.Count;

        /// <summary>Writes every text still showing what this put there in the current language.</summary>
        public void Apply()
        {
            foreach (var entry in _entries)
            {
                if (entry.Text != null && entry.Element is TextElement t && t.text == entry.ShownText)
                    t.text = entry.ShownText = Ui.T(entry.Text);
                if (entry.Tooltip != null && entry.Element.tooltip == entry.ShownTooltip)
                    entry.Element.tooltip = entry.ShownTooltip = Ui.T(entry.Tooltip);
                if (entry.Label != null && entry.Element is BaseField<string> field && field.label == entry.ShownLabel)
                    field.label = entry.ShownLabel = Ui.T(entry.Label);
            }
        }
    }
}
