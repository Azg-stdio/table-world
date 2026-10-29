using System.Collections;
using System.Linq;
using Core.Engine;
using Core.Rules;
using NUnit.Framework;
using TableWorld.View;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>
    /// A zone that catches a player's cards (M12): while they carry one, a beam stands on their spot; let go anywhere but
    /// the hand, the card goes to the spot face down; a second one the spot's rules refuse comes back.
    /// </summary>
    public class CatchGuideTests
    {
        private const string Game = @"{
  ""players"": { ""seats"": [ { ""id"": ""a"", ""color"": ""red"" }, { ""id"": ""b"", ""color"": ""blue"" } ] },
  ""objects"": [
    { ""id"": ""yes"", ""type"": ""Card"", ""text"": { ""title"": ""Yes"" }, ""tags"": [ ""vote"" ], ""transform"": { ""position"": [-0.2, 0, 0] } },
    { ""id"": ""no"", ""type"": ""Card"", ""text"": { ""title"": ""No"" }, ""tags"": [ ""vote"" ], ""transform"": { ""position"": [-0.4, 0, 0] } } ],
  ""zones"": [
    { ""id"": ""spot"", ""type"": ""LayoutZone"", ""facing"": ""faceDown"", ""transform"": { ""position"": [0.3, 0.05, -0.2] }, ""size"": [0.11, 0.1, 0.13],
      ""catch"": { ""tag"": ""vote"", ""for"": ""a"" },
      ""onEnter"": [ { ""if"": { ""condition"": { ""inList"": { ""list"": ""$var.voted"", ""value"": ""$actor"" } }, ""then"": [ { ""refuse"": { ""reason"": ""One vote."" } } ] } },
                    { ""setVariable"": { ""name"": ""voted"", ""add"": ""$actor"" } } ] } ],
  ""variables"": { ""voted"": [] },
  ""turn"": { ""phases"": [ ""vote"" ] },
  ""phases"": [ { ""id"": ""vote"", ""actions"": [] } ]
}";

        private TableView _table;
        private TableInteraction _interaction;
        private CatchGuide _guide;

        [UnitySetUp]
        public IEnumerator LoadTable()
        {
            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            _table = Object.FindAnyObjectByType<TableView>();
            _interaction = Object.FindAnyObjectByType<TableInteraction>();
            _interaction.IgnoreDevices = true;
            Assert.IsTrue(_table.Load(Game, null));
            Assert.IsTrue(_table.Execute(new StartGameCommand()).Ok);
            _table.SeatOverride = "a";
            yield return new WaitForSeconds(0.5f);
            _guide = _table.GetComponent<CatchGuide>();
            Assert.IsNotNull(_guide);
        }

        [UnityTearDown]
        public IEnumerator Restore()
        {
            _table.SeatOverride = null;
            yield return null;
        }

        private IEnumerator Carry(string id, Vector3 to)
        {
            Assert.IsTrue(_interaction.Grab(_table.ViewOf(id), moveContainer: false), $"grab {id}");
            yield return null;
            _interaction.DragTo(to);
            yield return new WaitForSeconds(0.2f);
        }

        [UnityTest]
        public IEnumerator ACarriedVote_LightsItsSpot_AndLandsThereFaceDown_WhereverItIsLetGo()
        {
            Assert.IsNull(_guide.ShownZone);
            yield return Carry("yes", new Vector3(-0.5f, 0f, 0.3f));
            Assert.AreEqual("spot", _guide.ShownZone, "the spot lights up while the card is carried");
            _interaction.Release();
            yield return new WaitForSeconds(1f);
            Assert.IsNull(_guide.ShownZone);
            Assert.Contains("spot", _table.Engine.ZonesOf("yes").ToList(), "let go far away, it went to the spot");
            Assert.IsFalse(((Card)_table.Engine.Find("yes")).IsFaceUp);

            // The second vote: the spot refuses it, and it comes back where it was.
            var before = _table.Engine.Find("no").Transform.Position;
            yield return Carry("no", new Vector3(0.6f, 0f, 0.3f));
            _interaction.Release();
            yield return new WaitForSeconds(1f);
            Assert.IsFalse(_table.Engine.ZonesOf("no").Contains("spot"));
            Assert.AreEqual(before.X, _table.Engine.Find("no").Transform.Position.X, 0.05f);
        }

        [UnityTest]
        public IEnumerator AnotherPlayersCard_IsNotCaught()
        {
            _table.SeatOverride = "b";
            yield return Carry("yes", new Vector3(-0.5f, 0f, 0.3f));
            Assert.IsNull(_guide.ShownZone, "the spot is a's");
            _interaction.Release();
            yield return new WaitForSeconds(1f);
            Assert.IsFalse(_table.Engine.ZonesOf("yes").Contains("spot"));
        }
    }
}
