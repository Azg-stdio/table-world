using System;
using System.Collections.Generic;
using System.Linq;
using Core.Rules;

namespace Core.Models
{
    /// <summary>
    /// The outline of an image's opaque part (TTS cuts custom tokens along their image's transparent edges): the pixel
    /// edges between opaque and transparent pixels are chained into closed loops, holes are filled, specks dropped, and
    /// each loop simplified (Douglas–Peucker) and triangulated (ear clipping). Coordinates are image UVs: u right, v up,
    /// 0..1, with row 0 of the mask at the bottom (as a texture's pixel data is).
    /// </summary>
    public static class Silhouette
    {
        /// <summary>Loops smaller than this share of the image's area are dust, not part of the piece.</summary>
        public const double MinAreaShare = 0.004;
        /// <summary>The most loops kept (the largest), so a noisy image does not become hundreds of islands.</summary>
        public const int MaxLoops = 8;

        /// <summary>One outline: counter-clockwise points (u, v) and the triangles filling it (indices into the points).</summary>
        public sealed class Loop
        {
            public List<Vec2> Points { get; } = new List<Vec2>();
            public List<int> Triangles { get; } = new List<int>();
        }

        /// <summary>
        /// The opaque part of <paramref name="alpha"/> (row-major, <paramref name="width"/> × <paramref name="height"/>,
        /// row 0 at the bottom): pixels at or above <paramref name="threshold"/> count as solid. Empty when the image is
        /// all solid (nothing to cut) or all clear, or when a loop cannot be filled (the caller keeps its plain shape).
        /// <paramref name="tolerance"/> is how far, in mask pixels, the simplified outline may stray from the pixels.
        /// </summary>
        public static List<Loop> Trace(byte[] alpha, int width, int height, byte threshold = 128, double tolerance = 0.8)
        {
            if (width <= 0 || height <= 0 || alpha.Length < width * height)
                throw new ArgumentException("The mask is smaller than its size.");
            bool Solid(int x, int y) => x >= 0 && y >= 0 && x < width && y < height && alpha[y * width + x] >= threshold;

            var solid = 0;
            for (var i = 0; i < width * height; i++)
                if (alpha[i] >= threshold)
                    solid++;
            if (solid == 0 || solid == width * height)
                return new List<Loop>();

            // Directed pixel edges with the solid pixel on their left (counter-clockwise round each solid region).
            var outgoing = new Dictionary<long, List<(int X, int Y)>>();
            void Edge(int x0, int y0, int x1, int y1)
            {
                var key = Key(x0, y0);
                if (!outgoing.TryGetValue(key, out var list))
                    outgoing[key] = list = new List<(int, int)>(1);
                list.Add((x1, y1));
            }
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    if (!Solid(x, y))
                        continue;
                    if (!Solid(x, y - 1))
                        Edge(x, y, x + 1, y);
                    if (!Solid(x + 1, y))
                        Edge(x + 1, y, x + 1, y + 1);
                    if (!Solid(x, y + 1))
                        Edge(x + 1, y + 1, x, y + 1);
                    if (!Solid(x - 1, y))
                        Edge(x, y + 1, x, y);
                }

            // Chain them. Where two solid pixels touch only at a corner, keep turning left: each loop stays round its own
            // pixels, so loops never cross.
            var loops = new List<List<(int X, int Y)>>();
            while (outgoing.Count > 0)
            {
                var startKey = outgoing.Keys.First();
                var start = Unkey(startKey);
                var loop = new List<(int X, int Y)> { start };
                var previous = start;
                var current = Take(start, null);
                while (current != start)
                {
                    loop.Add(current);
                    var next = Take(current, (current.X - previous.X, current.Y - previous.Y));
                    previous = current;
                    current = next;
                }
                loops.Add(loop);
            }

            (int X, int Y) Take((int X, int Y) at, (int X, int Y)? heading)
            {
                var key = Key(at.X, at.Y);
                var list = outgoing[key];
                var pick = 0;
                if (list.Count > 1 && heading is { } h)
                {
                    // Left of heading (hx, hy) is (-hy, hx).
                    var best = int.MinValue;
                    for (var i = 0; i < list.Count; i++)
                    {
                        var d = (list[i].X - at.X, list[i].Y - at.Y);
                        var score = d.Item1 * -h.Y + d.Item2 * h.X;
                        if (score > best)
                        {
                            best = score;
                            pick = i;
                        }
                    }
                }
                var next = list[pick];
                list.RemoveAt(pick);
                if (list.Count == 0)
                    outgoing.Remove(key);
                return next;
            }

            // Outer loops wind counter-clockwise (positive area); holes clockwise, and are filled.
            var minArea = MinAreaShare * width * height;
            var result = new List<Loop>();
            foreach (var raw in loops.Select(l => (Points: l, Area: Area(l))).Where(l => l.Area >= minArea).OrderByDescending(l => l.Area).Take(MaxLoops))
            {
                var simple = Simplify(raw.Points.Select(p => (X: (double)p.X, Y: (double)p.Y)).ToList(), tolerance);
                if (simple.Count < 3)
                    continue;
                var triangles = Triangulate(simple);
                if (triangles == null)
                    return new List<Loop>();
                var loop = new Loop();
                foreach (var p in simple)
                    loop.Points.Add(new Vec2((float)(p.X / width), (float)(p.Y / height)));
                loop.Triangles.AddRange(triangles);
                result.Add(loop);
            }
            return result;
        }

