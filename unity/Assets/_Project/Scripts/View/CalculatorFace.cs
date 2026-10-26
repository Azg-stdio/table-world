using Core.Rules;
using TableWorld.View.Builtin;
using UnityEngine;

namespace TableWorld.View
{
    /// <summary>
    /// The top of a table calculator: a glowing display and 16 keys drawn with the pixel font, redrawn when the display
    /// changes. <see cref="KeyAt"/> maps a point of the face to the key under it, so a click on the calculator presses
    /// that key; the drawing and the hit test share one layout.
    /// </summary>
    public sealed class CalculatorFace : MonoBehaviour
    {
        public const int Width = 120;
        public const int Height = 176;
        private const int Margin = 6;
        private const int DisplayTop = 6;
        private const int DisplayBottom = 40;
        private const int KeysTop = 48;
        private const int KeyWidth = 26;
        private const int KeyHeight = 30;
        private const int Gap = 2;

        /// <summary>The keypad as drawn, row by row ('x' is the multiply key).</summary>
        private static readonly string[] Rows = { "789/", "456x", "123-", "C0=+" };

        private static readonly Color32 Body = new Color32(38, 40, 46, 255);
        private static readonly Color32 Screen = new Color32(14, 24, 20, 255);
        private static readonly Color32 Ink = new Color32(120, 255, 170, 255);
        private static readonly Color32 Key = new Color32(70, 74, 84, 255);
        private static readonly Color32 OpKey = new Color32(110, 88, 44, 255);
        private static readonly Color32 Label = new Color32(240, 236, 226, 255);

        private ObjectView _view;
        private Texture2D _texture;
        private readonly Builtin.Canvas _canvas = new Builtin.Canvas(Width, Height);
        private string _shown;

        public Material Material { get; private set; }

        internal void Init(ObjectView view)
        {
            _view = view;
            if (_texture != null)
                return;
            _texture = new Texture2D(Width, Height, TextureFormat.RGBA32, mipChain: false)
            {
                name = $"Calculator {view.Id}",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            Material = new Material(RequiredShaders.Unlit) { name = $"Calculator {view.Id}" };
            Material.SetTexture("_BaseMap", _texture);
            Material.mainTexture = _texture;
            _shown = null;
            Redraw();
        }

        private void Update() => Redraw();

        private void Redraw()
        {
            if (_view == null || _texture == null || !(_view.Model is Calculator calculator) || calculator.Display == _shown)
                return;
            _shown = calculator.Display;
            _canvas.FillRect(0, 0, Width, Height, Body);
            _canvas.FillRect(Margin, DisplayTop, Width - 2 * Margin, DisplayBottom - DisplayTop, Screen);
            var text = calculator.Display;
            var scale = PixelFont.TextWidth(text, 2) <= Width - 2 * Margin - 8 ? 2 : 1;
            var x = Width - Margin - 4 - PixelFont.TextWidth(text, scale);
            var y = DisplayTop + (DisplayBottom - DisplayTop - PixelFont.GlyphHeight * scale) / 2;
            PixelFont.DrawText(_canvas, text, x, y, scale, Ink);
            for (var row = 0; row < Rows.Length; row++)
                for (var col = 0; col < Rows[row].Length; col++)
                {
                    var key = Rows[row][col];
                    var (kx, ky) = KeyOrigin(row, col);
                    _canvas.FillRect(kx, ky, KeyWidth, KeyHeight, char.IsDigit(key) ? Key : OpKey);
                    var label = key.ToString();
                    var lx = kx + (KeyWidth - PixelFont.TextWidth(label, 2)) / 2;
                    var ly = ky + (KeyHeight - PixelFont.GlyphHeight * 2) / 2;
                    PixelFont.DrawText(_canvas, label, lx, ly, 2, Label);
                }
            _canvas.CopyTo(_texture);
        }

        private static (int X, int Y) KeyOrigin(int row, int col) => (Margin + col * (KeyWidth + Gap), KeysTop + row * (KeyHeight + Gap));

        /// <summary>
        /// The key at a point of the face, <paramref name="u"/> from its left and <paramref name="v"/> from its top (0–1),
        /// as <see cref="Calculator.Press"/> takes it; null between keys or on the display.
        /// </summary>
        public static char? KeyAt(float u, float v)
        {
            var px = u * Width;
            var py = v * Height;
            for (var row = 0; row < Rows.Length; row++)
                for (var col = 0; col < Rows[row].Length; col++)
                {
                    var (kx, ky) = KeyOrigin(row, col);
                    if (px >= kx && px < kx + KeyWidth && py >= ky && py < ky + KeyHeight)
                        return Rows[row][col] == 'x' ? '*' : Rows[row][col];
                }
            return null;
        }

        /// <summary>The key under a world point on the calculator's top face (the face's top edge is its far side).</summary>
        public static char? KeyAt(Transform calculator, Vector3 worldPoint)
        {
            var local = calculator.InverseTransformPoint(worldPoint);
            var size = Calculator.Size;
            return KeyAt(0.5f + local.x / size.X, 0.5f - local.z / size.Y);
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
