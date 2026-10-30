using System.Collections.Generic;
using UnityEngine;

namespace TableWorld.View.Meshes
{
    /// <summary>
    /// Procedural meshes centred on the origin. Prisms have three submeshes: 0 = top (+Y) face, 1 = bottom (−Y) face,
    /// 2 = sides. Top UVs map the outline's bounding box to 0..1 with v towards +Z (image top points away from the
    /// south seat); bottom UVs are mirrored in U so a face turned over around Z reads correctly. The <c>Image*</c>
    /// shapes turn both faces' images 180° as TTS draws cards, tiles, tokens and boards: upright from +Z at yaw 0,
    /// so a piece facing the south seat has yaw 180 (ADR 0005).
    /// </summary>
    public static class MeshFactory
    {
        public const int TopSubmesh = 0;
        public const int BottomSubmesh = 1;
        public const int SideSubmesh = 2;

        private static readonly Dictionary<string, Mesh> Cache = new Dictionary<string, Mesh>();

        /// <summary>
        /// <paramref name="mesh"/> turned <paramref name="quarters"/> quarter turns about +Y (its name keeps its kind, so a
        /// turned box still gets a box collider): a card drawn turned toward one player without moving it.
        /// </summary>
        public static Mesh Turned(Mesh mesh, int quarters)
        {
            quarters = ((quarters % 4) + 4) % 4;
            if (quarters == 0)
                return mesh;
            return Cached($"{mesh.name}:q{quarters}", () =>
            {
                var turn = Quaternion.Euler(0f, 90f * quarters, 0f);
                var turned = Object.Instantiate(mesh);
                var vertices = mesh.vertices;
                var normals = mesh.normals;
                for (var i = 0; i < vertices.Length; i++)
                    vertices[i] = turn * vertices[i];
                for (var i = 0; i < normals.Length; i++)
                    normals[i] = turn * normals[i];
                turned.vertices = vertices;
                turned.normals = normals;
                turned.RecalculateBounds();
                turned.RecalculateTangents();
                return turned;
            });
        }

        public static Mesh Box(Vector3 size) =>
            Cached($"box:{size.x:F5}:{size.y:F5}:{size.z:F5}", () => Prism(Rectangle(size.x, size.z), size.y));

        public static Mesh Cylinder(float radius, float height, int segments = 32) =>
            Cached($"cyl:{radius:F5}:{height:F5}:{segments}", () => Prism(Polygon(radius, segments, 0f), height));

        /// <summary>Hexagon with flat edges facing ±Z.</summary>
        public static Mesh HexPrism(float radius, float height) =>
            Cached($"hex:{radius:F5}:{height:F5}", () => Prism(Polygon(radius, 6, 0f), height));

        public static Mesh ImageBox(Vector3 size) =>
            Cached($"ibox:{size.x:F5}:{size.y:F5}:{size.z:F5}", () => Prism(Rectangle(size.x, size.z), size.y, turned: true));

        public static Mesh ImageCylinder(float radius, float height, int segments = 32) =>
            Cached($"icyl:{radius:F5}:{height:F5}:{segments}", () => Prism(Polygon(radius, segments, 0f), height, turned: true));

        public static Mesh ImageHexPrism(float radius, float height) =>
            Cached($"ihex:{radius:F5}:{height:F5}", () => Prism(Polygon(radius, 6, 0f), height, turned: true));

