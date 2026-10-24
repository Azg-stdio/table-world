using System.Linq;
using Core.Engine;
using Core.Rules;
using Core.Scripting;
using Core.Tests.Engine;
using Core.Tts;

namespace Core.Tests.Scripting
{
    /// <summary>M5 (T6.3), ADR 0004: XmlUI read into our UI tree, TTS <c>UI.*</c>, <c>Player</c>, and UI events.</summary>
    public class ScriptUiTests
    {
        private const string Sample = @"<!-- comment -->
<Defaults>
   <Button color=""#828282""/>
   <Button class=""ini"" color=""rgba(0, 0, 0, 0.95)"" fontSize=""16""/>
   <Image preserveAspect=""true""/>
</Defaults>
<Button id=""showButtons"" onClick=""showButtons"" width=""23"" text=""&#x25B2;"" rectAlignment=""UpperRight""></Button>
<VerticalLayout id=""buttons"" padding=""0 0 0 20"">
   <Button class=""ini"" onClick=""hits(sub)""><Image image=""BI Drawer""></Image></Button>
   <Text id=""title"" active=""false"" minheight=""30"">Battle  1</Text>
</VerticalLayout>";

        private static (TableEngine Engine, ScriptHost Host, System.Collections.Generic.List<string> Said) Load(string global, TableEngine? engine = null)
        {
            engine ??= EngineFixture.Create();
            engine.State.Scripts[ScriptHost.GlobalName] = global;
            var said = new System.Collections.Generic.List<string>();
            ScriptHost? host = null;
            host = new ScriptHost(() => engine, c =>
            {
                var r = engine.Execute(c);
                host!.OnCommand(c, r, null);
                return r;
            });
            host.Broadcast += said.Add;
            host.Log += said.Add;
            host.Load(0);
            return (engine, host, said);
        }

        [Fact]
        public void XmlUi_AppliesDefaultsAndClasses_AndCanonicalNames()
        {
            var nodes = XmlUi.Parse(Sample);
            Assert.Equal(new[] { "Button", "VerticalLayout" }, nodes.Select(n => n.Tag));
            Assert.Equal("#828282", nodes[0].Get("color"));
            Assert.Equal("▲", nodes[0].Get("text"));
            var ini = nodes[1].Children[0];
            Assert.Equal("rgba(0, 0, 0, 0.95)", ini.Get("color"));
            Assert.Equal("16", ini.Get("fontSize"));
            Assert.Equal("True", ini.Children[0].Get("preserveAspect"));
            var title = nodes[1].Children[1];
            Assert.Equal("Battle  1", title.Value);
            Assert.Equal("False", title.Get("active"));
            Assert.Equal("30", title.Get("minHeight"));
            Assert.Equal(title, UiNode.Find(nodes, "title"));
            var again = XmlUi.Parse(XmlUi.Write(nodes));
            Assert.Equal(XmlUi.Write(nodes), XmlUi.Write(again));
        }

