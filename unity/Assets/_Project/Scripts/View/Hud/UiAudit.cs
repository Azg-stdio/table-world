using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UIElements;

namespace TableWorld.View.Hud
{
    /// <summary>
    /// Checks that what the HUD shows can be read (M12): every visible text against what is really behind it (its panel's
    /// colour, its background image's average colour, translucent layers composed down to the table), every coloured word
    /// of a rich line too, faded and disabled ones included; and that text keeps clear of its panel's edges and inside the
    /// border of a 9-sliced frame (a parchment's inner line, a wooden frame). Run it from a test or a probe on any screen:
    /// <c>UiAudit.Check(root)</c> lists what fails, worst first.
    /// </summary>
    public static class UiAudit
    {
        /// <summary>WCAG 2 AA: 4.5:1 for body text, 3:1 for large text (18 px, or 14 px bold) and for disabled controls.</summary>
        public const float BodyContrast = 4.5f;
        public const float LargeContrast = 3f;
        /// <summary>Text keeps this far from its panel's edge (px, panel units); buttons need less on top and bottom.</summary>
        public const float PanelInset = 8f;
        public const float ButtonInsetX = 6f;
        public const float ButtonInsetY = 2f;
        /// <summary>A background element no taller than this is a control (a chip, a field, a title bar), not a panel.</summary>
        public const float ControlHeight = 44f;

        /// <summary>What a translucent HUD layer shows through when nothing under it is opaque: the table, assumed mid-dark.</summary>
        public static readonly Color Scene = new Color(0.3f, 0.27f, 0.24f);

        public sealed class Problem
        {
            public string Kind = "";
            public string Where = "";
            public string Text = "";
            public float Value;
            public float Needed;
            public string Detail = "";

            /// <summary>How far from passing (0 passes, 1 is nothing at all).</summary>
            public float Severity => Kind == "overflow" ? 1f : Needed <= 0f ? 0f : Mathf.Clamp01(1f - Value / Needed);

            public override string ToString() =>
                Kind == "contrast"
                    ? $"contrast {Value:0.0}:1 (needs {Needed:0.#}) {Where} \"{Text}\" {Detail}"
                    : Kind == "overflow" ? $"overflow {-Value:0}px {Where} \"{Text}\" {Detail}"
                    : $"{Kind} {Value:0}px (needs {Needed:0}) {Where} \"{Text}\" {Detail}";
        }

        public static List<Problem> Check(VisualElement root)
        {
            var problems = new List<Problem>();
            var screen = root.worldBound;
            foreach (var text in root.Query<TextElement>().ToList())
            {
                var content = text.text;
                if (string.IsNullOrWhiteSpace(content) || !Shown(text, root, out var opacity))
                    continue;
                var bounds = text.worldBound;
                if (!(bounds.width > 1f && bounds.height > 1f) || !bounds.Overlaps(screen) || Clipped(text, bounds))
                    continue;
                // A text with nothing behind it (a name floating over the 3D table) is not a panel's: skipped.
                if (!Behind(text, out var background, out var panel))
                    continue;
                var style = text.resolvedStyle;
                var large = style.fontSize >= 18f || style.fontSize >= 14f && (style.unityFontStyleAndWeight == FontStyle.Bold || style.unityFontStyleAndWeight == FontStyle.BoldAndItalic);
                var disabled = !text.enabledInHierarchy;
                var needed = large || disabled ? LargeContrast : BodyContrast;
                var where = Path(text);
                var plain = Plain(content);
                foreach (var (segment, colour) in Segments(content, style.color))
                {
                    var shown = Over(new Color(colour.r, colour.g, colour.b, colour.a * opacity), background);
                    var ratio = Contrast(shown, background);
                    if (ratio < needed)
                        problems.Add(new Problem
                        {
                            Kind = "contrast", Where = where, Text = Short(segment), Value = ratio, Needed = needed,
                            Detail = $"text #{Hex(shown)} on #{Hex(background)}{(disabled ? " (disabled)" : "")}",
                        });
                }
                Edges(text, panel, plain, where, problems);
                // A control spilling out of the bar or panel it sits in (cut off by the screen, or under its neighbour).
                if (text.parent != null && Behind(text.parent, out _, out var container) && container != null && container != text)
                {
                    var box = container.worldBound;
                    var spill = Mathf.Max(box.xMin - bounds.xMin, bounds.xMax - box.xMax, box.yMin - bounds.yMin, bounds.yMax - box.yMax);
                    if (spill > 1f)
                        problems.Add(new Problem { Kind = "overflow", Where = where, Text = Short(plain), Value = -spill, Needed = 0f, Detail = $"out of {Path(container)}" });
                }
            }
            return problems.OrderByDescending(p => p.Severity).ThenBy(p => p.Where).ToList();
        }

        /// <summary>The problems as lines, for a test message or a probe.</summary>
        public static string Report(IEnumerable<Problem> problems) => string.Join("\n", problems.Select(p => p.ToString()));

