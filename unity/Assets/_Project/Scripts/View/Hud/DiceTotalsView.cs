using System.Collections.Generic;
using System.Linq;
using Core.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace TableWorld.View.Hud
{
    /// <summary>
    /// Dice zones (<see cref="Zone.DiceTotal"/>): over each one, while dice lie in it, the total and every player's share in
    /// their colour, so a duel or a group fight is read off the table instead of added up by hand.
    /// </summary>
    public sealed class DiceTotalsView
    {
        private readonly VisualElement _layer;
        private readonly TableView _table;
        private readonly Dictionary<string, (VisualElement Box, Label Sum, VisualElement Seats, string Shown)> _boxes =
            new Dictionary<string, (VisualElement, Label, VisualElement, string)>();

        public DiceTotalsView(VisualElement root, TableView table)
        {
            _table = table;
            _layer = new VisualElement { name = "diceTotals", pickingMode = PickingMode.Ignore };
            _layer.AddToClassList("dice-totals");
            root.Insert(0, _layer);
        }

        public void Update(Camera camera)
        {
            var engine = _table.Engine;
            var zones = engine == null ? new List<Zone>() : engine.State.Zones.Where(z => z.DiceTotal).ToList();
            foreach (var gone in _boxes.Keys.Where(id => zones.All(z => z.Id != id)).ToList())
            {
                _boxes[gone].Box.RemoveFromHierarchy();
                _boxes.Remove(gone);
            }
            if (camera == null || _layer.panel == null)
                return;
            foreach (var zone in zones)
            {
                if (!_boxes.TryGetValue(zone.Id, out var box))
                {
                    var element = new VisualElement { pickingMode = PickingMode.Ignore };
                    element.AddToClassList("dice-total");
                    var sum = new Label { pickingMode = PickingMode.Ignore };
                    sum.AddToClassList("dice-total-sum");
                    var seats = new VisualElement { pickingMode = PickingMode.Ignore };
                    seats.AddToClassList("dice-total-seats");
                    element.Add(sum);
                    element.Add(seats);
                    _layer.Add(element);
                    box = (element, sum, seats, null);
                }
                var (total, dice, bySeat) = engine.DiceIn(zone.Id);
                var world = new Vector3(zone.Transform.Position.X, zone.Transform.Position.Y + zone.Size.Y / 2f + 0.02f, zone.Transform.Position.Z);
                var screen = camera.WorldToScreenPoint(world);
                var visible = dice > 0 && screen.z > 0f;
                box.Box.EnableInClassList("hidden", !visible);
                if (visible)
                {
                    var key = total + "|" + string.Join(",", bySeat.OrderBy(p => p.Key).Select(p => p.Key + "=" + p.Value));
                    if (key != box.Shown)
                    {
                        box.Shown = key;
                        box.Sum.text = total.ToString();
                        box.Seats.Clear();
                        foreach (var seat in engine.State.Players.Seats.Where(s => bySeat.ContainsKey(s.Id)))
                        {
                            var chip = new VisualElement { pickingMode = PickingMode.Ignore };
                            chip.AddToClassList("dice-total-seat");
                            var swatch = new VisualElement { pickingMode = PickingMode.Ignore };
                            swatch.AddToClassList("seat-swatch");
                            swatch.AddToClassList("seat-swatch--" + seat.Color.ToLowerInvariant());
                            var value = new Label(bySeat[seat.Id].ToString()) { pickingMode = PickingMode.Ignore };
                            value.AddToClassList("dice-total-value");
                            chip.Add(swatch);
                            chip.Add(value);
                            box.Seats.Add(chip);
                        }
                    }
                    var at = RuntimePanelUtils.ScreenToPanel(_layer.panel, new Vector2(screen.x, Screen.height - screen.y));
                    // Positions follow the camera every frame, so they are set in code; everything else is Hud.uss.
                    box.Box.style.left = at.x;
                    box.Box.style.top = at.y;
                }
                _boxes[zone.Id] = box;
            }
        }
    }
}
