using System;
using System.Collections.Generic;
using System.Linq;
using Core.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace TableWorld.View.Hud
{
    /// <summary>
    /// Before a game starts, a marker over each seat on the table (TTS's seat plus): a free seat shows a "+" in its colour
    /// that sits you there, a taken one who sits there. Start then plays the seated seats, so the player count comes from
    /// who sat down. What a click does and who sits where belong to <see cref="HudController"/>.
    /// </summary>
    public sealed class SeatJoinView
    {
        private readonly VisualElement _layer;
        private readonly TableView _table;
        private readonly Func<bool> _active;
        /// <summary>For a seat: who sits there ("Red (you)"), or null when it is free.</summary>
        private readonly Func<Seat, string> _sitter;
        /// <summary>Whether a click on the seat's marker does something (a free seat, or your own at a hot-seat table).</summary>
        private readonly Func<Seat, bool> _clickable;
        private readonly Action<string> _click;
        private readonly Dictionary<string, (VisualElement Marker, Label Text, string Shown)> _markers =
            new Dictionary<string, (VisualElement, Label, string)>();

        public SeatJoinView(VisualElement root, TableView table, Func<bool> active, Func<Seat, string> sitter, Func<Seat, bool> clickable, Action<string> click)
        {
            _table = table;
            _active = active;
            _sitter = sitter;
            _clickable = clickable;
            _click = click;
            _layer = new VisualElement { name = "seatJoins", pickingMode = PickingMode.Ignore };
            _layer.AddToClassList("seat-joins");
            root.Insert(0, _layer);
        }

        /// <summary>The marker over a seat, or null (tests click it).</summary>
        public VisualElement MarkerOf(string seatId) => _markers.TryGetValue(seatId, out var m) ? m.Marker : null;

        public void Update(Camera camera)
        {
            var engine = _table.Engine;
            var show = engine != null && camera != null && _layer.panel != null && _active();
            _layer.EnableInClassList("hidden", !show);
            var seats = engine == null ? new List<Seat>() : engine.State.Players.Seats;
            foreach (var gone in _markers.Keys.Where(id => seats.All(s => s.Id != id)).ToList())
            {
                _markers[gone].Marker.RemoveFromHierarchy();
                _markers.Remove(gone);
            }
            if (!show)
                return;
            foreach (var seat in seats)
            {
                if (!_markers.TryGetValue(seat.Id, out var marker))
                {
                    var element = new VisualElement();
                    element.AddToClassList("seat-join");
                    var swatch = new VisualElement { pickingMode = PickingMode.Ignore };
                    swatch.AddToClassList("seat-swatch");
                    swatch.AddToClassList("seat-swatch--" + seat.Color.ToLowerInvariant());
                    var text = new Label { pickingMode = PickingMode.Ignore, enableRichText = false };
                    text.AddToClassList("seat-join-label");
                    element.Add(swatch);
                    element.Add(text);
                    var id = seat.Id;
                    element.RegisterCallback<ClickEvent>(_ => _click(id));
                    _layer.Add(element);
                    marker = (element, text, null);
                }
                var sitter = _sitter(seat);
                var clickable = _clickable(seat);
                var key = sitter + "|" + clickable;
                if (key != marker.Shown)
                {
                    marker.Shown = key;
                    marker.Text.text = sitter ?? "+";
                    marker.Marker.EnableInClassList("seat-join--free", sitter == null);
                    marker.Marker.EnableInClassList("seat-join--seated", sitter != null);
                    marker.Marker.EnableInClassList("seat-join--fixed", !clickable);
                    marker.Marker.pickingMode = clickable ? PickingMode.Position : PickingMode.Ignore;
                    var name = Names.Of(engine.State, seat);
                    marker.Marker.tooltip = sitter == null ? Ui.T("Sit at {name}", ("name", name)) : clickable ? Ui.T("Leave {name}", ("name", name)) : "";
                }
                var hand = engine.State.Zones.FirstOrDefault(z => z.Id == seat.HandZone);
                // Over the seat's hand, at its edge of the table; a seat without a hand has no place to show it.
                var place = hand?.Transform.Position;
                var screen = place is { } p ? camera.WorldToScreenPoint(new Vector3(p.X, p.Y + 0.06f, p.Z)) : Vector3.back;
                var visible = screen.z > 0f;
                marker.Marker.EnableInClassList("hidden", !visible);
                if (visible)
                {
                    var at = RuntimePanelUtils.ScreenToPanel(_layer.panel, new Vector2(screen.x, Screen.height - screen.y));
                    // Positions follow the camera every frame, so they are set in code; everything else is Hud.uss.
                    marker.Marker.style.left = at.x;
                    marker.Marker.style.top = at.y;
                }
                _markers[seat.Id] = marker;
            }
        }
    }
}
