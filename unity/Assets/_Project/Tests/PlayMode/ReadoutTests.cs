using System.Collections;
using System.Linq;
using Core.Engine;
using Core.Rules;
using NUnit.Framework;
using TableWorld.View;
using TableWorld.View.Hud;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>G27: the sample table's counter and timer, through the same gestures the keys, panel and menu use.</summary>
    public class ReadoutTests
    {
        private TableView _table;
        private TableInteraction _interaction;
        private TableOverlay _overlay;

        [UnitySetUp]
        public IEnumerator LoadTable()
        {
            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            _table = Object.FindAnyObjectByType<TableView>();
            _interaction = Object.FindAnyObjectByType<TableInteraction>();
            _interaction.IgnoreDevices = true;
            _overlay = Object.FindAnyObjectByType<HudController>().Overlay;
            yield return new WaitForSeconds(0.5f);
        }

        private ObjectView V(string id) => _table.ViewOf(id);

        private string Shown(string id) => V(id).GetComponent<Readout>().Text;

        [UnityTest]
        public IEnumerator Counter_TypedDigitsMenuAndPanelChangeIt_AndTheDisplayFollows()
        {
            var score = V("score");
            Assert.AreEqual("0", Shown("score"));
            _interaction.TypeDigit(score, 1);
            _interaction.TypeDigit(score, 2);
            Assert.IsTrue(_interaction.FlushDigits());
            yield return null;
            Assert.AreEqual(12, ((Counter)score.Model).Value);
            Assert.AreEqual("12", Shown("score"));

            _overlay.OpenMenu(score, Vector2.zero);
            Assert.IsTrue(_overlay.Choose("Add: −10"));
            _overlay.OpenMenu(score, Vector2.zero);
            Assert.IsTrue(_overlay.Choose("Add: −10"));
            yield return null;
            Assert.AreEqual("-8", Shown("score"));

            _table.Execute(new LockCommand { ObjectId = "score" });
            Assert.IsTrue(_interaction.AddToCounter(score, 1), "a locked counter still counts");
            _overlay.OpenMenu(score, Vector2.zero);
            Assert.IsTrue(_overlay.Choose("Reset to 0"));
            yield return null;
            Assert.AreEqual("0", Shown("score"));
        }

        [UnityTest]
        public IEnumerator Stopwatch_RunsAndPausesOnTheShownReading()
        {
            var clock = V("timer");
            _overlay.OpenMenu(clock, Vector2.zero);
            Assert.IsTrue(_overlay.Choose("Mode: Stopwatch"));
            yield return null;
            Assert.AreEqual("0:00", Shown("timer"));
            Assert.IsTrue(_interaction.StartOrPause(clock));
            yield return new WaitForSecondsRealtime(2.2f);
            Assert.AreEqual("0:02", Shown("timer"));
            Assert.IsTrue(_interaction.StartOrPause(clock));
            var paused = (Clock)clock.Model;
            Assert.IsFalse(paused.Running);
            Assert.AreEqual(2.2f, paused.Seconds, 0.3f, "paused on the reading it showed");
            yield return new WaitForSecondsRealtime(1.2f);
            Assert.AreEqual("0:02", Shown("timer"), "a paused stopwatch stays put");
        }

        [UnityTest]
        public IEnumerator Timer_CountsDown_StopsAtZero_AndRings()
        {
            var sounds = Object.FindAnyObjectByType<TableSounds>();
            var clock = V("timer");
            Assert.AreEqual("1:00", Shown("timer"));
            Assert.IsTrue(_interaction.SetTimer(clock, 2f));
            Assert.IsTrue(_interaction.StartOrPause(clock));
            yield return new WaitForSecondsRealtime(1.1f);
            Assert.AreEqual("0:01", Shown("timer"));
            yield return new WaitForSecondsRealtime(1.4f);
            var model = (Clock)clock.Model;
            Assert.IsFalse(model.Running, "stopped at zero");
            Assert.AreEqual(0f, model.Seconds);
            Assert.AreEqual("0:00", Shown("timer"));
            Assert.AreEqual(1, sounds.Played.TryGetValue("Ping", out var rang) ? rang : 0, "time's up rings once");

            Assert.IsTrue(_interaction.ResetClock(clock));
            yield return null;
            Assert.AreEqual("0:02", Shown("timer"), "reset goes back to what it was set to");
        }
    }
}
