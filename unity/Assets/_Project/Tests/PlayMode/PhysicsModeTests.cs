using System.Collections;
using Core.Engine;
using Core.Rules;
using NUnit.Framework;
using TableWorld.View;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>G19: the host's physics modes. Semi-lock keeps things upright, locked sets drops down in place.</summary>
    public class PhysicsModeTests
    {
        private TableView _table;
        private TableInteraction _interaction;

        [UnitySetUp]
        public IEnumerator LoadTable()
        {
            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            _table = Object.FindAnyObjectByType<TableView>();
            _interaction = Object.FindAnyObjectByType<TableInteraction>();
            _interaction.IgnoreDevices = true;
            yield return new WaitForSeconds(0.5f);
        }

        private ObjectView V(string id) => _table.ViewOf(id);

        private void SetMode(PhysicsMode mode) => Assert.IsTrue(_table.Execute(new SetPhysicsCommand { Mode = mode }).Ok);

        private float _leastUpright;

        /// <summary>Knocks the statue sideways with a spin that tips it with full physics; records how far it leaned.</summary>
        private IEnumerator Knock(ObjectView statue)
        {
            statue.Body.linearVelocity = new Vector3(0.3f, 0.4f, 0f);
            statue.Body.angularVelocity = new Vector3(0f, 0f, -12f);
            _leastUpright = 1f;
            for (var t = 0f; t < 1.5f; t += Time.fixedDeltaTime)
            {
                yield return new WaitForFixedUpdate();
                _leastUpright = Mathf.Min(_leastUpright, Vector3.Dot(statue.transform.up, Vector3.up));
            }
        }

        [UnityTest]
        public IEnumerator Full_AKnockedObjectTips()
        {
            yield return Knock(V("statue"));
            Assert.Less(_leastUpright, 0.7f, "with full physics it tips");
        }

        [UnityTest]
        public IEnumerator SemiLock_NothingTipsOver_ButDiceStillTumble()
        {
            SetMode(PhysicsMode.SemiLock);
            Assert.AreEqual(RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ, V("statue").Body.constraints);
            Assert.AreEqual(RigidbodyConstraints.None, V("d6").Body.constraints, "dice are free to tumble");
            var statue = V("statue");
            var start = statue.transform.position;
            yield return Knock(statue);
            Assert.Greater(_leastUpright, 0.999f, "it never leaned");
            Assert.Greater(Vector3.Distance(statue.transform.position, start), 0.01f, "the knock still moved it");

            SetMode(PhysicsMode.Full);
            Assert.AreEqual(RigidbodyConstraints.None, V("statue").Body.constraints);
        }

        [UnityTest]
        public IEnumerator Locked_DropsAreSetDownWhereReleased_AndNothingMoves()
        {
            SetMode(PhysicsMode.Locked);
            Assert.IsTrue(V("hero").Body.isKinematic && V("ace_spades").Body.isKinematic, "every body is pinned");

            // Dropped from a height over bare felt: set down flat right there, no fall, no slide.
            var card = V("ace_spades");
            Assert.IsTrue(_interaction.Grab(card, false));
            _interaction.RaiseHeld(0.2f);
            _interaction.DragTo(new Vector3(0.2f, 0f, 0.05f));
            yield return new WaitForSeconds(0.6f);
            var releasedAt = card.transform.position;
            _interaction.Release();
            Assert.Less(card.Renderer.bounds.min.y, 0.002f, "on the felt at once");
            Assert.AreEqual(releasedAt.x, card.transform.position.x, 0.0005f);
            Assert.AreEqual(releasedAt.z, card.transform.position.z, 0.0005f);
            yield return new WaitForSeconds(0.5f);
            Assert.Less(Vector3.Distance(card.transform.position, card.Model.Transform.Position.ToUnity()), 0.002f, "the state has it");
            Assert.IsTrue(card.Body.isKinematic);

            // Dropped over the board: it lies on top of it.
            var board = V("board");
            Assert.IsTrue(_interaction.Grab(V("token_red"), false));
            _interaction.DragTo(board.transform.position);
            yield return new WaitForSeconds(0.6f);
            _interaction.Release();
            var token = V("token_red");
            Assert.AreEqual(board.Renderer.bounds.max.y, token.Renderer.bounds.min.y, 0.003f, "resting on the board's top");
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(board.Renderer.bounds.max.y, token.Renderer.bounds.min.y, 0.003f, "and staying there");

            // Dice still roll (an animation, not physics).
            Assert.IsTrue(_table.Execute(new RollCommand { ObjectId = "d6" }).Ok);
            yield return new WaitForSeconds(1f);
            Assert.IsTrue(V("d6").Body.isKinematic);

            SetMode(PhysicsMode.Full);
            Assert.IsFalse(V("hero").Body.isKinematic, "full physics again");
        }
    }
}
