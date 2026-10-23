using System.Collections.Generic;
using System.Linq;
using TableWorld.View.Builtin;
using UnityEngine;

namespace TableWorld.View.Meshes
{
    /// <summary>
    /// d4–d20 as convex polyhedra. Faces are found by brute force (planes through three vertices with every other vertex
    /// behind them), numbered so opposite faces add up to n + 1, and UV-mapped onto the builtin dice number atlas.
    /// A die shows value v when face v points up; a d4 reads the face pointing down.
    /// </summary>
    public sealed class DiceGeometry
    {
        private static readonly Dictionary<int, DiceGeometry> Cache = new Dictionary<int, DiceGeometry>();

        public int Faces { get; }
        public Mesh Mesh { get; }
        /// <summary>Outward local normal of the face showing value v, at index v − 1.</summary>
        public Vector3[] ValueNormals { get; }
        public bool ReadsDown => Faces == 4;

        private DiceGeometry(int faces, Mesh mesh, Vector3[] valueNormals)
        {
            Faces = faces;
            Mesh = mesh;
            ValueNormals = valueNormals;
        }

        /// <summary>Circumradius in metres; a d6 is a 16 mm cube.</summary>
        public static float RadiusOf(int faces) => faces == 6 ? 0.016f * 0.8660254f : faces == 4 ? 0.014f : 0.012f;

        public static DiceGeometry Get(int faces)
        {
            if (!Cache.TryGetValue(faces, out var geometry) || geometry.Mesh == null)
            {
                geometry = Build(faces, RadiusOf(faces));
                Cache[faces] = geometry;
            }
            return geometry;
        }

        /// <summary>Rotation that shows <paramref name="value"/> with the die turned <paramref name="yaw"/> degrees.</summary>
        public Quaternion RotationFor(int value, float yaw)
        {
            var target = ReadsDown ? Vector3.down : Vector3.up;
            return Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.FromToRotation(ValueNormals[value - 1], target);
        }

        public int ValueFor(Quaternion rotation)
        {
            var target = ReadsDown ? Vector3.down : Vector3.up;
            var best = 0;
            var bestDot = float.MinValue;
            for (var i = 0; i < ValueNormals.Length; i++)
            {
                var dot = Vector3.Dot(rotation * ValueNormals[i], target);
                if (dot > bestDot)
                {
                    bestDot = dot;
                    best = i;
                }
            }
            return best + 1;
        }