        [Fact]
        public void Attributes_AreSetAndRead_LikeTts()
        {
            var engine = EngineFixture.Create();
            engine.State.ScriptUi = XmlUi.Parse(Sample);
            var (_, _, said) = Load(@"
                UI.setAttribute('title', 'active', true)
                broadcastToAll(UI.getAttribute('title', 'active') .. ' ' .. UI.getAttribute('title', 'text') .. ' ' .. UI.getValue('title'))
                UI.setAttributes('showButtons', { width = 40, textColor = '#FFFFFF' })
                UI.setValue('title', 'Battle 2')
                UI.hide('buttons')
                broadcastToAll(UI.getAttribute('buttons', 'active') .. ' ' .. UI.getAttribute('showButtons', 'width') .. ' ' .. UI.getValue('title'))
                broadcastToAll(tostring(UI.getAttribute('nope', 'active')))", engine);
            Assert.Equal(new[] { "True Battle  1 Battle  1", "False 40 Battle 2", "nil" }, said);
            Assert.Equal("#FFFFFF", UiNode.Find(engine.State.ScriptUi, "showButtons")!.Get("textColor"));
        }

        [Fact]
        public void SetXml_ReplacesTheUi_AndGetXmlTableRoundTrips()
        {
            var (engine, _, said) = Load(@"
                UI.setXml('<Panel id=""p""><Text id=""t"">Hi</Text></Panel>')
                local t = UI.getXmlTable()
                broadcastToAll(t[1].tag .. ' ' .. t[1].attributes.id .. ' ' .. t[1].children[1].value)
                t[1].children[1].value = 'Bye'
                UI.setXmlTable(t)
                broadcastToAll(UI.getValue('t'))
                broadcastToAll(tostring(UI.setXml('<Panel>')))");
            Assert.Equal("Panel p Hi", said[0]);
            Assert.Equal("Bye", said[1]);
            Assert.Contains(said, s => s.Contains("UI.setXml"));
            Assert.Equal("false", said.Last());
            Assert.Equal("t", engine.State.ScriptUi.Single().Children.Single().Id);
        }

        [Fact]
        public void ObjectUi_IsItsOwn()
        {
            var (engine, _, said) = Load(@"
                local d = getObjectFromGUID('d6')
                d.UI.setXml('<Text id=""n"">3</Text>')
                broadcastToAll(d.UI.getValue('n') .. ' ' .. tostring(UI.getValue('n')))");
            Assert.Equal("3 nil", said.Single());
            Assert.Single(engine.Find("d6")!.ScriptUi);
            Assert.Empty(engine.State.ScriptUi);
        }

        [Fact]
        public void Clicks_RunTheHandler_WithPlayerValueAndId()
        {
            var engine = EngineFixture.Create();
            engine.State.ScriptUi = XmlUi.Parse(Sample);
            var (_, host, said) = Load(@"
                function showButtons(player, value, id) broadcastToAll(player.color .. ' ' .. value .. ' ' .. id) end
                function hits(player, value, id) broadcastToAll('hits ' .. value .. ' ' .. tostring(id) .. ' ' .. player.steam_name) end", engine);
            host.PlayerName = color => color == "red" ? "Ana" : null;
            void Use(params int[] path)
            {
                var c = new UiEventCommand { Path = path.ToList() };
                host.OnCommand(c, engine.Execute(c), "red");
            }
            Use(0);
            Use(1, 0);
            Assert.Equal(new[] { "Red -1 showButtons", "hits sub nil Ana" }, said);
            Assert.False(engine.Execute(new UiEventCommand { Path = { 1 } }).Ok, "the layout has no onClick");
            Assert.False(engine.Execute(new UiEventCommand { Path = { 5 } }).Ok);
        }

        [Fact]
        public void ObjectUiHandlers_DefaultToTheObjectsScript_OrNamedOwner()
        {
            var engine = EngineFixture.Create();
            engine.Find("d6")!.Script = "function mine(player, value) broadcastToAll('own ' .. value) end";
            engine.Find("d6")!.ScriptUi = XmlUi.Parse(@"<Button onClick=""mine"" /><InputField onEndEdit=""Global/typed"" />");
            var (_, host, said) = Load("function typed(player, value, id) broadcastToAll('global ' .. value) end", engine);
            var click = new UiEventCommand { ObjectId = "d6", Path = { 0 }, Value = "-2" };
            host.OnCommand(click, engine.Execute(click), "blue");
            var edit = new UiEventCommand { ObjectId = "d6", Path = { 1 }, Event = "onEndEdit", Value = "Gloom" };
            host.OnCommand(edit, engine.Execute(edit), "blue");
            Assert.Equal(new[] { "own -2", "global Gloom" }, said);
        }

        [Fact]
        public void Player_TablesAnswerByColour()
        {
            var (_, _, said) = Load(@"
                local p = Player['Red']
                broadcastToAll(p.color .. ' ' .. tostring(p.seated) .. ' ' .. #Player.getPlayers() .. ' ' .. Player.Blue.color)
                p.broadcast('hello')
                p.lookAt({ position = { 0, 0, 0 } })");
            Assert.StartsWith("Red ", said[0]);
            Assert.Equal("hello", said[1]);
        }

        [Fact]
        public void Ui_SurvivesTheTableJson_AndBadUiIsRefused()
        {
            var engine = EngineFixture.Create();
            engine.State.ScriptUi = XmlUi.Parse(Sample);
            var parsed = TableJson.Parse(TableJson.Serialize(engine.State));
            Assert.True(parsed.Ok, string.Join("; ", parsed.Errors));
            Assert.Equal(XmlUi.Write(engine.State.ScriptUi), XmlUi.Write(parsed.State!.ScriptUi));
            Assert.False(engine.Execute(new SetScriptUiCommand { Nodes = { new UiNode { Tag = "" } } }).Ok);
            Assert.False(engine.Execute(new SetUiAttributesCommand { ElementId = "missing", Attributes = { ["active"] = "False" } }).Ok);
        }
    }
}
