using Core.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace TableWorld.View
{
    /// <summary>One script input on an object (M5): its index in the model's list, for typing.</summary>
    public sealed class ScriptInputView : MonoBehaviour
    {
        public ObjectView Owner { get; internal set; }
        public int Index { get; internal set; }
        public InputDef Def { get; internal set; }
    }

    /// <summary>
    /// Draws the text fields scripts put on objects (M5, TTS <c>createInput</c>) like their buttons: a world-space panel
    /// with the text (or the grey placeholder) and a trigger collider. Clicking one opens the HUD's input editor, which
    /// sends what the player types.
    /// </summary>
    public static class ScriptInputsView
    {
        public const string RootName = "Script inputs";
        private const float PixelsPerMetre = 4000f;
        private const float Lift = 0.0004f;

        private static PanelSettings _panel;
        private static StyleSheet _style;

        public static void Refresh(ObjectView view)
        {
            var root = view.transform.Find(RootName);
            if (root != null)
            {
                root.SetParent(null);
                Object.Destroy(root.gameObject);
            }
            var inputs = view.Model.Inputs;
            if (inputs.Count == 0)
                return;
            root = new GameObject(RootName).transform;
            root.SetParent(view.transform, false);
            for (var i = 0; i < inputs.Count; i++)
                Build(view, root, i, inputs[i]);
        }

        private static void Build(ObjectView view, Transform root, int index, InputDef def)
        {
            if (def.Width <= 0f || def.Height <= 0f)
                return;
            var go = new GameObject($"Input {index}");
            var t = go.transform;
            t.SetParent(root, false);
            t.localPosition = def.Position.ToUnity() + Vector3.up * Lift;
            t.localRotation = Quaternion.Euler(def.Rotation.X, def.Rotation.Y, def.Rotation.Z);
            var input = go.AddComponent<ScriptInputView>();
            input.Owner = view;
            input.Index = index;
            input.Def = def;
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(def.Width, 0.002f, def.Height);

            var face = new GameObject("Face").transform;
            face.SetParent(t, false);
            // Upright from +z at rotation 0, like buttons (ScriptButtonsView).
            face.localRotation = Quaternion.Euler(0f, 180f, 0f) * Quaternion.Euler(90f, 0f, 0f);
            face.localScale = Vector3.one * (NotecardView.PixelsPerUnit / PixelsPerMetre);
            var document = face.gameObject.AddComponent<UIDocument>();
            document.panelSettings = Panel();
            document.worldSpaceSizeMode = WorldSpaceSizeMode.Fixed;
            document.worldSpaceSize = new Vector2(def.Width, def.Height) * PixelsPerMetre;
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
            body.AddToClassList("script-input");
            // The script decides these colours and sizes, so they are set from its data rather than in USS.
            body.style.backgroundColor = def.Color.ToColor(Color.white);
            var empty = string.IsNullOrEmpty(def.Value);
            var label = new Label(empty ? def.Label : def.Value) { pickingMode = PickingMode.Ignore, enableRichText = false };
            label.AddToClassList("script-button-label");
            label.EnableInClassList("script-input-placeholder", empty);
            if (!empty)
                label.style.color = def.FontColor.ToColor(Color.black);
            label.style.fontSize = Mathf.Max(1f, def.FontSize * PixelsPerMetre * 0.75f);
            label.style.unityTextAlign = def.Alignment switch
            {
                2 => TextAnchor.MiddleLeft,
                4 => TextAnchor.MiddleRight,
                _ => TextAnchor.MiddleCenter,
            };
            body.Add(label);
            panelRoot.Add(body);
        }

        private static PanelSettings Panel()
        {
            if (_panel != null)
                return _panel;
            _panel = ScriptableObject.CreateInstance<PanelSettings>();
            _panel.name = "Script inputs (world space)";
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
