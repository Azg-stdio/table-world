using System.Collections;
using NUnit.Framework;
using TableWorld.View;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>M7: a table says how big it is (imported TTS games are far bigger than our default) or that it has none.</summary>
    public class TableSizeTests
    {
        [UnityTest]
        public IEnumerator TableSize_ComesFromTheTable_AndPiecesFarOutStayPut()
        {
            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            var table = Object.FindAnyObjectByType<TableView>();
            Object.FindAnyObjectByType<TableInteraction>().IgnoreDevices = true;
            yield return new WaitForSeconds(0.3f);

            var json = @"{ ""schemaVersion"": ""0.1"", ""environment"": { ""table"": { ""size"": [4.0, 2.0] } },
              ""objects"": [ { ""id"": ""far"", ""type"": ""Die"", ""transform"": { ""position"": [1.8, 0.02, 0.8] } } ] }";
            Assert.IsTrue(table.Load(json, null));
            yield return new WaitForSeconds(1f);
            Assert.AreEqual(new Vector2(4f, 2f), TableGeometry.Size);
            var surface = GameObject.Find("Table").transform.Find("Surface");
            Assert.AreEqual(4f, surface.localScale.x, 0.001f, "the felt fits the table");
            Assert.AreEqual(1.8f, table.ViewOf("far").transform.position.x, 0.05f, "a piece near the far edge is on the table, not put back");
            Assert.AreEqual(0f, table.ViewOf("far").transform.position.y, 0.03f, "and rests on it");

            var none = @"{ ""schemaVersion"": ""0.1"", ""environment"": { ""table"": { ""hidden"": true, ""lowestY"": -1.0 } },
              ""objects"": [ { ""id"": ""board"", ""type"": ""Board"", ""locked"": true, ""transform"": { ""position"": [0, -0.5, 0] } },
                             { ""id"": ""die"", ""type"": ""Die"", ""transform"": { ""position"": [0, -0.4, 0] } } ] }";
            Assert.IsTrue(table.Load(none, null));
            yield return new WaitForSeconds(1f);
            Assert.IsFalse(GameObject.Find("Table") != null && GameObject.Find("Table").activeInHierarchy, "no table of ours");
            Assert.Less(table.ViewOf("die").transform.position.y, -0.4f, "the die fell onto the game's own board; it is at " + table.ViewOf("die").transform.position + ", the board at " + table.ViewOf("board").transform.position);
            Assert.Greater(table.ViewOf("die").transform.position.y, -0.5f, "and rests there, not put back on a table that is not there");

            Assert.IsTrue(table.Load(@"{ ""schemaVersion"": ""0.1"" }", null));
            yield return null;
            Assert.AreEqual(TableGeometry.DefaultSize, TableGeometry.Size, "a table without settings gets the default back");
            Assert.IsTrue(GameObject.Find("Table").activeInHierarchy);
        }
    }
}
