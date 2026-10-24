using System.Linq;
using Core.Engine;
using Core.Rules;
using Core.Scripting;
using Core.Tests.Engine;

namespace Core.Tests.Scripting
{
    /// <summary>M5 (T6.3): more of the TTS calls the local mods make: notebook, states, scripting zones, logging.</summary>
    public class ScriptCompatTests
    {
        private static (TableEngine Engine, ScriptHost Host, System.Collections.Generic.List<string> Said) Load(string global, System.Action<TableEngine>? prepare = null)
        {
            var engine = EngineFixture.Create();
            prepare?.Invoke(engine);
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

        [Fact]
        public void Notebook_IsReadAndWritten()
        {
            var (engine, _, said) = Load(@"
                local i = addNotebookTab({ title = 'Rules', body = 'Draw two.' })
                addNotebookTab({ title = 'Red notes', body = 'secret', color = 'Red' })
                editNotebookTab({ index = i, body = 'Draw three.' })
                local tabs = getNotebookTabs()
                broadcastToAll(#tabs .. ' ' .. tabs[1].title .. ' ' .. tabs[1].body .. ' ' .. tabs[1].color .. ' ' .. tabs[2].color)
                removeNotebookTab(1)");
            Assert.Equal("2 Rules Draw three. Grey Red", said.Single());
            Assert.Equal("Rules", engine.State.Notebook.Single().Title);
        }

        [Fact]
        public void States_Switch_AndAreListed()
        {
            var parsed = TableJson.Parse(@"{ ""schemaVersion"": ""0.1"", ""objects"": [
                { ""id"": ""coin"", ""type"": ""Token"", ""name"": ""Heads"", ""states"": { ""2"": { ""id"": ""coin_tails"", ""type"": ""Token"", ""name"": ""Tails"" } } } ],
                ""scripts"": { ""global"": ""local coin = getObjectFromGUID('coin') broadcastToAll(#coin.getStates() .. ' other state') local tails = coin.setState(2) broadcastToAll(tails.getName())"" } }");
            Assert.True(parsed.Ok, string.Join("; ", parsed.Errors));
            var engine = new TableEngine(parsed.State!, 1);
            var said = new System.Collections.Generic.List<string>();
            var host = new ScriptHost(() => engine, c => engine.Execute(c));
            host.Broadcast += said.Add;
            host.Load(0);
            Assert.Equal("1 other state", said[0]);
            Assert.Equal("Tails", said[1]);
        }

        [Fact]
        public void ScriptingZones_AlsoRaiseTheirOldEventNames()
        {
            var engine = EngineFixture.Create();
            engine.State.Zones.Add(new ScriptingZone { Id = "pool", Size = new Vec3(0.2f, 0.2f, 0.2f), Transform = new TableTransform { Position = new Vec3(0.6f, 0f, 0f) } });
            engine.State.Scripts[ScriptHost.GlobalName] = "function onObjectEnterScriptingZone(zone, obj) broadcastToAll('in ' .. zone.getGUID()) end";
            var said = new System.Collections.Generic.List<string>();
            ScriptHost? host = null;
            host = new ScriptHost(() => engine, c => { var r = engine.Execute(c); host!.OnCommand(c, r, null); return r; });
            host.Broadcast += said.Add;
            host.Load(0);
            var move = new MoveCommand { ObjectId = "d6", Position = new Vec3(0.6f, 0f, 0f) };
            host.OnCommand(move, engine.Execute(move), null);
            Assert.Equal("in pool", said.Single());
        }

        [Fact]
        public void Tags_AreAddedRemovedAndQueried()
        {
            var (engine, _, said) = Load(@"
                local d = getObjectFromGUID('d6')
                d.addTag('dice') d.addTag('red') d.addTag('dice')
                broadcastToAll(#getObjectsWithAllTags({ 'dice', 'red' }) .. ' ' .. #getObjectsWithAnyTags({ 'red', 'none' }) .. ' ' .. tostring(d.hasTag('red')))
                d.removeTag('red')
                broadcastToAll(#getObjectsWithAllTags({ 'dice', 'red' }) .. ' ' .. #d.getTags())");
            Assert.Equal(new[] { "1 1 true", "0 1" }, said);
            Assert.Equal(new[] { "dice" }, engine.Find("d6")!.Tags);
        }

        [Fact]
        public void CloneDealStateIdDestroyedAndInvisible_Work()
        {
            var (engine, _, said) = Load(@"
                local d = getObjectFromGUID('d6')
                local copy = d.clone({ position = { 5, 1, 0 } })
                broadcastToAll(tostring(copy ~= nil and copy.getGUID() ~= 'd6') .. ' ' .. tostring(copy.isDestroyed()))
                copy.destruct()
                broadcastToAll(tostring(copy.isDestroyed()) .. ' ' .. d.getStateId())
                getObjectFromGUID('deck').deal(2, 'Red')
                d.setInvisibleTo({ 'Blue' })
                broadcastToAll(d.getInvisibleTo()[1])");
            Assert.Equal("true false", said[0]);
            Assert.Equal("true -1", said[1]);
            Assert.Equal("Blue", said[2]);
            Assert.Equal(new[] { "blue" }, engine.Find("d6")!.InvisibleTo);
            Assert.Equal(8, ((Deck)engine.Find("deck")!).Cards.Count);
        }

        [Fact]
        public void TimerCreate_CallsByName_WithParameters_AndDestroyStops()
        {
            var (_, host, said) = Load(@"
                function tick(p) broadcastToAll('tick ' .. p.n) end
                Timer.create({ identifier = 'a', function_name = 'tick', parameters = { n = 7 }, delay = 1, repetitions = 0 })
                broadcastToAll(tostring(Timer.create({ identifier = 'a', function_name = 'tick', delay = 1 })))");
            host.Tick(1.01);
            host.Tick(2.02);
            Assert.Equal(new[] { "false", "tick 7", "tick 7" }, said);
        }

        [Fact]
        public void SpawnObject_MakesSupportedPieces_AndCallsBackNextFrame()
        {
            var (engine, host, said) = Load(@"
                local d = spawnObject({ type = 'Die_20', position = { 3, 2, 0 }, callback_function = function(obj) broadcastToAll('ready ' .. obj.getGUID()) end })
                local p = spawnObject({ type = 'PlayerPawn' })
                local x = spawnObject({ type = 'Custom_Assetbundle' })
                broadcastToAll(d.getGUID() .. ' ' .. p.type .. ' ' .. tostring(x))");
            var die = engine.State.Objects.OfType<Die>().Single(o => o.Faces == 20);
            Assert.Equal(die.Id + " Model nil", said.Single());
            Assert.Equal(0.09f, die.Transform.Position.X, 4);
            Assert.Contains(engine.State.Assets, a => a.Path == "builtin:pawn");
            host.Tick(0.02);
            Assert.Equal("ready " + die.Id, said.Last());
            Assert.False(engine.Execute(new SpawnCommand { ObjectJson = "{ \"type\": \"Die\", \"faces\": 7 }" }).Ok, "checked like a loaded table");
        }

        [Fact]
        public void ContainerEvents_AndChat_ReachScripts()
        {
            var (engine, host, said) = Load(@"
                function onObjectLeaveContainer(bag, obj) broadcastToAll('out ' .. bag.getGUID() .. ' ' .. obj.getGUID()) end
                function onObjectEnterContainer(bag, obj) broadcastToAll('in ' .. bag.getGUID() .. ' ' .. obj.getGUID()) end
                function onChat(message, player) broadcastToAll(player.color .. ' said ' .. message) return message ~= 'secret' end
                function onLoad()
                  getObjectFromGUID('bag').takeObject({ guid = 't1', position = { 5, 2, 0 } })
                  getObjectFromGUID('bag').putObject(getObjectFromGUID('t1'))
                end");
            Assert.Equal(new[] { "out bag t1", "in bag t1" }, said);
            Assert.True(host.OnChat("hello", "red"));
            Assert.False(host.OnChat("secret", "red"));
            Assert.Equal("Red said secret", said.Last());
        }

        [Fact]
        public void VectorAndColor_WorkLikeTts_AndTheApiReturnsThem()
        {
            var (_, _, said) = Load(@"
                local v = Vector(1, 2, 3) + Vector({ 1, 1, 1 })
                local p = getObjectFromGUID('d6').getPosition() + Vector(0, 1, 0)
                local d = Vector(3, 0, 4)
                broadcastToAll(v.x .. ' ' .. v[3] .. ' ' .. d:magnitude() .. ' ' .. tostring(v * 2) .. ' ' .. p.y .. ' ' .. p:copy():scale(2).y)
                local c = Color.Red
                broadcastToAll(c:toString() .. ' ' .. Color(1, 0, 0):toHex() .. ' ' .. tostring(Color.fromHex('#00ff00'):equals({ 0, 1, 0 })))
                broadcastToAll(tostring(getObjectFromGUID('d6').getColorTint():equals(Color.White)) .. ' ' .. MusicPlayer.player_status)
                getObjectFromGUID('d6').AssetBundle.playLoopingEffect(0)");
            Assert.Equal("2 4 5 {4, 6, 8} 1.96 3.92", said[0]);
            Assert.Equal("Red ff0000 true", said[1]);
            Assert.Equal("true Stop", said[2]);
        }

        [Fact]
        public void LogPrettyJsonAndSmoothMoving_Work()
        {
            var (_, host, said) = Load(@"
                log('a', 1)
                broadcastToAll(JSON.encode_pretty({1}) .. ' ' .. tostring(getObjectFromGUID('d6').isSmoothMoving()))");
            Assert.Equal("[1] false", said.Single());
        }
    }
}