        /// <summary>The colour behind an element as it is drawn now (its panel's, composed down to the table), or null.</summary>
        public static Color? BackgroundOf(VisualElement element) => Behind(element, out var colour, out _) ? colour : (Color?)null;

        // ---- visibility ---------------------------------------------------------------------------------------------

        private static bool Shown(VisualElement element, VisualElement root, out float opacity)
        {
            opacity = 1f;
            for (var e = element; e != null; e = e.parent)
            {
                var style = e.resolvedStyle;
                if (style.display == DisplayStyle.None || style.visibility == Visibility.Hidden)
                    return false;
                opacity *= style.opacity;
                if (e == root)
                    break;
            }
            return opacity > 0.05f;
        }

        /// <summary>
        /// A text a clipping container (a scroll list's viewport) hides or cuts: scrolled out of view, or half in it, as
        /// scrolling leaves it. Not a layout problem.
        /// </summary>
        private static bool Clipped(VisualElement element, Rect bounds)
        {
            for (var e = element.parent; e != null; e = e.parent)
                if (e is ScrollView scroll)
                {
                    var clip = scroll.contentViewport.worldBound;
                    if (bounds.xMin < clip.xMin - 0.5f || bounds.xMax > clip.xMax + 0.5f || bounds.yMin < clip.yMin - 0.5f || bounds.yMax > clip.yMax + 0.5f)
                        return true;
                }
            return false;
        }

        // ---- what is behind a text ------------------------------------------------------------------------------------

        /// <summary>
        /// The colour behind <paramref name="element"/>: its own background and its ancestors', translucent ones composed
        /// over what is under them, down to <see cref="Scene"/>; and the nearest element that has a background (its panel).
        /// False when nothing at all is behind it.
        /// </summary>
        private static bool Behind(VisualElement element, out Color colour, out VisualElement panel)
        {
            panel = null;
            var layers = new List<Color>();
            for (var e = element; e != null; e = e.parent)
            {
                var style = e.resolvedStyle;
                var image = style.backgroundImage;
                Color? layer = null;
                if (image.texture != null)
                    layer = Average(image.texture) * style.unityBackgroundImageTintColor;
                else if (image.sprite != null)
                    layer = Average(image.sprite.texture) * style.unityBackgroundImageTintColor;
                else if (style.backgroundColor.a > 0.02f)
                    layer = style.backgroundColor;
                if (layer is { } c)
                {
                    panel ??= e;
                    // A background image is drawn opaque over the element's own colour.
                    layers.Add(image.texture != null || image.sprite != null ? new Color(c.r, c.g, c.b, 1f) : c);
                    if (layers[layers.Count - 1].a >= 0.98f)
                        break;
                }
            }
            colour = Scene;
            if (panel == null)
                return false;
            for (var i = layers.Count - 1; i >= 0; i--)
                colour = Over(layers[i], colour);
            colour.a = 1f;
            return true;
        }

        private static readonly Dictionary<Texture, Color> Averages = new Dictionary<Texture, Color>();

        /// <summary>A texture's average colour (sRGB), read from a small copy on the GPU (the texture need not be readable).</summary>
        public static Color Average(Texture texture)
        {
            if (Averages.TryGetValue(texture, out var known))
                return known;
            var rt = RenderTexture.GetTemporary(16, 16, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            Graphics.Blit(texture, rt);
            RenderTexture.active = rt;
            var read = new Texture2D(16, 16, TextureFormat.RGBA32, false, false);
            read.ReadPixels(new Rect(0, 0, 16, 16), 0, 0);
            read.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            var pixels = read.GetPixels();
            UnityEngine.Object.Destroy(read);
            float r = 0, g = 0, b = 0, a = 0;
            foreach (var p in pixels)
            {
                r += p.r * p.a;
                g += p.g * p.a;
                b += p.b * p.a;
                a += p.a;
            }
            var average = a > 0.001f ? new Color(r / a, g / a, b / a, a / pixels.Length) : Color.clear;
            Averages[texture] = average;
            return average;
        }

        // ---- colours ------------------------------------------------------------------------------------------------

        private static Color Over(Color top, Color under) =>
            new Color(top.r * top.a + under.r * (1f - top.a), top.g * top.a + under.g * (1f - top.a), top.b * top.a + under.b * (1f - top.a), 1f);

        private static float Channel(float c) => c <= 0.03928f ? c / 12.92f : Mathf.Pow((c + 0.055f) / 1.055f, 2.4f);

        /// <summary>WCAG relative luminance of an sRGB colour.</summary>
        public static float Luminance(Color c) => 0.2126f * Channel(c.r) + 0.7152f * Channel(c.g) + 0.0722f * Channel(c.b);

        public static float Contrast(Color a, Color b)
        {
            var la = Luminance(a);
            var lb = Luminance(b);
            return (Mathf.Max(la, lb) + 0.05f) / (Mathf.Min(la, lb) + 0.05f);
        }

        private static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c).ToLowerInvariant();

