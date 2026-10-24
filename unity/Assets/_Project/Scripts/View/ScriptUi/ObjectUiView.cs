using System.Collections.Generic;
using System.Linq;
using Core.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace TableWorld.View.ScriptUi
{
    /// <summary>
    /// One top-level element of an object's script UI, drawn on the object (M5, ADR 0004): a world-space panel with a
    /// trigger collider, so the table's pointer ray finds it and <see cref="TryUse"/> turns a click into a UI event.
    /// </summary>
    public sealed class ObjectUiSurface : MonoBehaviour
    {
        internal UIDocument Document;
        internal Vector2 Size;
        internal ScriptUiContext Context;

        /// <summary>Has the panel been laid out (it is, once it has been drawn)? Clicks are placed only then.</summary>
        public bool Ready
        {
            get
            {
                var root = Document != null ? Document.rootVisualElement : null;
                return root != null && root.childCount > 0 && root[0].layout.width > 0f;
            }
        }

        /// <summary>Where a world point falls on the panel and the clickable rects (diagnostics).</summary>
        public string Describe(Vector3 worldPoint)
        {
            var root = Document.rootVisualElement;
            var local = transform.InverseTransformPoint(worldPoint) * NotecardView.PixelsPerUnit;
            var rects = string.Join(" ", root.Query<VisualElement>(className: ScriptUiBuilder.ClickableClass).Build().Select(e => e.worldBound.ToString()));
            return $"local {local} size {Size} root {root.worldBound} clickables {rects}";
        }

        /// <summary>
        /// Clicks the clickable element under <paramref name="worldPoint"/>, if any. <paramref name="button"/> is TTS's
        /// value ("-1" left, "-2" right). False when nothing there reacts, so the click goes to the object.
        /// </summary>
        public bool TryUse(Vector3 worldPoint, string button)
        {
            var root = Document != null ? Document.rootVisualElement : null;
            if (root == null)
                return false;
            // Where the point falls on the panel, 0–1 from its top left. Bounds are compared as shares of the root's,
            // because a world-space panel reports them in pixels or in world units depending on when it was last drawn.
            var local = transform.InverseTransformPoint(worldPoint) * NotecardView.PixelsPerUnit;
            var point = new Vector2((local.x + Size.x / 2f) / Size.x, (Size.y / 2f - local.y) / Size.y);
            var whole = root.worldBound;
            if (!(whole.width > 0f && whole.height > 0f))
                return false;
            VisualElement best = null;
            foreach (var e in root.Query<VisualElement>(className: ScriptUiBuilder.ClickableClass).Build())
            {
                var b = e.worldBound;
                var share = new Rect((b.x - whole.x) / whole.width, (b.y - whole.y) / whole.height, b.width / whole.width, b.height / whole.height);
                if (share.Contains(point))
                    best = e;
            }
            if (best?.userData is not List<int> path)
                return false;
            Context.Raise(path, "onClick", button);
            return true;
        }
    }

    /// <summary>
    /// Draws an object's script UI on it. TTS's object UI frame lies on the object's top, its z pointing down into the
    /// object (so <c>position="0 0 -10"</c> lifts an element) and its x mirrored; 100 UI pixels make one local TTS unit.
    /// Inputs on objects show their text but take no typing yet.
    /// </summary>
    public static class ObjectUiView
    {
        public const string RootName = "Script UI";
        /// <summary>One UI pixel in metres before the object's scale: a hundredth of a TTS unit.</summary>
        public const float PixelToMetres = 0.01f * Core.Tts.TtsUnits.TtsUnitsToMeters;
        /// <summary>TTS's object UI frame in the object's local frame.</summary>
        private static readonly Quaternion Frame = Quaternion.Euler(0f, 180f, 0f) * Quaternion.Euler(90f, 0f, 0f);

        private static PanelSettings _panel;
        private static StyleSheet _style;

        public static void Refresh(ObjectView view, ScriptUiContext context) => Refresh(view.transform, view.Model.ScriptUi, context);

        /// <summary>Draws <paramref name="nodes"/> in <paramref name="host"/>'s frame (an object's view, or a zone's anchor).</summary>
        public static void Refresh(Transform host, IReadOnlyList<UiNode> nodes, ScriptUiContext context)
        {
            var old = host.Find(RootName);
            if (old != null)
            {
                old.SetParent(null);
                Object.Destroy(old.gameObject);
            }
            if (nodes.Count == 0)
                return;
            var root = new GameObject(RootName).transform;
            root.SetParent(host, false);
            for (var i = 0; i < nodes.Count; i++)
                if (nodes[i].Active)
                    Surface(root, nodes[i], i, context);
        }

        private static void Surface(Transform root, UiNode node, int index, ScriptUiContext context)
        {
            var size = new Vector2(Pixels(node.Get("width")), Pixels(node.Get("height")));
            var go = new GameObject($"UI {node.Id ?? index.ToString()}");
            var t = go.transform;
            t.SetParent(root, false);
            var position = ScriptUiBuilder.Vector(node.Get("position"), 3) ?? new[] { 0f, 0f, 0f };
            var rotation = ScriptUiBuilder.Vector(node.Get("rotation"), 3) ?? new[] { 0f, 0f, 0f };
            var scale = ScriptUiBuilder.Vector(node.Get("scale"), 3) ?? new[] { 1f, 1f, 1f };
            t.localPosition = Frame * new Vector3(position[0], position[1], position[2]) * PixelToMetres;
            t.localRotation = Frame * Quaternion.Euler(rotation[0], rotation[1], rotation[2]);
            // A world-space panel draws 100 pixels per unit; one UI pixel is PixelToMetres.
            var k = PixelToMetres * NotecardView.PixelsPerUnit;
            t.localScale = new Vector3(k * scale[0], k * scale[1], k * (scale.Length > 2 && scale[2] != 0f ? scale[2] : 1f));

            var document = go.AddComponent<UIDocument>();
            document.panelSettings = Panel();
            document.worldSpaceSizeMode = WorldSpaceSizeMode.Fixed;
            document.worldSpaceSize = size;
            var panelRoot = document.rootVisualElement;
            if (panelRoot == null)
                return;
            panelRoot.pickingMode = PickingMode.Ignore;
            if (_style == null)
                _style = Resources.Load<StyleSheet>("UI/ScriptUi");
            if (_style != null && !panelRoot.styleSheets.Contains(_style))
                panelRoot.styleSheets.Add(_style);

            // The element fills its own panel: its place on the object is the surface's transform.
            var placed = node.Clone();
            foreach (var key in new[] { "position", "rotation", "scale", "rectAlignment", "offsetXY" })
                placed.Attributes.Remove(key);
            placed.Attributes["width"] = "100%";
            placed.Attributes["height"] = "100%";
            ScriptUiBuilder.BuildOne(panelRoot, placed, index, context);

            var surface = go.AddComponent<ObjectUiSurface>();
            surface.Document = document;
            surface.Size = size;
            surface.Context = context;
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(size.x / NotecardView.PixelsPerUnit, size.y / NotecardView.PixelsPerUnit, 0.05f);
        }

        /// <summary>Pixels, defaulting to XmlUI's 100 (percentages have no parent on an object).</summary>
        private static float Pixels(string value) =>
            value != null && !value.EndsWith("%") && float.TryParse(value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var px) && px > 0f ? px : 100f;

        private static PanelSettings Panel()
        {
            if (_panel != null)
                return _panel;
            _panel = ScriptableObject.CreateInstance<PanelSettings>();
            _panel.name = "Object script UI (world space)";
            _panel.renderMode = PanelRenderMode.WorldSpace;
            foreach (var document in Object.FindObjectsByType<UIDocument>())
                if (document.panelSettings != null && document.panelSettings.themeStyleSheet != null)
                {
                    _panel.themeStyleSheet = document.panelSettings.themeStyleSheet;
                    break;
                }
            return _panel;
        }
    }
}
