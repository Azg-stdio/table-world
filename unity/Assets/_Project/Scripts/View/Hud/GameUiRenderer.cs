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
                if (panel.Title != null)
                {
                    var title = new Label(panel.Title);
                    title.AddToClassList("game-ui-title");
                    root.Add(title);
                }
                foreach (var def in panel.Elements)
                {
                    var element = Create(def);
                    root.Add(element);
                    _bound.Add((def, element));
                }
                _layer.Add(root);
                _panels.Add((panel, root));
            }
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

        private static bool IsLegal(GameEngine game, UiElementDef def, string viewer)
        {
            if (!(game.Session is { Started: true, Over: false }) || def.Action == null)
                return false;
            var seat = game.SeatFor(def.Seat ?? "$viewer", viewer);
            if (seat == null)
                return false;
            var legal = game.LegalActions(seat).FirstOrDefault(a => a.ActionId == def.Action);
            return legal != null && legal.Choices.Any(c => def.Params.All(p => c.TryGetValue(p.Key, out var v) && v == p.Value));
        }

        private static string Kebab(string pascal) =>
            string.Concat(pascal.Select((c, i) => char.IsUpper(c) && i > 0 ? "-" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
    }
}
