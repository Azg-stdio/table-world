using System.Collections;
using Core.Engine;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using TableWorld.View;
using TableWorld.View.Hud;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>
    /// T11.7 in the game window: while the phase keeps this player's eyes closed, the table is dark behind the phase's
    /// words; once they are awake it is back.
    /// </summary>
    public class NightHudTests
    {
        private const string Game = @"{
  ""players"": { ""seats"": [ { ""id"": ""s1"", ""color"": ""red"" }, { ""id"": ""s2"", ""color"": ""blue"" } ] },
  ""variables"": { ""awake"": [] },
  ""turn"": { ""phases"": [ ""night"" ] },
  ""phases"": [ { ""id"": ""night"", ""awake"": ""$var.awake"", ""sleepText"": ""Night falls. Eyes closed."", ""actions"": [ ""look"" ] } ],
  ""actions"": [ { ""id"": ""look"", ""actor"": ""any"", ""effects"": [ { ""message"": { ""text"": ""{$actor.name} looks around."" } } ] } ]
}";

        [UnityTest]
        public IEnumerator Asleep_TheTableIsDark_AwakeItIsBack()
        {
            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            Object.FindAnyObjectByType<TableInteraction>().IgnoreDevices = true;
            var table = Object.FindAnyObjectByType<TableView>();
            var root = Object.FindAnyObjectByType<HudController>().GetComponent<UIDocument>().rootVisualElement;
            Assert.IsTrue(table.Load(Game, null));
            Assert.IsTrue(table.Execute(new StartGameCommand()).Ok);
            yield return new WaitForSeconds(0.4f);

            var overlay = root.Q("sleepOverlay");
            Assert.IsFalse(overlay.ClassListContains("hidden"), "eyes closed: the table is dark");
            Assert.AreEqual("Night falls. Eyes closed.", root.Q<Label>("sleepText").text);
            var look = table.Execute(new PerformActionCommand { Actor = table.LocalSeat, ActionId = "look" });
            Assert.IsFalse(look.Ok, "a sleeper cannot act");

            var wake = table.Execute(new ScriptRulesCommand { Op = "setVariable", Name = "awake", Value = new JArray(table.LocalSeat) });
            Assert.IsTrue(wake.Ok, wake.Error?.ToString());
            yield return new WaitForSeconds(0.4f);
            Assert.IsTrue(overlay.ClassListContains("hidden"), "awake: the table is back");
        }
    }
}
