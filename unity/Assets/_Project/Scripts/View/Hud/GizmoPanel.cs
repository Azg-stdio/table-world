using System;
using System.Globalization;
using Core.Engine;
using Core.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace TableWorld.View.Hud
{
    /// <summary>
    /// The gizmo (G28, TTS's precise move / rotate / scale): exact position across the table and height in centimetres,
    /// turn and tilt in degrees, and scale, for one object. Enter or Apply moves it with ordinary commands, so it is
    /// shared, logged and undoable like any move. Opened by the Gizmo tool or the object menu. Styling lives in Hud.uss
    /// (gizmo-*).
    /// </summary>
    public sealed class GizmoPanel
    {
        private const string Hidden = "hidden";

        private readonly VisualElement _panel;
        private readonly Label _title;
        private readonly TableView _table;
        private readonly Action<string, bool> _tell;
        private readonly TextField _x, _z, _height, _turn, _tiltX, _tiltZ, _scale;
        private string _objectId;

        public bool Visible => !_panel.ClassListContains(Hidden);
        public string ObjectId => _objectId;

        public GizmoPanel(VisualElement root, TableView table, Action<string, bool> tell)
        {
            _table = table;
            _tell = tell;
            _panel = root.Q("gizmoPanel");
            _title = root.Q<Label>("gizmoTitle");
            var fields = root.Q("gizmoFields");
            _x = Field(fields, Ui.T("Across (cm)"));
            _z = Field(fields, Ui.T("Forward (cm)"));
            _height = Field(fields, Ui.T("Height (cm)"));
            _turn = Field(fields, Ui.T("Turn (°)"));
            _tiltX = Field(fields, Ui.T("Tilt forward (°)"));
            _tiltZ = Field(fields, Ui.T("Tilt sideways (°)"));
            _scale = Field(fields, Ui.T("Scale (×)"));
            root.Q<Button>("gizmoApply").clicked += () => Apply();
            root.Q<Button>("gizmoClose").clicked += Hide;
        }

        private TextField Field(VisualElement into, string label)
        {
            var row = new VisualElement();
            row.AddToClassList("gizmo-row");
            var name = new Label(label);
            name.AddToClassList("gizmo-label");
            var field = new TextField();
            field.AddToClassList("settings-text");
            field.AddToClassList("gizmo-field");
            field.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                    Apply();
            });
            row.Add(name);
            row.Add(field);
            into.Add(row);
            return field;
        }

        /// <summary>Shows the panel for <paramref name="view"/> with its current pose.</summary>
        public void Open(ObjectView view)
        {
            if (view == null)
                return;
            _objectId = view.Id;
            var model = view.Model;
            var t = model.Transform;
            _title.text = string.IsNullOrEmpty(model.Name) ? $"{model.Type} {model.Id}" : model.Name;
            _x.value = Number(t.Position.X * 100f);
            _z.value = Number(t.Position.Z * 100f);
            _height.value = Number(t.Position.Y * 100f);
            _turn.value = Number(Geometry.NormalizeAngle(t.Rotation.Y));
            _tiltX.value = Number(Geometry.NormalizeAngle(t.Rotation.X));
            _tiltZ.value = Number(Geometry.NormalizeAngle(t.Rotation.Z));
            _scale.value = Number(t.Scale.X);
            _panel.RemoveFromClassList(Hidden);
        }

        public void Hide()
        {
            _panel.AddToClassList(Hidden);
            _objectId = null;
        }

        /// <summary>Sets a field as a player would type it (tests); field: x, z, height, turn, tiltX, tiltZ, scale.</summary>
        public void Type(string field, string text)
        {
            var target = field switch { "x" => _x, "z" => _z, "height" => _height, "turn" => _turn, "tiltX" => _tiltX, "tiltZ" => _tiltZ, _ => _scale };
            target.value = text;
        }

        /// <summary>Moves, turns and scales the object to the typed values; false (with a message) when one is not a number.</summary>
        public bool Apply()
        {
            var model = _objectId == null ? null : _table.Engine.Find(_objectId);
            if (model == null)
            {
                Hide();
                return false;
            }
            if (!(Parse(_x.value) is { } x && Parse(_z.value) is { } z && Parse(_height.value) is { } h && Parse(_turn.value) is { } turn &&
                  Parse(_tiltX.value) is { } tiltX && Parse(_tiltZ.value) is { } tiltZ && Parse(_scale.value) is { } scale))
            {
                _tell(Ui.T("Type numbers (for example 12.5)."), true);
                return false;
            }
            var position = new Vec3(Mathf.Clamp(x / 100f, -5f, 5f), Mathf.Clamp(h / 100f, 0f, 3f), Mathf.Clamp(z / 100f, -5f, 5f));
            var moved = _table.Execute(new MoveCommand { ObjectId = model.Id, Position = position, Rotation = new Vec3(tiltX, turn, tiltZ), Actor = _table.LocalSeat });
            if (!moved.Ok)
                return false;
            var current = model.Transform.Scale.X;
            if (scale > 0f && Mathf.Abs(scale - current) > 0.0001f)
                _table.Execute(new ScaleCommand { ObjectId = model.Id, Factor = scale / current, Actor = _table.LocalSeat });
            Open(_table.ViewOf(model.Id));
            return true;
        }

        /// <summary>Accepts a decimal point or comma, whatever the system's language.</summary>
        private static float? Parse(string text) =>
            float.TryParse((text ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && !float.IsNaN(v) && !float.IsInfinity(v) ? v : (float?)null;

        private static string Number(float v) => (Mathf.Round(v * 100f) / 100f).ToString("0.##", CultureInfo.InvariantCulture);
    }
}
