using System.Collections;
using System.IO;
using System.Net;
using System.Threading;
using NUnit.Framework;
using TableWorld.View;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>
    /// M7: files named by web address (TTS games, setCustomObject) are fetched by each machine and shown when they land.
    /// A server on this computer stands in for the web.
    /// </summary>
    public class WebAssetTests
    {
        [UnityTest]
        public IEnumerator ImageNamedByWebAddress_IsFetched_AndShown()
        {
            var png = Image();
            var port = 18000 + Random.Range(0, 1000);
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();
            var served = 0;
            var server = new Thread(() =>
            {
                try
                {
                    while (listener.IsListening)
                    {
                        var context = listener.GetContext();
                        Interlocked.Increment(ref served);
                        context.Response.ContentType = "image/png";
                        context.Response.ContentLength64 = png.Length;
                        context.Response.OutputStream.Write(png, 0, png.Length);
                        context.Response.Close();
                    }
                }
                catch (HttpListenerException)
                {
                }
                catch (System.ObjectDisposedException)
                {
                }
            }) { IsBackground = true };
            server.Start();
            var url = $"http://127.0.0.1:{port}/token-{System.Guid.NewGuid():N}.png";
            try
            {
                yield return SceneManager.LoadSceneAsync("Table");
                yield return null;
                var table = Object.FindAnyObjectByType<TableView>();
                Object.FindAnyObjectByType<TableInteraction>().IgnoreDevices = true;
                yield return new WaitForSeconds(0.3f);
                var json = @"{ ""schemaVersion"": ""0.1"",
                  ""assets"": [ { ""id"": ""web"", ""type"": ""image"", ""path"": ""urls/token.png"", ""url"": """ + url + @""" } ],
                  ""objects"": [ { ""id"": ""token"", ""type"": ""Token"", ""asset"": ""web"" } ] }";
                Assert.IsTrue(table.Load(json, null));
                Assert.IsNull(table.Assets.Texture("web"), "nothing here yet: the token shows plain");
                for (var wait = 0; wait < 300 && table.Assets.Texture("web") == null; wait++)
                    yield return null;
                var texture = table.Assets.Texture("web");
                Assert.IsNotNull(texture, "fetched and decoded");
                Assert.AreEqual(64, texture.width);
                yield return null;
                var view = table.ViewOf("token");
                Assert.AreEqual(texture, view.Renderer.sharedMaterials[0].GetTexture("_BaseMap"), "the token redrew with it");
                Assert.AreEqual(1, served, "fetched once");
                var cached = Path.Combine(AssetLibrary.UrlCacheFolder, Core.Tts.TtsImporter.CacheKey(url) + ".png");
                Assert.IsTrue(File.Exists(cached), "and kept for next time");
                File.Delete(cached);
            }
            finally
            {
                listener.Close();
            }
        }

        private static byte[] Image()
        {
            var texture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            var pixels = new Color32[64 * 64];
            for (var i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(200, 40, 40, 255);
            texture.SetPixels32(pixels);
            texture.Apply();
            var png = texture.EncodeToPNG();
            Object.Destroy(texture);
            return png;
        }
    }
}
