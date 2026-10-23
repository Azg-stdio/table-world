using System.Collections.Generic;
using UnityEngine;

namespace TableWorld.View.Meshes
{
    /// <summary>
    /// Procedural meshes centred on the origin. Prisms have three submeshes: 0 = top (+Y) face, 1 = bottom (−Y) face,
    /// 2 = sides. Top UVs map the outline's bounding box to 0..1 with v towards +Z (image top points away from the
    /// south seat); bottom UVs are mirrored in U so a face turned over around Z reads correctly.
    /// </summary>
    public static class MeshFactory
    {
        public const int TopSubmesh = 0;
        public const int BottomSubmesh = 1;
        public const int SideSubmesh = 2;

        private static readonly Dictionary<string, Mesh> Cache = new Dictionary<string, Mesh>();

        public static Mesh Box(Vector3 size) =>
            Cached($"box:{size.x:F5}:{size.y:F5}:{size.z:F5}", () => Prism(Rectangle(size.x, size.z), size.y));

        public static Mesh Cylinder(float radius, float height, int segments = 32) =>
            Cached($"cyl:{radius:F5}:{height:F5}:{segments}", () => Prism(Polygon(radius, segments, 0f), height));

        /// <summary>Hexagon with flat edges facing ±Z.</summary>
        public static Mesh HexPrism(float radius, float height) =>
            Cached($"hex:{radius:F5}:{height:F5}", () => Prism(Polygon(radius, 6, 0f), height));

        private static Mesh Cached(string key, System.Func<Mesh> build)
        {
            if (!Cache.TryGetValue(key, out var mesh) || mesh == null)
            {
                mesh = build();
                mesh.name = key;
                Cache[key] = mesh;
            }
            return mesh;
        }

        /// <summary>Counter-clockwise (seen from above) rectangle in the XZ plane.</summary>
        private static Vector2[] Rectangle(float width, float depth)
        {
            var w = width / 2f;
            var d = depth / 2f;
            return new[] { new Vector2(-w, -d), new Vector2(w, -d), new Vector2(w, d), new Vector2(-w, d) };
        }

        /// <summary>Counter-clockwise regular polygon in the XZ plane, first vertex at <paramref name="startDegrees"/>.</summary>
        private static Vector2[] Polygon(float radius, int sides, float startDegrees)
        {
            var points = new Vector2[sides];
            for (var i = 0; i < sides; i++)
            {
                var a = (startDegrees + 360f * i / sides) * Mathf.Deg2Rad;
                points[i] = new Vector2(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius);
            }
            return points;
        }

        /// <summary>Extrudes a convex counter-clockwise outline (x, z) to <paramref name="height"/>, centred on Y = 0.</summary>
        public static Mesh Prism(Vector2[] outline, float height)
        {
            var n = outline.Length;
            var min = outline[0];
            var max = outline[0];
            foreach (var p in outline)
            {
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
            var extent = max - min;
            var top = height / 2f;
            var bottom = -height / 2f;

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var topTris = new List<int>();
            var bottomTris = new List<int>();
            var sideTris = new List<int>();

            // Caps (fan from the centre). Unity front faces wind clockwise as seen by the viewer.
            var centre = (min + max) / 2f;
            AddCap(top, Vector3.up, mirrorU: false, topTris, flip: true);
            AddCap(bottom, Vector3.down, mirrorU: true, bottomTris, flip: false);

            // Sides, one quad per edge with its own normal (flat shading).
            var perimeter = 0f;
            for (var i = 0; i < n; i++)
                perimeter += Vector2.Distance(outline[i], outline[(i + 1) % n]);
            var run = 0f;
            for (var i = 0; i < n; i++)
            {
                var a = outline[i];
                var b = outline[(i + 1) % n];
                var edge = b - a;
                var normal = new Vector3(edge.y, 0f, -edge.x).normalized;
                var u0 = run / perimeter;
                run += edge.magnitude;
                var u1 = run / perimeter;
                var start = vertices.Count;
                vertices.Add(new Vector3(a.x, bottom, a.y));
                vertices.Add(new Vector3(a.x, top, a.y));
                vertices.Add(new Vector3(b.x, top, b.y));
                vertices.Add(new Vector3(b.x, bottom, b.y));
                for (var k = 0; k < 4; k++)
                    normals.Add(normal);
                uvs.Add(new Vector2(u0, 0f));
                uvs.Add(new Vector2(u0, 1f));
                uvs.Add(new Vector2(u1, 1f));
                uvs.Add(new Vector2(u1, 0f));
                sideTris.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
            }

            var mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = 3;
            mesh.SetTriangles(topTris, TopSubmesh);
            mesh.SetTriangles(bottomTris, BottomSubmesh);
            mesh.SetTriangles(sideTris, SideSubmesh);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;

            void AddCap(float y, Vector3 normal, bool mirrorU, List<int> tris, bool flip)
            {
                var c = vertices.Count;
                vertices.Add(new Vector3(centre.x, y, centre.y));
                normals.Add(normal);
                uvs.Add(Uv(centre, mirrorU));
                for (var i = 0; i < n; i++)
                {
                    vertices.Add(new Vector3(outline[i].x, y, outline[i].y));
                    normals.Add(normal);
                    uvs.Add(Uv(outline[i], mirrorU));
                }
                for (var i = 0; i < n; i++)
                {
                    var a = c + 1 + i;
                    var b = c + 1 + (i + 1) % n;
                    if (flip)
                        tris.AddRange(new[] { c, b, a });
                    else
                        tris.AddRange(new[] { c, a, b });
                }
            }

            Vector2 Uv(Vector2 p, bool mirrorU)
            {
                var u = (p.x - min.x) / extent.x;
                var v = (p.y - min.y) / extent.y;
                return new Vector2(mirrorU ? 1f - u : u, v);
            }
        }
    }
}
