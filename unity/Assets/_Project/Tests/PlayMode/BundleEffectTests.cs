using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Core.Engine;
using TableWorld.View;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>
    /// ADR 0008: a TTS asset bundle with effects shows the looping effect the table chose, switches when a command picks
    /// another, and plays a trigger effect once before going back. Uses the first bundle with effects in this machine's
    /// TTS cache; without one the test is skipped.
    /// </summary>
    public class BundleEffectTests
    {
        private static string CacheFolder => Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments),
            "My Games", "Tabletop Simulator", "Mods", "Assetbundles");

        /// <summary>The first cached bundle whose effects component has two looping effects and a trigger, or null.</summary>
        private static string FindBundleWithEffects()
        {
            if (!Directory.Exists(CacheFolder))
                return null;
            foreach (var file in Directory.GetFiles(CacheFolder, "*.unity3d").OrderBy(f => new FileInfo(f).Length))
            {
                var bundle = AssetBundle.LoadFromFile(file);
                if (bundle == null)
                    continue;
                var found = bundle.LoadAllAssets<GameObject>()
                    .Select(go => go.GetComponentInChildren<ITtsBundleEffects>(true))
                    .Any(e => e != null && e.LoopingCount > 1 && e.TriggerCount > 0);
                bundle.Unload(true);
                if (found)
                    return file;
            }
            return null;
        }

        [UnityTest]
        public IEnumerator BundleEffects_LoopAndTrigger()
        {
            var source = FindBundleWithEffects();
            if (source == null)
                Assert.Ignore("No cached Tabletop Simulator bundle with effects.");
            var package = Path.Combine(Application.temporaryCachePath, "bundle-effect-test");
            Directory.CreateDirectory(package);
            File.Copy(source, Path.Combine(package, "fx.unity3d"), overwrite: true);

            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            var table = Object.FindAnyObjectByType<TableView>();
            Object.FindAnyObjectByType<TableInteraction>().IgnoreDevices = true;
            yield return new WaitForSeconds(0.3f);
            Assert.IsTrue(table.Load(@"{ ""schemaVersion"": ""0.1"",
              ""assets"": [ { ""id"": ""fx"", ""type"": ""assetBundle"", ""path"": ""fx.unity3d"" } ],
              ""objects"": [ { ""id"": ""m"", ""type"": ""Model"", ""asset"": ""fx"", ""loopingEffect"": 1, ""transform"": { ""position"": [0, 0.02, 0] } } ] }", package));
            yield return null;

            var view = table.ViewOf("m");
            var effects = view.transform.Find("Bundle")?.GetComponentInChildren<ITtsBundleEffects>(true);
            Assert.IsNotNull(effects, "the bundle's effects component binds to the stand-in");
            Assert.AreEqual(1, view.ShownLoopingEffect, "it shows the saved looping effect");
            var names = table.EffectNames("m", looping: true);
            Assert.AreEqual(effects.LoopingCount, names.Count);
            Debug.Log($"[BundleEffects] {Path.GetFileName(source)}: looping [{string.Join(", ", names)}], trigger [{string.Join(", ", table.EffectNames("m", looping: false))}]");

            Assert.IsTrue(table.Execute(new PlayEffectCommand { ObjectId = "m", Index = 0 }).Ok);
            yield return null;
            Assert.AreEqual(0, view.ShownLoopingEffect, "a command switches the looping effect");

            var animation = view.GetComponentInChildren<Animation>(true);
            Assert.IsTrue(table.Execute(new PlayEffectCommand { ObjectId = "m", Index = 0, Looping = false }).Ok);
            yield return null;
            if (animation != null)
                Assert.IsTrue(animation.isPlaying, "the trigger effect animates the figure");
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(0, view.ShownLoopingEffect, "a trigger does not change the looping effect");
        }
    }
}
