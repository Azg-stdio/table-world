using UnityEngine;
using UnityEngine.UIElements;

namespace TableWorld.View.Hud
{
    /// <summary>
    /// A card shown in the HUD (the hand bar, a choice in the action picker, the search window) that Alt can look at, as
    /// it looks at pieces on the table: the element carries it as its <see cref="VisualElement.userData"/>, and holding
    /// Alt over the element shows <see cref="Image"/> big in the close-up panel (<see cref="TableOverlay"/>).
    /// </summary>
    public sealed class UiLook
    {
        public Sprite Image { get; }
        public string Caption { get; }

        private UiLook(Sprite image, string caption)
        {
            Image = image;
            Caption = caption;
        }

        /// <summary>Lets Alt look at <paramref name="image"/> over <paramref name="element"/>.</summary>
        public static void Mark(VisualElement element, Sprite image, string caption)
        {
            if (image != null)
                element.userData = new UiLook(image, caption);
        }

        /// <summary>The card under <paramref name="screen"/> (Input System pixels, origin bottom left), or null.</summary>
        public static UiLook At(IPanel panel, Vector2 screen)
        {
            if (panel == null)
                return null;
            var point = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screen.x, Screen.height - screen.y));
            for (var element = panel.Pick(point); element != null; element = element.parent)
                if (element.userData is UiLook look)
                    return look;
            return null;
        }
    }
}
