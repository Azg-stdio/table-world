using UnityEngine;
using UnityEngine.UIElements;

namespace TableWorld.View.Hud
{
    /// <summary>
    /// Keeps a one-line label's text inside its width (M12): when the words are wider than the label may grow (its
    /// max-width), the font shrinks a pixel at a time down to <see cref="MinSize"/>; when they fit again it goes back to
    /// the size its stylesheet gives. Only the size is set from code; everything else stays in USS.
    /// </summary>
    public sealed class TextFit
    {
        public const float MinSize = 12f;

        private readonly Label _label;
        private float _styled = -1f;
        private string _fitText;
        private float _fitWidth = -1f;
        private bool _fitting;

        private TextFit(Label label)
        {
            _label = label;
            label.RegisterCallback<GeometryChangedEvent>(_ => FitNow());
        }

        public static TextFit Attach(Label label) => label == null ? null : new TextFit(label);

        /// <summary>The size the label's text is drawn at now (tests read it).</summary>
        public float Size => _label.resolvedStyle.fontSize;

        /// <summary>Fits again (after the text or the label's class changed), once its new style is resolved: next frame.</summary>
        public void Fit() => _label.schedule.Execute(FitNow);

        /// <summary>Fits now, with the style as last resolved (a layout change fits by itself).</summary>
        public void FitNow()
        {
            if (_fitting || _label.panel == null)
                return;
            // A label allowed to wrap keeps its size: only a one-line label shrinks.
            if (_label.resolvedStyle.whiteSpace != WhiteSpace.NoWrap)
            {
                if (_label.style.fontSize.keyword != StyleKeyword.Null)
                    _label.style.fontSize = StyleKeyword.Null;
                if (_label.style.minWidth.keyword != StyleKeyword.Null)
                    _label.style.minWidth = StyleKeyword.Null;
                _fitText = null;
                return;
            }
            var text = Core.Text.Highlights.Strip(_label.text ?? "");
            var width = _label.contentRect.width;
            if (text == _fitText && Mathf.Abs(width - _fitWidth) < 0.5f)
                return;
            _fitting = true;
            try
            {
                // The stylesheet's size, read while no size of ours is set.
                if (_styled < 0f || _label.style.fontSize.keyword == StyleKeyword.Null)
                    _styled = _label.resolvedStyle.fontSize;
                var room = MaxWidth();
                var size = _styled;
                var measured = 0f;
                for (; ; size -= 1f)
                {
                    _label.style.fontSize = size;
                    measured = _label.MeasureTextSize(text, 0f, VisualElement.MeasureMode.Undefined, 0f, VisualElement.MeasureMode.Undefined).x;
                    if (measured <= room || size <= MinSize)
                        break;
                }
                if (size >= _styled - 0.01f)
                    _label.style.fontSize = StyleKeyword.Null;
                // The label as wide as its words (the layout alone may give a one-line label less than its text needs).
                var style = _label.resolvedStyle;
                // A minimum, not a width: flex layout gives a row's children their minimum before anything else.
                _label.style.minWidth = Mathf.Min(measured, room) + 2f + style.paddingLeft + style.paddingRight + style.borderLeftWidth + style.borderRightWidth;
                _fitText = text;
                _fitWidth = width;
            }
            finally
            {
                _fitting = false;
            }
        }

        /// <summary>How wide the label's text may be: its max-width (resolved to pixels) less its padding.</summary>
        private float MaxWidth()
        {
            var max = _label.resolvedStyle.maxWidth;
            var parent = _label.parent != null ? _label.parent.contentRect.width : _label.contentRect.width;
            // Resolved, a max-width is in pixels already (a share of the parent worked out).
            var limit = float.IsNaN(max.value) || max.value <= 0f ? parent : Mathf.Min(max.value, parent);
            var style = _label.resolvedStyle;
            return Mathf.Max(20f, limit - style.paddingLeft - style.paddingRight - style.borderLeftWidth - style.borderRightWidth) - 1f;
        }
    }
}
