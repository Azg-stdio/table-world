using System.Collections;
using NUnit.Framework;
using TableWorld.View;
using UnityEngine;
using UnityEngine.TestTools;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>
    /// Images are made GPU-ready off the main thread (TextureBaker): opaque ones as BC1, see-through ones as BC3, with
    /// mipmaps, sides a multiple of 4 and the longer side at most 4096, looking like the original.
    /// </summary>
    public class TextureBakerTests
    {
        private static Texture2D Make(int width, int height, TextureFormat format, bool hole)
        {
            var texture = new Texture2D(width, height, format, mipChain: false);
            var pixels = new Color32[width * height];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var inHole = hole && x > width / 4 && x < width / 2 && y > height / 4 && y < height / 2;
                pixels[y * width + x] = new Color32((byte)(255 * x / width), (byte)(255 * y / height), (byte)((x / 16 + y / 16) % 2 * 200), (byte)(inHole ? 0 : 255));
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        private static IEnumerator BakeAndWait(Texture2D source, Texture2D[] result)
        {
            TextureBaker.Start(source, baked => result[0] = baked);
            for (var t = 0f; t < 10f && result[0] == null; t += Time.unscaledDeltaTime)
                yield return null;
            Assert.IsNotNull(result[0], "baked in time");
        }

        /// <summary>Mean channel error between the baked texture (read back from the GPU) and the expected pixels.</summary>
        private static float MeanError(Texture2D baked, Color32[] expected)
        {
            var target = RenderTexture.GetTemporary(baked.width, baked.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(baked, target);
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var readback = new Texture2D(baked.width, baked.height, TextureFormat.RGBA32, false);
            readback.ReadPixels(new Rect(0, 0, baked.width, baked.height), 0, 0);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            var got = readback.GetPixels32();
            Object.Destroy(readback);
            double error = 0;
            for (var i = 0; i < got.Length; i++)
                error += Mathf.Abs(got[i].r - expected[i].r) + Mathf.Abs(got[i].g - expected[i].g) + Mathf.Abs(got[i].b - expected[i].b)
                         + Mathf.Abs(got[i].a - expected[i].a);
            return (float)(error / (got.Length * 4));
        }

        [UnityTest]
        public IEnumerator OpaqueImage_BecomesBc1WithMipmaps_AndLooksTheSame()
        {
            var source = Make(256, 128, TextureFormat.RGB24, hole: false);
            var expected = source.GetPixels32();
            var result = new Texture2D[1];
            yield return BakeAndWait(source, result);
            var baked = result[0];
            Assert.AreEqual(TextureFormat.DXT1, baked.format);
            Assert.AreEqual(new Vector2Int(256, 128), new Vector2Int(baked.width, baked.height));
            Assert.AreEqual(9, baked.mipmapCount);
            Assert.IsFalse(baked.isReadable, "the CPU copy is released");
            var error = MeanError(baked, expected);
            Debug.Log($"[TextureBaker] BC1 mean error {error:F2} of 255");
            Assert.Less(error, 6f);
            Object.Destroy(baked);
        }

        [UnityTest]
        public IEnumerator SeeThroughImage_BecomesBc3_KeepingItsHole()
        {
            var source = Make(128, 128, TextureFormat.RGBA32, hole: true);
            var expected = source.GetPixels32();
            var result = new Texture2D[1];
            yield return BakeAndWait(source, result);
            var baked = result[0];
            Assert.AreEqual(TextureFormat.DXT5, baked.format);
            var error = MeanError(baked, expected);
            Debug.Log($"[TextureBaker] BC3 mean error {error:F2} of 255");
            Assert.Less(error, 6f);
            Object.Destroy(baked);
        }

        [UnityTest]
        public IEnumerator HugeImage_IsScaledDown_ToMultiplesOfFour()
        {
            var source = Make(5002, 1001, TextureFormat.RGBA32, hole: false);
            var result = new Texture2D[1];
            yield return BakeAndWait(source, result);
            var baked = result[0];
            Assert.AreEqual(AssetLibrary.MaxTextureSize, baked.width);
            Assert.AreEqual(0, baked.height % 4);
            Assert.AreEqual(Mathf.RoundToInt(1001f * AssetLibrary.MaxTextureSize / 5002 / 4) * 4, baked.height, "the proportions stay");
            Assert.AreEqual(TextureFormat.DXT1, baked.format, "fully opaque RGBA is BC1");
            Object.Destroy(baked);
        }

        [UnityTest]
        public IEnumerator OnLowGraphics_ImagesStopAt2048()
        {
            var before = TableWorld.View.Settings.GraphicsPresets.Current;
            TableWorld.View.Settings.GraphicsPresets.Apply(TableWorld.View.Settings.GraphicsQuality.Low);
            try
            {
                var result = new Texture2D[1];
                yield return BakeAndWait(Make(5000, 2500, TextureFormat.RGBA32, hole: false), result);
                Assert.AreEqual(new Vector2Int(2048, 1024), new Vector2Int(result[0].width, result[0].height));
                Object.Destroy(result[0]);
            }
            finally
            {
                TableWorld.View.Settings.GraphicsPresets.Apply(before);
            }
        }
    }
}
