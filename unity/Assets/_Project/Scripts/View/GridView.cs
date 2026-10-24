using System.Collections.Generic;
using Core.Rules;
using UnityEngine;

namespace TableWorld.View
{
    /// <summary>
    /// The table grid's lines (G28), drawn just above the felt over the playing surface: thin see-through strips in the
    /// grid's colour and opacity. Hex grids draw each shared edge once, so overlapping strips do not darken.
    /// </summary>
    public static class GridView
    {
        public const float LineWidth = 0.0012f;
        public const float Lift = 0.0004f;

        private static Material _material;

        /// <summary>Builds (or rebuilds) the lines for <paramref name="grid"/> under <paramref name="parent"/>; null when there are none.</summary>
        public static GameObject Build(GridDef grid, Transform parent)
        {
            if (grid == null || !grid.Lines || grid.Opacity <= 0f)
                return null;
            var segments = grid.Shape == GridShape.Hex ? HexSegments(grid) : SquareSegments(grid);
            var go = new GameObject("Grid");
            go.transform.SetParent(parent, false);
            var mesh = new Mesh { name = "grid lines", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            foreach (var (a, b) in segments)
            {
                var along = (b - a).normalized;
                var side = new Vector3(-along.z, 0f, along.x) * (LineWidth / 2f);
                var start = vertices.Count;
                vertices.Add(a - side);
                vertices.Add(a + side);
                vertices.Add(b + side);
                vertices.Add(b - side);
                triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
            }
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            var material = new Material(Material());
            var c = grid.Color;
            material.SetColor("_BaseColor", new Color(c[0], c[1], c[2], grid.Opacity));
            renderer.sharedMaterial = material;
            return go;
        }

        /// <summary>URP Unlit set up for alpha blending.</summary>
        private static Material Material()
        {
            if (_material != null)
                return _material;
            _material = new Material(RequiredShaders.Unlit) { name = "Grid" };
            _material.SetFloat("_Surface", 1f);
            _material.SetFloat("_Blend", 0f);
            _material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _material.SetFloat("_ZWrite", 0f);
            _material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return _material;
        }

        private static float HalfX => TableGeometry.Size.x / 2f;
        private static float HalfZ => TableGeometry.Size.y / 2f;

        private static IEnumerable<(Vector3, Vector3)> SquareSegments(GridDef grid)
        {
            var first = Mathf.Ceil((-HalfX - grid.OffsetX) / grid.SizeX);
            for (var x = grid.OffsetX + first * grid.SizeX; x <= HalfX; x += grid.SizeX)
                yield return (new Vector3(x, Lift, -HalfZ), new Vector3(x, Lift, HalfZ));
            first = Mathf.Ceil((-HalfZ - grid.OffsetZ) / grid.SizeZ);
            for (var z = grid.OffsetZ + first * grid.SizeZ; z <= HalfZ; z += grid.SizeZ)
                yield return (new Vector3(-HalfX, Lift, z), new Vector3(HalfX, Lift, z));
        }

        private static IEnumerable<(Vector3, Vector3)> HexSegments(GridDef grid)
        {
            var seen = new HashSet<(int, int, int, int)>();
            var rows = Mathf.CeilToInt(HalfZ / grid.HexRow) + 1;
            var columns = Mathf.CeilToInt(HalfX / grid.SizeX) + 1;
            var rowShift = Mathf.RoundToInt(grid.OffsetZ / grid.HexRow);
            var columnShift = Mathf.RoundToInt(grid.OffsetX / grid.SizeX);
            for (var r = -rows - rowShift; r <= rows - rowShift; r++)
            for (var c = -columns - columnShift; c <= columns - columnShift; c++)
            {
                var (cx, cz) = grid.HexCenterOf(c, r);
                if (Mathf.Abs(cx) > HalfX + grid.SizeX || Mathf.Abs(cz) > HalfZ + grid.SizeZ)
                    continue;
                var corners = grid.HexCorners(cx, cz);
                for (var i = 0; i < 6; i++)
                {
                    var a = corners[i];
                    var b = corners[(i + 1) % 6];
                    if (!Clip(ref a, ref b))
                        continue;
                    var key = Key(a, b);
                    if (seen.Add(key))
                        yield return (new Vector3(a.X, Lift, a.Z), new Vector3(b.X, Lift, b.Z));
                }
            }
        }

        /// <summary>Same edge from either hex, either direction: the same key (endpoints rounded to 0.1 mm).</summary>
        private static (int, int, int, int) Key((float X, float Z) a, (float X, float Z) b)
        {
            var ka = (Mathf.RoundToInt(a.X * 10000f), Mathf.RoundToInt(a.Z * 10000f));
            var kb = (Mathf.RoundToInt(b.X * 10000f), Mathf.RoundToInt(b.Z * 10000f));
            return ka.CompareTo(kb) <= 0 ? (ka.Item1, ka.Item2, kb.Item1, kb.Item2) : (kb.Item1, kb.Item2, ka.Item1, ka.Item2);
        }

        /// <summary>Clips a segment to the playing surface (Liang–Barsky); false when it lies outside.</summary>
        private static bool Clip(ref (float X, float Z) a, ref (float X, float Z) b)
        {
            float t0 = 0f, t1 = 1f;
            var dx = b.X - a.X;
            var dz = b.Z - a.Z;
            bool Edge(float p, float q)
            {
                if (Mathf.Abs(p) < 1e-9f)
                    return q >= 0f;
                var t = q / p;
                if (p < 0f)
                {
                    if (t > t1) return false;
                    if (t > t0) t0 = t;
                }
                else
                {
                    if (t < t0) return false;
                    if (t < t1) t1 = t;
                }
                return true;
            }
            if (!Edge(-dx, a.X + HalfX) || !Edge(dx, HalfX - a.X) || !Edge(-dz, a.Z + HalfZ) || !Edge(dz, HalfZ - a.Z))
                return false;
            var start = (a.X + t0 * dx, a.Z + t0 * dz);
            var end = (a.X + t1 * dx, a.Z + t1 * dz);
            a = start;
            b = end;
            return true;
        }
    }
}
