using System.Collections.Generic;
using Core.Rules;
using UnityEngine;

namespace TableWorld.View
{
    /// <summary>
    /// Lines and arrows drawn on the table (G28): flat strips just above where they were drawn, in the drawer's colour,
    /// with a triangle head on arrows. One child object per drawing.
    /// </summary>
    public static class DrawingView
    {
        public const float Lift = 0.0008f;
        private static readonly Dictionary<Color, Material> Materials = new Dictionary<Color, Material>();

        /// <summary>Replaces the children of <paramref name="root"/> with one object per drawing.</summary>
        public static void Rebuild(Transform root, IReadOnlyList<DrawingDef> drawings)
        {
            for (var i = root.childCount - 1; i >= 0; i--)
            {
                var child = root.GetChild(i).gameObject;
                child.SetActive(false);
                Object.Destroy(child);
            }
            foreach (var drawing in drawings)
                Build(drawing, root);
        }

        public static GameObject Build(DrawingDef drawing, Transform parent)
        {
            var go = new GameObject($"Drawing {drawing.Id}");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = Mesh(drawing);
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            var c = drawing.Color;
            renderer.sharedMaterial = MaterialFor(new Color(c[0], c[1], c[2]));
            return go;
        }

        private static Material MaterialFor(Color color)
        {
            if (Materials.TryGetValue(color, out var material) && material != null)
                return material;
            material = new Material(RequiredShaders.Unlit) { name = "Drawing" };
            material.SetColor("_BaseColor", color);
            Materials[color] = material;
            return material;
        }

        private static Mesh Mesh(DrawingDef drawing)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var half = drawing.Thickness / 2f;
            var points = new List<Vector3>();
            foreach (var p in drawing.Points)
                points.Add(new Vector3(p.X, p.Y + Lift, p.Z));
            var headLength = drawing.Thickness * 5f;
            if (drawing.Arrow && points.Count >= 2)
            {
                // Stop the shaft where the head begins, so the tip is sharp.
                var end = points[points.Count - 1];
                var dir = Flat(end - points[points.Count - 2]);
                points[points.Count - 1] = end - dir * Mathf.Min(headLength * 0.8f, (end - points[points.Count - 2]).magnitude);
            }
            for (var i = 0; i + 1 < points.Count; i++)
            {
                var a = points[i];
                var b = points[i + 1];
                var side = Side(b - a) * half;
                var start = vertices.Count;
                vertices.Add(a - side);
                vertices.Add(a + side);
                vertices.Add(b + side);
                vertices.Add(b - side);
                triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
                // A round-ish joint: a small square at each bend hides the gaps between segments.
                if (i + 2 < points.Count)
                {
                    var j = vertices.Count;
                    vertices.Add(b + new Vector3(-half, 0f, -half));
                    vertices.Add(b + new Vector3(-half, 0f, half));
                    vertices.Add(b + new Vector3(half, 0f, half));
                    vertices.Add(b + new Vector3(half, 0f, -half));
                    triangles.AddRange(new[] { j, j + 1, j + 2, j, j + 2, j + 3 });
                }
            }
            if (points.Count == 1)
            {
                var p = points[0];
                var j = vertices.Count;
                vertices.Add(p + new Vector3(-half, 0f, -half));
                vertices.Add(p + new Vector3(-half, 0f, half));
                vertices.Add(p + new Vector3(half, 0f, half));
                vertices.Add(p + new Vector3(half, 0f, -half));
                triangles.AddRange(new[] { j, j + 1, j + 2, j, j + 2, j + 3 });
            }
            if (drawing.Arrow && drawing.Points.Count >= 2)
            {
                var n = drawing.Points.Count;
                var tip = new Vector3(drawing.Points[n - 1].X, drawing.Points[n - 1].Y + Lift, drawing.Points[n - 1].Z);
                var from = new Vector3(drawing.Points[n - 2].X, drawing.Points[n - 2].Y + Lift, drawing.Points[n - 2].Z);
                var dir = Flat(tip - from);
                var baseCentre = tip - dir * headLength;
                var wing = Side(dir) * headLength * 0.6f;
                var k = vertices.Count;
                vertices.Add(tip);
                vertices.Add(baseCentre - wing);
                vertices.Add(baseCentre + wing);
                triangles.AddRange(new[] { k, k + 1, k + 2 });
            }
            var mesh = new Mesh { name = "drawing", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            // Both windings, so the strip shows whichever way the line was drawn.
            var both = new List<int>(triangles);
            for (var i = 0; i < triangles.Count; i += 3)
                both.AddRange(new[] { triangles[i], triangles[i + 2], triangles[i + 1] });
            mesh.SetTriangles(both, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude < 1e-12f ? Vector3.forward : v.normalized;
        }

        private static Vector3 Side(Vector3 along)
        {
            var f = Flat(along);
            return new Vector3(-f.z, 0f, f.x);
        }
    }
}
