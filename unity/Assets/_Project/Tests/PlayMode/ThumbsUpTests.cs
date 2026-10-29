using System.Collections;
using System.Linq;
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
    /// A raised thumb on the worn model reads as one (M12): a fist, its four fingers curled into the palm, its thumb
    /// straight up, held above the shoulder; and a sleeping chalker's cards rest low, so no other hand is up.
    /// </summary>
    public class ThumbsUpTests
    {
        private const string Game = @"{
  ""players"": { ""seats"": [ { ""id"": ""a"", ""color"": ""red"" }, { ""id"": ""b"", ""color"": ""blue"" }, { ""id"": ""c"", ""color"": ""green"" } ] },
  ""objects"": [ { ""id"": ""card_b"", ""type"": ""Card"", ""text"": { ""title"": ""Role"" }, ""transform"": { ""position"": [0, 0, 0] } } ],
  ""turn"": { ""phases"": [ ""look"" ] },
  ""phases"": [ { ""id"": ""look"", ""awake"": ""a"", ""raised"": ""b"", ""actions"": [] } ]
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
            _table.SeatOverride = "a";
            // Hot seat: every seat at the table, so b and c have their chalkers.
            _chalkers.LocalSeats = () => new[] { "a", "b", "c" };
            yield return new WaitForSeconds(1f);
        }

        [UnityTearDown]
        public IEnumerator Restore()
        {
            _chalkers.LocalSeats = null;
            _table.SeatOverride = null;
            yield return null;
        }

        private static Transform Bone(Chalker chalker, string name) =>
            chalker.Root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

        [UnityTest]
        public IEnumerator ARaisedThumb_IsAFist_ThumbUp_AboveTheShoulder()
        {
            Assert.IsTrue(_chalkers.Shown.TryGetValue("b", out var b), "b has its chalker");
            Assert.IsTrue(b.ThumbUp, "the phase raises b's thumb for a");
            yield return null;
            var hand = Bone(b, "hand_r");
            var thumb = Bone(b, "thumb_03_r");
            var shoulder = Bone(b, "upperarm_r");
            Assume.That(hand != null && thumb != null && shoulder != null, "the worn model has finger bones");
            var up = b.Root.transform.up;
            Assert.Greater(Vector3.Dot(hand.position - shoulder.position, up), 0.1f, "the fist is held above the shoulder");
            var thumbDirection = (thumb.position - Bone(b, "thumb_01_r").position).normalized;
            Assert.Greater(Vector3.Dot(thumbDirection, up), 0.8f, "the thumb points up");
            foreach (var finger in new[] { "index", "middle", "ring", "pinky" })
            {
                var knuckle = Bone(b, finger + "_01_r");
                var tip = Bone(b, finger + "_03_r");
                var length = Vector3.Distance(knuckle.position, Bone(b, finger + "_02_r").position) + Vector3.Distance(Bone(b, finger + "_02_r").position, tip.position);
                Assert.Less(Vector3.Distance(knuckle.position, tip.position), 0.75f * length, $"the {finger} is curled");
            }
            // c sleeps (only a is awake) and holds nothing up: its hands rest near the table.
            if (_chalkers.Shown.TryGetValue("c", out var c) && Bone(c, "hand_l") is { } left)
                Assert.Less(c.Root.transform.InverseTransformPoint(left.position).y, 0.15f, "a sleeper's hand rests low");
        }
    }
}
