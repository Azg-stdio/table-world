using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using Core.Rules;

namespace Core.Tts
{
    /// <summary>
    /// TTS XmlUI ↔ our script UI tree (ADR 0004). Reading applies <c>&lt;Defaults&gt;</c> (per tag, then per class, then the
    /// element's own attributes), writes attribute names in their canonical case (XmlUI is lenient: <c>minheight</c>) and
    /// booleans as True / False. Used by <c>UI.setXml</c> at run time and by the importer (M7).
    /// </summary>
    public static class XmlUi
    {
        /// <summary>Canonical attribute names, by lower case.</summary>
        private static readonly Dictionary<string, string> Canonical = new[]
        {
            "id", "class", "active", "visibility", "color", "colors", "text", "textColor", "fontSize", "fontStyle", "font",
            "alignment", "textAlignment", "outline", "outlineSize", "shadow", "width", "height", "minWidth", "minHeight",
            "preferredWidth", "preferredHeight", "flexibleWidth", "flexibleHeight", "rectAlignment", "offsetXY", "position",
            "rotation", "scale", "padding", "spacing", "childAlignment", "childForceExpandWidth", "childForceExpandHeight",
            "cellSize", "columns", "columnWidths", "image", "preserveAspect", "type", "onClick", "onValueChanged", "onEndEdit",
            "onMouseEnter", "onMouseExit", "placeholder", "characterValidation", "characterLimit", "lineType", "readOnly",
            "isOn", "interactable", "percentage", "fillImageColor", "showPercentageText", "minValue", "maxValue", "value",
            "wholeNumbers", "selected", "tooltip", "tooltipPosition", "allowDragging", "returnToOriginalPositionWhenReleased",
            "ignoreLayout", "showAnimation", "hideAnimation", "animationDuration", "itemHeight", "itemBackgroundColors",
            "dropdownBackgroundColor", "raycastTarget", "horizontalOverflow", "verticalOverflow", "resizeTextForBestFit",
            "resizeTextMinSize", "resizeTextMaxSize", "contentSizeFitter", "toggleWidth", "toggleHeight", "textOffset",
            "backgroundColor", "checkColor", "handleColor", "fillColor", "iconColor", "scrollbarColors",
        }.ToDictionary(n => n.ToLowerInvariant(), n => n);

        public static string CanonicalName(string name) =>
            Canonical.TryGetValue(name.ToLowerInvariant(), out var known) ? known : name;

        /// <summary>Reads XmlUI text (several top-level elements allowed). Throws <see cref="XmlException"/> when it is not XML.</summary>
        public static List<UiNode> Parse(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml))
                return new List<UiNode>();
            var document = new XmlDocument { XmlResolver = null };
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, IgnoreComments = true };
            using (var reader = XmlReader.Create(new System.IO.StringReader("<root>" + xml + "</root>"), settings))
                document.Load(reader);
            var defaults = new List<(string Tag, string? Class, Dictionary<string, string> Attributes)>();
            foreach (var block in document.DocumentElement!.ChildNodes.OfType<XmlElement>().Where(e => e.Name == "Defaults"))
                foreach (var rule in block.ChildNodes.OfType<XmlElement>())
                {
                    var attributes = Read(rule);
                    attributes.TryGetValue("class", out var cls);
                    attributes.Remove("class");
                    defaults.Add((rule.Name, cls, attributes));
                }
            return document.DocumentElement.ChildNodes.OfType<XmlElement>().Where(e => e.Name != "Defaults")
                .Select(e => Node(e, defaults)).ToList();
        }

        private static Dictionary<string, string> Read(XmlElement element)
        {
            var attributes = new Dictionary<string, string>();
            foreach (XmlAttribute a in element.Attributes)
            {
                var name = CanonicalName(a.Name);
                attributes[name] = UiNode.Normalize(name, a.Value);
            }
            return attributes;
        }

        private static UiNode Node(XmlElement element, List<(string Tag, string? Class, Dictionary<string, string> Attributes)> defaults)
        {
            var own = Read(element);
            var classes = own.TryGetValue("class", out var cls) ? cls.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries) : Array.Empty<string>();
            var node = new UiNode { Tag = element.Name };
            foreach (var rule in defaults.Where(d => d.Tag == element.Name && d.Class == null))
                foreach (var pair in rule.Attributes)
                    node.Attributes[pair.Key] = pair.Value;
            foreach (var c in classes)
                foreach (var rule in defaults.Where(d => d.Tag == element.Name && d.Class == c))
                    foreach (var pair in rule.Attributes)
                        node.Attributes[pair.Key] = pair.Value;
            foreach (var pair in own)
                node.Attributes[pair.Key] = pair.Value;
            var children = element.ChildNodes.OfType<XmlElement>().ToList();
            node.Children = children.Select(c => Node(c, defaults)).ToList();
            if (children.Count == 0)
            {
                var text = element.InnerText;
                if (!string.IsNullOrWhiteSpace(text))
                    node.Value = text.Trim();
            }
            return node;
        }

        /// <summary>Writes the tree back as XmlUI (TTS <c>UI.getXml</c>); defaults come out applied.</summary>
        public static string Write(IEnumerable<UiNode> nodes)
        {
            var sb = new StringBuilder();
            foreach (var node in nodes)
                Write(sb, node, 0);
            return sb.ToString();
        }

        private static void Write(StringBuilder sb, UiNode node, int depth)
        {
            sb.Append(' ', depth * 2).Append('<').Append(node.Tag);
            foreach (var pair in node.Attributes)
                sb.Append(' ').Append(pair.Key).Append("=\"").Append(Escape(pair.Value)).Append('"');
            if (node.Children.Count == 0 && node.Value == null)
            {
                sb.Append(" />\n");
                return;
            }
            sb.Append('>');
            if (node.Children.Count > 0)
            {
                sb.Append('\n');
                foreach (var child in node.Children)
                    Write(sb, child, depth + 1);
                sb.Append(' ', depth * 2);
            }
            else
                sb.Append(Escape(node.Value!));
            sb.Append("</").Append(node.Tag).Append(">\n");
        }

        private static string Escape(string s) =>
            s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    }
}
