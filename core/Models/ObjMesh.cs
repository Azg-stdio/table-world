using System;
using System.Collections.Generic;
using System.Globalization;
using Core.Rules;

namespace Core.Models
{
    /// <summary>A triangle mesh ready for a renderer: one vertex per position/uv/normal combination.</summary>
    public sealed class MeshData
    {
        public List<Vec3> Positions { get; } = new List<Vec3>();
        public List<Vec2> Uvs { get; } = new List<Vec2>();
        /// <summary>Empty when the file had no normals (the renderer computes them).</summary>
        public List<Vec3> Normals { get; } = new List<Vec3>();
        public List<int> Triangles { get; } = new List<int>();

        public int TriangleCount => Triangles.Count / 3;
    }

    /// <summary>
    /// Wavefront OBJ (TTS <c>Custom_Model</c> meshes, M4/G38): positions, texture coordinates, normals and faces
    /// (polygons are fanned into triangles; negative indices count back from the end). Materials, groups and smoothing
    /// are ignored: TTS draws a custom model with one diffuse image. Converted to Unity's left-handed axes (X mirrored,
    /// winding reversed) as Unity's own OBJ importer and TTS do. Limits keep a hostile file from exhausting memory.
    /// </summary>
    public static class ObjMesh
    {
        public const int MaxVertices = 500_000;
        public const int MaxTriangles = 500_000;

        /// <summary>Parses <paramref name="text"/>; null (with <paramref name="error"/>) when it is not a usable mesh.</summary>
        public static MeshData? Parse(string text, out string? error)
        {
            error = null;
            var positions = new List<Vec3>();
            var uvs = new List<Vec2>();
            var normals = new List<Vec3>();
            var mesh = new MeshData();
            var corners = new Dictionary<(int, int, int), int>();
            var face = new List<int>();
            var lineNumber = 0;
            foreach (var raw in text.Split('\n'))
            {
                lineNumber++;
                var line = raw.Trim();
                var hash = line.IndexOf('#');
                if (hash >= 0)
                    line = line.Substring(0, hash).Trim();
                if (line.Length < 2)
                    continue;
                var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                switch (parts[0])
                {
                    case "v":
                        if (!(Float(parts, 1) is { } x && Float(parts, 2) is { } y && Float(parts, 3) is { } z))
                            return Fail($"line {lineNumber}: a vertex needs three numbers.", out error);
                        positions.Add(new Vec3(-x, y, z));
                        break;
                    case "vt":
                        if (!(Float(parts, 1) is { } u))
                            return Fail($"line {lineNumber}: a texture coordinate needs a number.", out error);
                        uvs.Add(new Vec2(u, Float(parts, 2) ?? 0f));
                        break;
                    case "vn":
                        if (!(Float(parts, 1) is { } nx && Float(parts, 2) is { } ny && Float(parts, 3) is { } nz))
                            return Fail($"line {lineNumber}: a normal needs three numbers.", out error);
                        normals.Add(new Vec3(-nx, ny, nz));
                        break;
                    case "f":
                        face.Clear();
                        for (var i = 1; i < parts.Length; i++)
                        {
                            var refs = parts[i].Split('/');
                            var p = Index(refs, 0, positions.Count);
                            if (p < 0)
                                return Fail($"line {lineNumber}: face corner '{parts[i]}' points at no vertex.", out error);
                            var t = refs.Length > 1 && refs[1].Length > 0 ? Index(refs, 1, uvs.Count) : -1;
                            var n = refs.Length > 2 && refs[2].Length > 0 ? Index(refs, 2, normals.Count) : -1;
                            var key = (p, t, n);
                            if (!corners.TryGetValue(key, out var vertex))
                            {
                                if (mesh.Positions.Count >= MaxVertices)
                                    return Fail($"more than {MaxVertices} vertices.", out error);
                                vertex = mesh.Positions.Count;
                                corners[key] = vertex;
                                mesh.Positions.Add(positions[p]);
                                mesh.Uvs.Add(t >= 0 ? uvs[t] : new Vec2(0f, 0f));
                                mesh.Normals.Add(n >= 0 ? normals[n] : new Vec3(0f, 0f, 0f));
                            }
                            face.Add(vertex);
                        }
                        if (face.Count < 3)
                            return Fail($"line {lineNumber}: a face needs at least three corners.", out error);
                        // Fan triangulation; the winding is reversed because X was mirrored.
                        for (var i = 1; i + 1 < face.Count; i++)
                        {
                            mesh.Triangles.Add(face[0]);
                            mesh.Triangles.Add(face[i + 1]);
                            mesh.Triangles.Add(face[i]);
                        }
                        if (mesh.TriangleCount > MaxTriangles)
                            return Fail($"more than {MaxTriangles} triangles.", out error);
                        break;
                }
            }
            if (mesh.TriangleCount == 0)
                return Fail("no faces.", out error);
            if (normals.Count == 0)
                mesh.Normals.Clear();
            return mesh;
        }

        private static MeshData? Fail(string message, out string? error)
        {
            error = message;
            return null;
        }

        private static float? Float(string[] parts, int i) =>
            i < parts.Length && float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && !float.IsNaN(v) && !float.IsInfinity(v)
                ? v
                : (float?)null;

        /// <summary>A 1-based (or negative, from the end) OBJ index as a 0-based one; −1 when out of range.</summary>
        private static int Index(string[] refs, int slot, int count)
        {
            if (slot >= refs.Length || !int.TryParse(refs[slot], NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
                return -1;
            var index = i > 0 ? i - 1 : count + i;
            return index >= 0 && index < count ? index : -1;
        }
    }
}
