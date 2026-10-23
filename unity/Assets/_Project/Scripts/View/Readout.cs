using System;
using Core.Rules;
using TableWorld.View.Builtin;
using UnityEngine;

namespace TableWorld.View
{
    /// <summary>
    /// The digital display on a counter or a clock (G27), drawn with the pixel font into a small glowing texture that is
    /// redrawn only when the shown text changes. A running clock counts from the moment this machine saw it start, so
    /// players see the same time give or take their network delay.
    /// </summary>
    public sealed class Readout : MonoBehaviour
    {
        public const int Width = 96;
        public const int Height = 40;
        private static readonly Color32 ScreenColor = new Color32(14, 24, 20, 255);
        private static readonly Color32 Ink = new Color32(120, 255, 170, 255);
        private static readonly Color32 Dim = new Color32(70, 140, 100, 255);

        private ObjectView _view;
        private Texture2D _texture;
        private readonly Builtin.Canvas _canvas = new Builtin.Canvas(Width, Height);

        private bool _running;
        private float _baseSeconds;
        private float _since;
        private bool _doneReported;
        private bool _dim;

        /// <summary>The glowing face material (top face of the object).</summary>
        public Material Material { get; private set; }

        /// <summary>What the display shows now.</summary>
        public string Text { get; private set; }

        /// <summary>Set by <see cref="TableView"/>: a running timer reached zero on this machine.</summary>
        internal Action<ObjectView> TimerDone;

        internal void Init(ObjectView view)
        {
            _view = view;
            if (_texture != null)
                return;
            _texture = new Texture2D(Width, Height, TextureFormat.RGBA32, mipChain: false)
            {
                name = $"Readout {view.Id}",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            Material = new Material(RequiredShaders.Unlit) { name = $"Readout {view.Id}" };
            Material.SetTexture("_BaseMap", _texture);
            Material.mainTexture = _texture;
            Text = null;
            Redraw();
        }

        /// <summary>A clock's reading in seconds right now (what a pause should freeze).</summary>
        public float CurrentSeconds
        {
            get
            {
                if (!(_view?.Model is Clock clock))
                    return 0f;
                Track(clock);
                if (!_running)
                    return clock.Seconds;
                var passed = Time.realtimeSinceStartup - _since;
                return clock.Mode == ClockMode.Timer ? Mathf.Max(0f, _baseSeconds - passed) : _baseSeconds + passed;
            }
        }

        private void Track(Clock clock)
        {
            if (clock.Running == _running && clock.Seconds == _baseSeconds)
                return;
            _running = clock.Running;
            _baseSeconds = clock.Seconds;
            _since = Time.realtimeSinceStartup;
            _doneReported = false;
        }

        private void Update()
        {
            if (_view == null)
                return;
            if (_view.Model is Clock { Mode: ClockMode.Timer, Running: true } && CurrentSeconds <= 0f && !_doneReported)
            {
                _doneReported = true;
                TimerDone?.Invoke(_view);
            }
            Redraw();
        }

        private void Redraw()
        {
            var text = _view.Model switch
            {
                Counter counter => counter.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Clock { Mode: ClockMode.Clock } => DateTime.Now.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture),
                Clock clock => Format(clock.Mode == ClockMode.Timer ? Mathf.Ceil(CurrentSeconds) : Mathf.Floor(CurrentSeconds)),
                _ => "",
            };
            // A paused clock shows dimmer, like a stopped display.
            var dim = _view.Model is Clock { Mode: not ClockMode.Clock, Running: false };
            if ((text == Text && dim == _dim) || _texture == null)
                return;
            Text = text;
            _dim = dim;
            _canvas.FillRect(0, 0, Width, Height, ScreenColor);
            var scale = PixelFont.TextWidth(text, 2) <= Width - 8 ? 2 : 1;
            var x = (Width - PixelFont.TextWidth(text, scale)) / 2;
            var y = (Height - PixelFont.GlyphHeight * scale) / 2;
            var ink = dim ? Dim : Ink;
            PixelFont.DrawText(_canvas, text, x, y, scale, ink);
            _canvas.CopyTo(_texture);
        }

        /// <summary>M:SS under an hour, H:MM:SS above.</summary>
        public static string Format(float seconds)
        {
            var total = Mathf.Max(0, Mathf.RoundToInt(seconds));
            var h = total / 3600;
            var m = total / 60 % 60;
            var s = total % 60;
            return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m}:{s:00}";
        }

        private void OnDestroy()
        {
            if (_texture != null)
                Destroy(_texture);
            if (Material != null)
                Destroy(Material);
        }
    }
}
