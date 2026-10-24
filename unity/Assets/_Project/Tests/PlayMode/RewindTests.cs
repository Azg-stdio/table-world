using System.Collections;
using System.Linq;
using Core.Engine;
using Core.Net;
using Core.Rules;
using NUnit.Framework;
using TableWorld.View;
using TableWorld.View.Hud;
using TableWorld.View.Net;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>G26: the table menu rewinds the whole table to a saved moment and goes forward again.</summary>
    public class RewindTests
    {
        private TableView _table;
        private TableOverlay _overlay;

        [UnitySetUp]
        public IEnumerator LoadTable()
        {
            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            _table = Object.FindAnyObjectByType<TableView>();
            _overlay = Object.FindAnyObjectByType<HudController>().Overlay;
            Object.FindAnyObjectByType<TableInteraction>().IgnoreDevices = true;
            yield return new WaitForSeconds(0.3f);
        }

        [UnityTearDown]
        public IEnumerator Stop()
        {
            Object.FindAnyObjectByType<NetworkTableSession>().Stop();
            yield return null;
        }

        private float X(string id) => _table.Engine.Find(id).Transform.Position.X;

        [UnityTest]
        public IEnumerator RewindAndForward_FromTheTableMenu()
        {
            var start = X("ace_spades");
            _table.Execute(new MoveCommand { ObjectId = "ace_spades", Position = new Vec3(0.3f, 0f, 0.25f) });
            _table.Execute(new ShuffleCommand { ObjectId = "deck_main" });
            yield return new WaitForSeconds(0.3f);

            _overlay.OpenMenu(null, new Vector2(200f, 200f));
            var back = _overlay.MenuEntries.SingleOrDefault(e => e.StartsWith("Rewind: ◀ 0:0"));
            Assert.IsNotNull(back, "the table menu offers to rewind a few seconds: " + string.Join(", ", _overlay.MenuEntries));
            Assert.IsTrue(_overlay.Choose(back));
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(start, X("ace_spades"), 0.001f, "the card is back where the table started");
            Assert.AreEqual(start, _table.ViewOf("ace_spades").transform.position.x, 0.01f, "and is drawn there");

            _overlay.OpenMenu(null, new Vector2(200f, 200f));
            Assert.IsTrue(_overlay.Choose("Rewind: Forward ▶"));
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(0.3f, X("ace_spades"), 0.01f, "forward returns to the present");
            var session = Object.FindAnyObjectByType<NetworkTableSession>();
            Assert.IsTrue(session.Chat.Entries.Any(e => e.Kind == ChatKind.Notice && e.Text.Contains("rewound the table")));
        }

        [UnityTest]
        public IEnumerator Hosting_RewindSendsTheTableOn_AndAClientCannotRewind()
        {
            var session = Object.FindAnyObjectByType<NetworkTableSession>();
            Assert.IsTrue(session.StartHost(7793));
            yield return null;
            _table.Execute(new MoveCommand { ObjectId = "ace_spades", Position = new Vec3(0.3f, 0f, 0.25f) });
            var game = _table.Game;
            Assert.IsTrue(_table.Rewind(), "the host rewinds");
            yield return null;
            Assert.AreNotSame(game, _table.Game);
            Assert.AreSame(_table.Game, session.Host.Game, "the session hosts the rewound table (and sends it to everyone)");

            _table.HostsTable = false;
            Assert.IsFalse(_table.CanRewind, "a client follows the host's table");
            Assert.IsFalse(_table.Rewind());
            _table.HostsTable = true;
        }
    }
}
