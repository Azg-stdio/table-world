using System.Collections;
using NUnit.Framework;
using TableWorld.View;
using TableWorld.View.Builtin;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>M7: TTS's Bowl is a dish pieces fall into and stay in (Anna's Roundtable has four).</summary>
    public class BowlTests
    {
        [UnityTest]
        public IEnumerator Bowl_HoldsWhatFallsIn()
        {
            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            Object.FindAnyObjectByType<TableInteraction>().IgnoreDevices = true;
            var table = Object.FindAnyObjectByType<TableView>();
            yield return new WaitForSeconds(0.2f);
            var json = @"{ ""schemaVersion"": ""0.1"",
                ""assets"": [ { ""id"": ""bowl_mesh"", ""type"": ""model"", ""path"": ""builtin:bowl"" } ],
                ""objects"": [
                  { ""id"": ""bowl"", ""type"": ""Model"", ""asset"": ""bowl_mesh"", ""locked"": true, ""tint"": [0.93, 0.52, 0], ""transform"": { ""position"": [0, 0, 0] } },
                  { ""id"": ""coin"", ""type"": ""Token"", ""shape"": ""round"", ""transform"": { ""position"": [0.01, 0.08, 0] } } ] }";
            Assert.IsTrue(table.Load(json, null));
            yield return new WaitForSeconds(2f);
            var coin = table.ViewOf("coin").transform.position;
            var flat = new Vector2(coin.x, coin.z).magnitude;
            Assert.Less(flat, BuiltinModels.BowlRadius * 0.8f, "the coin stays inside the bowl");
            Assert.Greater(coin.y, 0f, "on the bowl's floor, not through it");
            Assert.Less(coin.y, BuiltinModels.BowlHeight, "and down in it, not on a solid lid");
        }
    }
}