        private static DiceGeometry Build(int faces, float radius)
        {
            // Uniform scale, not per-vertex normalization: the d10 ring and apexes sit at different distances.
            var raw = Vertices(faces).ToList();
            var scale = radius / raw.Max(v => v.magnitude);
            var points = raw.Select(v => v * scale).ToList();
            var polygons = FindFaces(points);
            if (polygons.Count != faces)
                throw new System.InvalidOperationException($"d{faces}: found {polygons.Count} faces.");

            var values = AssignValues(polygons, faces);
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            var valueNormals = new Vector3[faces];

            for (var f = 0; f < polygons.Count; f++)
            {
                var (normal, loop) = polygons[f];
                var value = values[f];
                valueNormals[value - 1] = normal;

                var centroid = loop.Aggregate(Vector3.zero, (s, p) => s + p) / loop.Count;
                // Face-local axes: "up" of the printed number is the direction towards the first vertex.
                var up = (loop[0] - centroid).normalized;
                var right = Vector3.Cross(normal, up).normalized;
                var circumradius = loop.Max(p => (p - centroid).magnitude);
                var cell = new Vector2((value - 1) % BuiltinTextures.DiceColumns, (value - 1) / BuiltinTextures.DiceColumns);
                var cellSize = new Vector2(1f / BuiltinTextures.DiceColumns, 1f / BuiltinTextures.DiceRows);

                var start = vertices.Count;
                foreach (var p in loop)
                {
                    var local = p - centroid;
                    var a = Vector3.Dot(local, right) / circumradius * 0.48f;
                    var b = Vector3.Dot(local, up) / circumradius * 0.48f;
                    vertices.Add(p);
                    normals.Add(normal);
                    uvs.Add(new Vector2((cell.x + 0.5f + a) * cellSize.x, 1f - (cell.y + 0.5f - b) * cellSize.y));
                }
                for (var i = 1; i < loop.Count - 1; i++)
                    triangles.AddRange(new[] { start, start + i, start + i + 1 });
            }

            var mesh = new Mesh { name = $"d{faces}" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return new DiceGeometry(faces, mesh, valueNormals);
        }

        private static IEnumerable<Vector3> Vertices(int faces)
        {
            var phi = (1f + Mathf.Sqrt(5f)) / 2f;
            switch (faces)
            {
                case 4:
                    return new[] { new Vector3(1, 1, 1), new Vector3(1, -1, -1), new Vector3(-1, 1, -1), new Vector3(-1, -1, 1) };
                case 6:
                    return from x in new[] { -1, 1 } from y in new[] { -1, 1 } from z in new[] { -1, 1 } select new Vector3(x, y, z);
                case 8:
                    return new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
                case 10:
                {
                    // Pentagonal trapezohedron: ring at ±b, apexes at ±h with h = b(1 + cos36°)/(1 − cos36°) so every kite is planar.
                    const float b = 0.105f;
                    var cos36 = Mathf.Cos(36f * Mathf.Deg2Rad);
                    var h = b * (1f + cos36) / (1f - cos36);
                    var list = new List<Vector3> { new Vector3(0, h, 0), new Vector3(0, -h, 0) };
                    for (var k = 0; k < 10; k++)
                    {
                        var a = 36f * k * Mathf.Deg2Rad;
                        list.Add(new Vector3(Mathf.Cos(a), k % 2 == 0 ? b : -b, Mathf.Sin(a)));
                    }
                    return list;
                }
                case 12:
                {
                    var list = new List<Vector3>();
                    foreach (var x in new[] { -1f, 1f })
                    foreach (var y in new[] { -1f, 1f })
                    {
                        foreach (var z in new[] { -1f, 1f })
                            list.Add(new Vector3(x, y, z));
                        list.Add(new Vector3(0, x / phi, y * phi));
                        list.Add(new Vector3(x / phi, y * phi, 0));
                        list.Add(new Vector3(x * phi, 0, y / phi));
                    }
                    return list;
                }
                case 20:
                {
                    var list = new List<Vector3>();
                    foreach (var x in new[] { -1f, 1f })
                    foreach (var y in new[] { -1f, 1f })
                    {
                        list.Add(new Vector3(0, x, y * phi));
                        list.Add(new Vector3(x, y * phi, 0));
                        list.Add(new Vector3(x * phi, 0, y));
                    }
                    return list;
                }
                default:
                    throw new System.ArgumentException($"Unsupported die d{faces}.");
            }
        }

        /// <summary>
        /// Each face as its outward normal and its vertices ordered by increasing angle from <c>axis</c> towards
        /// <c>normal × axis</c>, which is clockwise on screen seen from outside: Unity's front-face winding.
        /// </summary>
        private static List<(Vector3 Normal, List<Vector3> Loop)> FindFaces(List<Vector3> points)
        {
            const float epsilon = 1e-4f;
            var faces = new List<(Vector3, List<Vector3>)>();
            var scale = points.Max(p => p.magnitude);
            for (var i = 0; i < points.Count; i++)
            for (var j = i + 1; j < points.Count; j++)
            for (var k = j + 1; k < points.Count; k++)
            {
                var normal = Vector3.Cross(points[j] - points[i], points[k] - points[i]);
                if (normal.sqrMagnitude < 1e-12f)
                    continue;
                normal.Normalize();
                var d = Vector3.Dot(normal, points[i]);
                if (d < 0)
                {
                    normal = -normal;
                    d = -d;
                }
                if (points.Any(p => Vector3.Dot(normal, p) - d > epsilon * scale))
                    continue;
                if (faces.Any(f => Vector3.Dot(f.Item1, normal) > 1f - 1e-5f))
                    continue;

                var onPlane = points.Where(p => Mathf.Abs(Vector3.Dot(normal, p) - d) <= epsilon * scale).ToList();
                var centroid = onPlane.Aggregate(Vector3.zero, (s, p) => s + p) / onPlane.Count;
                var axis = (onPlane[0] - centroid).normalized;
                var other = Vector3.Cross(normal, axis);
                var loop = onPlane
                    .OrderBy(p => Mathf.Atan2(Vector3.Dot(p - centroid, other), Vector3.Dot(p - centroid, axis)))
                    .ToList();
                faces.Add((normal, loop));
            }
            return faces;
        }

        /// <summary>Values 1..n; each face's opposite (antiparallel normal) gets n + 1 − v when one exists.</summary>
        private static int[] AssignValues(List<(Vector3 Normal, List<Vector3> Loop)> faces, int n)
        {
            var order = Enumerable.Range(0, faces.Count)
                .OrderByDescending(i => Mathf.Round(faces[i].Normal.y * 1000f))
                .ThenBy(i => Mathf.Atan2(faces[i].Normal.z, faces[i].Normal.x))
                .ToList();
            var values = new int[faces.Count];
            var next = 1;
            foreach (var i in order)
            {
                if (values[i] != 0)
                    continue;
                values[i] = next++;
                for (var j = 0; j < faces.Count; j++)
                {
                    if (values[j] == 0 && Vector3.Dot(faces[j].Normal, faces[i].Normal) < -0.999f)
                    {
                        values[j] = n + 1 - values[i];
                        break;
                    }
                }
            }
            // No opposite faces (d4) or a clash: number in reading order instead.
            if (values.Distinct().Count() != n || values.Any(v => v < 1 || v > n))
                for (var i = 0; i < order.Count; i++)
                    values[order[i]] = i + 1;
            return values;
        }
    }
}
