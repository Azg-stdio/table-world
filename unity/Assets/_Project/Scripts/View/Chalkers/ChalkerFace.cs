using System;
using UnityEngine;

namespace TableWorld.View.Chalkers
{
    /// <summary>Little chalk drawings around a face, for emotes and personalities.</summary>
    public enum FaceExtra { None, Blush, Tear, Sweat, Hearts, Anger, Dots, Zzz, Sparkle }

    /// <summary>
    /// What a chalker's face shows, as numbers that blend: personalities set a resting face, emotes blend toward theirs,
    /// and the voice opens the mouth on top.
    /// </summary>
    public struct FaceShape
    {
        /// <summary>Eye height: 1 round, 0.4 half-lidded, 0 shut, 1.4 wide.</summary>
        public float EyeOpen;
        /// <summary>0 round eyes, 1 happy arcs (^ ^).</summary>
        public float EyeSmile;
        /// <summary>Pupil size, 1 normal.</summary>
        public float Pupil;
        /// <summary>0 no brows, 1 drawn.</summary>
        public float Brows;
        /// <summary>-1 low – 1 high.</summary>
        public float BrowRaise;
        /// <summary>+1 angry (inner ends down), -1 worried (inner ends up).</summary>
        public float BrowTilt;
        /// <summary>The left brow raised this much more than the right (a curious look).</summary>
        public float BrowAsym;
        /// <summary>-1 frown – 1 grin.</summary>
        public float Smile;
        /// <summary>Lifts one mouth corner (a smirk), -1..1.</summary>
        public float Smirk;
        /// <summary>Mouth width, 1 normal.</summary>
        public float MouthWidth;
        /// <summary>How open the mouth is without any voice (laughing, surprise), 0–1.</summary>
        public float MouthOpen;
        public FaceExtra Extra;

        public static FaceShape Neutral => new FaceShape { EyeOpen = 1f, Pupil = 1f, MouthWidth = 1f };

        public static FaceShape Lerp(FaceShape a, FaceShape b, float t)
        {
            t = Mathf.Clamp01(t);
            return new FaceShape
            {
                EyeOpen = Mathf.Lerp(a.EyeOpen, b.EyeOpen, t),
                EyeSmile = Mathf.Lerp(a.EyeSmile, b.EyeSmile, t),
                Pupil = Mathf.Lerp(a.Pupil, b.Pupil, t),
                Brows = Mathf.Lerp(a.Brows, b.Brows, t),
                BrowRaise = Mathf.Lerp(a.BrowRaise, b.BrowRaise, t),
                BrowTilt = Mathf.Lerp(a.BrowTilt, b.BrowTilt, t),
                BrowAsym = Mathf.Lerp(a.BrowAsym, b.BrowAsym, t),
                Smile = Mathf.Lerp(a.Smile, b.Smile, t),
                Smirk = Mathf.Lerp(a.Smirk, b.Smirk, t),
                MouthWidth = Mathf.Lerp(a.MouthWidth, b.MouthWidth, t),
                MouthOpen = Mathf.Lerp(a.MouthOpen, b.MouthOpen, t),
                Extra = t < 0.5f ? a.Extra : b.Extra,
            };
        }
    }

    /// <summary>
    /// A chalker's face, drawn at runtime on a small chalkboard texture in chalk strokes. Shapes are laid out on a
    /// 128-unit grid (origin bottom left) and drawn at <see cref="Size"/> pixels. The face is redrawn only when what it
    /// shows changes by a visible step.
    /// </summary>
    public sealed class ChalkerFace
    {
        public const int Size = 256;
        private const float Scale = Size / 128f;
        public static readonly Color32 Board = new Color32(44, 58, 50, 255);
        public static readonly Color32 Chalk = new Color32(236, 236, 226, 255);
        private const float Stroke = 2.4f;
        private const float EyeY = 80f;
        private const float MouthY = 38f;
        private static readonly float[] EyeX = { 40f, 88f };

        private readonly Color32[] _background;
        private readonly Color32[] _pixels = new Color32[Size * Size];
        private long _shown = long.MinValue;

        public Texture2D Texture { get; }

        public ChalkerFace()
        {
            Texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = "ChalkerFace", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            // Chalk dust: a board that is never quite clean.
            _background = new Color32[Size * Size];
            var random = new System.Random(5);
            for (var i = 0; i < _background.Length; i++)
            {
                var dust = random.Next(0, 9);
                _background[i] = new Color32((byte)(Board.r + dust), (byte)(Board.g + dust), (byte)(Board.b + dust), 255);
            }
            Draw(FaceShape.Neutral, 0f, Vector2.zero, false);
        }

