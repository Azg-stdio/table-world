using System.Collections;
using System.Linq;
using Core.Net;
using NUnit.Framework;
using TableWorld.View;
using TableWorld.View.Hud;
using TableWorld.View.Net;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>
    /// G23 on a hosted table in the Editor (the host's side; two machines are checked with built players): the host's
    /// name shows on its seat, in chat and in the Players panel; a Game Master sees inside hidden areas.
    /// </summary>
    public class PlayersTests
    {
        private TableView _table;
        private NetworkTableSession _session;
        private HudController _hud;

        [UnitySetUp]
        public IEnumerator LoadTable()
        {
            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            _table = Object.FindAnyObjectByType<TableView>();
            _session = Object.FindAnyObjectByType<NetworkTableSession>();
            _hud = Object.FindAnyObjectByType<HudController>();
            Object.FindAnyObjectByType<TableInteraction>().IgnoreDevices = true;
            yield return new WaitForSeconds(0.3f);
        }

        [UnityTearDown]
        public IEnumerator StopHosting()
        {
            _session.Stop();
            yield return null;
        }

        [UnityTest]
        public IEnumerator Host_Name_Panel_AndGameMasterSight()
        {
            Assert.IsTrue(_session.StartHost(7791));
            _session.SetLocalName("  Miguel  ");
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual("Miguel", _session.NameOf(SessionHost.HostId));
            Assert.AreEqual("Miguel", _session.NameAtSeat(_table.LocalSeat));
            var root = _hud.GetComponent<UIDocument>().rootVisualElement;
            Assert.IsTrue(root.Query<Label>(className: "seat-name").ToList().Any(l => l.text == "Miguel (you)"), "the seat chip shows the name");

            _session.Say("hello");
            Assert.AreEqual("Miguel", _session.Chat.Entries.Last(e => e.Kind == ChatKind.Say).From, "chat signs with the name");

            Assert.IsFalse(root.Q<Button>("playersButton").ClassListContains("hidden"), "online, the Players button shows");
            _hud.Players.Toggle();
            yield return null;
            Assert.IsTrue(root.Query<Label>(className: "player-name").ToList().Any(l => l.text == "Miguel (you)"));
            Assert.IsTrue(root.Query<Button>(className: "player-action").ToList().Any(b => b.text == "Make GM"), "the host has controls");

            // The north seat's hidden area: the host (south) sees fog until made Game Master.
            Assert.IsTrue(_table.ViewOf("secret_card").ZoneHidden);
            _session.SetGameMaster(SessionHost.HostId, true);
            yield return null;
            Assert.IsTrue(_table.SeesEverything);
            Assert.IsFalse(_table.ViewOf("secret_card").ZoneHidden, "the Game Master sees inside");
            Assert.IsTrue(_session.Chat.Entries.Any(e => e.Text == "Miguel is the Game Master"));
            yield return null;
            Assert.IsTrue(root.Query<Label>(className: "player-badges").ToList().Any(l => l.text.Contains("Game Master")));

            _session.SetGameMaster(SessionHost.HostId, false);
            yield return null;
            Assert.IsTrue(_table.ViewOf("secret_card").ZoneHidden);

            _session.SetTeam(SessionHost.HostId, Team.Hearts);
            _session.Say("/t team only");
            var team = _session.Chat.Entries.Last(e => e.Kind == ChatKind.Say);
            Assert.AreEqual(Team.Hearts, team.Team);
            _hud.PressEscape();
            Assert.IsFalse(_hud.Players.Visible, "Escape closes the panel");
        }
    }
}
