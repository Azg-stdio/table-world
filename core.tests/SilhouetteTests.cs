using System;
using System.Collections.Generic;
using System.Linq;
using Core.Models;
using Xunit;

namespace Core.Tests
{
    /// <summary>Token outlines traced from an image's alpha (TTS custom tokens are cut along it).</summary>
    public class SilhouetteTests
    {
        private const int Size = 100;

        private static byte[] Mask(Func<int, int, bool> solid, int width = Size, int height = Size)
        {
            var alpha = new byte[width * height];
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                    alpha[y * width + x] = solid(x, y) ? (byte)255 : (byte)0;
            return alpha;
        }

        private static bool InDisc(int x, int y, double cx, double cy, double r) => Math.Pow(x + 0.5 - cx, 2) + Math.Pow(y + 0.5 - cy, 2) <= r * r;

        private static double PolygonArea(Silhouette.Loop loop)
        {
            double sum = 0;
            for (var i = 0; i < loop.Points.Count; i++)
            {
                var a = loop.Points[i];
                var b = loop.Points[(i + 1) % loop.Points.Count];
                sum += a.X * b.Y - b.X * a.Y;
            }
            return sum / 2;
        }

        private static double TriangleArea(Silhouette.Loop loop)
        {
            double sum = 0;
            for (var i = 0; i < loop.Triangles.Count; i += 3)
            {
                var a = loop.Points[loop.Triangles[i]];
                var b = loop.Points[loop.Triangles[i + 1]];
                var c = loop.Points[loop.Triangles[i + 2]];
                var cross = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
                Assert.True(cross >= -1e-9, "triangles wind counter-clockwise");
                sum += cross / 2;
            }
            return sum;
        }

        [Fact]
        public void Disc_OneLoopHuggingTheCircle()
        {
            var loops = Silhouette.Trace(Mask((x, y) => InDisc(x, y, 50, 50, 40)), Size, Size);
            var loop = Assert.Single(loops);
            Assert.InRange(PolygonArea(loop), Math.PI * 0.4 * 0.4 * 0.97, Math.PI * 0.4 * 0.4 * 1.03);
            foreach (var p in loop.Points)
                Assert.InRange(Math.Sqrt(Math.Pow(p.X - 0.5, 2) + Math.Pow(p.Y - 0.5, 2)), 0.385, 0.415);
            Assert.InRange(loop.Points.Count, 12, 120);
            Assert.Equal(PolygonArea(loop), TriangleArea(loop), 6);
        }

        [Fact]
        public void SolidOrClearImages_HaveNothingToCut()
        {
            Assert.Empty(Silhouette.Trace(Mask((x, y) => true), Size, Size));
            Assert.Empty(Silhouette.Trace(Mask((x, y) => false), Size, Size));
        }

        [Fact]
        public void Ring_HoleIsFilled()
        {
            var loops = Silhouette.Trace(Mask((x, y) => InDisc(x, y, 50, 50, 40) && !InDisc(x, y, 50, 50, 20)), Size, Size);
            var loop = Assert.Single(loops);
            Assert.InRange(PolygonArea(loop), Math.PI * 0.4 * 0.4 * 0.97, Math.PI * 0.4 * 0.4 * 1.03);
        }

        [Fact]
        public void TwoBlobs_TwoLoops_SpecksDropped()
        {
            var loops = Silhouette.Trace(Mask((x, y) => InDisc(x, y, 25, 50, 18) || InDisc(x, y, 75, 50, 12) || (x == 3 && y == 3)), Size, Size);
            Assert.Equal(2, loops.Count);
            Assert.True(PolygonArea(loops[0]) > PolygonArea(loops[1]), "largest first");
            Assert.All(loops, l => Assert.Equal(PolygonArea(l), TriangleArea(l), 6));
        }

        [Fact]
        public void SquaresTouchingAtACorner_StaySeparate()
        {
            // Two 30-pixel squares meeting only at the point (50, 50).
            var loops = Silhouette.Trace(Mask((x, y) => (x >= 20 && x < 50 && y >= 20 && y < 50) || (x >= 50 && x < 80 && y >= 50 && y < 80)), Size, Size);
            Assert.Equal(2, loops.Count);
            Assert.All(loops, l =>
            {
                Assert.Equal(0.09, PolygonArea(l), 6);
                Assert.Equal(4, l.Points.Count);
                Assert.Equal(0.09, TriangleArea(l), 6);
            });
        }

        [Fact]
        public void ConcaveStar_IsFilledExactly()
        {
            // A five-pointed star: concave, so ear clipping must skip the inner corners.
            bool Star(int x, int y)
            {
                var dx = x + 0.5 - 50;
                var dy = y + 0.5 - 50;
                var angle = Math.Atan2(dy, dx);
                var r = Math.Sqrt(dx * dx + dy * dy);
                var t = Math.Abs(((angle / (2 * Math.PI / 5)) % 1 + 1) % 1 - 0.5) * 2;
                return r <= 18 + 27 * t;
            }
            var loop = Assert.Single(Silhouette.Trace(Mask(Star), Size, Size));
            Assert.Equal(PolygonArea(loop), TriangleArea(loop), 6);
            Assert.True(loop.Triangles.Count / 3 == loop.Points.Count - 2 || loop.Triangles.Count / 3 < loop.Points.Count - 2, "a fan of n - 2 triangles at most");
        }

        [Fact]
        public void NonSquareMask_UsesItsOwnSides()
        {
            var loop = Assert.Single(Silhouette.Trace(Mask((x, y) => x >= 10 && x < 190 && y >= 25 && y < 75, 200, 100), 200, 100));
            Assert.Equal(0.05, loop.Points.Min(p => p.X), 6);
            Assert.Equal(0.95, loop.Points.Max(p => p.X), 6);
            Assert.Equal(0.25, loop.Points.Min(p => p.Y), 6);
            Assert.Equal(0.75, loop.Points.Max(p => p.Y), 6);
        }
    }
}
