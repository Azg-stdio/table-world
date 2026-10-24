using System.Linq;
using Core.Engine;
using Core.Rules;
using Core.Scripting;
using Core.Tests.Engine;

namespace Core.Tests.Scripting
{
    /// <summary>M5 (T6.3): script buttons, TTS createButton / editButton / clearButtons and their clicks.</summary>
    public class ScriptButtonTests
    {
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
            host.Load(0);
            return (engine, host, said);
        }

        private static void Click(TableEngine engine, ScriptHost host, string id, int index, string color, bool alt = false)
        {
            var click = new ClickButtonCommand { ObjectId = id, Index = index, Alt = alt };
            host.OnCommand(click, engine.Execute(click), color);
        }

        [Fact]
        public void CreateButton_PutsItOnTheObject_InMetres()
        {
            var (engine, _, _) = Load(@"
                getObjectFromGUID('d6').createButton({ label = 'Roll', click_function = 'go', position = { 0, 1, 2 },
                    width = 1000, height = 500, font_size = 250, color = { 1, 0, 0 }, font_color = 'Blue', tooltip = 'roll it' })");
            var b = engine.Find("d6")!.Buttons.Single();
            Assert.Equal("Roll", b.Label);
            Assert.Equal("go", b.Click);
            Assert.Null(b.Owner);
            Assert.Equal(0.03f, b.Position.Y, 4);
            Assert.Equal(0.06f, b.Position.Z, 4);
            Assert.Equal(0.06f, b.Width, 4);
            Assert.Equal(0.03f, b.Height, 4);
            Assert.Equal(0.015f, b.FontSize, 4);
            Assert.Equal(new[] { 1f, 0f, 0f }, b.Color);
            Assert.Equal(1f, b.FontColor![2]);
            Assert.Equal("roll it", b.Tooltip);
        }

        [Fact]
        public void Click_CallsTheFunction_WithObjectColourAndAlt()
        {
            var (engine, host, said) = Load(@"
                function go(obj, color, alt) broadcastToAll(obj.getGUID() .. ' ' .. color .. ' ' .. tostring(alt)) end
                function onLoad() getObjectFromGUID('d6').createButton({ label = 'Go', click_function = 'go' }) end");
            Click(engine, host, "d6", 0, "red", alt: true);
            Assert.Equal("d6 Red true", said.Single());
        }

        [Fact]
        public void Click_RunsInTheOwnersScript()
        {
            var engine = EngineFixture.Create();
            engine.Find("d6")!.Script = "function mine(obj, color) broadcastToAll('own ' .. color) end";
            var (_, host, said) = Load("function onLoad() local d = getObjectFromGUID('d6') d.createButton({ click_function = 'mine', function_owner = d, label = 'x' }) end", engine);
            Click(engine, host, "d6", 0, "blue");
            Assert.Equal("own Blue", said.Single());
        }

        [Fact]
        public void EditRemoveClearAndGet_Work()
        {
            var (engine, _, said) = Load(@"
                local d = getObjectFromGUID('d6')
                d.createButton({ label = 'A', click_function = 'a' })
                d:createButton({ label = 'B', click_function = 'b', width = 400 })
                d.editButton({ index = 1, label = 'B2' })
                local list = d.getButtons()
                broadcastToAll(#list .. ' ' .. list[2].label .. ' ' .. list[2].width .. ' ' .. list[2].index .. ' ' .. list[2].click_function)
                d.removeButton(0)
                broadcastToAll(d.getButtons()[1].label)
                d.clearButtons()
                broadcastToAll(tostring(d.getButtons()))");
            Assert.Equal(new[] { "2 B2 400 1 b", "B2", "nil" }, said);
            Assert.Empty(engine.Find("d6")!.Buttons);
        }

        [Fact]
        public void Load_ClearsSavedButtons_BeforeOnLoadMakesThemAgain()
        {
            var engine = EngineFixture.Create();
            engine.Find("d6")!.Buttons.Add(new ButtonDef { Label = "old" });
            Load("function onLoad() getObjectFromGUID('d6').createButton({ label = 'new' }) end", engine);
            Assert.Equal("new", engine.Find("d6")!.Buttons.Single().Label);
        }

        [Fact]
        public void Buttons_SurviveTheTableJson()
        {
            var engine = EngineFixture.Create();
            engine.Find("d6")!.Buttons.Add(new ButtonDef { Label = "Go", Click = "go", Width = 0.1f, Color = new[] { 0f, 1f, 0f } });
            var parsed = TableJson.Parse(TableJson.Serialize(engine.State));
            Assert.True(parsed.Ok, string.Join("; ", parsed.Errors));
            var b = parsed.State!.Objects.Single(o => o.Id == "d6").Buttons.Single();
            Assert.Equal("go", b.Click);
            Assert.Equal(0.1f, b.Width);
            Assert.Equal(ButtonDef.DefaultSize, b.Height);
        }

        [Fact]
        public void Inputs_AreMade_Typed_AndAnsweredByTheirFunction()
        {
            var (engine, host, said) = Load(@"
                function typed(obj, color, value, selected)
                  broadcastToAll(obj.getGUID() .. ' ' .. color .. ' ' .. value .. ' ' .. tostring(selected))
                  if not selected then return string.upper(value) end
                end
                function onLoad()
                  local d = getObjectFromGUID('d6')
                  d.createInput({ input_function = 'typed', label = 'Name', value = 'x', width = 800, validation = 1, alignment = 3 })
                  d.editInput({ index = 0, value = 'start' })
                  local i = d.getInputs()[1]
                  broadcastToAll(i.value .. ' ' .. i.label .. ' ' .. i.width .. ' ' .. i.input_function .. ' ' .. i.alignment)
                end");
            Assert.Equal("start Name 800 typed 3", said.Single());
            void Type(string value, bool selected)
            {
                var c = new TypeInputCommand { ObjectId = "d6", Index = 0, Value = value, Selected = selected };
                host.OnCommand(c, engine.Execute(c), "red");
            }
            Type("ana", true);
            Assert.Equal("ana", engine.Find("d6")!.Inputs[0].Value);
            Type("ana", false);
            Assert.Equal(new[] { "d6 Red ana true", "d6 Red ana false" }, said.Skip(1));
            Assert.Equal("ANA", engine.Find("d6")!.Inputs[0].Value);
            Assert.False(engine.Execute(new TypeInputCommand { ObjectId = "d6", Index = 3, Value = "x" }).Ok);
        }

        [Fact]
        public void Engine_RefusesBadButtons_AndClicksOnMissingOnes()
        {
            var engine = EngineFixture.Create();
            Assert.False(engine.Execute(new SetButtonsCommand { ObjectId = "d6", Buttons = { new ButtonDef { Width = 5f } } }).Ok);
            Assert.False(engine.Execute(new SetButtonsCommand { ObjectId = "d6", Buttons = { new ButtonDef { Color = new[] { 2f, 0f, 0f } } } }).Ok);
            Assert.False(engine.Execute(new ClickButtonCommand { ObjectId = "d6", Index = 0 }).Ok);
            Assert.True(engine.Execute(new SetButtonsCommand { ObjectId = "d6", Buttons = { new ButtonDef() } }).Ok);
            Assert.True(engine.Execute(new ClickButtonCommand { ObjectId = "d6", Index = 0 }).Ok);
        }
    }
}
