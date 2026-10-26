using Core.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace TableWorld.View.Hud
{
    /// <summary>
    /// A card shown to everyone (ShowCommand): its face big in the middle of every screen, with who shows it, for
    /// <see cref="Seconds"/> or until clicked. The way an event card is read out to the table.
    /// </summary>
    public sealed class ShowcaseView
    {
        public const float Seconds = 10f;

        private readonly VisualElement _panel;
        private readonly VisualElement _face;
        private readonly Label _caption;
        private readonly CardImages _images = new CardImages();
        private Card _card;
        private float _until;
        private bool _drawn;

        public ShowcaseView(VisualElement root)
        {
            _panel = new VisualElement { name = "showcase" };
            _panel.AddToClassList("showcase");
            _panel.AddToClassList("hidden");
            _face = new VisualElement { pickingMode = PickingMode.Ignore };
            _face.AddToClassList("showcase-face");
            _caption = new Label { pickingMode = PickingMode.Ignore };
            _caption.AddToClassList("showcase-caption");
            _panel.Add(_face);
            _panel.Add(_caption);
            _panel.RegisterCallback<ClickEvent>(_ => Hide());
            root.Add(_panel);
        }

        public bool Visible => !_panel.ClassListContains("hidden");

        public void Show(Card card, string caption)
        {
            _card = card;
            _drawn = false;
            _caption.text = caption;
            _until = Time.unscaledTime + Seconds;
            _face.style.backgroundImage = StyleKeyword.None;
            _panel.RemoveFromClassList("hidden");
        }

        public void Hide()
        {
            _card = null;
            _panel.AddToClassList("hidden");
        }

        /// <summary>Hides it when its time is up, and draws the face once its image is ready (a text card is drawn first).</summary>
        public void Update(AssetLibrary assets)
        {
            if (_card == null)
                return;
            if (Time.unscaledTime > _until)
            {
                Hide();
                return;
            }
            if (_drawn || assets == null)
                return;
            var (asset, index) = assets.Face(_card);
            if (_images.Face(assets, asset, index) is { } sprite)
            {
                // A runtime card image (a cell of a sprite sheet, or a drawn text card) can only be set from code.
                _face.style.backgroundImage = new StyleBackground(sprite);
                _drawn = true;
            }
        }
    }
}
