using System.Collections.Generic;
using System.Linq;
using Core.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace TableWorld.View.Hud
{
    /// <summary>
    /// TTS hand view: the local seat's hand shown at the bottom of the screen, whatever the camera does. Hover raises and
    /// enlarges a card; pressing one picks up the real card (it then follows the pointer like any grab, so dropping it
    /// on the table plays or places it); releasing over the bar puts a card into the hand at that position.
    /// Styling lives in Hud.uss (hand-bar, hand-card); only positions and card images are set from code.
    /// </summary>
    public sealed class HandBar
    {
        /// <summary>Horizontal distance between card centres at the reference resolution when they fit.</summary>
        public const float CardStep = 92f;
        public const float CardWidth = 100f;
        /// <summary>The bar never grows wider than this; more cards overlap.</summary>
        public const float MaxWidth = 900f;
        /// <summary>Releasing within this many pixels above the bar still counts as dropping into the hand.</summary>
        public const float DropMargin = 60f;

        private readonly VisualElement _bar;
        private readonly TableView _table;
        private readonly TableInteraction _interaction;
        private readonly Dictionary<(string Asset, int Index), Sprite> _sprites = new Dictionary<(string, int), Sprite>();
        private AssetLibrary _spritesFor;
        private string _shownKey;

        public HandBar(VisualElement root, TableView table, TableInteraction interaction)
        {
            _bar = root.Q("handBar");
            _table = table;
            _interaction = interaction;
            if (_interaction != null)
            {
                _interaction.HandDrop = DropSlot;
                _interaction.HandCardAt = CardAt;
            }
        }

        /// <summary>The local seat's hand cards, left to right as the owner sees them.</summary>
        private List<TableObject> HandCards(out HandZone hand)
        {
            hand = null;
            var engine = _table.Engine;
            var seat = engine?.State.Players.Seats.FirstOrDefault(s => s.Id == _table.LocalSeat);
            hand = seat == null ? null : engine.State.Zones.FirstOrDefault(z => z.Id == seat.HandZone) as HandZone;
            if (hand == null)
                return new List<TableObject>();
            var yaw = Quaternion.Euler(0f, hand.Transform.Rotation.Y, 0f);
            var center = hand.Transform.Position.ToUnity();
            return engine.ObjectsIn(hand.Id).Where(o => o is Card)
                .OrderBy(o => (Quaternion.Inverse(yaw) * (o.Transform.Position.ToUnity() - center)).x).ToList();
        }

        public void Update()
        {
            if (_bar == null || _table.Engine == null)
                return;
            var cards = HandCards(out _);
            // Hidden while the player carries one of its cards, so the bar shows where it can go back.
            var held = _interaction != null ? _interaction.Held?.Id : null;
            var key = $"{_table.LocalSeat}|{held}|" + string.Join(",", cards.Select(c => c.Id + ":" + ((Card)c).FaceAsset + ((Card)c).FaceIndex));
            if (key == _shownKey && _spritesFor == _table.Assets)
                return;
            _shownKey = key;
            if (_spritesFor != _table.Assets)
            {
                _sprites.Clear();
                _spritesFor = _table.Assets;
            }
            Rebuild(cards, held);
        }

        private void Rebuild(List<TableObject> cards, string held)
        {
            _bar.Clear();
            var shown = cards.Where(c => c.Id != held).ToList();
            _bar.EnableInClassList("hidden", shown.Count == 0 && held == null);
            var step = shown.Count < 2 ? 0f : Mathf.Min(CardStep, (MaxWidth - CardWidth) / (shown.Count - 1));
            var width = shown.Count == 0 ? CardWidth : CardWidth + step * (shown.Count - 1);
            _bar.style.width = width;
            for (var i = 0; i < shown.Count; i++)
            {
                var card = (Card)shown[i];
                var element = new VisualElement { name = "hand-" + card.Id };
                element.AddToClassList("hand-card");
                // Runtime card images (cells of a sprite sheet) can only be set from code.
                if (FaceSprite(card) is { } sprite)
                    element.style.backgroundImage = new StyleBackground(sprite);
                element.style.left = i * step;
                element.tooltip = card.Name;
                // The hovered card comes to the front; the others keep their places (positions are absolute).
                element.RegisterCallback<PointerEnterEvent>(_ => element.BringToFront());
                _bar.Add(element);
            }
        }

        /// <summary>
        /// The id of the hand card under <paramref name="screen"/>, or null. Presses are handled by
        /// <see cref="TableInteraction"/> like every other press, so they work with any input device.
        /// </summary>
        private string CardAt(Vector2 screen)
        {
            if (_bar == null || _bar.panel == null || _bar.ClassListContains("hidden"))
                return null;
            var panelPoint = RuntimePanelUtils.ScreenToPanel(_bar.panel, new Vector2(screen.x, Screen.height - screen.y));
            for (var element = _bar.panel.Pick(panelPoint); element != null; element = element.parent)
                if (element.ClassListContains("hand-card") && element.name.StartsWith("hand-"))
                    return element.name.Substring("hand-".Length);
            return null;
        }

        /// <summary>For a release at <paramref name="screen"/> (Input System pixels, origin bottom left): 0..1 across the bar, or null when not over it.</summary>
        private float? DropSlot(Vector2 screen)
        {
            if (_bar == null || _bar.panel == null || _bar.ClassListContains("hidden"))
                return null;
            var panelPoint = RuntimePanelUtils.ScreenToPanel(_bar.panel, new Vector2(screen.x, Screen.height - screen.y));
            var rect = _bar.worldBound;
            // An empty bar still accepts drops across a card-wide area.
            var left = rect.xMin - CardWidth / 2f;
            var right = rect.xMax + CardWidth / 2f;
            if (panelPoint.x < left || panelPoint.x > right || panelPoint.y < rect.yMin - DropMargin)
                return null;
            return Mathf.InverseLerp(left, right, panelPoint.x);
        }

        /// <summary>The card's face cell as a sprite (cached per asset and cell).</summary>
        private Sprite FaceSprite(Card card)
        {
            var key = (card.FaceAsset, card.FaceIndex);
            if (_sprites.TryGetValue(key, out var cached))
                return cached;
            var texture = _table.Assets?.Texture(card.FaceAsset);
            if (texture == null)
                return null;
            var st = _table.Assets.CellScaleOffset(card.FaceAsset, card.FaceIndex);
            var rect = new Rect(st.z * texture.width, st.w * texture.height, st.x * texture.width, st.y * texture.height);
            var sprite = Sprite.Create(texture, rect, new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            sprite.name = $"{card.FaceAsset}#{card.FaceIndex}";
            _sprites[key] = sprite;
            return sprite;
        }
    }
}
