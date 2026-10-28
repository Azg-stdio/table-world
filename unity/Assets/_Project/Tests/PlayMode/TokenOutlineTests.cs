using System.Collections;
using System.IO;
using NUnit.Framework;
using TableWorld.View;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>Image tokens are cut along their image's transparent edges, as TTS cuts custom tokens.</summary>
    public class TokenOutlineTests
    {
        [UnityTest]
        public IEnumerator SeeThroughImage_CutsTheToken_OpaqueImageKeepsTheBox()
        {
            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            var table = Object.FindAnyObjectByType<TableView>();
            Object.FindAnyObjectByType<TableInteraction>().IgnoreDevices = true;
            var folder = Path.Combine(Application.temporaryCachePath, "outline-test");
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "star.png"), Png(200, 200, (x, y) =>
            {
                var dx = x - 100f;
                var dy = y - 100f;
                var t = Mathf.Abs(Mathf.Repeat(Mathf.Atan2(dy, dx) / (2f * Mathf.PI / 5f), 1f) - 0.5f) * 2f;
                return Mathf.Sqrt(dx * dx + dy * dy) <= 40f + 55f * t;
            }));
            File.WriteAllBytes(Path.Combine(folder, "square.png"), Png(100, 100, (x, y) => true));
            const string json = @"{ ""schemaVersion"": ""0.1"",
  ""assets"": [ { ""id"": ""star"", ""type"": ""image"", ""path"": ""star.png"" }, { ""id"": ""square"", ""type"": ""image"", ""path"": ""square.png"" } ],
  ""objects"": [ { ""id"": ""s"", ""type"": ""Token"", ""shape"": ""image"", ""asset"": ""star"", ""size"": 0.05, ""transform"": { ""position"": [0, 0.02, 0] } },
                 { ""id"": ""q"", ""type"": ""Token"", ""shape"": ""image"", ""asset"": ""square"", ""size"": 0.05, ""transform"": { ""position"": [0.1, 0.02, 0] } } ],
  ""players"": { ""seats"": [ { ""id"": ""south"", ""color"": ""red"" } ] } }";
            Assert.IsTrue(table.Load(json, folder));
            var star = table.ViewOf("s");
            for (var t = 0f; t < 5f && !star.Filter.sharedMesh.name.StartsWith("isil:"); t += Time.deltaTime)
                yield return null;
            yield return new WaitForSeconds(1f);

            var mesh = star.Filter.sharedMesh;
            StringAssert.StartsWith("isil:", mesh.name, "cut along the image's outline");
            // Points reach 95 px from the centre; with one point along +x the star spans 95 + 95 cos 36° of the 200 px.
            Assert.AreEqual(0.05f * (95f + 95f * Mathf.Cos(36f * Mathf.Deg2Rad)) / 200f, mesh.bounds.size.x, 0.003f, "as wide as the star's points, not the image");
            Assert.Greater(mesh.vertexCount, 24, "more than a box");
            Assert.IsInstanceOf<MeshCollider>(star.Collider);
            Assert.IsTrue(((MeshCollider)star.Collider).convex);
            Assert.Less(Vector3.Angle(star.transform.up, Vector3.up), 5f, "it lies flat");

            var square = table.ViewOf("q").Filter.sharedMesh;
            StringAssert.StartsWith("ibox:", square.name, "an image with no transparency stays a box");
            Directory.Delete(folder, true);
        }

        private static byte[] Png(int width, int height, System.Func<int, int, bool> solid)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color32[width * height];
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                    pixels[y * width + x] = solid(x, y) ? new Color32(200, 160, 40, 255) : new Color32(0, 0, 0, 0);
            texture.SetPixels32(pixels);
            var png = texture.EncodeToPNG();
            Object.Destroy(texture);
            return png;
        }
    }
}
