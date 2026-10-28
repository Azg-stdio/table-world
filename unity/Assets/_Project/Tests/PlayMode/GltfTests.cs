using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TableWorld.View;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>M4 T5.1: glTF models load in the background onto Model objects, with their colour, scale and colliders.</summary>
    public class GltfTests
    {
        [UnityTest]
        public IEnumerator Glb_ShowsOnTheModelWithColliders()
        {
            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            var table = Object.FindAnyObjectByType<TableView>();
            Object.FindAnyObjectByType<TableInteraction>().IgnoreDevices = true;
            var folder = Path.Combine(Application.temporaryCachePath, "gltf-test");
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "tower.glb"), Glb(new[] { (Vector3.zero, new Vector3(0.04f, 0.06f, 0.04f)) }, new Color(0.9f, 0.1f, 0.1f)));
            File.WriteAllBytes(Path.Combine(folder, "pair.glb"),
                Glb(new[] { (new Vector3(-0.03f, 0f, 0f), new Vector3(0.02f, 0.02f, 0.02f)), (new Vector3(0.03f, 0f, 0f), new Vector3(0.02f, 0.02f, 0.02f)) }, Color.blue));
            const string json = @"{ ""schemaVersion"": ""0.1"",
  ""assets"": [ { ""id"": ""tower"", ""type"": ""model"", ""path"": ""tower.glb"" },
               { ""id"": ""half"", ""type"": ""model"", ""path"": ""pair.glb"", ""meshScale"": 0.5 },
               { ""id"": ""broken"", ""type"": ""model"", ""path"": ""missing.glb"" } ],
  ""objects"": [ { ""id"": ""t"", ""type"": ""Model"", ""asset"": ""tower"", ""transform"": { ""position"": [0, 0.05, 0] } },
                 { ""id"": ""p"", ""type"": ""Model"", ""asset"": ""half"", ""transform"": { ""position"": [0.2, 0.05, 0] } },
                 { ""id"": ""m"", ""type"": ""Model"", ""asset"": ""broken"", ""transform"": { ""position"": [-0.2, 0.05, 0] } } ],
  ""players"": { ""seats"": [ { ""id"": ""south"", ""color"": ""red"" } ] } }";
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Missing model .*missing.glb"));
            Assert.IsTrue(table.Load(json, folder));
            for (var t = 0f; t < 5f && (table.ViewOf("t").transform.Find("Bundle") == null || table.ViewOf("p").transform.Find("Bundle") == null); t += Time.deltaTime)
                yield return null;
            yield return new WaitForSeconds(2f);

            var tower = table.ViewOf("t");
            var model = tower.transform.Find("Bundle");
            Assert.IsNotNull(model, "the glTF shows on the object");
            var renderer = model.GetComponentInChildren<MeshRenderer>();
            Assert.AreEqual(0.06f, renderer.bounds.size.y, 0.002f, "glTF is in metres");
            Assert.AreEqual(RequiredShaders.LitName, renderer.sharedMaterial.shader.name);
            Assert.Greater(renderer.sharedMaterial.GetColor("_BaseColor").r, 0.8f, "its base colour");
            var collider = tower.Collider as MeshCollider;
            Assert.IsNotNull(collider, "a part's own convex collider");
            Assert.IsTrue(collider.convex);
            Assert.AreEqual(0f, renderer.bounds.min.y, 0.004f, "it stands on the table");
            Assert.IsNull(tower.Filter.sharedMesh, "the placeholder is gone");

            var pair = table.ViewOf("p").transform.Find("Bundle");
            Assert.AreEqual(2, pair.GetComponentsInChildren<MeshCollider>().Length, "each part gets a collider");
            var bounds = pair.GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; });
            Assert.AreEqual(0.04f, bounds.size.x, 0.002f, "meshScale 0.5 halves it (0.08 m wide in the file)");

            Assert.IsNull(table.ViewOf("m").transform.Find("Bundle"), "a missing file keeps the placeholder");
            Assert.IsNotNull(table.ViewOf("m").Filter.sharedMesh);
            Directory.Delete(folder, true);
        }

        /// <summary>A GLB of boxes (minimum corner at <c>Item1</c>, size <c>Item2</c>), one node and mesh each, in one colour.</summary>
        public static byte[] Glb(IList<(Vector3 Min, Vector3 Size)> boxes, Color color)
        {
            var bin = new List<byte>();
            var accessors = new List<string>();
            var views = new List<string>();
            var meshes = new List<string>();
            var nodes = new List<string>();
            int View(byte[] data, int target)
            {
                while (bin.Count % 4 != 0)
                    bin.Add(0);
                views.Add($"{{\"buffer\":0,\"byteOffset\":{bin.Count},\"byteLength\":{data.Length},\"target\":{target}}}");
                bin.AddRange(data);
                return views.Count - 1;
            }
            foreach (var (min, size) in boxes)
            {
                var max = min + size;
                var positions = new List<Vector3>();
                var normals = new List<Vector3>();
                var indices = new List<ushort>();
                foreach (var n in new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back })
                {
                    var u = Vector3.Cross(n, Mathf.Abs(n.y) > 0.5f ? Vector3.forward : Vector3.up);
                    var v = Vector3.Cross(n, u);
                    var start = (ushort)positions.Count;
                    foreach (var (a, b) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
                    {
                        var corner = (n + u * a + v * b + Vector3.one) / 2f;
                        positions.Add(min + Vector3.Scale(corner, size));
                        normals.Add(n);
                    }
                    // The material is double-sided, so the winding does not matter here.
                    indices.AddRange(new[] { start, (ushort)(start + 1), (ushort)(start + 2), start, (ushort)(start + 2), (ushort)(start + 3) });
                }
                // glTF is right-handed: x is mirrored against Unity's.
                var p = View(positions.SelectMany(q => new[] { -q.x, q.y, q.z }).SelectMany(System.BitConverter.GetBytes).ToArray(), 34962);
                var nv = View(normals.SelectMany(q => new[] { -q.x, q.y, q.z }).SelectMany(System.BitConverter.GetBytes).ToArray(), 34962);
                var iv = View(indices.SelectMany(System.BitConverter.GetBytes).ToArray(), 34963);
                accessors.Add(System.FormattableString.Invariant($"{{\"bufferView\":{p},\"componentType\":5126,\"count\":{positions.Count},\"type\":\"VEC3\",\"min\":[{-max.x},{min.y},{min.z}],\"max\":[{-min.x},{max.y},{max.z}]}}"));
                accessors.Add($"{{\"bufferView\":{nv},\"componentType\":5126,\"count\":{normals.Count},\"type\":\"VEC3\"}}");
                accessors.Add($"{{\"bufferView\":{iv},\"componentType\":5123,\"count\":{indices.Count},\"type\":\"SCALAR\"}}");
                var a0 = accessors.Count - 3;
                meshes.Add($"{{\"primitives\":[{{\"attributes\":{{\"POSITION\":{a0},\"NORMAL\":{a0 + 1}}},\"indices\":{a0 + 2},\"material\":0}}]}}");
                nodes.Add($"{{\"mesh\":{meshes.Count - 1}}}");
            }
            while (bin.Count % 4 != 0)
                bin.Add(0);
            var material = System.FormattableString.Invariant(
                $"\"materials\":[{{\"doubleSided\":true,\"pbrMetallicRoughness\":{{\"baseColorFactor\":[{color.r},{color.g},{color.b},1],\"metallicFactor\":0,\"roughnessFactor\":0.6}}}}],");
            var json = ("{\"asset\":{\"version\":\"2.0\"},\"scene\":0,\"scenes\":[{\"nodes\":[" + string.Join(",", Enumerable.Range(0, nodes.Count)) + "]}],"
                + "\"nodes\":[" + string.Join(",", nodes) + "],\"meshes\":[" + string.Join(",", meshes) + "],"
                + material
                + "\"accessors\":[" + string.Join(",", accessors) + "],\"bufferViews\":[" + string.Join(",", views) + "],"
                + $"\"buffers\":[{{\"byteLength\":{bin.Count}}}]}}");
            var jsonBytes = System.Text.Encoding.UTF8.GetBytes(json).ToList();
            while (jsonBytes.Count % 4 != 0)
                jsonBytes.Add((byte)' ');
            var glb = new List<byte>();
            glb.AddRange(System.BitConverter.GetBytes(0x46546C67u));
            glb.AddRange(System.BitConverter.GetBytes(2u));
            glb.AddRange(System.BitConverter.GetBytes((uint)(12 + 8 + jsonBytes.Count + 8 + bin.Count)));
            glb.AddRange(System.BitConverter.GetBytes((uint)jsonBytes.Count));
            glb.AddRange(System.BitConverter.GetBytes(0x4E4F534Au));
            glb.AddRange(jsonBytes);
            glb.AddRange(System.BitConverter.GetBytes((uint)bin.Count));
            glb.AddRange(System.BitConverter.GetBytes(0x004E4942u));
            glb.AddRange(bin);
            return glb.ToArray();
        }
    }
}
