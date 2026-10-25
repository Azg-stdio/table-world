using System.Collections;
using Core.Engine;
using NUnit.Framework;
using TableWorld.View;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>
    /// M5/M7: TTS joints and attachments. Master Trainer fixes each Pokémon model to its chip, so moving the chip moves
    /// the model; an attached piece (Anna's boss tokens, Gloomhaven's board parts) is taken with what it is attached to.
    /// </summary>
    public class JointTests
    {
        private TableView _table;
        private TableInteraction _interaction;

        [UnitySetUp]
        public IEnumerator LoadTable()
        {
            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            _interaction = Object.FindAnyObjectByType<TableInteraction>();
            _interaction.IgnoreDevices = true;
            _table = Object.FindAnyObjectByType<TableView>();
            yield return new WaitForSeconds(0.2f);
            var json = @"{ ""schemaVersion"": ""0.1"", ""objects"": [
                { ""id"": ""chip"", ""type"": ""Token"", ""shape"": ""round"", ""transform"": { ""position"": [0, 0, 0] } },
                { ""id"": ""model"", ""type"": ""Figure"", ""transform"": { ""position"": [0, 0.01, 0] }, ""joint"": { ""to"": ""chip"" } },
                { ""id"": ""board"", ""type"": ""Board"", ""size"": [0.2, 0.2], ""transform"": { ""position"": [0.4, 0, 0] } },
                { ""id"": ""part"", ""type"": ""Token"", ""transform"": { ""position"": [0.4, 0.01, 0] }, ""joint"": { ""to"": ""board"", ""attached"": true } } ] }";
            Assert.IsTrue(_table.Load(json, null));
            yield return new WaitForSeconds(0.5f);
        }

        [UnityTest]
        public IEnumerator MovingTheChip_MovesItsModel()
        {
            Assert.IsNotNull(_table.ViewOf("model").GetComponent<FixedJoint>(), "the model is fixed to its chip");
            _interaction.Grab(_table.ViewOf("chip"), moveContainer: false);
            _interaction.DragTo(new Vector3(-0.3f, 0f, 0.2f));
            yield return new WaitForSeconds(0.6f);
            _interaction.Release();
            yield return new WaitForSeconds(0.8f);
            var chip = _table.ViewOf("chip").transform.position;
            var model = _table.ViewOf("model").transform.position;
            Assert.Less(Vector2.Distance(new Vector2(chip.x, chip.z), new Vector2(-0.3f, 0.2f)), 0.03f, "the chip went where it was dragged");
            Assert.Less(Vector2.Distance(new Vector2(chip.x, chip.z), new Vector2(model.x, model.z)), 0.02f, "and the model came with it");

            // Freed, the model stays behind when the chip moves again.
            Assert.IsTrue(_table.Execute(new SetJointCommand { ObjectId = "model" }).Ok);
            yield return null;
            Assert.IsNull(_table.ViewOf("model").GetComponent<FixedJoint>());
        }

        [UnityTest]
        public IEnumerator GrabbingAnAttachedPart_TakesTheWhole()
        {
            Assert.IsTrue(_interaction.Grab(_table.ViewOf("part"), moveContainer: false));
            Assert.AreEqual("board", _interaction.Held.Id, "the hand takes the board the part belongs to");
            _interaction.Release();
            yield return null;
        }
    }
}
