using System.Collections.Generic;
using System.Linq;
using Core.Engine;
using Core.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace TableWorld.View.ScriptUi
{
    /// <summary>
    /// Draws the global script UI on the screen (M5, ADR 0004), under the HUD. Rebuilt at most once a frame after the
    /// scripts change it (a script often changes many elements at once), when a table arrives, and when the viewer's seat
    /// changes (elements can be visible to some colours only). Using an element sends a <c>UiEventCommand</c>.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class ScriptUiView : MonoBehaviour
    {
        /// <summary>XmlUI is laid out on a 1920 × 1080 canvas that scales with the screen.</summary>
        public static readonly Vector2Int ReferenceResolution = new Vector2Int(1920, 1080);

        private TableView _table;
        private VisualElement _root;
        private bool _dirty = true;
        private TableState _drawnState;
        private string _drawnViewer;
        private bool _drawnHost;
        private ScriptUiContext _context;

        /// <summary>Times the global UI was rebuilt (tests).</summary>
        public int Builds { get; private set; }
        public VisualElement Root => _root;

        /// <summary>
        /// Creates the view beside the HUD: its own panel (sorted under the HUD's), sharing the HUD's theme. At the scene
        /// root: a UIDocument under another one would join that document's panel.
        /// </summary>
        public static ScriptUiView Create(PanelSettings hud, TableView table)
        {
            var go = new GameObject("Script UI");
            var panel = ScriptableObject.CreateInstance<PanelSettings>();
            panel.name = "Script UI (screen)";
            panel.themeStyleSheet = hud != null ? hud.themeStyleSheet : null;
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = ReferenceResolution;
            panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panel.match = 0.5f;
            panel.sortingOrder = hud != null ? hud.sortingOrder - 1 : -1;
            var document = go.AddComponent<UIDocument>();
            document.panelSettings = panel;
            var view = go.AddComponent<ScriptUiView>();
            view.Attach(table);
            return view;
        }

        private void OnEnable()
        {
            _root = GetComponent<UIDocument>().rootVisualElement;
            _root.pickingMode = PickingMode.Ignore;
            var style = Resources.Load<StyleSheet>("UI/ScriptUi");
            if (style != null && !_root.styleSheets.Contains(style))
                _root.styleSheets.Add(style);
            _root.AddToClassList("script-ui-root");
            if (_table != null)
                _table.CommandExecuted += OnCommand;
            _dirty = true;
        }

        /// <summary>Follows <paramref name="table"/> (AddComponent runs OnEnable before the creator can set it).</summary>
        private void Attach(TableView table)
        {
            if (_table != null)
                _table.CommandExecuted -= OnCommand;
            _table = table;
            if (_table != null && isActiveAndEnabled)
                _table.CommandExecuted += OnCommand;
            _dirty = true;
        }

        private void OnDisable()
        {
            if (_table != null)
                _table.CommandExecuted -= OnCommand;
        }

        private void OnCommand(Command command, CommandResult result)
        {
            if (result.Ok && result.Events.Any(e => e is ScriptUiChanged changed && changed.ObjectId == ""))
                _dirty = true;
        }

        private void LateUpdate()
        {
            var state = _table != null ? _table.Engine?.State : null;
            var viewer = ViewerColor();
            var host = _table != null && _table.HostsTable;
            if (state != _drawnState || viewer != _drawnViewer || host != _drawnHost)
                _dirty = true;
            if (!_dirty || _root == null)
                return;
            _dirty = false;
            _drawnState = state;
            _drawnViewer = viewer;
            _drawnHost = host;
            Rebuild(state);
        }

        private string ViewerColor()
        {
            var seat = _table?.LocalSeat;
            return seat == null ? null : _table.Engine?.State.Players.Seats.FirstOrDefault(s => s.Id == seat)?.Color;
        }

        private void Rebuild(TableState state)
        {
            // Rebuilding drops the element being typed into: keep its path and text and give them back.
            var focused = _root.panel?.focusController?.focusedElement as VisualElement;
            var typing = focused as TextField ?? focused?.GetFirstAncestorOfType<TextField>();
            var typingPath = typing?.userData as List<int>;
            var typingText = typing?.value;

            Builds++;
            _context ??= new ScriptUiContext();
            _context.Texture = id => _table?.Assets?.Texture(id);
            _context.ViewerColor = _drawnViewer;
            _context.ViewerIsHost = _drawnHost;
            _context.Raise = (path, evt, value) => _table?.Execute(new UiEventCommand
            {
                Path = path, Event = evt, Value = value, Actor = _table.LocalSeat,
            });
            ScriptUiBuilder.Build(_root, state?.ScriptUi ?? new List<UiNode>(), _context);

            if (typingPath != null && Find(_root, typingPath) is TextField again)
            {
                again.SetValueWithoutNotify(typingText);
                again.Focus();
            }
        }

        private static VisualElement Find(VisualElement root, List<int> path)
        {
            foreach (var e in root.Query<VisualElement>().Build())
                if (e.userData is List<int> p && p.SequenceEqual(path))
                    return e;
            return null;
        }

        /// <summary>Is the pointer (screen pixels, origin bottom left) over an element that catches it?</summary>
        public bool IsOver(Vector2 screenPosition)
        {
            if (_root?.panel == null)
                return false;
            var panelPosition = RuntimePanelUtils.ScreenToPanel(_root.panel, new Vector2(screenPosition.x, Screen.height - screenPosition.y));
            var picked = _root.panel.Pick(panelPosition);
            return picked != null && picked != _root;
        }

        /// <summary>Is the player typing into a script's input field?</summary>
        public bool Typing
        {
            get
            {
                var focused = _root?.panel?.focusController?.focusedElement as VisualElement;
                return focused != null && (focused is TextField || focused.GetFirstAncestorOfType<TextField>() != null);
            }
        }
    }
}
