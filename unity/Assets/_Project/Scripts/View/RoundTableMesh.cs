using System.Collections.Generic;
using UnityEngine;

namespace TableWorld.View
{
    /// <summary>
    /// The meshes of a round table (M12): a disc for the playing surface and a ring round it for the rim, both made here
    /// for the table's size, so no model asset is needed. The disc's UVs span 0–1 across it like the box table's top, so
    /// a felt image or the tavern's planks lie on it the same way.
    /// </summary>
    public static class RoundTableMesh
    {
        /// <summary>Sides of the circle: enough that the edge reads as round from a seat.</summary>
        public const int Segments = 96;

        /// <summary>A disc <paramref name="radius"/> wide, its top at y = 0 and its bottom <paramref name="thickness"/> below.</summary>
        public static Mesh Disc(float radius, float thickness)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            var diameter = 2f * radius;

            // Top: a fan round the centre, facing up.
            vertices.Add(Vector3.zero);
            normals.Add(Vector3.up);
            uvs.Add(new Vector2(0.5f, 0.5f));
            for (var i = 0; i <= Segments; i++)
            {
                var p = Edge(i, radius);
                vertices.Add(p);
                normals.Add(Vector3.up);
                uvs.Add(new Vector2(p.x / diameter + 0.5f, p.z / diameter + 0.5f));
            }
            for (var i = 0; i < Segments; i++)
                triangles.AddRange(new[] { 0, i + 1, i + 2 });

            // The edge, facing out.
            var side = vertices.Count;
            for (var i = 0; i <= Segments; i++)
            {
                var p = Edge(i, radius);
                var n = new Vector3(p.x, 0f, p.z).normalized;
                vertices.Add(p);
                vertices.Add(p + Vector3.down * thickness);
                normals.Add(n);
                normals.Add(n);
                uvs.Add(new Vector2((float)i / Segments, 1f));
                uvs.Add(new Vector2((float)i / Segments, 0f));
            }
            for (var i = 0; i < Segments; i++)
            {
                var a = side + 2 * i;
                triangles.AddRange(new[] { a, a + 1, a + 2, a + 1, a + 3, a + 2 });
            }

            // Bottom, facing down.
            var bottom = vertices.Count;
            vertices.Add(Vector3.down * thickness);
            normals.Add(Vector3.down);
            uvs.Add(new Vector2(0.5f, 0.5f));
            for (var i = 0; i <= Segments; i++)
            {
                var p = Edge(i, radius) + Vector3.down * thickness;
                vertices.Add(p);
                normals.Add(Vector3.down);
                uvs.Add(new Vector2(p.x / diameter + 0.5f, p.z / diameter + 0.5f));
            }
            for (var i = 0; i < Segments; i++)
                triangles.AddRange(new[] { bottom, bottom + i + 2, bottom + i + 1 });

            return Build("Round Table Surface", vertices, normals, uvs, triangles);
        }

        /// <summary>
        /// A ring from <paramref name="inner"/> to <paramref name="inner"/> + <paramref name="width"/>, its top
        /// <paramref name="height"/> above the surface and its bottom <paramref name="depth"/> below it.
        /// </summary>
        public static Mesh Ring(float inner, float width, float height, float depth)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            var outer = inner + width;

            // Each wall or face is a strip between two circles: a at the start of the strip's edge, b at its end.
            void Strip(float radiusA, float yA, float radiusB, float yB, System.Func<Vector3, Vector3> normal)
            {
                var start = vertices.Count;
                for (var i = 0; i <= Segments; i++)
                {
                    var a = Edge(i, radiusA) + Vector3.up * yA;
                    var b = Edge(i, radiusB) + Vector3.up * yB;
                    vertices.Add(a);
                    vertices.Add(b);
                    normals.Add(normal(a));
                    normals.Add(normal(b));
                    uvs.Add(new Vector2((float)i / Segments * 8f, 0f));
                    uvs.Add(new Vector2((float)i / Segments * 8f, 1f));
                }
                for (var i = 0; i < Segments; i++)
                {
                    var a = start + 2 * i;
                    triangles.AddRange(new[] { a, a + 1, a + 2, a + 1, a + 3, a + 2 });
                }
            }

            Vector3 Out(Vector3 p) => new Vector3(p.x, 0f, p.z).normalized;
            // Inner wall (facing the centre), top, outer wall; each wound so its face points the way of its normal.
            Strip(inner, -depth, inner, height, p => -Out(p));
            Strip(inner, height, outer, height, _ => Vector3.up);
            Strip(outer, height, outer, -depth, Out);
            return Build("Round Table Rim", vertices, normals, uvs, triangles);
        }

        private static Vector3 Edge(int i, float radius)
        {
            // Clockwise seen from above (+x towards -z).
            var angle = -2f * Mathf.PI * i / Segments;
            return new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
        }

        private static Mesh Build(string name, List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> triangles)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }
    }
}
