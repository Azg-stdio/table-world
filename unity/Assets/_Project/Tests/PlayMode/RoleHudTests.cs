using System.Collections;
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
    /// T11.4 in the game window: before the start an optional role is a toggle beside Start, ticked ones join the deal,
    /// and once dealt the panel tells this player their role and what they know.
    /// </summary>
    public class RoleHudTests
    {
        private const string Game = @"{
  ""players"": { ""seats"": [ { ""id"": ""s1"", ""color"": ""red"" }, { ""id"": ""s2"", ""color"": ""blue"" }, { ""id"": ""s3"", ""color"": ""green"" },
                              { ""id"": ""s4"", ""color"": ""yellow"" }, { ""id"": ""s5"", ""color"": ""purple"" } ] },
  ""game"": { ""id"": ""mini_avalon"", ""name"": ""Mini Avalon"", ""minPlayers"": 5, ""maxPlayers"": 5 },
  ""roles"": {
    ""teams"": [ { ""id"": ""good"", ""name"": ""Loyal servants"" }, { ""id"": ""evil"", ""name"": ""Minions"" } ],
    ""list"": [
      { ""id"": ""merlin"", ""name"": ""Merlin"", ""team"": ""good"" },
      { ""id"": ""percival"", ""name"": ""Percival"", ""team"": ""good"", ""optional"": true, ""description"": ""Sees Merlin."" },
      { ""id"": ""servant"", ""name"": ""Servant"", ""team"": ""good"", ""count"": ""fill"" },
      { ""id"": ""assassin"", ""name"": ""Assassin"", ""team"": ""evil"" },
      { ""id"": ""minion"", ""name"": ""Minion"", ""team"": ""evil"", ""count"": ""fill"" }
    ],
    ""byPlayers"": { ""5"": { ""good"": 3, ""evil"": 2 } },
    ""knows"": [ { ""role"": ""merlin"", ""sees"": { ""team"": ""evil"" } }, { ""team"": ""evil"", ""sees"": { ""team"": ""evil"" } } ]
  },
  ""turn"": { ""phases"": [ ""play"" ] },
  ""phases"": [ { ""id"": ""play"", ""actions"": [ ""pass"" ], ""advanceWhen"": { ""currentPlayerEnds"": true } } ],
  ""actions"": [ { ""id"": ""pass"", ""actor"": ""current"", ""effects"": [ { ""endTurn"": {} } ] } ]
}";

        private TableView _table;
        private VisualElement _root;

        [UnitySetUp]
        public IEnumerator LoadTable()
        {
            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            Object.FindAnyObjectByType<TableInteraction>().IgnoreDevices = true;
            _table = Object.FindAnyObjectByType<TableView>();
            _root = Object.FindAnyObjectByType<HudController>().GetComponent<UIDocument>().rootVisualElement;
            Assert.IsTrue(_table.Load(Game, null));
            yield return new WaitForSeconds(0.4f);
        }

        private Button Toggle(string text) => _root.Q("gameActions").Query<Button>().ToList().FirstOrDefault(b => b.text == text);

        [UnityTest]
        public IEnumerator OptionalRoles_AreToggles_AndTheRoleLineTellsYourRole()
        {
            var percival = Toggle("Percival");
            Assert.IsNotNull(percival, "an optional role is a toggle before the start");
            Assert.IsFalse(percival.ClassListContains("hud-button--picked"));
            using (var click = NavigationSubmitEvent.GetPooled())
            {
                click.target = percival;
                percival.SendEvent(click);
            }
            // The game panel redraws four times a second.
            yield return new WaitForSeconds(0.4f);
            Assert.IsTrue(Toggle("Percival").ClassListContains("hud-button--picked"), "ticked");

            // Start as the toggles say (the HUD's Start needs five people sat; the command is the same).
            var result = _table.Execute(new StartGameCommand { Roles = { "percival" } });
            Assert.IsTrue(result.Ok, result.Error?.ToString());
            Assert.Contains("percival", _table.Game.Session.Roles.Values.ToList());
            yield return new WaitForSeconds(0.4f);

            var line = _root.Q<Label>("gameRole");
            Assert.IsFalse(line.ClassListContains("hidden"));
            var seat = _table.LocalSeat;
            var role = GameEngine.RoleName(_table.Game.State.Roles, _table.Game.Session.Roles[seat]);
            StringAssert.StartsWith($"You are {role} (", line.text);
            Assert.AreEqual(string.Join("\n", GameEngine.RoleLines(_table.Game.State, seat)), line.text);
            Assert.IsNull(Toggle("Percival"), "no toggles once the game runs");
        }

        /// <summary>T11.5: a vote shows a button per option, then how many have voted and this player's own vote.</summary>
        [UnityTest]
        public IEnumerator AVote_ShowsItsOptions_ThenWhoHasVoted()
        {
            const string vote = @"{
  ""players"": { ""seats"": [ { ""id"": ""s1"", ""color"": ""red"" }, { ""id"": ""s2"", ""color"": ""blue"" }, { ""id"": ""s3"", ""color"": ""green"" } ] },
  ""turn"": { ""phases"": [ ""vote"" ] },
  ""phases"": [ { ""id"": ""vote"", ""actions"": [ ""team"" ] } ],
  ""actions"": [ { ""id"": ""team"", ""name"": ""Team vote"", ""actor"": ""any"",
    ""vote"": { ""options"": [ ""yes"", ""no"" ], ""labels"": { ""yes"": ""approve"", ""no"": ""reject"" } } } ]
}";
            Assert.IsTrue(_table.Load(vote, null));
            Assert.IsTrue(_table.Execute(new StartGameCommand()).Ok);
            yield return new WaitForSeconds(0.4f);
            Assert.IsNotNull(Toggle("Team vote · approve"), "one button per option");
            Assert.IsNotNull(Toggle("Team vote · reject"));

            var seat = _table.LocalSeat;
            var cast = _table.Execute(new PerformActionCommand { Actor = seat, ActionId = "team", Params = { [GameEngine.VoteParam] = "yes" } });
            Assert.IsTrue(cast.Ok, cast.Error?.ToString());
            yield return new WaitForSeconds(0.4f);
            var line = _root.Q<Label>("gameRole");
            Assert.IsFalse(line.ClassListContains("hidden"));
            Assert.AreEqual("Team vote: 1 of 3 voted (you: approve)", line.text);
        }

        /// <summary>A vote with a time limit shows the time left and is closed by the host when it runs out.</summary>
        [UnityTest]
        public IEnumerator ATimedVote_ClosesWhenItsTimeIsUp()
        {
            const string vote = @"{
  ""players"": { ""seats"": [ { ""id"": ""s1"", ""color"": ""red"" }, { ""id"": ""s2"", ""color"": ""blue"" }, { ""id"": ""s3"", ""color"": ""green"" } ] },
  ""turn"": { ""phases"": [ ""vote"" ] },
  ""phases"": [ { ""id"": ""vote"", ""actions"": [ ""team"" ] } ],
  ""actions"": [ { ""id"": ""team"", ""name"": ""Team vote"", ""actor"": ""any"", ""vote"": { ""options"": [ ""yes"", ""no"" ], ""seconds"": 2 } } ]
}";
            Assert.IsTrue(_table.Load(vote, null));
            Assert.IsTrue(_table.Execute(new StartGameCommand()).Ok);
            Assert.IsTrue(_table.Execute(new PerformActionCommand { Actor = _table.LocalSeat, ActionId = "team", Params = { [GameEngine.VoteParam] = "no" } }).Ok);
            yield return new WaitForSeconds(0.6f);
            StringAssert.Contains("s left", _root.Q<Label>("gameRole").text);
            yield return new WaitForSeconds(2.2f);
            Assert.IsTrue(_table.Game.Session.Votes.ContainsKey("team"), "counted when the time ran out");
            Assert.AreEqual("no", _table.Game.Session.Votes["team"].Result);
        }
    }
}
