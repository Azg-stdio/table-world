using System.Collections;
using Core.Engine;
using NUnit.Framework;
using TableWorld.View;
using TableWorld.View.Chalkers;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>
    /// Switching seats at a hot-seat table (Miguel's Avalon match, 2026-10-29: two players vanished after he looked out
    /// of their seats): the seat you leave shows its chalker again; only the one you look out of hides its body.
    /// </summary>
    public class SeatSwitchTests
    {
        private const string Game = @"{
  ""players"": { ""seats"": [ { ""id"": ""a"", ""color"": ""red"" }, { ""id"": ""b"", ""color"": ""blue"" }, { ""id"": ""c"", ""color"": ""green"" } ] },
  ""turn"": { ""phases"": [ ""play"" ] },
  ""phases"": [ { ""id"": ""play"", ""actions"": [] } ]
}";

        private TableView _table;
        private ChalkersView _chalkers;

        [UnitySetUp]
        public IEnumerator LoadTable()
        {
            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            Object.FindAnyObjectByType<TableInteraction>().IgnoreDevices = true;
            _table = Object.FindAnyObjectByType<TableView>();
            _chalkers = Object.FindAnyObjectByType<ChalkersView>();
            Assert.IsTrue(_table.Load(Game, null));
            Assert.IsTrue(_table.Execute(new StartGameCommand()).Ok);
            _chalkers.LocalSeats = () => new[] { "a", "b", "c" };
        }

        [UnityTearDown]
        public IEnumerator Restore()
        {
            _chalkers.LocalSeats = null;
            _table.SeatOverride = null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheSeatYouLeave_ShowsItsChalkerAgain()
        {
            var camera = Camera.main.GetComponent<TableCamera>();
            _table.SeatOverride = "b";
            camera.Sit();
            yield return new WaitForSeconds(1f);
            Assert.IsTrue(camera.IsSeated);
            Assert.IsTrue(_chalkers.Shown["b"].HeadHidden, "you look out of b's eyes");

            _table.SeatOverride = "c";
            yield return new WaitForSeconds(1f);
            Assert.IsTrue(_chalkers.Shown["c"].HeadHidden, "now out of c's");
            Assert.IsFalse(_chalkers.Shown["b"].HeadHidden, "b, left behind, shows again");
            Assert.IsFalse(_chalkers.Shown["a"].HeadHidden);
        }
    }
}