        /// <param name="face">The expression.</param>
        /// <param name="voice">How far the voice opens the mouth, 0–1, on top of the expression's own opening.</param>
        /// <param name="look">Where the pupils point, each axis -1..1 (right, up).</param>
        /// <param name="blink">The eyes are shut for a blink.</param>
        public void Draw(FaceShape face, float voice, Vector2 look, bool blink)
        {
            var open = Mathf.Clamp01(Mathf.Max(face.MouthOpen, voice));
            var lx = Mathf.Clamp(look.x, -1f, 1f);
            var ly = Mathf.Clamp(look.y, -1f, 1f);
            var key = Key(face.EyeOpen, face.EyeSmile, face.Pupil, face.Brows, face.BrowRaise, face.BrowTilt, face.BrowAsym,
                face.Smile, face.Smirk, face.MouthWidth, open, lx, ly) * 31 + (int)face.Extra * 2 + (blink ? 1 : 0);
            if (key == _shown)
                return;
            _shown = key;
            _background.CopyTo(_pixels, 0);
            DrawEyes(face, lx, ly, blink);
            DrawMouth(face, open);
            DrawExtra(face.Extra);
            Texture.SetPixels32(_pixels);
            Texture.Apply(false);
        }

        /// <summary>A hash of the values in visible steps (twentieths), so tiny changes do not redraw.</summary>
        private static long Key(params float[] values)
        {
            long key = 17;
            foreach (var v in values)
                key = key * 41 + Mathf.RoundToInt(v * 20f);
            return key;
        }

        private void DrawEyes(FaceShape face, float lx, float ly, bool blink)
        {
            var height = Mathf.Clamp(face.EyeOpen, 0f, 1.5f);
            for (var side = 0; side < 2; side++)
            {
                var x = EyeX[side];
                var outward = side == 0 ? -1f : 1f;
                if (face.Extra == FaceExtra.Hearts && !blink)
                    Heart(x, EyeY, 1.05f);
                else if (blink || height < 0.12f)
                    Line(x - 13f, EyeY, x + 13f, EyeY, Stroke);
                else if (face.EyeSmile > 0.5f)
                    Arc(x, EyeY - 4f, 13f, 10f, 0f, 180f, Stroke);
                else
                {
                    var ry = 14f * height;
                    Ellipse(x, EyeY, 14f, ry, Stroke);
                    var pupil = 6.5f * face.Pupil;
                    var room = Mathf.Max(0f, ry - pupil - 2f);
                    Disc(x + lx * Mathf.Max(0f, 14f - pupil - 2f), EyeY + ly * room, Mathf.Min(pupil, ry));
                    // Half-shut eyes get a lid line across the top.
                    if (height < 0.7f)
                        Line(x - 15f, EyeY + ry, x + 15f, EyeY + ry, Stroke);
                }

                if (face.Brows > 0.5f)
                {
                    var raise = face.BrowRaise + (side == 0 ? face.BrowAsym : 0f);
                    var y = EyeY + 14f * Mathf.Max(height, 0.7f) + 8f + raise * 5f;
                    var tilt = face.BrowTilt * 5f;
                    // The inner end faces the nose.
                    var inner = x - outward * 12f;
                    var outer = x + outward * 12f;
                    Line(inner, y - tilt, outer, y + tilt, Stroke);
                }
            }
        }

        /// <summary>
        /// A closed mouth is one stroke bent by the smile; an open one is the shape between two curves, flattened on top
        /// for a grin and underneath for a frown.
        /// </summary>
        private void DrawMouth(FaceShape face, float open)
        {
            var half = 18f * Mathf.Clamp(face.MouthWidth, 0.3f, 1.6f);
            const int steps = 24;
            float Bend(float u) => -face.Smile * 7f * (1f - u * u) + face.Smirk * 5f * u + face.Smile * 2f;
            if (open < 0.06f)
            {
                Polyline(steps, u => new Vector2(64f + u * half, MouthY + Bend(u)));
                return;
            }
            var h = 3f + 22f * open;
            float Top(float u) => MouthY + Bend(u) + h / 2f * Mathf.Sqrt(Mathf.Max(0f, 1f - u * u)) * (1f - 0.8f * Mathf.Max(0f, face.Smile));
            float Bottom(float u) => MouthY + Bend(u) - h / 2f * Mathf.Sqrt(Mathf.Max(0f, 1f - u * u)) * (1f - 0.8f * Mathf.Max(0f, -face.Smile));
            Polyline(steps, u => new Vector2(64f + u * half, Top(u)));
            Polyline(steps, u => new Vector2(64f + u * half, Bottom(u)));
        }

        private void DrawExtra(FaceExtra extra)
        {
            switch (extra)
            {
                case FaceExtra.Blush:
                    foreach (var x in new[] { 22f, 98f })
                        for (var i = 0; i < 3; i++)
                            Line(x + i * 5f, 54f, x + i * 5f + 4f, 62f, 1.6f);
                    break;
                case FaceExtra.Tear:
                    Drop(33f, 58f, 1f);
                    break;
                case FaceExtra.Sweat:
                    Drop(112f, 98f, 1.2f);
                    break;
                case FaceExtra.Anger:
                    // The cartoon vein: four short curves around a point.
                    for (var q = 0; q < 4; q++)
                        Arc(108f + (q % 2 == 0 ? -4f : 4f), 110f + (q < 2 ? -4f : 4f), 4f, 4f, q * 90f + 180f, q * 90f + 270f, 2f);
                    break;
                case FaceExtra.Dots:
                    for (var i = 0; i < 3; i++)
                        Disc(98f + i * 9f, 112f, 2.6f);
                    break;
                case FaceExtra.Zzz:
                    Zed(100f, 104f, 7f);
                    Zed(112f, 114f, 5f);
                    break;
                case FaceExtra.Sparkle:
                    Star(110f, 104f, 6f);
                    Star(20f, 100f, 4f);
                    break;
            }
        }

