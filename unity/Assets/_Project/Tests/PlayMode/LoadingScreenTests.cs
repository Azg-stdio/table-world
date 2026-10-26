using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using TableWorld.View;
using TableWorld.View.Hud;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>
    /// Opening a game shows the loading screen: each blocking step is announced and drawn before it runs, then the images
    /// on their way; it leaves by itself once they are in. A load without steps (scripts, the network) shows nothing.
    /// </summary>
    public class LoadingScreenTests
    {
        [UnityTest]
        public IEnumerator SteppedLoad_ShowsEachStep_ThenLeavesWhenTheImagesAreIn()
        {
            var package = Path.Combine(Application.temporaryCachePath, "loading-test");
            Directory.CreateDirectory(package);
            for (var i = 0; i < 3; i++)
            {
                var image = new Texture2D(512, 512, TextureFormat.RGBA32, false);
                File.WriteAllBytes(Path.Combine(package, $"i{i}.png"), image.EncodeToPNG());
                Object.Destroy(image);
            }
            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            Object.FindAnyObjectByType<TableInteraction>().IgnoreDevices = true;
            var table = Object.FindAnyObjectByType<TableView>();
            var screen = Object.FindAnyObjectByType<HudController>().GetComponent<UIDocument>().rootVisualElement.Q("loadingScreen");
            yield return new WaitForSeconds(0.2f);
            Assert.IsTrue(screen.ClassListContains("hidden"), "hidden at first");

            var steps = new List<string>();
            table.LoadStepped += (title, step) => steps.Add(step);
            var json = @"{ ""schemaVersion"": ""0.1"",
              ""assets"": [ { ""id"": ""i0"", ""type"": ""image"", ""path"": ""i0.png"" }, { ""id"": ""i1"", ""type"": ""image"", ""path"": ""i1.png"" }, { ""id"": ""i2"", ""type"": ""image"", ""path"": ""i2.png"" } ],
              ""objects"": [ { ""id"": ""a"", ""type"": ""Tile"", ""asset"": ""i0"" }, { ""id"": ""b"", ""type"": ""Tile"", ""asset"": ""i1"", ""transform"": { ""position"": [0.2, 0, 0] } },
                             { ""id"": ""c"", ""type"": ""Tile"", ""asset"": ""i2"", ""transform"": { ""position"": [-0.2, 0, 0] } } ],
              ""scripts"": { ""global"": ""function onLoad() broadcastToAll('ready') end"" } }";
            var load = table.LoadInSteps(json, package, "Test game");
            Assert.IsTrue(load.MoveNext());
            yield return null;
            Assert.IsFalse(screen.ClassListContains("hidden"), "shown before the first step runs");
            while (load.MoveNext())
                yield return null;
            Assert.IsTrue(table.LastLoadOk);
            CollectionAssert.AreEqual(new[] { "Reading the table", "Placing 3 objects", "Starting the game's scripts", null }, steps);

            for (var t = 0f; t < 10f && !screen.ClassListContains("hidden"); t += Time.unscaledDeltaTime)
                yield return null;
            Assert.IsTrue(screen.ClassListContains("hidden"), "it leaves once the images are in");
            Assert.AreEqual(0, table.Assets.Progress.ImagesLeft);

            // A plain load (a script, a host's table arriving) does not show it.
            Assert.IsTrue(table.Load(json, package));
            yield return null;
            Assert.IsTrue(screen.ClassListContains("hidden"));
        }
    }
}
