using System.Collections;
using System.IO;
using System.Linq;
using Core.Engine;
using NUnit.Framework;
using TableWorld.View;
using TableWorld.View.Hud;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>
    /// The HUD can be read (M12): Avalon in both panel themes, on the screens players meet (before the start, the role and
    /// the ritual banner, the Music panel, eyes closed, the table menu, the Escape menu), passes <see cref="UiAudit"/>:
    /// every text at WCAG AA contrast against what is behind it, and clear of its panel's edges and frames.
    /// </summary>
    public class UiAuditTests
    {
        private TableView _table;
        private HudController _hud;
        private VisualElement _root;
        private string _language;

        private static string Avalon(string theme)
        {
            var path = Path.Combine(Application.dataPath, "..", "..", "games", "avalon.json");
            var json = File.ReadAllText(path);
            return theme == "parchment" ? json : json.Replace("\"theme\": \"parchment\"", "\"theme\": \"default\"").Replace("\"room\": \"tavern\"", "\"room\": \"gameNight\"");
        }

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            _language = Ui.Override;
            Ui.Override = "es";
            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            Object.FindAnyObjectByType<TableInteraction>().IgnoreDevices = true;
            _table = Object.FindAnyObjectByType<TableView>();
            _hud = Object.FindAnyObjectByType<HudController>();
            _root = _hud.GetComponent<UIDocument>().rootVisualElement;
        }

        [UnityTearDown]
        public IEnumerator Restore()
        {
            Ui.Override = _language;
            yield return null;
        }

        private IEnumerator Settle()
        {
            // The HUD refreshes the game panel four times a second; layout follows on the next frame.
            yield return new WaitForSeconds(0.6f);
        }

        private void Readable(string screen)
        {
            var problems = UiAudit.Check(_root);
            Assert.IsEmpty(problems, $"{screen}: {problems.Count} problems\n{UiAudit.Report(problems)}");
        }

        private IEnumerator Start(string theme)
        {
            Assert.IsTrue(_table.Load(Avalon(theme), Path.Combine(Application.dataPath, "..", "..", "games")));
            // The whole top bar and the tools' drawer open, then the bar folded to the turn and the players.
            _hud.FoldTop(false);
            _hud.OpenTools(true);
            yield return Settle();
            Readable($"{theme}, before the start, the bar and the tools open");
            _hud.FoldTop(true);
            yield return Settle();
            Readable($"{theme}, before the start, the bar folded");
            Assert.IsFalse(_root.Q<Label>("turnLabel").ClassListContains("hidden"));
            Assert.IsTrue(_root.Q("topMore").ClassListContains("hidden"));
            _hud.FoldTop(false);
            _hud.OpenTools(false);
            Assert.IsTrue(_root.Q("toolBar").ClassListContains("hidden"), "the closed drawer hides the tools");
            var start = new StartGameCommand { Language = "es" };
            start.Seats.AddRange(_table.Engine.State.Players.Seats.Take(5).Select(s => s.Id));
            Assert.IsTrue(_table.Execute(start).Ok);
            yield return Settle();
            Readable($"{theme}, the role and the ritual's first banner");
            // Folded, the turn's line stays one line: a little wider, its font smaller when it must.
            _hud.FoldTop(true);
            yield return Settle();
            var turn = _root.Q<Label>("turnLabel");
            Assert.That(turn.text, Is.Not.Empty);
            Assert.Less(turn.layout.height, 2f * _hud.TurnFit.Size, $"'{turn.text}' fits on one line folded");
            Assert.GreaterOrEqual(_hud.TurnFit.Size, TextFit.MinSize);
            Readable($"{theme}, the game started, the bar folded");
            _hud.FoldTop(false);
            yield return Settle();
        }

        /// <summary>A seated phase (Avalon's ritual) sits everyone in their chalker's eyes and keeps them there until it ends.</summary>
        [UnityTest]
        public IEnumerator TheRitual_SeatsYou_AndKeepsYouSeated_UntilItEnds()
        {
            Assert.IsTrue(_table.Load(Avalon("parchment"), Path.Combine(Application.dataPath, "..", "..", "games")));
            yield return Settle();
            var camera = Camera.main.GetComponent<TableCamera>();
            var start = new StartGameCommand { Language = "es" };
            start.Seats.AddRange(_table.Engine.State.Players.Seats.Take(5).Select(s => s.Id));
            // Lifted out before the start, as a player looking over the table would be.
            camera.Lift();
            yield return null;
            Assert.IsTrue(_table.Execute(start).Ok);
            yield return Settle();
            Assert.IsTrue(camera.IsSeated, "the ritual sits you down");
            Assert.IsTrue(camera.HoldSeated);
            camera.Lift();
            camera.ToggleSeated();
            camera.ResetView();
            yield return null;
            Assert.IsTrue(camera.IsSeated, "nothing lifts you out during the ritual");
            // Through the ritual to the team pick: free to lift again.
            var sawEyesOpen = false;
            for (var i = 0; i < 8 && _table.Engine.State.Session.Phase.StartsWith("ritual"); i++)
            {
                var session = _table.Engine.State.Session;
                // Eyes closed, a player turns to look at someone (Merlin at the minions).
                var closed = _table.Game.EyesClosedPhase;
                if (closed)
                    camera.TurnHead(60f, 10f);
                Assert.IsTrue(_table.Execute(new EndTimedPhaseCommand { Phase = session.Phase, Visit = session.PhaseVisit }).Ok);
                yield return Settle();
                Assert.IsTrue(camera.IsSeated || !_table.Engine.State.Session.Phase.StartsWith("ritual"));
                if (closed && !_table.Game.EyesClosedPhase)
                {
                    sawEyesOpen = true;
                    Assert.AreEqual(0f, camera.HeadYaw, 0.01f, "everyone's eyes open: the head is straight again");
                }
            }
            Assert.IsTrue(sawEyesOpen, "the ritual closes and opens everyone's eyes");
            Assert.AreEqual("propose", _table.Engine.State.Session.Phase);
            Assert.IsFalse(camera.HoldSeated);
            camera.Lift();
            Assert.IsFalse(camera.IsSeated, "after the ritual you may lift out");
        }

        [UnityTest]
        public IEnumerator Parchment_IsReadable_OnEveryScreen() => Screens("parchment");

        [UnityTest]
        public IEnumerator TheDefaultTheme_IsReadable_OnEveryScreen() => Screens("default");

        private IEnumerator Screens(string theme)
        {
            yield return Start(theme);

            _hud.Music.Toggle();
            yield return Settle();
            Readable($"{theme}, the Music panel");
            _hud.Music.Hide();

            _hud.Overlay.OpenMenu(null, new Vector2(Screen.width / 2f, Screen.height / 2f));
            yield return Settle();
            Readable($"{theme}, the table menu");
            _hud.Overlay.CloseMenu();

            _hud.Escape.Open();
            yield return Settle();
            Readable($"{theme}, the Escape menu");
            _hud.Escape.Close();

            // Eyes closed: the host's Next ends the role step.
            var session = _table.Engine.State.Session;
            Assert.IsTrue(_table.Execute(new EndTimedPhaseCommand { Phase = session.Phase, Visit = session.PhaseVisit }).Ok);
            yield return Settle();
            Readable($"{theme}, eyes closed");
        }
    }
}
