using System.Collections;
using NUnit.Framework;
using TableWorld.View;
using TableWorld.View.Meshes;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>
    /// Rolling as in TTS (Miguel, 2026-10-25): R tosses the die with physics, R again while it is up sends it higher
    /// (the same die even once it has jumped out from under the pointer), and it always lands at rest on the value the
    /// engine chose.
    /// </summary>
    public class DiceTossTests
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
            Assert.IsTrue(_table.Load(@"{ ""schemaVersion"": ""0.1"", ""objects"": [ { ""id"": ""d"", ""type"": ""Die"", ""faces"": 6, ""transform"": { ""position"": [0, 0, 0] } } ] }", null));
            yield return new WaitForSeconds(0.8f);
        }

        private IEnumerator Roll(int presses, float[] peak)
        {
            var die = _table.ViewOf("d");
            var rest = die.transform.position.y;
            var top = rest;
            for (var i = 0; i < presses; i++)
            {
                Assert.AreEqual(1, _interaction.RollDice(new[] { die }));
                for (var t = 0f; t < 0.12f; t += Time.deltaTime)
                {
                    top = Mathf.Max(top, die.transform.position.y);
                    yield return null;
                }
            }
            for (var t = 0f; t < 4f && (die.GetComponent<DieToss>()?.Busy ?? false); t += Time.deltaTime)
            {
                top = Mathf.Max(top, die.transform.position.y);
                yield return null;
            }
            yield return new WaitForSeconds(0.3f);
            peak[0] = top - rest;
            Assert.IsFalse(die.GetComponent<DieToss>().Busy, "the roll has finished");
            Assert.AreEqual(rest, die.transform.position.y, 0.004f, "at rest on the table");
            var value = ((Core.Rules.Die)die.Model).Value;
            Assert.AreEqual(value, DiceGeometry.Get(6).ValueFor(die.transform.rotation), "showing the engine's value");
        }

        [UnityTest]
        public IEnumerator SpammingR_SendsItHigher_AndItLandsOnItsValue()
        {
            var once = new float[1];
            yield return Roll(1, once);
            var thrice = new float[1];
            yield return Roll(3, thrice);
            Debug.Log($"[DiceToss] one roll rose {once[0] * 100f:F1} cm, three quick rolls {thrice[0] * 100f:F1} cm");
            Assert.Greater(once[0], 0.02f, "a roll tosses it up");
            Assert.Greater(thrice[0], once[0] * 1.5f, "pressing again while it is up sends it higher");
        }
    }
}
