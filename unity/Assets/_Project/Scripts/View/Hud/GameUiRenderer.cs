using System.Collections.Generic;
using System.Linq;
using Core.Engine;
using Core.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace TableWorld.View.Hud
{
    /// <summary>
    /// Renders the game's declarative <c>ui</c> panels (T4.6) into the HUD's <c>gameUi</c> layer: one VisualElement tree
    /// per panel, styled by Hud.uss classes. <see cref="Refresh"/> fills texts and counters and enables buttons whose
    /// action is legal now; it is cheap enough to call a few times per second.
    /// </summary>
    public sealed class GameUiRenderer
    {
        private readonly VisualElement _layer;
        private readonly TableView _table;
        private readonly List<(UiElementDef Def, VisualElement Element)> _bound = new List<(UiElementDef, VisualElement)>();
        private readonly List<(PanelDef Def, VisualElement Element)> _panels = new List<(PanelDef, VisualElement)>();

        public GameUiRenderer(VisualElement layer, TableView table)
        {
            _layer = layer;
            _table = table;
        }

        /// <summary>Rebuilds every panel from the table's <c>ui</c> definition.</summary>
        public void Build()
        {
            _layer.Clear();
            _bound.Clear();
            _panels.Clear();
            var ui = _table.Engine?.State.Ui;
            if (ui == null)
                return;
            foreach (var panel in ui.Panels)
            {
                var root = new VisualElement();
                root.AddToClassList("game-ui-panel");
                root.AddToClassList("game-ui-panel--" + Kebab(panel.Anchor.ToString()));
                var body = new VisualElement();
                body.AddToClassList("game-ui-body");
                root.Add(TitleBar(root, body, panel));
                foreach (var def in panel.Elements)
                {
                    var element = Create(def);
                    body.Add(element);
                    _bound.Add((def, element));
                }
                root.Add(body);
                _layer.Add(root);
                _panels.Add((panel, root));
                Restore(root, body, panel);
            }
        }

        // ---- windows: a game's panels move and fold like windows, each player as they like ------------------------

        private string PrefKey(PanelDef panel) => $"tw.panel.{_table.Engine?.State.Game?.Id ?? "table"}.{panel.Id}";

        /// <summary>
        /// The window's bar: the panel's name (what it shows: "Inventory"), dragged to move the window, and a button at
        /// the right that folds the window to its bar and opens it again.
        /// </summary>
        private VisualElement TitleBar(VisualElement root, VisualElement body, PanelDef panel)
        {
            var bar = new VisualElement { tooltip = "Drag to move" };
            bar.AddToClassList("game-ui-titlebar");
            var title = new Label(panel.Title ?? Names.Humanize(panel.Id));
            title.AddToClassList("game-ui-title");
            var fold = new Button { text = "\u2013", tooltip = "Fold or open" };
            fold.AddToClassList("game-ui-fold");
            fold.clicked += () =>
            {
                SetFolded(root, body, !body.ClassListContains("hidden"));
                Save(root, body, panel);
            };
            bar.Add(title);
            bar.Add(fold);

            Vector2? grab = null;
            bar.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0 || e.target == fold)
                    return;
                var at = root.worldBound.position;
                grab = (Vector2)e.position - at;
                PlaceAt(root, at);
                bar.CapturePointer(e.pointerId);
                e.StopPropagation();
            });
            bar.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (grab is not { } offset || root.parent == null)
                    return;
                var area = root.parent.worldBound;
                var to = (Vector2)e.position - offset - area.position;
                // Kept on screen: at least the bar stays reachable.
                to.x = Mathf.Clamp(to.x, 0f, Mathf.Max(0f, area.width - root.worldBound.width));
                to.y = Mathf.Clamp(to.y, 0f, Mathf.Max(0f, area.height - bar.worldBound.height));
                PlaceAt(root, to + area.position);
            });
            bar.RegisterCallback<PointerUpEvent>(e =>
            {
                if (grab == null)
                    return;
                grab = null;
                if (bar.HasPointerCapture(e.pointerId))
                    bar.ReleasePointer(e.pointerId);
                Save(root, body, panel);
            });
            return bar;
        }

        /// <summary>A folded window shows only its bar: the panel's own padding and background go with the body.</summary>
        private static void SetFolded(VisualElement root, VisualElement body, bool folded)
        {
            body.EnableInClassList("hidden", folded);
            root.EnableInClassList("game-ui-panel--folded", folded);
            if (root.Q<Button>(className: "game-ui-fold") is { } fold)
                fold.text = folded ? "+" : "\u2013";
        }

        /// <summary>Puts a window at a panel position (it leaves its anchor; where a player drags it is set from code).</summary>
        private static void PlaceAt(VisualElement root, Vector2 worldPosition)
        {
            var origin = root.parent?.worldBound.position ?? Vector2.zero;
            Unanchor(root);
            root.style.left = worldPosition.x - origin.x;
            root.style.top = worldPosition.y - origin.y;
        }

        /// <summary>
        /// A placed window is held by its top left corner only. Its anchor's other edges (a bottom panel's bottom, which the
        /// HUD lifts while the hand shows) would otherwise stretch it as it moves, so they are cleared here, after every
        /// stylesheet rule.
        /// </summary>
        private static void Unanchor(VisualElement root)
        {
            root.AddToClassList("game-ui-panel--placed");
            root.style.right = StyleKeyword.Auto;
            root.style.bottom = StyleKeyword.Auto;
            root.style.translate = new Translate(0f, 0f);
        }

        private void Save(VisualElement root, VisualElement body, PanelDef panel)
        {
            try
            {
                var placed = root.ClassListContains("game-ui-panel--placed");
                var x = placed ? root.resolvedStyle.left : float.NaN;
                var y = placed ? root.resolvedStyle.top : float.NaN;
                PlayerPrefs.SetString(PrefKey(panel), string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}|{1}|{2}",
                    x, y, body.ClassListContains("hidden") ? 1 : 0));
            }
            catch (System.Exception)
            {
                // A window that cannot remember its place opens at its anchor next time.
            }
        }

        private void Restore(VisualElement root, VisualElement body, PanelDef panel)
        {
            string saved;
            try
            {
                saved = PlayerPrefs.GetString(PrefKey(panel), "");
            }
            catch (System.Exception)
            {
                return;
            }
            var parts = saved.Split('|');
            if (parts.Length != 3)
                return;
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            if (float.TryParse(parts[0], System.Globalization.NumberStyles.Float, culture, out var x) && !float.IsNaN(x) &&
                float.TryParse(parts[1], System.Globalization.NumberStyles.Float, culture, out var y) && !float.IsNaN(y))
            {
                Unanchor(root);
                root.style.left = x;
                root.style.top = y;
            }
            if (parts[2] == "1")
                SetFolded(root, body, true);
        }

        public void Refresh()
        {
            var game = _table.Game;
            if (game == null)
                return;
            var viewer = _table.LocalSeat;
            foreach (var (def, root) in _panels)
                root.EnableInClassList("hidden", def.VisibleTo.Count > 0 && !def.VisibleTo.Contains(viewer));

            foreach (var (def, element) in _bound)
            {
                switch (def.Type)
                {
                    case UiElementType.Label:
                        ((Label)element).text = game.Format(def.Text ?? "", viewer);
                        break;
                    case UiElementType.Counter:
                        element.Q<Label>(className: "game-ui-counter-value").text = def.Value == null ? "" : game.Evaluate(def.Value, viewer)?.ToString() ?? "–";
                        break;
                    case UiElementType.Button:
                        var button = (Button)element;
                        button.text = game.Format(def.Text ?? def.Action ?? "", viewer);
                        button.SetEnabled(IsLegal(game, def, viewer));
                        break;
                }
            }
        }

        private VisualElement Create(UiElementDef def)
        {
            switch (def.Type)
            {
                case UiElementType.Counter:
                {
                    var row = new VisualElement();
                    row.AddToClassList("game-ui-counter");
                    var caption = new Label(def.Text ?? "");
                    caption.AddToClassList("game-ui-label");
                    var value = new Label();
                    value.AddToClassList("game-ui-counter-value");
                    row.Add(caption);
                    row.Add(value);
                    return row;
                }
                case UiElementType.Button:
                {
                    var button = new Button(() => Press(def));
                    button.AddToClassList("hud-button");
                    return button;
                }
                case UiElementType.Image:
                {
                    var image = new VisualElement();
                    image.AddToClassList("game-ui-image");
                    // The texture is loaded at runtime from the game package, so it can only be set from code.
                    if (_table.Assets.Texture(def.Asset) is { } texture)
                        image.style.backgroundImage = new StyleBackground(texture);
                    return image;
                }
                default:
                {
                    var label = new Label();
                    label.AddToClassList("game-ui-label");
                    return label;
                }
            }
        }

        private void Press(UiElementDef def)
        {
            var game = _table.Game;
            var seat = game.SeatFor(def.Seat ?? "$viewer", _table.LocalSeat);
            _table.Execute(new PerformActionCommand { Actor = seat, ActionId = def.Action, Params = new Dictionary<string, string>(def.Params) });
        }

        private bool IsLegal(GameEngine game, UiElementDef def, string viewer)
        {
            if (!(game.Session is { Started: true, Over: false }) || def.Action == null)
                return false;
            var seat = game.SeatFor(def.Seat ?? "$viewer", viewer);
            // Online each player acts only for their own seat (the host refuses anything else).
            if (seat == null || (_table.Networked && seat != viewer))
                return false;
            var legal = game.LegalActions(seat).FirstOrDefault(a => a.ActionId == def.Action);
            return legal != null && legal.Choices.Any(c => def.Params.All(p => c.TryGetValue(p.Key, out var v) && v == p.Value));
        }

        private static string Kebab(string pascal) =>
            string.Concat(pascal.Select((c, i) => char.IsUpper(c) && i > 0 ? "-" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
    }
}
