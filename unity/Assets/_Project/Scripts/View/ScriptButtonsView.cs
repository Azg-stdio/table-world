using Core.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace TableWorld.View
{
    /// <summary>One script button on an object (M5): its index in the model's list, for the click.</summary>
    public sealed class ScriptButtonView : MonoBehaviour
    {
        public ObjectView Owner { get; internal set; }
        public int Index { get; internal set; }
        public ButtonDef Def { get; internal set; }
        internal VisualElement Shade;

        /// <summary>Darkens the button while the pointer is on it, like TTS.</summary>
        public void SetHovered(bool hovered) => Shade?.EnableInClassList("script-button-shade--hover", hovered);

        private UIRenderer _renderer;

        /// <summary>
        /// Buttons drawn in the same place show in the order the script made them, as in TTS (Riftbound writes deck
        /// counts as eight black copies around a white one). Transparent panels are otherwise sorted by distance, and an
        /// outline copy a few millimetres nearer the camera covered the white one. The panel's renderer is made by the
        /// document after the button is built (and the document's own sorting order does not reach it), so it is set here.
        /// </summary>
        private void LateUpdate()
        {
            if (_renderer == null)
                _renderer = GetComponentInChildren<UIRenderer>();
            if (_renderer != null && _renderer.sortingOrder != Index)
                _renderer.sortingOrder = Index;
        }
    }

    /// <summary>
    /// Draws the buttons scripts put on an object (M5, TTS <c>createButton</c>): each one a world-space UI Toolkit panel
    /// lying on the object with a trigger collider, so the table's pointer ray finds it and a click becomes a
    /// <c>ClickButtonCommand</c>. A button 0 wide or high is a label only (a common TTS trick for text on objects).
    /// </summary>
    public static class ScriptButtonsView
    {
        public const string RootName = "Script buttons";
        /// <summary>Panel pixels per metre: enough for sharp text on a 6 mm button.</summary>
        private const float PixelsPerMetre = 4000f;
        /// <summary>Above its spot, so it does not fight the surface it lies on.</summary>
        private const float Lift = 0.0004f;
        /// <summary>Each later button sits this much further out, so it never fights an earlier one in depth.</summary>
        private const float LayerStep = 0.00005f;

        private static PanelSettings _panel;
        private static StyleSheet _style;

        /// <summary>Rebuilds the buttons of <paramref name="view"/> from its model.</summary>
        public static void Refresh(ObjectView view)
        {
            var buttons = view.Model.Buttons;
            var root = view.transform.Find(RootName);
            if (root != null)
            {
                // Rebuilt every time: scripts replace the whole list, and few objects carry many buttons.
                root.SetParent(null);
                Object.Destroy(root.gameObject);
            }
            if (buttons.Count == 0)
                return;
            root = new GameObject(RootName).transform;
            root.SetParent(view.transform, false);
            for (var i = 0; i < buttons.Count; i++)
                Build(view, root, i, buttons[i]);
        }

        private static void Build(ObjectView view, Transform root, int index, ButtonDef def)
        {
            var go = new GameObject($"Button {index}");
            var t = go.transform;
            t.SetParent(root, false);
            // Outward on the side it is on: a face-down deck carries its buttons on its underside (TTS y below 0).
            var outward = def.Position.Y < 0f ? Vector3.down : Vector3.up;
            t.localPosition = def.Position.ToUnity() + outward * (Lift + index * LayerStep);
            t.localRotation = Quaternion.Euler(def.Rotation.X, def.Rotation.Y, def.Rotation.Z);
            var clickable = def.Width > 0f && def.Height > 0f;
            var button = go.AddComponent<ScriptButtonView>();
            button.Owner = view;
            button.Index = index;
            button.Def = def;
            if (clickable)
            {
                var box = go.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = new Vector3(def.Width, 0.002f, def.Height);
            }

            // A label-only button still needs room for its text.
            var lines = Mathf.Max(1, (def.Label ?? "").Split('\n').Length);
            var longest = 1;
            foreach (var line in (def.Label ?? "").Split('\n'))
                longest = Mathf.Max(longest, line.Length);
            var size = clickable
                ? new Vector2(def.Width, def.Height)
                : new Vector2(longest * def.FontSize * 0.6f, lines * def.FontSize * 1.3f);

            var face = new GameObject("Face").transform;
            face.SetParent(t, false);
            // TTS buttons read upright from +z at rotation 0, like its card images (Riftbound's south-side counters and
            // recycle buttons lie at yaw 180 unrotated); object XmlUI is the other way round (ObjectUiView).
            face.localRotation = Quaternion.Euler(0f, 180f, 0f) * Quaternion.Euler(90f, 0f, 0f);
            face.localScale = Vector3.one * (NotecardView.PixelsPerUnit / PixelsPerMetre);
            var document = face.gameObject.AddComponent<UIDocument>();
            document.panelSettings = Panel();
            document.worldSpaceSizeMode = WorldSpaceSizeMode.Fixed;
            document.worldSpaceSize = size * PixelsPerMetre;
            var panelRoot = document.rootVisualElement;
            if (panelRoot == null)
                return;
            panelRoot.pickingMode = PickingMode.Ignore;
            if (_style == null)
                _style = Resources.Load<StyleSheet>("UI/ScriptButton");
            if (_style != null && !panelRoot.styleSheets.Contains(_style))
                panelRoot.styleSheets.Add(_style);

            var body = new VisualElement { pickingMode = PickingMode.Ignore };
            body.AddToClassList("script-button");
            body.EnableInClassList("script-button--bare", !clickable);
            // The script decides these colours, so they are set from its data rather than in USS.
            if (clickable)
                body.style.backgroundColor = def.Color.ToColor(Color.white);
            var label = new Label(def.Label ?? "") { pickingMode = PickingMode.Ignore, enableRichText = false };
            label.AddToClassList("script-button-label");
            label.style.color = def.FontColor.ToColor(Color.black);
            label.style.fontSize = Mathf.Max(1f, def.FontSize * PixelsPerMetre * 0.75f);
            var shade = new VisualElement { pickingMode = PickingMode.Ignore };
            shade.AddToClassList("script-button-shade");
            body.Add(label);
            body.Add(shade);
            button.Shade = shade;
            panelRoot.Add(body);
        }

        /// <summary>The world-space panel settings, sharing the HUD's theme (its font and defaults).</summary>
        private static PanelSettings Panel()
        {
            if (_panel != null)
                return _panel;
            _panel = ScriptableObject.CreateInstance<PanelSettings>();
            _panel.name = "Script buttons (world space)";
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
