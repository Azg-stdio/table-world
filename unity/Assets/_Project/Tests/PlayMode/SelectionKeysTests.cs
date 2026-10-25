using System.Collections;
using System.Linq;
using NUnit.Framework;
using TableWorld.View;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>
    /// G46, as in TTS: the object you click is selected (outlined in your seat's colour) and the keys act on it while the
    /// pointer is elsewhere; a click on the table clears it and the keys go back to the camera.
    /// </summary>
    public class SelectionKeysTests
    {
        [UnityTest]
        public IEnumerator TheSelection_KeepsTheKeys_UntilCleared()
        {
            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            var interaction = Object.FindAnyObjectByType<TableInteraction>();
            interaction.IgnoreDevices = true;
            var table = Object.FindAnyObjectByType<TableView>();
            yield return new WaitForSeconds(0.2f);
            var json = @"{ ""schemaVersion"": ""0.1"",
                ""objects"": [ { ""id"": ""a"", ""type"": ""Token"", ""transform"": { ""position"": [0, 0, 0] } },
                               { ""id"": ""b"", ""type"": ""Token"", ""transform"": { ""position"": [0.2, 0, 0] } } ],
                ""players"": { ""seats"": [ { ""id"": ""south"", ""color"": ""red"" } ] } }";
            Assert.IsTrue(table.Load(json, null));
            yield return null;
            Assert.IsEmpty(interaction.KeyTargets(), "nothing selected, nothing under the pointer: the keys turn the camera");

            var a = table.ViewOf("a");
            interaction.SetSelection(new[] { a });
            yield return null;
            Assert.IsNull(interaction.Hovered, "the pointer is not on it");
            Assert.AreEqual(new[] { "a" }, interaction.KeyTargets().Select(v => v.Id).ToArray(), "yet the keys act on the selection");
            Assert.AreEqual(ZoneViewFactory.SeatColor("red"), Outline.ColorOf(a), "outlined in the seat's colour");

            interaction.ClearSelection();
            yield return null;
            Assert.IsEmpty(interaction.KeyTargets(), "cleared: back to the camera");
            Assert.IsNull(Outline.ColorOf(a), "and the outline goes");
        }
    }
}
