using UnityEngine;
using UnityEngine.UIElements;

namespace TableWorld.View
{
    /// <summary>
    /// The printed face of a notecard (G27): a world-space UI Toolkit panel lying on the card, so its title and text use
    /// the HUD's font and wrap like any HUD text. One shared world-space panel serves every notecard.
    /// </summary>
    public static class NotecardView
    {
        /// <summary>Panel size in panel pixels; scaled down onto the card.</summary>
        public static readonly Vector2 PanelSize = new Vector2(440f, 300f);
        public const string FaceName = "Note face";
        /// <summary>World-space UI Toolkit panels draw 100 panel pixels per world unit.</summary>
        public const float PixelsPerUnit = 100f;

        private static PanelSettings _panel;
        private static StyleSheet _style;

        /// <summary>Creates or updates the face on <paramref name="view"/> with the model's name and description.</summary>
        public static void Refresh(ObjectView view, Vector2 cardSize, float thickness)
        {
            var face = view.transform.Find(FaceName);
            if (face == null)
            {
                var go = new GameObject(FaceName);
                face = go.transform;
                face.SetParent(view.transform, false);
                var document = go.AddComponent<UIDocument>();
                document.panelSettings = Panel();
                document.worldSpaceSizeMode = WorldSpaceSizeMode.Fixed;
                document.worldSpaceSize = PanelSize;
            }
            // Lying flat on top of the card, its top edge away from the card's front.
            face.localPosition = new Vector3(0f, thickness / 2f + 0.0003f, 0f);
            face.localRotation = Quaternion.Euler(90f, 0f, 0f);
            face.localScale = Vector3.one * (cardSize.x / PanelSize.x * PixelsPerUnit);
            var root = face.GetComponent<UIDocument>().rootVisualElement;
            if (root == null)
                return;
            root.pickingMode = PickingMode.Ignore;
            if (_style == null)
                _style = Resources.Load<StyleSheet>("UI/Note");
            if (_style != null && !root.styleSheets.Contains(_style))
                root.styleSheets.Add(_style);
            var note = root.Q("note");
            if (note == null)
            {
                note = new VisualElement { name = "note", pickingMode = PickingMode.Ignore };
                note.AddToClassList("note");
                var title = new Label { name = "noteTitle", pickingMode = PickingMode.Ignore, enableRichText = false };
                title.AddToClassList("note-title");
                var body = new Label { name = "noteBody", pickingMode = PickingMode.Ignore, enableRichText = false };
                body.AddToClassList("note-body");
                note.Add(title);
                note.Add(body);
                root.Add(note);
            }
            note.Q<Label>("noteTitle").text = view.Model.Name ?? "";
            note.Q<Label>("noteBody").text = (view.Model.Description ?? "").Trim();
        }

        /// <summary>The world-space panel settings, sharing the HUD's theme (its font and defaults).</summary>
        private static PanelSettings Panel()
        {
            if (_panel != null)
                return _panel;
            _panel = ScriptableObject.CreateInstance<PanelSettings>();
            _panel.name = "Notecards (world space)";
            _panel.renderMode = PanelRenderMode.WorldSpace;
            foreach (var document in Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None))
                if (document.panelSettings != null && document.panelSettings.themeStyleSheet != null)
                {
                    _panel.themeStyleSheet = document.panelSettings.themeStyleSheet;
                    break;
                }
            return _panel;
        }
    }
}