        private static readonly Regex Tag = new Regex(@"<color=(#[0-9a-fA-F]{6})>(.*?)</color>|<noparse>(.*?)</noparse>|<[^>]+>", RegexOptions.Singleline);

        /// <summary>The runs of a rich line with the colour each is drawn in.</summary>
        private static IEnumerable<(string Text, Color Colour)> Segments(string rich, Color normal)
        {
            var at = 0;
            foreach (Match m in Tag.Matches(rich))
            {
                if (m.Index > at)
                    yield return (rich.Substring(at, m.Index - at), normal);
                if (m.Groups[1].Success && ColorUtility.TryParseHtmlString(m.Groups[1].Value, out var c))
                    yield return (m.Groups[2].Value, c);
                else if (m.Groups[3].Success)
                    yield return (m.Groups[3].Value, normal);
                at = m.Index + m.Length;
            }
            if (at < rich.Length)
                yield return (rich.Substring(at), normal);
        }

        private static string Plain(string rich) => Tag.Replace(rich, m => m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Success ? m.Groups[3].Value : "");

        // ---- edges --------------------------------------------------------------------------------------------------

        private static void Edges(TextElement text, VisualElement panel, string plain, string where, List<Problem> problems)
        {
            // A symbol (+, –, ▶) is an icon in its button: it may fill it.
            if (panel == null || string.IsNullOrWhiteSpace(plain) || plain.Trim().Length <= 2)
                return;
            // Where the letters are: the text's content box, as wide as its words when it is narrower.
            var box = text.LocalToWorld(text.contentRect);
            var measured = text.MeasureTextSize(plain, box.width, VisualElement.MeasureMode.AtMost, box.height, VisualElement.MeasureMode.Undefined);
            var align = text.resolvedStyle.unityTextAlign;
            var width = Mathf.Min(box.width, measured.x);
            var x = align == TextAnchor.UpperCenter || align == TextAnchor.MiddleCenter || align == TextAnchor.LowerCenter ? box.center.x - width / 2f
                : align == TextAnchor.UpperRight || align == TextAnchor.MiddleRight || align == TextAnchor.LowerRight ? box.xMax - width : box.xMin;
            var letters = new Rect(x, box.y, width, box.height);
            var edge = panel.worldBound;
            var style = panel.resolvedStyle;
            var framed = style.backgroundImage.texture != null || style.backgroundImage.sprite != null;
            float insetX, insetY;
            if (panel == text || panel is Button || text is Button || edge.height <= ControlHeight)
            {
                insetX = ButtonInsetX;
                insetY = ButtonInsetY;
            }
            else
            {
                insetX = insetY = PanelInset;
            }
            if (framed)
            {
                // Inside a 9-sliced frame the letters stay in its middle: past the drawn border, and a few px more.
                var scale = style.unitySliceScale > 0f ? style.unitySliceScale : 1f;
                insetX = Mathf.Max(insetX, Mathf.Max(style.unitySliceLeft, style.unitySliceRight) * scale + 4f);
                insetY = Mathf.Max(insetY, Mathf.Max(style.unitySliceTop, style.unitySliceBottom) * scale + 2f);
            }
            var left = letters.xMin - edge.xMin;
            var right = edge.xMax - letters.xMax;
            var top = letters.yMin - edge.yMin;
            var bottom = edge.yMax - letters.yMax;
            var gapX = Mathf.Min(left, right);
            var gapY = Mathf.Min(top, bottom);
            if (gapX < insetX - 0.5f)
                problems.Add(new Problem { Kind = "edge", Where = where, Text = Short(plain), Value = gapX, Needed = insetX, Detail = $"to the {(left < right ? "left" : "right")} of {Path(panel)}" });
            if (gapY < insetY - 0.5f)
                problems.Add(new Problem { Kind = "edge", Where = where, Text = Short(plain), Value = gapY, Needed = insetY, Detail = $"to the {(top < bottom ? "top" : "bottom")} of {Path(panel)}" });
        }

        // ---- naming ---------------------------------------------------------------------------------------------------

        /// <summary>An element as a path of names and classes, short enough to find it in the UXML.</summary>
        public static string Path(VisualElement element)
        {
            var parts = new List<string>();
            for (var e = element; e != null && parts.Count < 4; e = e.parent)
            {
                var name = !string.IsNullOrEmpty(e.name) ? "#" + e.name : e.GetClasses().FirstOrDefault(c => !c.StartsWith("unity-")) is { } cls ? "." + cls : null;
                if (name != null)
                    parts.Add(name);
            }
            parts.Reverse();
            return string.Join(" ", parts);
        }

        private static string Short(string text)
        {
            var one = text.Replace("\n", " ").Trim();
            return one.Length > 48 ? one.Substring(0, 45) + "..." : one;
        }
    }
}
