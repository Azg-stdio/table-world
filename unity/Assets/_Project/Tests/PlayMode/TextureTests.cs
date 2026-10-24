using System.Collections;
using System.IO;
using NUnit.Framework;
using TableWorld.View;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>M4 T5.2: images from a package are fitted for the GPU: big ones scaled down, compressed, with mipmaps.</summary>
    public class TextureTests
    {
        [UnityTest]
        public IEnumerator BigImages_AreScaledDown_AndCompressed_SmallOnesKeptAsTheyAre()
        {
            var package = Path.Combine(Application.temporaryCachePath, "texture-test");
            Directory.CreateDirectory(package);
            File.WriteAllBytes(Path.Combine(package, "big.png"), Image(6000, 3000));
            File.WriteAllBytes(Path.Combine(package, "odd.png"), Image(301, 203));

            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            var table = Object.FindAnyObjectByType<TableView>();
            Object.FindAnyObjectByType<TableInteraction>().IgnoreDevices = true;
            yield return new WaitForSeconds(0.3f);
            var json = @"{ ""schemaVersion"": ""0.1"",
              ""assets"": [ { ""id"": ""big"", ""type"": ""image"", ""path"": ""big.png"", ""columns"": 10, ""rows"": 5 }, { ""id"": ""odd"", ""type"": ""image"", ""path"": ""odd.png"" } ],
              ""objects"": [ { ""id"": ""tile"", ""type"": ""Tile"", ""asset"": ""big"" }, { ""id"": ""token"", ""type"": ""Token"", ""asset"": ""odd"", ""transform"": { ""position"": [0.2, 0, 0] } } ] }";
            Assert.IsTrue(table.Load(json, package));
            yield return null;

            var big = table.Assets.Texture("big");
            Assert.AreEqual(4096, big.width, "the longest side fits the limit");
            Assert.AreEqual(2048, big.height, "and the proportions stay (a sheet keeps its grid)");
            Assert.IsTrue(big.format == TextureFormat.DXT5 || big.format == TextureFormat.DXT1 || big.format == TextureFormat.BC7, $"compressed ({big.format})");
            Assert.Greater(big.mipmapCount, 1, "with mipmaps");
            Assert.IsFalse(big.isReadable, "and its CPU copy freed");
            var odd = table.Assets.Texture("odd");
            Assert.AreEqual(301, odd.width, "a small image keeps its size");
            Assert.IsFalse(odd.format == TextureFormat.DXT5 || odd.format == TextureFormat.DXT1 || odd.format == TextureFormat.BC7,
                $"301 × 203 cannot be block-compressed, so it stays as loaded ({odd.format})");
        }

        /// <summary>A PNG with a gradient, so the scaled copy has something to keep.</summary>
        private static byte[] Image(int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color32[width * height];
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                    pixels[y * width + x] = new Color32((byte)(x * 255 / width), (byte)(y * 255 / height), 128, 255);
            texture.SetPixels32(pixels);
            texture.Apply();
            var png = texture.EncodeToPNG();
            Object.Destroy(texture);
            return png;
        }
    }
}