        private static long Key(int x, int y) => ((long)x << 32) | (uint)y;
        private static (int X, int Y) Unkey(long key) => ((int)(key >> 32), (int)(key & 0xffffffff));

        private static double Area(IReadOnlyList<(int X, int Y)> loop)
        {
            double sum = 0;
            for (var i = 0; i < loop.Count; i++)
            {
                var a = loop[i];
                var b = loop[(i + 1) % loop.Count];
                sum += (double)a.X * b.Y - (double)b.X * a.Y;
            }
            return sum / 2;
        }

        /// <summary>Douglas–Peucker on a closed loop: split at the point farthest from the first, simplify both halves.</summary>
        public static List<(double X, double Y)> Simplify(List<(double X, double Y)> loop, double tolerance)
        {
            if (loop.Count < 4)
                return loop;
            var far = 0;
            var farDistance = -1.0;
            for (var i = 1; i < loop.Count; i++)
            {
                var d = Sq(loop[i].X - loop[0].X) + Sq(loop[i].Y - loop[0].Y);
                if (d > farDistance)
                {
                    farDistance = d;
                    far = i;
                }
            }
            var keep = new bool[loop.Count + 1];
            keep[0] = keep[far] = keep[loop.Count] = true;
            var closed = new List<(double X, double Y)>(loop) { loop[0] };
            Mark(closed, 0, far, tolerance, keep);
            Mark(closed, far, loop.Count, tolerance, keep);
            var result = new List<(double X, double Y)>();
            for (var i = 0; i < loop.Count; i++)
                if (keep[i])
                    result.Add(loop[i]);
            return result;
        }

        private static void Mark(List<(double X, double Y)> points, int first, int last, double tolerance, bool[] keep)
        {
            var stack = new Stack<(int, int)>();
            stack.Push((first, last));
            while (stack.Count > 0)
            {
                var (a, b) = stack.Pop();
                var worst = -1;
                var worstDistance = tolerance;
                for (var i = a + 1; i < b; i++)
                {
                    var d = DistanceToSegment(points[i], points[a], points[b]);
                    if (d > worstDistance)
                    {
                        worstDistance = d;
                        worst = i;
                    }
                }
                if (worst < 0)
                    continue;
                keep[worst] = true;
                stack.Push((a, worst));
                stack.Push((worst, b));
            }
        }

        private static double DistanceToSegment((double X, double Y) p, (double X, double Y) a, (double X, double Y) b)
        {
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var length = dx * dx + dy * dy;
            var t = length > 0 ? Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / length)) : 0;
            return Math.Sqrt(Sq(p.X - a.X - t * dx) + Sq(p.Y - a.Y - t * dy));
        }

        private static double Sq(double v) => v * v;

        /// <summary>
        /// Ear clipping of a simple counter-clockwise polygon: triangles (counter-clockwise) as indices, or null when the
        /// polygon is not simple (no ear left).
        /// </summary>
        public static List<int>? Triangulate(IReadOnlyList<(double X, double Y)> polygon)
        {
            var remaining = Enumerable.Range(0, polygon.Count).ToList();
            var triangles = new List<int>();
            var guard = 0;
            while (remaining.Count > 3)
            {
                if (guard++ > polygon.Count * polygon.Count)
                    return null;
                var clipped = false;
                for (var i = 0; i < remaining.Count; i++)
                {
                    var ia = remaining[(i + remaining.Count - 1) % remaining.Count];
                    var ib = remaining[i];
                    var ic = remaining[(i + 1) % remaining.Count];
                    var a = polygon[ia];
                    var b = polygon[ib];
                    var c = polygon[ic];
                    var cross = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
                    if (cross <= 1e-12)
                    {
                        // A straight-through point adds nothing: drop it.
                        if (Math.Abs(cross) <= 1e-12)
                        {
                            remaining.RemoveAt(i);
                            clipped = true;
                            break;
                        }
                        continue;
                    }
                    var inside = false;
                    foreach (var j in remaining)
                        if (j != ia && j != ib && j != ic && InTriangle(polygon[j], a, b, c))
                        {
                            inside = true;
                            break;
                        }
                    if (inside)
                        continue;
                    triangles.AddRange(new[] { ia, ib, ic });
                    remaining.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (!clipped)
                    return null;
            }
            if (remaining.Count == 3)
                triangles.AddRange(remaining);
            return triangles;
        }

        private static bool InTriangle((double X, double Y) p, (double X, double Y) a, (double X, double Y) b, (double X, double Y) c)
        {
            double Side((double X, double Y) p1, (double X, double Y) p2, (double X, double Y) p3) => (p1.X - p3.X) * (p2.Y - p3.Y) - (p2.X - p3.X) * (p1.Y - p3.Y);
            var d1 = Side(p, a, b);
            var d2 = Side(p, b, c);
            var d3 = Side(p, c, a);
            var negative = d1 < 0 || d2 < 0 || d3 < 0;
            var positive = d1 > 0 || d2 > 0 || d3 > 0;
            return !(negative && positive);
        }
    }
}
