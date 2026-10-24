using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Rules
{
    /// <summary>
    /// One element of a script's UI (M5, ADR 0004): a tree of panels, texts, images, buttons, layouts and inputs that
    /// the host's scripts build and change (TTS <c>UI.*</c>), kept as table state so every machine draws it. Our own JSON
    /// format; its element and attribute names follow TTS XmlUI so the translator (<c>Core.Tts.XmlUi</c>) stays thin.
    /// Attribute values are strings, as XmlUI's are; booleans read <c>True</c> / <c>False</c> like TTS returns them.
    /// </summary>
    public sealed class UiNode
    {
        public const int MaxNodes = 20000;
        public const int MaxValueLength = 20000;

        /// <summary>Element type: <see cref="Tags"/>.</summary>
        public string Tag { get; set; } = "Panel";
        public Dictionary<string, string> Attributes { get; set; } = new Dictionary<string, string>();
        public bool ShouldSerializeAttributes() => Attributes.Count > 0;
        /// <summary>The element's text content (TTS <c>UI.getValue</c>): a Text's words, an input's text.</summary>
        public string? Value { get; set; }
        public List<UiNode> Children { get; set; } = new List<UiNode>();
        public bool ShouldSerializeChildren() => Children.Count > 0;

        public string? Id => Attributes.TryGetValue("id", out var id) ? id : null;

        public string? Get(string name) => Attributes.TryGetValue(name, out var v) ? v : null;

        /// <summary>Element types the game draws; others are kept and drawn as plain panels.</summary>
        public static readonly IReadOnlyList<string> Tags = new[]
        {
            "Panel", "Text", "Image", "RawImage", "Button", "HorizontalLayout", "VerticalLayout", "GridLayout", "TableLayout",
            "Row", "Cell", "HorizontalScrollView", "VerticalScrollView", "InputField", "Toggle", "ToggleButton", "ToggleGroup",
            "ProgressBar", "Slider", "Dropdown", "Option",
        };

        /// <summary>
        /// Attributes whose values are booleans: stored as <c>True</c> / <c>False</c>, whatever case the script or the
        /// XML used, because TTS scripts compare <c>getAttribute(id, "active") == "True"</c>.
        /// </summary>
        public static readonly HashSet<string> BooleanAttributes = new HashSet<string>(StringComparer.Ordinal)
        {
            "active", "interactable", "preserveAspect", "isOn", "readOnly", "childForceExpandWidth", "childForceExpandHeight",
            "showPercentageText", "allowDragging", "returnToOriginalPositionWhenReleased", "ignoreLayout", "selected",
            "raycastTarget", "wholeNumbers", "resizeTextForBestFit", "horizontalOverflow", "verticalOverflow",
        };

        /// <summary>A value as it is stored: booleans as True / False.</summary>
        public static string Normalize(string name, string value)
        {
            if (BooleanAttributes.Contains(name))
            {
                if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)) return "True";
                if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)) return "False";
            }
            return value;
        }

        /// <summary>The first element with this id, depth first (as TTS finds it), or null.</summary>
        public static UiNode? Find(IEnumerable<UiNode> nodes, string id)
        {
            foreach (var node in nodes)
            {
                if (node.Id == id)
                    return node;
                if (Find(node.Children, id) is { } inner)
                    return inner;
            }
            return null;
        }

        public static int Count(IEnumerable<UiNode> nodes) => nodes.Sum(n => 1 + Count(n.Children));

        /// <summary>Is <c>active</c> not False? (elements are shown unless switched off.)</summary>
        public bool Active => !string.Equals(Get("active"), "False", StringComparison.Ordinal);

        public UiNode Clone() => new UiNode
        {
            Tag = Tag, Value = Value, Attributes = new Dictionary<string, string>(Attributes),
            Children = Children.Select(c => c.Clone()).ToList(),
        };
    }
}
