using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace TableWorld.View
{
    // Burst jobs that turn a decoded image into a GPU-ready texture on worker threads (TextureBaker): resize, mipmaps and
    // BC1/BC3 (DXT1/DXT5) block compression. Texture2D.Compress did the same on the main thread, 100 ms and more per
    // sprite sheet, which made heavy tables stutter while their images arrived. Rows run bottom up, as Unity stores them.

    /// <summary>
    /// Copies (or resizes) the decoded pixels into RGBA bytes at the target size, averaging the source pixels each target
    /// pixel covers, and notes per row whether any pixel is see-through. One row per index.
    /// </summary>
    [BurstCompile]
    internal struct ResampleJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<byte> Source;
        public int SourceWidth, SourceHeight, Channels;
        /// <summary>Byte offsets of red, green, blue and alpha in a source pixel; alpha -1 when there is none.</summary>
        public int R, G, B, A;
        public int Width, Height;
        [NativeDisableParallelForRestriction] public NativeArray<byte> Target;
        [NativeDisableParallelForRestriction] public NativeArray<byte> RowHasAlpha;

        public void Execute(int y)
        {
            var y0 = (int)((long)y * SourceHeight / Height);
            var y1 = (int)((long)(y + 1) * SourceHeight / Height);
            if (y1 <= y0)
                y1 = y0 + 1;
            var see = (byte)0;
            for (var x = 0; x < Width; x++)
            {
                var x0 = (int)((long)x * SourceWidth / Width);
                var x1 = (int)((long)(x + 1) * SourceWidth / Width);
                if (x1 <= x0)
                    x1 = x0 + 1;
                int r = 0, g = 0, b = 0, a = 0, n = 0;
                for (var sy = y0; sy < y1 && sy < SourceHeight; sy++)
                for (var sx = x0; sx < x1 && sx < SourceWidth; sx++)
                {
                    var i = (sy * SourceWidth + sx) * Channels;
                    r += Source[i + R];
                    g += Source[i + G];
                    b += Source[i + B];
                    a += A >= 0 ? Source[i + A] : 255;
                    n++;
                }
                if (n == 0)
                    n = 1;
                var o = (y * Width + x) * 4;
                Target[o] = (byte)(r / n);
                Target[o + 1] = (byte)(g / n);
                Target[o + 2] = (byte)(b / n);
                var alpha = (byte)(a / n);
                Target[o + 3] = alpha;
                if (alpha < 255)
                    see = 1;
            }
            RowHasAlpha[y] = see;
        }
    }

    /// <summary>The next mipmap: each pixel the average of the 2 × 2 above it (edges clamped). One row per index.</summary>
    [BurstCompile]
    internal struct DownsampleJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<byte> Source;
        public int SourceWidth, SourceHeight;
        public int Width, Height;
        [NativeDisableParallelForRestriction] public NativeArray<byte> Target;

        public void Execute(int y)
        {
            var sy0 = y * 2 < SourceHeight ? y * 2 : SourceHeight - 1;
            var sy1 = sy0 + 1 < SourceHeight ? sy0 + 1 : sy0;
            for (var x = 0; x < Width; x++)
            {
                var sx0 = x * 2 < SourceWidth ? x * 2 : SourceWidth - 1;
                var sx1 = sx0 + 1 < SourceWidth ? sx0 + 1 : sx0;
                for (var c = 0; c < 4; c++)
                {
                    var sum = Source[(sy0 * SourceWidth + sx0) * 4 + c] + Source[(sy0 * SourceWidth + sx1) * 4 + c]
                            + Source[(sy1 * SourceWidth + sx0) * 4 + c] + Source[(sy1 * SourceWidth + sx1) * 4 + c];
                    Target[(y * Width + x) * 4 + c] = (byte)((sum + 2) >> 2);
                }
            }
        }
    }

    /// <summary>
    /// Compresses one 4 × 4 block per index into BC1 (8 bytes, opaque) or BC3 (16 bytes: an alpha block, then a colour
    /// block). Endpoints are the block's colour range, inset slightly to reduce the error (J.M.P. van Waveren, "Real-Time
    /// DXT Compression", 2006); each pixel takes the nearest of the palette.
    /// </summary>
    [BurstCompile]
    internal struct CompressJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<byte> Pixels;
        public int Width, Height;
        public bool Alpha;
        [NativeDisableParallelForRestriction] public NativeArray<byte> Output;

        public void Execute(int block)
        {
            var blocksWide = (Width + 3) / 4;
            var bx = block % blocksWide * 4;
            var by = block / blocksWide * 4;
            int minR = 255, minG = 255, minB = 255, minA = 255, maxR = 0, maxG = 0, maxB = 0, maxA = 0;
            for (var i = 0; i < 16; i++)
            {
                var p = Pixel(bx, by, i);
                int r = Pixels[p], g = Pixels[p + 1], b = Pixels[p + 2], a = Pixels[p + 3];
                if (r < minR) minR = r;
                if (g < minG) minG = g;
                if (b < minB) minB = b;
                if (a < minA) minA = a;
                if (r > maxR) maxR = r;
                if (g > maxG) maxG = g;
                if (b > maxB) maxB = b;
                if (a > maxA) maxA = a;
            }
            var o = block * (Alpha ? 16 : 8);
            if (Alpha)
            {
                WriteAlpha(o, bx, by, maxA, minA);
                o += 8;
            }

            var insetR = (maxR - minR) >> 4;
            var insetG = (maxG - minG) >> 4;
            var insetB = (maxB - minB) >> 4;
            var c0 = To565(maxR - insetR, maxG - insetG, maxB - insetB);
            var c1 = To565(minR + insetR, minG + insetG, minB + insetB);
            // Four-colour mode needs c0 > c1 in BC1 (BC3's colour block is always four-colour).
            if (c0 < c1)
            {
                var swap = c0;
                c0 = c1;
                c1 = swap;
            }
            uint indices = 0;
            if (c0 != c1)
            {
                int r0 = R(c0), g0 = G(c0), b0 = B(c0), r1 = R(c1), g1 = G(c1), b1 = B(c1);
                int r2 = (2 * r0 + r1) / 3, g2 = (2 * g0 + g1) / 3, b2 = (2 * b0 + b1) / 3;
                int r3 = (r0 + 2 * r1) / 3, g3 = (g0 + 2 * g1) / 3, b3 = (b0 + 2 * b1) / 3;
                for (var i = 0; i < 16; i++)
                {
                    var p = Pixel(bx, by, i);
                    int r = Pixels[p], g = Pixels[p + 1], b = Pixels[p + 2];
                    var best = 0;
                    var bestError = Distance(r, g, b, r0, g0, b0);
                    var e = Distance(r, g, b, r1, g1, b1);
                    if (e < bestError) { bestError = e; best = 1; }
                    e = Distance(r, g, b, r2, g2, b2);
                    if (e < bestError) { bestError = e; best = 2; }
                    e = Distance(r, g, b, r3, g3, b3);
                    if (e < bestError) best = 3;
                    indices |= (uint)best << (2 * i);
                }
            }
            Output[o] = (byte)c0;
            Output[o + 1] = (byte)(c0 >> 8);
            Output[o + 2] = (byte)c1;
            Output[o + 3] = (byte)(c1 >> 8);
            Output[o + 4] = (byte)indices;
            Output[o + 5] = (byte)(indices >> 8);
            Output[o + 6] = (byte)(indices >> 16);
            Output[o + 7] = (byte)(indices >> 24);
        }

        /// <summary>Byte index of pixel <paramref name="i"/> (row-major in the block) of the block at (x, y), clamped to the image.</summary>
        private int Pixel(int x, int y, int i)
        {
            var px = x + (i & 3);
            var py = y + (i >> 2);
            if (px >= Width) px = Width - 1;
            if (py >= Height) py = Height - 1;
            return (py * Width + px) * 4;
        }

        /// <summary>BC3's alpha block: the exact range (cut-out edges stay crisp), eight steps, three bits a pixel.</summary>
        private void WriteAlpha(int o, int bx, int by, int a0, int a1)
        {
            Output[o] = (byte)a0;
            Output[o + 1] = (byte)a1;
            ulong bits = 0;
            if (a0 != a1)
                for (var i = 0; i < 16; i++)
                {
                    var a = Pixels[Pixel(bx, by, i) + 3];
                    var best = 0;
                    var bestError = int.MaxValue;
                    for (var k = 0; k < 8; k++)
                    {
                        var value = k == 0 ? a0 : k == 1 ? a1 : ((8 - k) * a0 + (k - 1) * a1) / 7;
                        var e = a > value ? a - value : value - a;
                        if (e < bestError)
                        {
                            bestError = e;
                            best = k;
                        }
                    }
                    bits |= (ulong)best << (3 * i);
                }
            for (var k = 0; k < 6; k++)
                Output[o + 2 + k] = (byte)(bits >> (8 * k));
        }

        private static int To565(int r, int g, int b) => ((r >> 3) << 11) | ((g >> 2) << 5) | (b >> 3);
        private static int R(int c) => ((c >> 11) & 31) << 3 | ((c >> 11) & 31) >> 2;
        private static int G(int c) => ((c >> 5) & 63) << 2 | ((c >> 5) & 63) >> 4;
        private static int B(int c) => (c & 31) << 3 | (c & 31) >> 2;

        private static int Distance(int r, int g, int b, int r2, int g2, int b2)
        {
            int dr = r - r2, dg = g - g2, db = b - b2;
            // Green counts most, as the eye sees it.
            return 3 * dr * dr + 6 * dg * dg + db * db;
        }
    }
}
