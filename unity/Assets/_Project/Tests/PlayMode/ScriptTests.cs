using System.Collections;
using System.Linq;
using Core.Net;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using TableWorld.View;
using TableWorld.View.Net;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>M5 in the game: a loaded table's Lua runs on this machine, hears drops, changes the table and talks in chat.</summary>
    public class ScriptTests
    {
        [UnityTest]
        public IEnumerator TableScripts_Run_HearDrops_UseTimers_AndTalk()
        {
            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            var table = Object.FindAnyObjectByType<TableView>();
            var interaction = Object.FindAnyObjectByType<TableInteraction>();
            interaction.IgnoreDevices = true;
            var session = Object.FindAnyObjectByType<NetworkTableSession>();
            yield return new WaitForSeconds(0.3f);

            var global = @"
function onLoad()
  broadcastToAll('Welcome to the scripted table')
  Wait.time(function() getObjectFromGUID('note').setName('Timer fired') end, 0.5)
end
function onObjectDrop(color, obj)
  broadcastToAll(color .. ' dropped ' .. obj.getName())
  obj.flip()
end";
            var json = new JObject
            {
                ["schemaVersion"] = "0.1",
                ["assets"] = JArray.Parse(@"[ { ""id"": ""sheet"", ""type"": ""image"", ""path"": ""builtin:standard_deck"", ""columns"": 10, ""rows"": 7 } ]"),
                ["objects"] = JArray.Parse(@"[
                  { ""id"": ""ace"", ""type"": ""Card"", ""name"": ""Ace"", ""faceAsset"": ""sheet"", ""backAsset"": ""sheet"", ""transform"": { ""position"": [0, 0, 0] } },
                  { ""id"": ""note"", ""type"": ""Notecard"", ""name"": ""Note"", ""transform"": { ""position"": [0.3, 0, 0] } },
                  { ""id"": ""die"", ""type"": ""Die"", ""script"": ""function onDrop(color) broadcastToAll('the die felt that') end"", ""transform"": { ""position"": [-0.2, 0, 0] } } ]"),
                ["players"] = JObject.Parse(@"{ ""seats"": [ { ""id"": ""south"", ""color"": ""red"" } ] }"),
                ["scripts"] = new JObject { ["global"] = global },
            };
            Assert.IsTrue(table.Load(json.ToString(), null));
            yield return null;
            Assert.AreEqual(2, session.Scripts.ScriptCount);
            Assert.IsTrue(session.Chat.Entries.Any(e => e.Text == "Welcome to the scripted table"), "onLoad talks in chat");

            Assert.IsTrue(interaction.Grab(table.ViewOf("ace"), moveContainer: false));
            yield return null;
            interaction.DragTo(new Vector3(0.1f, 0f, 0.1f));
            yield return new WaitForSeconds(0.2f);
            interaction.Release();
            yield return new WaitForSeconds(0.3f);
            Assert.IsTrue(session.Chat.Entries.Any(e => e.Text == "Red dropped Ace"), "onObjectDrop hears the drop");
            Assert.IsFalse(table.Engine.Find("ace").IsFaceUp, "and the script's flip happened");

            Assert.IsTrue(interaction.Grab(table.ViewOf("die"), moveContainer: false));
            yield return null;
            interaction.Release();
            yield return new WaitForSeconds(0.3f);
            Assert.IsTrue(session.Chat.Entries.Any(e => e.Text == "the die felt that"), "an object's own script hears its drop");

            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual("Timer fired", table.Engine.Find("note").Name, "Wait.time runs on the host's clock");
        }
    }
}
