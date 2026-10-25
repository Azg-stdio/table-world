using System.Collections.Generic;
using Core.Tts;
using TableWorld.View.Meshes;
using UnityEngine;

namespace TableWorld.View.Builtin
{
    /// <summary>
    /// Model assets the game builds itself (<c>builtin:&lt;name&gt;</c>, G38): TTS's primitive pieces, so imported tables
    /// get their blocks and checkers without files. Sizes come from the TTS unit (<see cref="TtsUnits.TtsUnitsToMeters"/>);
    /// verify each against TTS. Every mesh has one submesh (models take one material) and its base at y = 0.
    /// </summary>
    public static class BuiltinModels
    {
        private static readonly Dictionary<string, Mesh> Cache = new Dictionary<string, Mesh>();

        /// <summary>The builtin mesh called <paramref name="name"/>, or null when there is none.</summary>
        public static Mesh Get(string name)
        {
            if (Cache.TryGetValue(name, out var cached) && cached != null)
                return cached;
            const float u = TtsUnits.TtsUnitsToMeters;
            var mesh = name switch
            {
                "block_square" => Lifted(MeshFactory.Box(new Vector3(u, u, u))),
                "block_rectangle" => Lifted(MeshFactory.Box(new Vector3(2f * u, u, u))),
                "checker" => Lifted(MeshFactory.Cylinder(0.75f * u, 0.25f * u, 32)),
                "pawn" => Pawn(),
                "bowl" => Bowl(),
                _ => null,
            };
            if (mesh != null)
                mesh.name = "model:builtin:" + name;
            Cache[name] = mesh;
            return mesh;
        }

        /// <summary>One submesh, base at y = 0 (the factory's prisms are centred and split into top, bottom and sides).</summary>
        private static Mesh Lifted(Mesh source)
        {
            var mesh = Object.Instantiate(source);
            var vertices = mesh.vertices;
            var lift = -mesh.bounds.min.y;
            for (var i = 0; i < vertices.Length; i++)
                vertices[i].y += lift;
            var triangles = new List<int>();
            for (var s = 0; s < mesh.subMeshCount; s++)
                triangles.AddRange(mesh.GetTriangles(s));
            mesh.vertices = vertices;
            mesh.subMeshCount = 1;
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>A chess pawn turned from a profile: 2 cm across, 3.4 cm tall.</summary>
        private static Mesh Pawn()
        {
            var profile = new[]
            {
                new Vector2(0f, 0f), new Vector2(0.010f, 0f), new Vector2(0.010f, 0.003f), new Vector2(0.007f, 0.006f),
                new Vector2(0.004f, 0.018f), new Vector2(0.007f, 0.020f), new Vector2(0.003f, 0.022f), new Vector2(0.006f, 0.026f),
                new Vector2(0.006f, 0.030f), new Vector2(0.003f, 0.0335f), new Vector2(0f, 0.034f),
            };
            return Turned(profile, 32);
        }

        /// <summary>
        /// TTS's Bowl: a round dish pieces sit in (it holds nothing; it is not a container). Its size is an estimate
        /// (about 3.6 TTS units across); verify against TTS. Colliders: <see cref="ObjectViewFactory"/> gives it a base
        /// and a ring of walls, as a solid hull would fill it.
        /// </summary>
        public const float BowlRadius = 1.8f * TtsUnits.TtsUnitsToMeters;
        public const float BowlHeight = 0.8f * TtsUnits.TtsUnitsToMeters;
        public const float BowlWall = 0.003f;

        private static Mesh Bowl()
        {
            const float r = BowlRadius, h = BowlHeight, t = BowlWall;
            // Outside from the centre of the base up to the rim, then down the inside to the centre of the floor.
            var profile = new[]
            {
                new Vector2(0f, 0f), new Vector2(0.55f * r, 0f), new Vector2(0.83f * r, 0.25f * h), new Vector2(0.96f * r, 0.6f * h),
                new Vector2(r, h), new Vector2(r - t, h), new Vector2(0.96f * r - t, 0.6f * h), new Vector2(0.83f * r - t, 0.3f * h),
                new Vector2(0.55f * r - t, t), new Vector2(0f, t),
            };
            return Turned(profile, 40);
        }

        /// <summary>A mesh turned about the vertical from <paramref name="profile"/> (radius, height), base at y = 0.</summary>
        private static Mesh Turned(Vector2[] profile, int sides)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            foreach (var p in profile)
                for (var s = 0; s <= sides; s++)
                {
                    var a = 2f * Mathf.PI * s / sides;
                    vertices.Add(new Vector3(p.x * Mathf.Cos(a), p.y, p.x * Mathf.Sin(a)));
                }
            var ring = sides + 1;
            for (var r = 0; r + 1 < profile.Length; r++)
                for (var s = 0; s < sides; s++)
                {
                    var a = r * ring + s;
                    var b = a + 1;
                    // Clockwise seen from outside (angle grows to the right, rings go up): Unity's front faces.
                    triangles.AddRange(new[] { a, b + ring, b, a, a + ring, b + ring });
                }
            var mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
