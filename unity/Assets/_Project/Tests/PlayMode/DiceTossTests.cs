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

        [UnityTest]
        public IEnumerator SpammingR_KeepsItWhereItWas()
        {
            var die = _table.ViewOf("d");
            var home = die.transform.position;
            var peak = new float[1];
            yield return Roll(6, peak);
            var drift = Vector2.Distance(new Vector2(home.x, home.z), new Vector2(die.transform.position.x, die.transform.position.z));
            Debug.Log($"[DiceToss] six quick rolls rose {peak[0] * 100f:F1} cm and drifted {drift * 100f:F1} cm");
            Assert.Less(drift, 0.03f, "it stays around where it was");
        }

        [UnityTest]
        public IEnumerator ShakingSeveralDice_RattlesThemAll_AndAThrowTumbles()
        {
            Assert.IsTrue(_table.Load(@"{ ""schemaVersion"": ""0.1"", ""objects"": [
                { ""id"": ""a"", ""type"": ""Die"", ""transform"": { ""position"": [-0.4, 0, 0] } },
                { ""id"": ""b"", ""type"": ""Die"", ""transform"": { ""position"": [-0.37, 0, 0] } } ] }", null));
            yield return new WaitForSeconds(0.8f);
            var a = _table.ViewOf("a");
            var b = _table.ViewOf("b");
            _interaction.SetSelection(new[] { a, b });
            var startA = a.transform.rotation;
            var startB = b.transform.rotation;
            Assert.IsTrue(_interaction.GrabSelection(a));
            // Back and forth hard, then a throw along the table.
            // Moved once per physics step, not per frame: an Editor in the background renders few, uneven frames, and the
            // hand's speed (measured each physics step) then varied from run to run.
            for (var t = 0f; t < 0.6f; t += Time.fixedDeltaTime)
            {
                _interaction.DragTo(new Vector3(-0.4f + 0.08f * Mathf.Sin(t * 30f), 0f, 0f));
                yield return new WaitForFixedUpdate();
            }
            Assert.Greater(Quaternion.Angle(startA, a.transform.rotation), 20f, "the die in the hand rattles");
            Assert.Greater(Quaternion.Angle(startB, b.transform.rotation), 20f, "and so does the other one");
            for (var t = 0f; t < 0.2f; t += Time.fixedDeltaTime)
            {
                _interaction.DragTo(Vector3.Lerp(new Vector3(-0.4f, 0f, 0f), new Vector3(0f, 0f, 0f), t / 0.2f));
                yield return new WaitForFixedUpdate();
            }
            var released = a.transform.position;
            _interaction.Release();
            var moving = 0f;
            for (var t = 0f; t < 4f; t += Time.deltaTime)
            {
                if (a.Body.linearVelocity.magnitude > 0.01f || a.Body.angularVelocity.magnitude > 0.5f)
                    moving = t;
                yield return null;
            }
            var travel = Vector3.Distance(released, a.transform.position);
            Debug.Log($"[DiceToss] thrown at 2 m/s: travelled {travel * 100f:F1} cm, tumbled for {moving:F2} s");
            // Stopping dead is what this guards against: a throw that carries the die well past where it was let go. How long
            // it then tumbles varies with the random spin and whether the two dice meet (one run settled in under 0.8 s after
            // carrying 52 cm), so it is not checked.
            Assert.Greater(travel, 0.2f, $"a thrown die carries on, it does not stop dead (travelled {travel * 100f:F1} cm, tumbled {moving:F2} s)");
        }
    }
}
