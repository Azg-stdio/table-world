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

        [UnityTest]
        public IEnumerator ScriptButtons_AreDrawn_HitByThePointer_AndClicked()
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
  getObjectFromGUID('board').createButton({ label = 'Deal', click_function = 'deal', position = { 0, 0.2, 0 },
    width = 1200, height = 500, font_size = 300, color = { 0.2, 0.5, 0.9 }, font_color = { 1, 1, 1 } })
  getObjectFromGUID('board').createButton({ label = 'Score: 0', click_function = '', position = { 0, 0.2, 3 }, width = 0, height = 0, font_size = 300 })
end
function deal(obj, color, alt)
  broadcastToAll(color .. ' pressed ' .. obj.getName() .. (alt and ' (alt)' or ''))
  obj.editButton({ index = 1, label = 'Score: 1' })
end";
            var json = new JObject
            {
                ["schemaVersion"] = "0.1",
                ["objects"] = JArray.Parse(@"[ { ""id"": ""board"", ""type"": ""Board"", ""name"": ""Board"", ""locked"": true, ""transform"": { ""position"": [0, 0, 0] } } ]"),
                ["players"] = JObject.Parse(@"{ ""seats"": [ { ""id"": ""south"", ""color"": ""red"" } ] }"),
                ["scripts"] = new JObject { ["global"] = global },
            };
            Assert.IsTrue(table.Load(json.ToString(), null));
            yield return null;
            var board = table.ViewOf("board");
            var buttons = board.GetComponentsInChildren<ScriptButtonView>();
            Assert.AreEqual(2, buttons.Length, "both buttons are drawn");
            Assert.IsNotNull(buttons[0].GetComponent<BoxCollider>(), "a sized button can be clicked");
            Assert.IsNull(buttons[1].GetComponent<BoxCollider>(), "a 0-sized one is a label");

            // The pointer ray (triggers included) finds the button above the board.
            var above = buttons[0].transform.position + Vector3.up * 0.5f;
            var hits = Physics.RaycastAll(above, Vector3.down, 1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
            Assert.IsTrue(hits.Any(h => h.collider.GetComponent<ScriptButtonView>() == buttons[0]));

            interaction.ClickButton(buttons[0], alt: false);
            yield return null;
            // The edit rebuilt the buttons: the old views are gone.
            interaction.ClickButton(board.GetComponentsInChildren<ScriptButtonView>()[0], alt: true);
            yield return null;
            Assert.IsTrue(session.Chat.Entries.Any(e => e.Text == "Red pressed Board"), "the click runs the function with the seat colour");
            Assert.IsTrue(session.Chat.Entries.Any(e => e.Text == "Red pressed Board (alt)"), "a right click is the alt click");
            Assert.AreEqual("Score: 1", table.Engine.Find("board").Buttons[1].Label);
            Assert.AreEqual(2, board.GetComponentsInChildren<ScriptButtonView>().Length, "edited buttons are redrawn, not added");
        }
    }
}
