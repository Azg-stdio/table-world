using System.Collections;
using System.Reflection;
using Core.Net;
using NUnit.Framework;
using TableWorld.View;
using TableWorld.View.Net;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>
    /// G3: what another player has selected is outlined in their colour on every machine, as in TTS, and the outline goes
    /// when they deselect. The other player's presence is fed in where a client's arrives on the host.
    /// </summary>
    public class SelectionPresenceTests
    {
        private NetworkTableSession _session;

        [UnitySetUp]
        public IEnumerator LoadTable()
        {
            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            Object.FindAnyObjectByType<TableInteraction>().IgnoreDevices = true;
            _session = Object.FindAnyObjectByType<NetworkTableSession>();
        }

        [UnityTearDown]
        public IEnumerator Stop()
        {
            _session.Stop();
            yield return null;
        }

        private void Arrives(PresenceFrame frame) =>
            typeof(NetworkTableSession).GetMethod("Take", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(_session, new object[] { frame });

        [UnityTest]
        public IEnumerator OthersSelections_AreOutlinedInTheirColour()
        {
            var table = Object.FindAnyObjectByType<TableView>();
            Assert.IsTrue(table.Load(@"{ ""schemaVersion"": ""0.1"",
              ""players"": { ""seats"": [ { ""id"": ""north"", ""color"": ""red"" }, { ""id"": ""south"", ""color"": ""blue"" } ] },
              ""objects"": [ { ""id"": ""a"", ""type"": ""Die"" }, { ""id"": ""b"", ""type"": ""Die"", ""transform"": { ""position"": [0.1, 0, 0] } } ] }", null));
            Assert.IsTrue(_session.StartHost(7796));
            yield return new WaitForSeconds(0.3f);

            Arrives(new PresenceFrame { Seat = "south", Sequence = 1, Selected = { "a" } });
            yield return null;
            yield return null;
            var outline = Outline.ColorOf(table.ViewOf("a"));
            Assert.IsNotNull(outline, "the die south selected is outlined");
            Assert.Greater(outline.Value.b, outline.Value.r, "in south's colour (blue)");
            Assert.IsNull(Outline.ColorOf(table.ViewOf("b")), "the other die is not");

            Arrives(new PresenceFrame { Seat = "south", Sequence = 2 });
            yield return null;
            yield return null;
            Assert.IsNull(Outline.ColorOf(table.ViewOf("a")), "deselected: the outline goes");
        }
    }
}
