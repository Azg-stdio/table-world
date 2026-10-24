using System.Collections;
using System.Linq;
using Core.Engine;
using Core.Rules;
using NUnit.Framework;
using TableWorld.View;
using TableWorld.View.Hud;
using TableWorld.View.Meshes;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>G21: the d6 numbers its faces like TTS; a custom d6 image uses the TTS template; labelled faces are announced.</summary>
    public class CustomDiceTests
    {
        private TableView _table;

        [UnitySetUp]
        public IEnumerator LoadTable()
        {
            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            _table = Object.FindAnyObjectByType<TableView>();
            Object.FindAnyObjectByType<TableInteraction>().IgnoreDevices = true;
            yield return new WaitForSeconds(0.3f);
        }

        [Test]
        public void D6_UsesTtsRotations()
        {
            var d6 = DiceGeometry.Get(6);
            // TTS RotationValues for a d6: value 1 at x −90, 2 at rest, 3 at z −90, 4 at z 90, 5 at z 180, 6 at x 90.
            var tts = new[] { new Vector3(-90, 0, 0), Vector3.zero, new Vector3(0, 0, -90), new Vector3(0, 0, 90), new Vector3(0, 0, 180), new Vector3(90, 0, 0) };
            for (var v = 1; v <= 6; v++)
                Assert.AreEqual(v, d6.ValueFor(Quaternion.Euler(tts[v - 1])), $"TTS rotation for {v}");
            Assert.IsNotNull(d6.TemplateMesh);
            Assert.IsNull(DiceGeometry.Get(20).TemplateMesh);
        }

        [UnityTest]
        public IEnumerator EventDie_DrawsTheTemplate_AndRollsAreAnnouncedByLabel()
        {
            var view = _table.ViewOf("event_die");
            Assert.AreEqual(DiceGeometry.Get(6).TemplateMesh, view.Filter.sharedMesh, "a custom d6 image uses the TTS template mapping");
            Assert.AreNotEqual(DiceGeometry.Get(6).TemplateMesh, _table.ViewOf("d6").Filter.sharedMesh, "a plain d6 keeps the builtin atlas");

            var die = (Die)view.Model;
            var status = Object.FindAnyObjectByType<HudController>().GetComponent<UIDocument>().rootVisualElement.Q<Label>("statusLabel");
            _table.Execute(new RollCommand { ObjectId = "event_die" });
            yield return new WaitForSeconds(2.5f);
            StringAssert.StartsWith("Rolled ", status.text);
            CollectionAssert.Contains(die.Labels, status.text.Substring("Rolled ".Length).Split(" (d6)")[0], "the status names the face");
            Assert.AreEqual(die.Value, DiceGeometry.Get(6).ValueFor(view.transform.rotation), "the face up is the value");

            var overlay = Object.FindAnyObjectByType<HudController>().Overlay;
            overlay.OpenMenu(view, Vector2.zero);
            CollectionAssert.Contains(overlay.MenuEntries, "Lose a turn");
            Assert.IsTrue(overlay.Choose("Lose a turn"));
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(6, die.Value);
            Assert.AreEqual(6, DiceGeometry.Get(6).ValueFor(view.transform.rotation));
        }
    }
}
