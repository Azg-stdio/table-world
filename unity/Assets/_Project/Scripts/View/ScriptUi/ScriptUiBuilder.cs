using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Core.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace TableWorld.View.ScriptUi
{
    /// <summary>What a built script UI needs from the game: images, who is looking, and where its events go.</summary>
    public sealed class ScriptUiContext
    {
        public Func<string, Texture2D> Texture = _ => null;
        /// <summary>The viewer's seat colour (lower case), or null for a spectator.</summary>
        public string ViewerColor;
        public bool ViewerIsHost;
        /// <summary>An element was used: (path of child indices, event, value).</summary>
        public Action<List<int>, string, string> Raise = (_, _, _) => { };
    }

    /// <summary>
    /// Builds UI Toolkit elements from a script UI tree (ADR 0004), reproducing XmlUI's layout: free elements are placed
    /// by <c>rectAlignment</c> + <c>offsetXY</c> with <c>width</c> / <c>height</c> (pixels or %, default the whole parent);
    /// layout groups stack their children with <c>spacing</c>, <c>padding</c>, <c>childAlignment</c> and
    /// <c>childForceExpand*</c>. Script data decides colours, sizes and positions, so those are set on the elements; the
    /// look that does not come from scripts (hover shade, fonts) is in <c>ScriptUi.uss</c>.
    /// </summary>
    public static class ScriptUiBuilder
    {
        public const string PathKey = "script-ui-path";

        private static readonly Color DefaultText = new Color32(0x32, 0x32, 0x32, 0xFF);

        public static void Build(VisualElement container, IReadOnlyList<UiNode> nodes, ScriptUiContext context)
        {
            container.Clear();
            for (var i = 0; i < nodes.Count; i++)
                if (Element(nodes[i], new List<int> { i }, null, context) is { } child)
                    container.Add(child);
        }

        private static bool IsLayout(string tag) =>
            tag == "HorizontalLayout" || tag == "VerticalLayout" || tag == "GridLayout" || tag == "TableLayout" || tag == "Row";

        private static VisualElement Element(UiNode node, List<int> path, UiNode parent, ScriptUiContext context)
        {
            if (!Visible(node, context))
                return null;
            VisualElement e;
            Label caption = null;
            switch (node.Tag)
            {
                case "Text":
                    caption = new Label();
                    e = caption;
                    break;
                case "Button":
                case "ToggleButton":
                    e = new VisualElement();
                    e.AddToClassList("script-ui-button");
                    caption = new Label();
                    caption.AddToClassList("script-ui-button-text");
                    break;
                case "InputField":
                    e = Input(node, path, context);
                    break;
                case "Toggle":
                {
                    var toggle = new Toggle { value = Bool(node.Get("isOn")), text = node.Value ?? node.Get("text") ?? "" };
                    toggle.RegisterValueChangedCallback(v => Raise(context, path, node, "onValueChanged", v.newValue ? "True" : "False"));
                    e = toggle;
                    break;
                }
                case "Slider":
                {
                    var slider = new Slider(Number(node.Get("minValue"), 0f), Number(node.Get("maxValue"), 1f)) { value = Number(node.Get("value"), 0f) };
                    slider.RegisterValueChangedCallback(v => Raise(context, path, node, "onValueChanged", v.newValue.ToString(CultureInfo.InvariantCulture)));
                    e = slider;
                    break;
                }
                case "Dropdown":
                {
                    var options = node.Children.Where(c => c.Tag == "Option").Select(c => c.Value ?? c.Get("text") ?? "").ToList();
                    var selected = node.Children.Where(c => c.Tag == "Option").ToList().FindIndex(c => Bool(c.Get("selected")));
                    var dropdown = new DropdownField(options, Math.Max(0, selected));
                    dropdown.RegisterValueChangedCallback(v => Raise(context, path, node, "onValueChanged", v.newValue));
                    e = dropdown;
                    break;
                }
                case "ProgressBar":
                    e = Progress(node);
                    break;
                case "Image":
                case "RawImage":
                    e = new VisualElement();
                    break;
                case "HorizontalScrollView":
                case "VerticalScrollView":
                    e = new ScrollView(node.Tag == "HorizontalScrollView" ? ScrollViewMode.Horizontal : ScrollViewMode.Vertical);
                    break;
                default:
                    e = new VisualElement();
                    break;
            }
            e.userData = path;
            e.AddToClassList("script-ui");
            e.pickingMode = PickingMode.Ignore;
            if (node.Id != null)
                e.name = node.Id;

            Place(e, node, parent);
            Paint(e, node, context);
            if (caption != null)
                Words(caption, node, node.Tag == "Text" ? "alignment" : "textAlignment", node.Tag == "Text" ? TextAnchor.UpperLeft : TextAnchor.MiddleCenter);

            var interactable = !string.Equals(node.Get("interactable"), "False", StringComparison.Ordinal);
            if (node.Get("onClick") != null && interactable)
            {
                e.pickingMode = PickingMode.Position;
                e.RegisterCallback<PointerUpEvent>(evt =>
                {
                    Raise(context, path, node, "onClick", evt.button == 1 ? "-2" : evt.button == 2 ? "-3" : "-1");
                    evt.StopPropagation();
                });
            }
            if (!interactable)
                e.AddToClassList("script-ui--disabled");

            if (node.Tag == "Button" || node.Tag == "ToggleButton")
            {
                e.pickingMode = PickingMode.Position;
                if (caption != null && (node.Get("text") ?? node.Value) is { Length: > 0 })
                    e.Add(caption);
            }
            if (node.Tag != "Dropdown" && node.Tag != "Text")
                for (var i = 0; i < node.Children.Count; i++)
                    if (Element(node.Children[i], new List<int>(path) { i }, node, context) is { } child)
                        (e is ScrollView scroll ? scroll.contentContainer : e).Add(child);
            if (node.Tag == "Button" || node.Tag == "ToggleButton")
            {
                // The hover shade sits above the button's images.
                var shade = new VisualElement { pickingMode = PickingMode.Ignore };
                shade.AddToClassList("script-ui-shade");
                e.Add(shade);
            }
            return e;
        }

        private static void Raise(ScriptUiContext context, List<int> path, UiNode node, string evt, string value)
        {
            if (node.Get(evt) != null)
                context.Raise(path, evt, value);
        }

        /// <summary>XmlUI <c>visibility</c>: colours (and Host / Admins) separated by |; empty for everyone.</summary>
        private static bool Visible(UiNode node, ScriptUiContext context)
        {
            if (!node.Active)
                return false;
            var visibility = node.Get("visibility");
            if (string.IsNullOrWhiteSpace(visibility))
                return true;
            foreach (var token in visibility.Split('|').Select(t => t.Trim()))
            {
                if ((token.Equals("Host", StringComparison.OrdinalIgnoreCase) || token.Equals("Admins", StringComparison.OrdinalIgnoreCase)) && context.ViewerIsHost)
                    return true;
                if (context.ViewerColor != null && token.Equals(context.ViewerColor, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        // ---- layout -------------------------------------------------------------------------------------------------

        private static void Place(VisualElement e, UiNode node, UiNode parent)
        {
            var inLayout = parent != null && IsLayout(parent.Tag) && !Bool(node.Get("ignoreLayout"));
            if (inLayout)
                PlaceInLayout(e, node, parent);
            else
                PlaceFree(e, node);

            if (Vector(node.Get("padding"), 4) is { } pad)
            {
                // XmlUI padding is left right top bottom.
                e.style.paddingLeft = pad[0];
                e.style.paddingRight = pad[1];
                e.style.paddingTop = pad[2];
                e.style.paddingBottom = pad[3];
            }
            if (IsLayout(node.Tag))
                Arrange(e, node);
            if (Vector(node.Get("rotation"), 3) is { } rotation && Mathf.Abs(rotation[2]) > 0.01f)
                e.style.rotate = new Rotate(Angle.Degrees(-rotation[2]));
            if (Vector(node.Get("scale"), 2) is { } scale)
                e.style.scale = new Scale(new Vector2(scale[0], scale[1]));
        }

        private static void PlaceFree(VisualElement e, UiNode node)
        {
            e.style.position = Position.Absolute;
            var width = Size(node.Get("width")) ?? Length.Percent(100);
            var height = Size(node.Get("height")) ?? Length.Percent(100);
            e.style.width = width;
            e.style.height = height;
            var offset = Vector(node.Get("offsetXY"), 2) ?? new[] { 0f, 0f };
            var alignment = node.Get("rectAlignment") ?? "MiddleCenter";
            float tx = 0f, ty = 0f;
            if (alignment.EndsWith("Left", StringComparison.Ordinal))
                e.style.left = 0f;
            else if (alignment.EndsWith("Right", StringComparison.Ordinal))
                e.style.right = 0f;
            else
            {
                e.style.left = Length.Percent(50);
                tx = -50f;
            }
            if (alignment.StartsWith("Upper", StringComparison.Ordinal))
                e.style.top = 0f;
            else if (alignment.StartsWith("Lower", StringComparison.Ordinal))
                e.style.bottom = 0f;
            else
            {
                e.style.top = Length.Percent(50);
                ty = -50f;
            }
            if (tx != 0f || ty != 0f)
                e.style.translate = new Translate(Length.Percent(tx), Length.Percent(ty));
            // offsetXY: x right, y up; margins move absolute elements.
            e.style.marginLeft = offset[0];
            e.style.marginRight = -offset[0];
            e.style.marginTop = -offset[1];
            e.style.marginBottom = offset[1];
        }

        private static void PlaceInLayout(VisualElement e, UiNode node, UiNode parent)
        {
            var row = parent.Tag == "HorizontalLayout" || parent.Tag == "Row";
            if (parent.Tag == "GridLayout")
            {
                var cell = Vector(parent.Get("cellSize"), 2) ?? new[] { 100f, 100f };
                e.style.width = cell[0];
                e.style.height = cell[1];
                e.style.flexShrink = 0f;
                return;
            }
            var expandMain = !string.Equals(parent.Get(row ? "childForceExpandWidth" : "childForceExpandHeight"), "False", StringComparison.Ordinal);
            var expandCross = !string.Equals(parent.Get(row ? "childForceExpandHeight" : "childForceExpandWidth"), "False", StringComparison.Ordinal);
            // In a layout group XmlUI's width / height are the preferred size.
            var main = Size(node.Get(row ? "preferredWidth" : "preferredHeight")) ?? Size(node.Get(row ? "width" : "height"));
            var cross = Size(node.Get(row ? "preferredHeight" : "preferredWidth")) ?? Size(node.Get(row ? "height" : "width"));
            var min = Size(node.Get(row ? "minWidth" : "minHeight"));
            var flexible = Number(node.Get(row ? "flexibleWidth" : "flexibleHeight"), -1f);
            e.style.flexBasis = main ?? (expandMain ? new StyleLength(0f) : new StyleLength(StyleKeyword.Auto));
            e.style.flexGrow = flexible >= 0f ? flexible : expandMain ? 1f : 0f;
            e.style.flexShrink = 1f;
            if (min is { } m)
            {
                if (row) e.style.minWidth = m; else e.style.minHeight = m;
            }
            if (Size(node.Get(row ? "minHeight" : "minWidth")) is { } crossMin)
            {
                if (row) e.style.minHeight = crossMin; else e.style.minWidth = crossMin;
            }
            if (expandCross)
                e.style.alignSelf = Align.Stretch;
            else if (cross is { } c)
            {
                if (row) e.style.height = c; else e.style.width = c;
            }
        }

        /// <summary>A layout group's own arrangement: direction, spacing, alignment.</summary>
        private static void Arrange(VisualElement e, UiNode node)
        {
            var row = node.Tag == "HorizontalLayout" || node.Tag == "Row";
            if (node.Tag == "GridLayout")
            {
                e.style.flexDirection = FlexDirection.Row;
                e.style.flexWrap = Wrap.Wrap;
                e.style.alignContent = Align.FlexStart;
            }
            else
                e.style.flexDirection = row ? FlexDirection.Row : FlexDirection.Column;
            var alignment = node.Get("childAlignment") ?? "UpperLeft";
            Justify Along(string part) => part == "Middle" || part == "Center" ? Justify.Center
                : part == "Lower" || part == "Right" ? Justify.FlexEnd : Justify.FlexStart;
            Align Across(string part) => part == "Middle" || part == "Center" ? Align.Center
                : part == "Lower" || part == "Right" ? Align.FlexEnd : Align.FlexStart;
            var vertical = alignment.StartsWith("Upper", StringComparison.Ordinal) ? "Upper" : alignment.StartsWith("Lower", StringComparison.Ordinal) ? "Lower" : "Middle";
            var horizontal = alignment.EndsWith("Left", StringComparison.Ordinal) ? "Left" : alignment.EndsWith("Right", StringComparison.Ordinal) ? "Right" : "Center";
            e.style.justifyContent = Along(row ? horizontal : vertical);
            e.style.alignItems = Across(row ? vertical : horizontal);
            e.RegisterCallback<GeometryChangedEvent>(_ => Space(e, node, row));
            Space(e, node, row);
        }

        /// <summary>XmlUI spacing between a group's children (UI Toolkit has no gap): a margin before every child but the first.</summary>
        private static void Space(VisualElement e, UiNode node, bool row)
        {
            var spacing = Vector(node.Get("spacing"), 1) is { } s ? s[0] : 0f;
            var spacingY = node.Tag == "GridLayout" && Vector(node.Get("spacing"), 2) is { } both ? both[1] : spacing;
            var first = true;
            foreach (var child in e.Children())
            {
                if (child.ClassListContains("script-ui-shade"))
                    continue;
                if (node.Tag == "GridLayout")
                {
                    child.style.marginRight = spacing;
                    child.style.marginBottom = spacingY;
                }
                else if (row)
                    child.style.marginLeft = first ? 0f : spacing;
                else
                    child.style.marginTop = first ? 0f : spacing;
                first = false;
            }
        }

        // ---- look ---------------------------------------------------------------------------------------------------

        private static void Paint(VisualElement e, UiNode node, ScriptUiContext context)
        {
            var colors = node.Get("colors")?.Split('|');
            var color = Colour(node.Get("color")) ?? (colors != null ? Colour(colors[0]) : null);
            switch (node.Tag)
            {
                case "Image":
                case "RawImage":
                    if (node.Get("image") is { } image && context.Texture(image) is { } texture)
                    {
                        e.style.backgroundImage = new StyleBackground(texture);
                        e.style.unityBackgroundImageTintColor = color ?? Color.white;
                        if (Bool(node.Get("preserveAspect")))
                            e.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
                    }
                    else if (color is { } tint)
                        e.style.backgroundColor = tint;
                    if (node.Get("onClick") == null && color is not { a: < 0.01f })
                        e.pickingMode = PickingMode.Position;
                    break;
                case "Button":
                case "ToggleButton":
                case "InputField":
                    e.style.backgroundColor = color ?? Color.white;
                    if (node.Get("image") is { } buttonImage && context.Texture(buttonImage) is { } buttonTexture)
                    {
                        e.style.backgroundImage = new StyleBackground(buttonTexture);
                        e.style.unityBackgroundImageTintColor = color ?? Color.white;
                    }
                    break;
                case "Text":
                    break;
                default:
                    if (color is { } panel && panel.a > 0.001f)
                    {
                        e.style.backgroundColor = panel;
                        // A visible panel catches the pointer, as XmlUI's do; a clear one lets it through to the table.
                        e.pickingMode = PickingMode.Position;
                    }
                    if (node.Get("image") is { } panelImage && context.Texture(panelImage) is { } panelTexture)
                    {
                        e.style.backgroundImage = new StyleBackground(panelTexture);
                        e.style.unityBackgroundImageTintColor = color ?? Color.white;
                        e.pickingMode = PickingMode.Position;
                    }
                    break;
            }
            if (node.Tag == "Text" && Colour(node.Get("color")) is { } textColor)
                e.style.color = textColor;
        }

        private static void Words(Label label, UiNode node, string alignmentAttribute, TextAnchor fallback)
        {
            label.pickingMode = PickingMode.Ignore;
            label.enableRichText = true;
            label.text = node.Get("text") ?? node.Value ?? "";
            label.AddToClassList("script-ui-text");
            label.style.fontSize = Number(node.Get("fontSize"), 14f);
            if (node.Tag != "Text" || Colour(node.Get("color")) == null)
                label.style.color = Colour(node.Get("textColor")) ?? DefaultText;
            label.style.unityTextAlign = Anchor(node.Get(alignmentAttribute)) ?? fallback;
            label.style.unityFontStyleAndWeight = (node.Get("fontStyle") ?? "Normal") switch
            {
                "Bold" => FontStyle.Bold,
                "Italic" => FontStyle.Italic,
                "BoldAndItalic" => FontStyle.BoldAndItalic,
                _ => FontStyle.Normal,
            };
            if (Colour(node.Get("outline")) is { } outline)
            {
                label.style.unityTextOutlineColor = outline;
                label.style.unityTextOutlineWidth = Mathf.Clamp(Vector(node.Get("outlineSize"), 1) is { } size ? Mathf.Abs(size[0]) * 0.25f : 0.25f, 0.05f, 1f);
            }
        }

        private static VisualElement Input(UiNode node, List<int> path, ScriptUiContext context)
        {
            var field = new TextField
            {
                value = node.Get("text") ?? node.Value ?? "",
                multiline = (node.Get("lineType") ?? "SingleLine") != "SingleLine",
                isReadOnly = Bool(node.Get("readOnly")),
            };
            if (int.TryParse(node.Get("characterLimit"), out var limit) && limit > 0)
                field.maxLength = limit;
            field.textEdition.placeholder = node.Get("placeholder") ?? "";
            field.AddToClassList("script-ui-input");
            field.style.fontSize = Number(node.Get("fontSize"), 14f);
            field.style.color = Colour(node.Get("textColor")) ?? DefaultText;
            field.style.unityTextAlign = Anchor(node.Get("textAlignment")) ?? TextAnchor.UpperLeft;
            field.RegisterValueChangedCallback(v => Raise(context, path, node, "onValueChanged", v.newValue));
            field.RegisterCallback<FocusOutEvent>(_ => Raise(context, path, node, "onEndEdit", field.value));
            return field;
        }

        private static VisualElement Progress(UiNode node)
        {
            var bar = new VisualElement();
            bar.AddToClassList("script-ui-progress");
            var fill = new VisualElement { pickingMode = PickingMode.Ignore };
            fill.AddToClassList("script-ui-progress-fill");
            var percentage = Mathf.Clamp(Number(node.Get("percentage"), 0f), 0f, 100f);
            fill.style.width = Length.Percent(percentage);
            fill.style.backgroundColor = Colour(node.Get("fillImageColor")) ?? new Color(0.3f, 0.65f, 0.35f);
            bar.Add(fill);
            if (Bool(node.Get("showPercentageText")))
            {
                var text = new Label($"{Mathf.RoundToInt(percentage)}%") { pickingMode = PickingMode.Ignore };
                text.AddToClassList("script-ui-progress-text");
                text.style.color = Colour(node.Get("textColor")) ?? DefaultText;
                bar.Add(text);
            }
            return bar;
        }

        // ---- values -------------------------------------------------------------------------------------------------

        private static bool Bool(string value) => string.Equals(value, "True", StringComparison.OrdinalIgnoreCase);

        private static float Number(string value, float fallback) =>
            float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : fallback;

        /// <summary>Pixels ("120"), or a share of the parent ("50%").</summary>
        private static Length? Size(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            value = value.Trim();
            if (value.EndsWith("%", StringComparison.Ordinal))
                return float.TryParse(value.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var p) ? Length.Percent(p) : (Length?)null;
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var px) ? new Length(px) : (Length?)null;
        }

        /// <summary>Numbers separated by spaces (or commas); a single number fills every slot.</summary>
        public static float[] Vector(string value, int count)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            var parts = value.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => float.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : 0f).ToArray();
            if (parts.Length == 0)
                return null;
            var result = new float[count];
            for (var i = 0; i < count; i++)
                result[i] = parts.Length == 1 ? parts[0] : i < parts.Length ? parts[i] : 0f;
            return result;
        }

        private static TextAnchor? Anchor(string value) =>
            value != null && Enum.TryParse<TextAnchor>(value, ignoreCase: true, out var anchor) ? anchor : (TextAnchor?)null;

        private static readonly Dictionary<string, Color> Named = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
        {
            ["White"] = Color.white, ["Black"] = Color.black, ["Clear"] = Color.clear, ["Grey"] = Color.grey, ["Gray"] = Color.grey,
            ["Red"] = new Color(0.856f, 0.1f, 0.094f), ["Orange"] = new Color(0.956f, 0.392f, 0.113f), ["Yellow"] = new Color(0.905f, 0.898f, 0.172f),
            ["Green"] = new Color(0.192f, 0.701f, 0.168f), ["Teal"] = new Color(0.129f, 0.694f, 0.607f), ["Blue"] = new Color(0.118f, 0.53f, 1f),
            ["Purple"] = new Color(0.627f, 0.125f, 0.941f), ["Pink"] = new Color(0.96f, 0.439f, 0.807f), ["Brown"] = new Color(0.443f, 0.231f, 0.09f),
        };

        /// <summary>XmlUI colours: #RGB, #RRGGBB, #RRGGBBAA, rgb(r,g,b), rgba(r,g,b,a) (0–1, or 0–255), or a name.</summary>
        public static Color? Colour(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            value = value.Trim();
            if (value.StartsWith("#", StringComparison.Ordinal))
                return ColorUtility.TryParseHtmlString(value, out var hex) ? hex : (Color?)null;
            if (value.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
            {
                var open = value.IndexOf('(');
                var close = value.LastIndexOf(')');
                if (open < 0 || close <= open)
                    return null;
                var parts = value.Substring(open + 1, close - open - 1).Split(',')
                    .Select(p => float.TryParse(p.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : 0f).ToArray();
                if (parts.Length < 3)
                    return null;
                var scale = parts.Take(3).Any(p => p > 1f) ? 1f / 255f : 1f;
                return new Color(parts[0] * scale, parts[1] * scale, parts[2] * scale, parts.Length > 3 ? parts[3] : 1f);
            }
            return Named.TryGetValue(value, out var named) ? named : (Color?)null;
        }
    }
}
