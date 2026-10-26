using System.Collections.Generic;
using System.Linq;
using Core.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace TableWorld.View.Hud
{
    /// <summary>
    /// TTS hand view: the local seat's hand shown at the bottom of the screen, whatever the camera does. It is a drawer:
    /// dragging its top edge shows as much of the cards as the player likes, from whole cards down to hidden (remembered
    /// on this machine). Hovering a card raises it into full view; pressing one
    /// picks up the real card (it then follows the pointer like any grab, so dropping it on the table plays or places
    /// it); releasing over the bar puts a card into the hand at that position.
    /// Styling lives in Hud.uss (hand-bar, hand-card, hand-grip); sizes, positions and card images are set from code.
    /// </summary>
    public sealed class HandBar
    {
        /// <summary>Card height as a share of the screen, within <see cref="MinCardHeight"/>..<see cref="MaxCardHeight"/> pixels.</summary>
        public const float CardHeightShare = 0.3f;
        public const float MinCardHeight = 150f;
        public const float MaxCardHeight = 340f;
        /// <summary>Space between neighbouring cards when they fit side by side; more cards than fit overlap.</summary>
        public const float CardGap = 8f;
        /// <summary>The bar never grows wider than this share of the screen; more cards overlap.</summary>
        public const float MaxWidthShare = 0.8f;
        /// <summary>The gap under a fully shown card.</summary>
        public const float BottomPadding = 12f;
        /// <summary>Releasing within this many pixels above the bar still counts as dropping into the hand.</summary>
        public const float DropMargin = 60f;
        public const float DefaultReveal = 0.6f;

        private readonly VisualElement _bar;
        /// <summary>The strip along the hand's top edge: drag it to show more or less of the hand, down to none.</summary>
        private readonly VisualElement _grip;
        private float _barWidth;
        private float _screenWidth = 1200f;

        /// <summary>How open the drawer is (0 a strip, 1 whole cards), remembered per player on this machine.</summary>
        public const string RevealPref = "tw.hand.reveal";
        /// <summary>The older shown/hidden switch: a hidden hand opens closed.</summary>
        public const string CollapsedPref = "tw.hand.collapsed";
        private readonly TableView _table;
        private readonly TableInteraction _interaction;
        private readonly CardImages _images = new CardImages();
        private AssetLibrary _spritesFor;
        private string _shownKey;
        private float _reveal;
        private float _openReveal = DefaultReveal;
        private float _cardHeight = MinCardHeight;
        private VisualElement _hovered;
        private float? _dragFrom;

        public HandBar(VisualElement root, TableView table, TableInteraction interaction)
        {
            _bar = root.Q("handBar");
            _grip = new VisualElement { name = "handGrip", tooltip = "Drag up or down: how much of your hand shows" };
            _grip.AddToClassList("hand-grip");
            _grip.AddToClassList("hidden");
            var line = new VisualElement { pickingMode = PickingMode.Ignore };
            line.AddToClassList("hand-grip-line");
            _grip.Add(line);
            _grip.RegisterCallback<PointerDownEvent>(StartDrag);
            _grip.RegisterCallback<PointerMoveEvent>(Drag);
            _grip.RegisterCallback<PointerUpEvent>(EndDrag);
            root.Add(_grip);
            // A new screen size gives new card sizes.
            root.RegisterCallback<GeometryChangedEvent>(_ => _shownKey = null);
            _reveal = LoadReveal();
            if (_reveal > 0.1f)
                _openReveal = _reveal;
            _table = table;
            // A card image that arrives later (decoded, downloaded, or a text card drawn) shows at once.
            if (_table != null)
                _table.AssetRefreshed += _ => _shownKey = null;
            _interaction = interaction;
            if (_interaction != null)
            {
                _interaction.HandDrop = DropSlot;
                _interaction.HandCardAt = CardAt;
            }
            Layout();
        }

        /// <summary>The hand is dragged all the way down: hidden, only its edge at the screen's bottom remains.</summary>
        public bool Collapsed => _reveal <= 0.001f;

        /// <summary>The bar shows cards at the bottom of the screen (the HUD lifts bottom panels above it).</summary>
        public bool Showing => _bar != null && !_bar.ClassListContains("hidden") && !Collapsed;

        /// <summary>How open the drawer is: 0 a strip of each card, 1 the whole cards.</summary>
        public float Reveal => _reveal;

        /// <summary>Hides the hand, or shows it at its last height.</summary>
        public void SetCollapsed(bool collapsed) => SetReveal(collapsed ? 0f : _openReveal);

        public void SetReveal(float reveal)
        {
            _reveal = Mathf.Clamp01(reveal);
            if (_reveal > 0.1f)
                _openReveal = _reveal;
            Layout();
            try
            {
                PlayerPrefs.SetFloat(RevealPref, _reveal);
            }
            catch (System.Exception)
            {
                // A preference that cannot be saved only costs the player a drag next time.
            }
        }

        private static float LoadReveal()
        {
            try
            {
                if (PlayerPrefs.HasKey(RevealPref))
                    return Mathf.Clamp01(PlayerPrefs.GetFloat(RevealPref, DefaultReveal));
                return PlayerPrefs.GetInt(CollapsedPref, 0) == 1 ? 0f : DefaultReveal;
            }
            catch (System.Exception)
            {
                return DefaultReveal;
            }
        }

        /// <summary>Pixels of each resting card in view: none when slid down, the whole card at the top.</summary>
        private float Visible => _reveal * (_cardHeight + BottomPadding);

        /// <summary>Places the drawer, its handle and a hovered card for the current opening.</summary>
        private void Layout()
        {
            if (_bar == null)
                return;
            var full = _cardHeight + BottomPadding;
            _bar.style.height = full;
            _bar.style.bottom = Visible - full;
            if (_grip != null)
            {
                // Along the top of the resting cards, as wide as the hand; at the screen's bottom when it is hidden.
                // Both follow the screen and the cards, so they are set in code.
                _grip.style.left = _screenWidth / 2f - _barWidth / 2f;
                _grip.style.width = _barWidth;
                _grip.style.bottom = Mathf.Max(0f, Visible - 3f);
                _grip.EnableInClassList("hand-grip--hidden-hand", Collapsed);
                // A raised card covers the edge; the edge would otherwise take the pointer and drop the card again.
                _grip.pickingMode = _hovered != null && _dragFrom == null ? PickingMode.Ignore : PickingMode.Position;
            }
            foreach (var card in _bar.Children())
                card.style.translate = card == _hovered ? new Translate(0f, -(full - Visible)) : new Translate(0f, 0f);
        }

        private void StartDrag(PointerDownEvent evt)
        {
            if (evt.button != 0)
                return;
            // Where on the edge it was taken, so the hand does not jump to the pointer.
            _dragFrom = Visible - Above(evt.position);
            _grip.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private void Drag(PointerMoveEvent evt)
        {
            if (_dragFrom is not { } offset)
                return;
            // The cards' tops follow the pointer: up shows more, down to the screen's edge hides them.
            SetReveal((Above(evt.position) + offset) / (_cardHeight + BottomPadding));
        }

        /// <summary>Panel pixels from the screen's bottom edge up to a pointer.</summary>
        private float Above(Vector2 panelPosition) => (_grip.panel?.visualTree.worldBound.height ?? 800f) - panelPosition.y;

        private void EndDrag(PointerUpEvent evt)
        {
            if (_dragFrom == null)
                return;
            _dragFrom = null;
            if (_grip.HasPointerCapture(evt.pointerId))
                _grip.ReleasePointer(evt.pointerId);
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
            var key = $"{_table.LocalSeat}|{held}|" + string.Join(",", cards.Select(c => c.Id + ":" + ((Card)c).FaceAsset + ((Card)c).FaceIndex + TextFaces.FaceId((Card)c)));
            if (key == _shownKey && _spritesFor == _table.Assets)
                return;
            _shownKey = key;
            _spritesFor = _table.Assets;
            Rebuild(cards, held);
        }

        private void Rebuild(List<TableObject> cards, string held)
        {
            _bar.Clear();
            _hovered = null;
            var shown = cards.Where(c => c.Id != held).ToList();
            _bar.EnableInClassList("hidden", shown.Count == 0 && held == null);
            _grip?.EnableInClassList("hidden", shown.Count == 0);
            var screen = _bar.panel?.visualTree.layout ?? new Rect(0f, 0f, 1200f, 800f);
            var screenHeight = float.IsNaN(screen.height) || screen.height <= 0f ? 800f : screen.height;
            var screenWidth = float.IsNaN(screen.width) || screen.width <= 0f ? 1200f : screen.width;
            _cardHeight = Mathf.Clamp(screenHeight * CardHeightShare, MinCardHeight, MaxCardHeight);
            // The cards' own proportions (a TTS card, a tarot card), from the first card.
            var size = shown.Count > 0 ? ((Card)shown[0]).Size ?? Card.DefaultSize : Card.DefaultSize;
            var cardWidth = _cardHeight * size.X / Mathf.Max(0.001f, size.Y);
            var maxWidth = screenWidth * MaxWidthShare;
            // Side by side with a small gap while they fit; beyond the bar's widest they overlap evenly.
            var step = shown.Count < 2 ? 0f : Mathf.Min(cardWidth + CardGap, (maxWidth - cardWidth) / (shown.Count - 1));
            _barWidth = shown.Count == 0 ? cardWidth : cardWidth + step * (shown.Count - 1);
            _screenWidth = screenWidth;
            _bar.style.width = _barWidth;
            for (var i = 0; i < shown.Count; i++)
            {
                var card = (Card)shown[i];
                var element = new VisualElement { name = "hand-" + card.Id };
                element.AddToClassList("hand-card");
                // Runtime card images (cells of a sprite sheet) can only be set from code.
                var (faceAsset, faceIndex) = _table.Assets.Face(card);
                if (_images.Face(_table.Assets, faceAsset, faceIndex) is { } sprite)
                    element.style.backgroundImage = new StyleBackground(sprite);
                element.style.width = cardWidth;
                element.style.height = _cardHeight;
                element.style.bottom = BottomPadding;
                element.style.left = i * step;
                element.tooltip = Names.Of(card);
                // The hovered card comes to the front and rises into full view; the others keep their places.
                element.RegisterCallback<PointerEnterEvent>(_ =>
                {
                    element.BringToFront();
                    _hovered = element;
                    Layout();
                });
                element.RegisterCallback<PointerLeaveEvent>(_ =>
                {
                    if (_hovered == element)
                        _hovered = null;
                    Layout();
                });
                _bar.Add(element);
            }
            Layout();
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
            var cardWidth = _cardHeight * Card.DefaultSize.X / Card.DefaultSize.Y;
            // An empty bar still accepts drops across a card-wide area. The bar's top is the top of what is in view (the
            // rest of it is below the screen edge).
            var left = rect.xMin - cardWidth / 2f;
            var right = rect.xMax + cardWidth / 2f;
            if (panelPoint.x < left || panelPoint.x > right || panelPoint.y < rect.yMin - DropMargin)
                return null;
            return Mathf.InverseLerp(left, right, panelPoint.x);
        }
    }
}