        // ---- chalk primitives, in 128-unit face coordinates -----------------------------------------------------

        private void Plot(int x, int y, float coverage)
        {
            if (x < 0 || y < 0 || x >= Size || y >= Size || coverage <= 0f)
                return;
            var i = y * Size + x;
            _pixels[i] = Color32.Lerp(_pixels[i], Chalk, Mathf.Clamp01(coverage));
        }

        private void Disc(float cx, float cy, float radius)
        {
            cx *= Scale;
            cy *= Scale;
            radius *= Scale;
            for (var y = (int)(cy - radius - 1); y <= cy + radius + 1; y++)
                for (var x = (int)(cx - radius - 1); x <= cx + radius + 1; x++)
                    Plot(x, y, radius + 0.5f - Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)));
        }

        private void Ellipse(float cx, float cy, float rx, float ry, float width) => Arc(cx, cy, rx, ry, 0f, 360f, width);

        /// <summary>Part of an ellipse from <paramref name="from"/> to <paramref name="to"/> degrees (0 = right, counter-clockwise).</summary>
        private void Arc(float cx, float cy, float rx, float ry, float from, float to, float width)
        {
            var steps = Mathf.Max(8, Mathf.CeilToInt(Mathf.Abs(to - from) / 8f));
            Polyline(steps, u =>
            {
                var a = Mathf.Lerp(from, to, (u + 1f) / 2f) * Mathf.Deg2Rad;
                return new Vector2(cx + rx * Mathf.Cos(a), cy + ry * Mathf.Sin(a));
            }, width);
        }

        /// <summary>Strokes <paramref name="point"/>(u) for u from -1 to 1 in <paramref name="steps"/> segments.</summary>
        private void Polyline(int steps, Func<float, Vector2> point, float width = Stroke)
        {
            var previous = point(-1f);
            for (var i = 1; i <= steps; i++)
            {
                var next = point(-1f + 2f * i / steps);
                Line(previous.x, previous.y, next.x, next.y, width);
                previous = next;
            }
        }

        private void Line(float x0, float y0, float x1, float y1, float width)
        {
            var a = new Vector2(x0, y0) * Scale;
            var b = new Vector2(x1, y1) * Scale;
            var w = width * Scale;
            var length = (b - a).sqrMagnitude;
            var margin = w + 1f;
            for (var y = (int)(Mathf.Min(a.y, b.y) - margin); y <= Mathf.Max(a.y, b.y) + margin; y++)
                for (var x = (int)(Mathf.Min(a.x, b.x) - margin); x <= Mathf.Max(a.x, b.x) + margin; x++)
                {
                    var p = new Vector2(x, y);
                    var t = length < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, b - a) / length);
                    Plot(x, y, w / 2f + 0.5f - Vector2.Distance(p, a + t * (b - a)));
                }
        }

        private void Heart(float cx, float cy, float size) => Polyline(40, u =>
        {
            var t = u * Mathf.PI;
            var s = Mathf.Sin(t);
            return new Vector2(cx + size * 0.9f * 16f * s * s * s / 1.4f,
                cy + size * (13f * Mathf.Cos(t) - 5f * Mathf.Cos(2f * t) - 2f * Mathf.Cos(3f * t) - Mathf.Cos(4f * t)) / 1.4f + 2f);
        });

        private void Drop(float cx, float cy, float size)
        {
            // A round bottom and two strokes up to the tip.
            Arc(cx, cy, 4f * size, 4f * size, 160f, 380f, 2f);
            Line(cx - 3.8f * size, cy + 1.4f * size, cx, cy + 10f * size, 2f);
            Line(cx + 3.8f * size, cy + 1.4f * size, cx, cy + 10f * size, 2f);
        }

        private void Zed(float x, float y, float s)
        {
            Line(x - s, y + s, x + s, y + s, 2f);
            Line(x + s, y + s, x - s, y - s, 2f);
            Line(x - s, y - s, x + s, y - s, 2f);
        }

        private void Star(float x, float y, float s)
        {
            Line(x - s, y, x + s, y, 1.8f);
            Line(x, y - s, x, y + s, 1.8f);
            Line(x - s * 0.5f, y - s * 0.5f, x + s * 0.5f, y + s * 0.5f, 1.4f);
            Line(x - s * 0.5f, y + s * 0.5f, x + s * 0.5f, y - s * 0.5f, 1.4f);
        }

        public void Destroy() => UnityEngine.Object.Destroy(Texture);
    }
}
