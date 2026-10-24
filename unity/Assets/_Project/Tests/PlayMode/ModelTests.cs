using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using TableWorld.View;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>G38: a model from an OBJ file in the game package is drawn with its mesh, collides by its shape and rests on the table.</summary>
    public class ModelTests
    {
        /// <summary>A chess pawn: a profile turned around Y, 2 cm wide and 3.4 cm tall, base at y = 0.</summary>
        public static string PawnObj()
        {
            var profile = new[] { (0.0f, 0f), (0.010f, 0f), (0.010f, 0.003f), (0.007f, 0.006f), (0.004f, 0.018f), (0.007f, 0.020f), (0.003f, 0.022f), (0.006f, 0.026f), (0.006f, 0.030f), (0.003f, 0.0335f), (0.0f, 0.034f) };
            const int sides = 24;
            var obj = new StringBuilder("# generated pawn (CC0)\n");
            foreach (var (r, y) in profile)
                for (var s = 0; s < sides; s++)
                {
                    var a = 2 * Mathf.PI * s / sides;
                    obj.AppendLine(string.Format(CultureInfo.InvariantCulture, "v {0} {1} {2}", r * Mathf.Cos(a), y, r * Mathf.Sin(a)));
                }
            for (var ring = 0; ring + 1 < profile.Length; ring++)
                for (var s = 0; s < sides; s++)
                {
                    var a = ring * sides + s + 1;
                    var b = ring * sides + (s + 1) % sides + 1;
                    obj.AppendLine($"f {a} {a + sides} {b + sides} {b}"); // counter-clockwise seen from outside, as OBJ expects
                }
            return obj.ToString();
        }

        [UnityTest]
        public IEnumerator BuiltinPieces_HaveTheirOwnMeshes_AndStandOnTheTable()
        {
            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            var table = Object.FindAnyObjectByType<TableView>();
            Object.FindAnyObjectByType<TableInteraction>().IgnoreDevices = true;
            yield return new WaitForSeconds(2f);
            foreach (var (id, mesh, height) in new[] { ("pawn_red", "model:builtin:pawn", 0.034f), ("block", "model:builtin:block_square", 0.03f), ("checker", "model:builtin:checker", 0.0075f) })
            {
                var view = table.ViewOf(id);
                Assert.AreEqual(mesh, view.Filter.sharedMesh.name, id);
                Assert.AreEqual(height, view.Filter.sharedMesh.bounds.size.y, 0.001f, id);
                Assert.AreEqual(0f, view.transform.position.y, 0.004f, $"{id} rests on the table");
                Assert.Greater(OutwardShare(view.Filter.sharedMesh), 0.95f, $"{id} faces outward (not inside out)");
            }
        }

        /// <summary>
        /// Share of the side's area whose triangles, wound clockwise as Unity draws front faces, face away from the
        /// piece's vertical axis. An inside-out mesh scores near 0.
        /// </summary>
        public static float OutwardShare(Mesh mesh)
        {
            var v = mesh.vertices;
            var t = mesh.triangles;
            var centre = mesh.bounds.center;
            float outward = 0f, total = 0f;
            for (var i = 0; i < t.Length; i += 3)
            {
                var a = v[t[i]]; var b = v[t[i + 1]]; var c = v[t[i + 2]];
                var normal = Vector3.Cross(b - a, c - a);
                var area = normal.magnitude;
                var radial = (a + b + c) / 3f - centre;
                radial.y = 0f;
                if (area < 1e-12f || radial.sqrMagnitude < 1e-10f || Mathf.Abs(normal.normalized.y) > 0.9f)
                    continue;
                total += area;
                if (Vector3.Dot(normal, radial) > 0f)
                    outward += area;
            }
            return total > 0f ? outward / total : 1f;
        }

        [UnityTest]
        public IEnumerator ObjModel_LoadsFromThePackage_AndRestsOnTheTable()
        {
            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            var table = Object.FindAnyObjectByType<TableView>();
            Object.FindAnyObjectByType<TableInteraction>().IgnoreDevices = true;
            var folder = Path.Combine(Application.temporaryCachePath, "model-test");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "pawn.obj"), PawnObj());
            const string json = @"{ ""schemaVersion"": ""0.1"",
  ""assets"": [ { ""id"": ""pawn"", ""type"": ""model"", ""path"": ""pawn.obj"" }, { ""id"": ""broken"", ""type"": ""model"", ""path"": ""missing.obj"" } ],
  ""objects"": [ { ""id"": ""p1"", ""type"": ""Model"", ""asset"": ""pawn"", ""tint"": [0.8, 0.2, 0.2], ""transform"": { ""position"": [0, 0.05, 0] } },
                 { ""id"": ""p2"", ""type"": ""Model"", ""asset"": ""broken"", ""transform"": { ""position"": [0.1, 0.05, 0] } } ],
  ""players"": { ""seats"": [ { ""id"": ""south"", ""color"": ""red"" } ] } }";
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Missing model 'missing.obj'"));
            Assert.IsTrue(table.Load(json, folder));
            yield return new WaitForSeconds(2f);

            var pawn = table.ViewOf("p1");
            StringAssert.StartsWith("model:", pawn.Filter.sharedMesh.name, "drawn with its own mesh");
            Assert.AreEqual(0.034f, pawn.Filter.sharedMesh.bounds.size.y, 0.001f);
            Assert.IsInstanceOf<MeshCollider>(pawn.Collider);
            Assert.Greater(OutwardShare(pawn.Filter.sharedMesh), 0.95f, "an OBJ wound as OBJ expects comes out facing outward");
            Assert.AreEqual(0f, pawn.transform.position.y, 0.003f, "it stands on the table, base down");
            Assert.Less(Vector3.Angle(pawn.transform.up, Vector3.up), 5f, "and stays upright");
            Assert.AreNotEqual(pawn.Filter.sharedMesh, table.ViewOf("p2").Filter.sharedMesh, "a missing file shows the placeholder");
            Directory.Delete(folder, true);
        }
    }
}
