using System;
using System.Collections.Generic;
using System.Linq;
using TableWorld.View.Chalkers;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using TableWorld.View.Settings;

namespace TableWorld.View.Hud
{
    /// <summary>
    /// The emote wheel (T7.4): hold its key (X unless moved: <see cref="GameAction.Emotes"/>), point toward an emote, let go to play it for everyone; or click
    /// one. Letting go near the centre plays nothing. The emote travels with presence (<c>NetworkTableSession.Emote</c>).
    /// </summary>
    public sealed class EmoteWheel
    {
        /// <summary>Distance of the emote labels from the centre, and the dead zone, in panel pixels.</summary>
        public const float Radius = 150f;
        public const float DeadZone = 45f;

        private readonly VisualElement _wheel;
        private readonly VisualElement _ring;
        private readonly Label _center;
        private readonly List<(ChalkerEmote Emote, Label Label)> _items = new List<(ChalkerEmote, Label)>();
        private readonly Action<ChalkerEmote> _play;

        public bool Visible => !_wheel.ClassListContains("hidden");

        /// <summary>The emote the pointer is on, or null.</summary>
        public ChalkerEmote? Hovered { get; private set; }

        public EmoteWheel(VisualElement root, Action<ChalkerEmote> play)
        {
            _play = play;
            _wheel = new VisualElement { name = "emoteWheel", pickingMode = PickingMode.Ignore };
            _wheel.AddToClassList("emote-wheel");
            _wheel.AddToClassList("hidden");
            _ring = new VisualElement { pickingMode = PickingMode.Ignore };
            _ring.AddToClassList("emote-wheel__ring");
            _wheel.Add(_ring);
            _center = new Label { pickingMode = PickingMode.Ignore };
            _center.AddToClassList("emote-wheel__center");
            _ring.Add(_center);
            var emotes = ChalkerLab.Emotes().ToList();
            for (var i = 0; i < emotes.Count; i++)
            {
                var emote = emotes[i];
                var label = new Label(emote.ToString());
                label.AddToClassList("emote-wheel__item");
                // Clockwise from the top. Positions are geometry, set from code.
                var angle = (-90f + i * 360f / emotes.Count) * Mathf.Deg2Rad;
                label.style.left = Radius + 40f + Mathf.Cos(angle) * Radius;
                label.style.top = Radius + 40f + Mathf.Sin(angle) * Radius;
                label.RegisterCallback<ClickEvent>(_ => Choose(emote));
                _ring.Add(label);
                _items.Add((emote, label));
            }
            root.Add(_wheel);
        }

        public void Show()
        {
            _wheel.RemoveFromClassList("hidden");
            SetHovered(null);
        }

        public void Hide() => _wheel.AddToClassList("hidden");

        private void Choose(ChalkerEmote emote)
        {
            Hide();
            _play(emote);
        }

        /// <param name="blocked">Typing in a field, or devices ignored: the key does nothing.</param>
        public void Update(bool blocked)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;
            if (!Visible)
            {
                if (!blocked && KeyBindings.Pressed(keyboard, GameAction.Emotes))
                    Show();
                return;
            }
            var mouse = Mouse.current;
            if (mouse != null && _ring.panel != null)
            {
                var screen = mouse.position.ReadValue();
                var point = RuntimePanelUtils.ScreenToPanel(_ring.panel, new Vector2(screen.x, Screen.height - screen.y));
                SetHovered(Pick(point - _ring.worldBound.center));
            }
            if (!KeyBindings.Held(keyboard, GameAction.Emotes))
            {
                if (Hovered is { } emote)
                    Choose(emote);
                else
                    Hide();
            }
        }

        /// <summary>The emote in the direction of <paramref name="offset"/> from the centre (panel pixels, y down), or null inside the dead zone.</summary>
        public ChalkerEmote? Pick(Vector2 offset)
        {
            if (offset.magnitude < DeadZone)
                return null;
            var angle = Mathf.Atan2(offset.y, offset.x) * Mathf.Rad2Deg + 90f;
            var step = 360f / _items.Count;
            var index = Mathf.RoundToInt(Mathf.Repeat(angle, 360f) / step) % _items.Count;
            return _items[index].Emote;
        }

        private void SetHovered(ChalkerEmote? emote)
        {
            Hovered = emote;
            foreach (var (e, label) in _items)
                label.EnableInClassList("emote-wheel__item--hover", e == emote);
            _center.text = emote?.ToString() ?? "Emote";
        }
    }
}
