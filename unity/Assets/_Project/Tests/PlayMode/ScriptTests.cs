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
using UnityEngine.UIElements;

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
        public IEnumerator ScriptUi_IsDrawn_LaidOut_Clicked_AndHidden()
        {
            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            var table = Object.FindAnyObjectByType<TableView>();
            Object.FindAnyObjectByType<TableInteraction>().IgnoreDevices = true;
            yield return new WaitForSeconds(0.3f);

            var global = @"
n = 0
function onLoad()
  UI.setXml([[
<Defaults><Button color=""#3060C0"" textColor=""#FFFFFF"" fontSize=""22""/></Defaults>
<Panel id=""bar"" color=""#202020"" width=""300"" height=""80"" rectAlignment=""UpperLeft"" offsetXY=""20 -20"">
  <HorizontalLayout spacing=""10"" padding=""10 10 10 10"">
    <Button id=""go"" onClick=""go"">Go</Button>
    <Text id=""count"" fontSize=""24"" color=""#FFFFFF"" alignment=""MiddleCenter"">0</Text>
  </HorizontalLayout>
</Panel>]])
end
function go(player, value, id)
  n = n + 1
  UI.setValue('count', tostring(n))
  broadcastToAll(player.color .. ' ' .. value .. ' ' .. id)
end";
            var json = new JObject
            {
                ["schemaVersion"] = "0.1",
                ["players"] = JObject.Parse(@"{ ""seats"": [ { ""id"": ""south"", ""color"": ""red"" } ] }"),
                ["scripts"] = new JObject { ["global"] = global },
            };
            Assert.IsTrue(table.Load(json.ToString(), null));
            yield return new WaitForSeconds(0.2f);
            var view = Object.FindAnyObjectByType<TableWorld.View.ScriptUi.ScriptUiView>();
            Assert.IsNotNull(view, "the HUD made the script UI view");
            var bar = view.Root.Q("bar");
            Assert.IsNotNull(bar);
            Assert.AreEqual(300f, bar.layout.width, 1.5f, $"bar {bar.layout} root {view.Root.layout}");
            Assert.AreEqual(20f, bar.layout.x, 1.5f, "offsetXY moves it right");
            Assert.AreEqual(20f, bar.layout.y, 1.5f, "and down from the top (y is up in XmlUI)");
            var go = view.Root.Q("go");
            var count = view.Root.Q<Label>("count");
            Assert.AreEqual("0", count.text);
            Assert.AreEqual(135f, go.layout.width, 1.5f, "two children share the row: (300 - 20 padding - 10 spacing) / 2");
            Assert.IsNotNull(view.Root.panel.Pick(go.worldBound.center), "the button catches the pointer");

            using (var click = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0, mousePosition = go.worldBound.center }))
                go.SendEvent(click);
            yield return null;
            yield return null;
            var session = Object.FindAnyObjectByType<NetworkTableSession>();
            Assert.IsTrue(session.Chat.Entries.Any(e => e.Text == "Red -1 go"), "the click ran go(player, value, id)");
            Assert.AreEqual("1", view.Root.Q<Label>("count").text, "and its setValue redrew the text");

            table.Execute(new Core.Engine.SetUiAttributesCommand { ElementId = "bar", Attributes = { ["visibility"] = "Blue" } });
            yield return null;
            Assert.IsNull(view.Root.Q("bar"), "visible to Blue only: red does not see it");
            table.Execute(new Core.Engine.SetUiAttributesCommand { ElementId = "bar", Attributes = { ["visibility"] = "Red|Blue", ["active"] = "False" } });
            yield return null;
            Assert.IsNull(view.Root.Q("bar"), "inactive");
            table.Execute(new Core.Engine.SetUiAttributesCommand { ElementId = "bar", Attributes = { ["active"] = "True" } });
            yield return null;
            Assert.IsNotNull(view.Root.Q("bar"), "shown again for red");
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