        /// <summary>
        /// A piece cut along its image's outline (TTS custom tokens): <paramref name="loops"/> in image UVs (from
        /// <see cref="Core.Models.Silhouette"/>) laid on a <paramref name="face"/> (x × z) rectangle and extruded to
        /// <paramref name="height"/>, with the image turned 180° as <see cref="ImageBox"/> does (or, not
        /// <paramref name="turned"/>, as <see cref="Box"/> lays it) and the same submeshes, so the image lands where it does
        /// on the box. Cached by the outline itself (one list per traced image).
        /// </summary>
        public static Mesh ImageSilhouette(IReadOnlyList<Core.Models.Silhouette.Loop> loops, Vector2 face, float height, bool turned = true) =>
            Cached($"isil:{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(loops)}:{loops.Count}:{face.x:F5}:{face.y:F5}:{height:F5}:{turned}", () =>
            {
                var vertices = new List<Vector3>();
                var normals = new List<Vector3>();
                var uvs = new List<Vector2>();
                var topTris = new List<int>();
                var bottomTris = new List<int>();
                var sideTris = new List<int>();
                var top = height / 2f;
                var bottom = -height / 2f;
                // The image turned 180°: UV (u, v) sits at x = (0.5 − u)·width, z = (0.5 − v)·depth; unturned at (u − 0.5, v − 0.5).
                var sign = turned ? -1f : 1f;
                Vector2 At(Core.Rules.Vec2 uv) => new Vector2(sign * (uv.X - 0.5f) * face.x, sign * (uv.Y - 0.5f) * face.y);
                var perimeter = 0f;
                foreach (var loop in loops)
                    for (var i = 0; i < loop.Points.Count; i++)
                        perimeter += Vector2.Distance(At(loop.Points[i]), At(loop.Points[(i + 1) % loop.Points.Count]));
                var run = 0f;
                foreach (var loop in loops)
                {
                    // Caps: the loop's own triangles, counter-clockwise in (x, z), so the top reverses them.
                    foreach (var (y, normal, tris, mirrorU) in new[] { (top, Vector3.up, topTris, false), (bottom, Vector3.down, bottomTris, true) })
                    {
                        var first = vertices.Count;
                        foreach (var p in loop.Points)
                        {
                            var at = At(p);
                            vertices.Add(new Vector3(at.x, y, at.y));
                            normals.Add(normal);
                            uvs.Add(new Vector2(mirrorU ? 1f - p.X : p.X, p.Y));
                        }
                        for (var t = 0; t < loop.Triangles.Count; t += 3)
                        {
                            int a = first + loop.Triangles[t], b = first + loop.Triangles[t + 1], c = first + loop.Triangles[t + 2];
                            tris.AddRange(normal.y > 0f ? new[] { c, b, a } : new[] { a, b, c });
                        }
                    }
                    // Sides: one quad per edge, flat shaded, the side image running round the perimeter.
                    for (var i = 0; i < loop.Points.Count; i++)
                    {
                        var a = At(loop.Points[i]);
                        var b = At(loop.Points[(i + 1) % loop.Points.Count]);
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
            });

        /// <summary>
        /// A cloth pouch tied at the neck (TTS's built-in bag): a profile turned about the vertical, <paramref name="radius"/>
        /// at its belly, <paramref name="height"/> tall, centred on the origin. One submesh.
        /// </summary>
        public static Mesh Pouch(float radius, float height, int segments = 28) =>
            Cached($"pouch:{radius:F5}:{height:F5}:{segments}", () =>
            {
                // (radius share, height share from the bottom): round bottom, belly, gathered neck, frilled mouth.
                var profile = new[]
                {
                    new Vector2(0f, 0f), new Vector2(0.5f, 0f), new Vector2(0.82f, 0.06f), new Vector2(0.97f, 0.2f),
                    new Vector2(1f, 0.38f), new Vector2(0.93f, 0.56f), new Vector2(0.72f, 0.7f), new Vector2(0.46f, 0.78f),
                    new Vector2(0.42f, 0.84f), new Vector2(0.56f, 0.93f), new Vector2(0.6f, 1f), new Vector2(0f, 0.98f),
                };
                var vertices = new List<Vector3>();
                var uvs = new List<Vector2>();
                var tris = new List<int>();
                for (var p = 0; p < profile.Length; p++)
                    for (var s = 0; s <= segments; s++)
                    {
                        var a = s * Mathf.PI * 2f / segments;
                        var r = profile[p].x * radius;
                        vertices.Add(new Vector3(Mathf.Cos(a) * r, (profile[p].y - 0.5f) * height, Mathf.Sin(a) * r));
                        uvs.Add(new Vector2((float)s / segments, profile[p].y));
                    }
                var ring = segments + 1;
                for (var p = 0; p < profile.Length - 1; p++)
                    for (var s = 0; s < segments; s++)
                    {
                        var a = p * ring + s;
                        var b = a + ring;
                        tris.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
                    }
                var mesh = new Mesh();
                mesh.SetVertices(vertices);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                mesh.RecalculateTangents();
                return mesh;
            });

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

        /// <summary>
        /// Extrudes a convex counter-clockwise outline (x, z) to <paramref name="height"/>, centred on Y = 0;
        /// <paramref name="turned"/> turns the faces' images 180°.
        /// </summary>
        public static Mesh Prism(Vector2[] outline, float height, bool turned = false)
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
                if (turned)
                {
                    u = 1f - u;
                    v = 1f - v;
                }
                return new Vector2(mirrorU ? 1f - u : u, v);
            }
        }
    }
}
