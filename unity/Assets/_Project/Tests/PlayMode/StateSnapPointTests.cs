using System.Collections;
using System.Linq;
using Core.Engine;
using NUnit.Framework;
using TableWorld.View;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>
    /// M7: each state of an object keeps its own snap points, as in TTS. A hidden state's points are not drawn and catch
    /// nothing; showing the state brings them, and the other state's go quiet.
    /// </summary>
    public class StateSnapPointTests
    {
        [UnityTest]
        public IEnumerator HiddenStates_SnapPoints_WaitForTheirState()
        {
            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            var interaction = Object.FindAnyObjectByType<TableInteraction>();
            interaction.IgnoreDevices = true;
            var table = Object.FindAnyObjectByType<TableView>();
            yield return new WaitForSeconds(0.2f);
            var json = @"{ ""schemaVersion"": ""0.1"",
                ""objects"": [
                  { ""id"": ""mat"", ""type"": ""Board"", ""size"": [0.4, 0.3], ""states"": { ""2"": { ""id"": ""mat_b"", ""type"": ""Board"", ""size"": [0.4, 0.3] } } },
                  { ""id"": ""chip"", ""type"": ""Token"", ""transform"": { ""position"": [0.5, 0, 0.3] } } ],
                ""zones"": [
                  { ""id"": ""front_point"", ""type"": ""SnapPoint"", ""parent"": ""mat"", ""transform"": { ""position"": [0.1, 0, 0] } },
                  { ""id"": ""back_point"", ""type"": ""SnapPoint"", ""parent"": ""mat_b"", ""transform"": { ""position"": [-0.1, 0, 0] } } ] }";
            Assert.IsTrue(table.Load(json, null));
            yield return null;
            bool Drawn(string id) => Object.FindObjectsByType<Transform>().Any(t => t.name.EndsWith(" " + id) && t.gameObject.activeInHierarchy);
            Assert.IsTrue(Drawn("front_point"), "the shown state's point is drawn");
            Assert.IsFalse(Drawn("back_point"), "the hidden state's point is not");

            // Dropped right on the hidden state's point: nothing catches it.
            var chip = table.ViewOf("chip");
            interaction.Grab(chip, moveContainer: false);
            interaction.DragTo(new Vector3(-0.1f, 0f, 0f));
            interaction.Release();
            yield return new WaitForSeconds(0.3f);
            Assert.Greater(Mathf.Abs(chip.transform.position.x + 0.1f) + Mathf.Abs(chip.transform.position.z), 0.002f, "no snapping to a hidden state's point");

            Assert.IsTrue(table.Execute(new SetStateCommand { ObjectId = "mat", State = "2" }).Ok);
            yield return null;
            yield return null;
            Assert.IsTrue(Drawn("back_point"), "showing the state brings its point");
            Assert.IsFalse(Drawn("front_point"), "and the other state's goes");
        }
    }
}
