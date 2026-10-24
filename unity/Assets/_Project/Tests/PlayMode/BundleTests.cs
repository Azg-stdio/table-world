using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TableWorld.View;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>
    /// M4 T5.4: TTS asset bundles from this machine's Tabletop Simulator cache load on objects with URP materials and
    /// colliders, fall into the physics, and hide in hidden zones. Bundles never enter the repository, so without a local
    /// TTS cache the test is skipped.
    /// </summary>
    public class BundleTests
    {
        private static string CacheFolder => Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments),
            "My Games", "Tabletop Simulator", "Mods", "Assetbundles");

        [UnityTest]
        public IEnumerator LocalTtsBundles_LoadWithUrpMaterials_AndColliders()
        {
            if (!Directory.Exists(CacheFolder) || Directory.GetFiles(CacheFolder, "*.unity3d").Length < 3)
                Assert.Ignore("No local Tabletop Simulator bundle cache.");
            // The package lives outside the project: a temporary folder with copies of three bundles.
            var package = Path.Combine(Application.temporaryCachePath, "bundle-test");
            Directory.CreateDirectory(package);
            var picks = Directory.GetFiles(CacheFolder, "*.unity3d").OrderBy(f => new FileInfo(f).Length).Skip(5).Take(3).ToList();
            for (var i = 0; i < picks.Count; i++)
                File.Copy(picks[i], Path.Combine(package, $"b{i}.unity3d"), overwrite: true);

            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            var table = Object.FindAnyObjectByType<TableView>();
            Object.FindAnyObjectByType<TableInteraction>().IgnoreDevices = true;
            yield return new WaitForSeconds(0.3f);
            var json = @"{ ""schemaVersion"": ""0.1"",
              ""assets"": [ { ""id"": ""b0"", ""type"": ""assetBundle"", ""path"": ""b0.unity3d"" }, { ""id"": ""b1"", ""type"": ""assetBundle"", ""path"": ""b1.unity3d"" },
                            { ""id"": ""b2"", ""type"": ""assetBundle"", ""path"": ""b2.unity3d"" }, { ""id"": ""gone"", ""type"": ""assetBundle"", ""path"": ""missing.unity3d"" } ],
              ""objects"": [ { ""id"": ""m0"", ""type"": ""Model"", ""asset"": ""b0"", ""transform"": { ""position"": [-0.2, 0.06, 0] } },
                             { ""id"": ""m1"", ""type"": ""Model"", ""asset"": ""b1"", ""transform"": { ""position"": [0, 0.06, 0] } },
                             { ""id"": ""bag"", ""type"": ""Bag"", ""asset"": ""b2"", ""transform"": { ""position"": [0.2, 0.06, 0] } },
                             { ""id"": ""placeholder"", ""type"": ""Model"", ""asset"": ""gone"", ""transform"": { ""position"": [0.4, 0.06, 0] } } ] }";
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Missing bundle .*missing.unity3d"));
            Assert.IsTrue(table.Load(json, package));
            yield return null;

            foreach (var id in new[] { "m0", "m1", "bag" })
            {
                var view = table.ViewOf(id);
                var bundle = view.transform.Find("Bundle");
                Assert.IsNotNull(bundle, $"{id} shows its bundle");
                var materials = bundle.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m != null).ToList();
                Assert.IsNotEmpty(materials);
                Assert.IsTrue(materials.All(m => m.shader.isSupported && m.shader.name.StartsWith("Universal Render Pipeline")),
                    $"{id}: every material is URP (" + string.Join(", ", materials.Select(m => m.shader.name).Distinct()) + ")");
                Assert.IsNotNull(view.Collider, $"{id} can be picked up and collides");
                Assert.IsNull(view.Filter.sharedMesh, "the placeholder box is gone");
            }
            Assert.IsNull(table.ViewOf("placeholder").transform.Find("Bundle"), "a missing bundle leaves the placeholder");

            // They are real bodies: dropped from 6 cm, they land on the table.
            yield return new WaitForSeconds(1.5f);
            Assert.Less(table.ViewOf("m1").transform.position.y, 0.06f, "it fell");
            Assert.Greater(table.ViewOf("m1").transform.position.y, -0.05f, "onto the table, not through it");

            // Loading the table again reuses the loaded bundles (Unity refuses to load one twice).
            Assert.IsTrue(table.Load(json, package));
            yield return null;
            Assert.IsNotNull(table.ViewOf("m0").transform.Find("Bundle"));
        }
    }
}
