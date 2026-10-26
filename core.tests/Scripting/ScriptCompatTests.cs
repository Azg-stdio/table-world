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

        /// <summary>
        /// Pokémon Master Trainer spawns a figure with takeObject({ callback_function }) and resets after
        /// Wait.condition(done, cond, 2, on_timeout) when the figure never came.
        /// </summary>
        [Fact]
        public void TakeObjectCallback_AndConditionTimeout_WorkLikeTts()
        {
            var (_, host, said) = Load(@"
                function onLoad()
                  broadcastToAll('in the bag: ' .. tostring(getObjectFromGUID('t1')))
                  getObjectFromGUID('bag').takeObject({ guid = 't1', position = { 5, 2, 0 },
                    callback_function = function(obj) broadcastToAll('arrived ' .. obj.getGUID()) end })
                  Wait.condition(function() broadcastToAll('met') end, function() return false end, 1,
                    function() broadcastToAll('timed out') end)
                  Wait.condition(function() broadcastToAll('never') end, function() return false end, 1)
                end");
            Assert.Equal(new[] { "in the bag: nil" }, said); // as in TTS, getObjectFromGUID does not see into bags
            host.Tick(0.02);
            Assert.Equal(new[] { "in the bag: nil", "arrived t1" }, said);
            host.Tick(1.5);
            Assert.Equal(new[] { "in the bag: nil", "arrived t1", "timed out" }, said); // the main function never runs on a timeout
        }

        /// <summary>One light alone while playing: night in the room, the cards still lit; a TTS apply leaves them be.</summary>
        [Fact]
        public void RoomAndTableLights_ChangeOneAtATime()
        {
            var (engine, _, said) = Load(@"
                function onLoad()
                  broadcastToAll('room ' .. tostring(Lighting.setRoomLight({ intensity = 0.25, color = { r = 0.6, g = 0.7, b = 1 } })))
                  broadcastToAll('table ' .. tostring(Lighting.setTableLight({ intensity = 1.2 })))
                  Lighting.light_intensity = 0.54
                  Lighting.apply()
                end");
            Assert.Equal(new[] { "room true", "table true" }, said.Take(2));
            var lighting = engine.State.Environment!.Lighting;
            Assert.Equal(0.25f, lighting.RoomScale!.Value, 3);
            Assert.Equal(new[] { 0.6f, 0.7f, 1f }, lighting.RoomTint!);
            Assert.Equal(1.2f, lighting.TableScale!.Value, 3);
            Assert.Null(lighting.TableTint);
            // Day again: the room back to normal, the table untouched.
            Assert.True(engine.Execute(new SetLightCommand { Light = "room" }).Ok);
            Assert.Null(lighting.RoomScale);
            Assert.Equal(1.2f, lighting.TableScale!.Value, 3);
            Assert.False(engine.Execute(new SetLightCommand { Light = "sky" }).Ok);
        }

        /// <summary>The last of the local mods' calls: Lighting, Notes, hover, changeColor and setAngularVelocity.</summary>
        [Fact]
        public void LightingNotesHoverAndSeatChanges_WorkLikeTts()
        {
            var (engine, host, said) = Load(@"
                function onObjectHover(color, obj) broadcastToAll('hover ' .. color .. ' ' .. (obj and obj.getGUID() or 'nothing')) end
                function onLoad()
                  broadcastToAll(string.format('light %.2f ambient %.2f', Lighting.light_intensity, Lighting.ambient_intensity))
                  Lighting.light_intensity = 0.27
                  Lighting.ambient_intensity = 2.6
                  Lighting.setLightColor({ r = 1, g = 0, b = 0 })
                  broadcastToAll('applied ' .. tostring(Lighting.apply()))
                  broadcastToAll('tab ' .. Notes.addNotebookTab({ title = 'Log', body = 'x' }))
                  getObjectFromGUID('d6').setAngularVelocity({ 0, 0, 0 })
                end
                function where() local o = Player.Red.getHoverObject() broadcastToAll('red hovers ' .. (o and o.getGUID() or 'nothing')) end
                function move() broadcastToAll('moved ' .. tostring(Player.Red.changeColor('Blue'))) end
                function onChat(message) if message == 'where' then where() elseif message == 'move' then move() end end");
            Assert.Equal("light 0.54 ambient 1.30", said[0]);
            Assert.Equal("applied true", said[1]);
            var lighting = engine.State.Environment!.Lighting;
            Assert.Equal(0.5f, lighting.LightScale!.Value, 3);
            Assert.Equal(2f, lighting.AmbientScale!.Value, 3);
            Assert.Equal(new[] { 1f, 0f, 0f }, lighting.LightTint!);
            Assert.Equal("tab 0", said[2]);
            Assert.Equal("Log", engine.State.Notebook.Single().Title);

            host.OnHover("red", "d6");
            host.OnHover("red", "d6");
            Assert.Equal("hover Red d6", said.Last());
            Assert.Equal(4, said.Count); // the same hover again is not news
            host.OnChat("where", "red");
            Assert.Equal("red hovers d6", said.Last());
            host.OnHover("red", null);
            Assert.Equal("hover Red nothing", said.Last());

            string? moved = null;
            host.ChangeColor = (from, to) => { moved = from + " to " + to; return true; };
            host.OnChat("move", "red");
            Assert.Equal("red to blue", moved);
            Assert.Equal("moved true", said.Last());
        }

        /// <summary>A save's TTS lighting becomes the room's multipliers; TTS's defaults leave the room as it is.</summary>
        [Fact]
        public void SavedLighting_IsImported()
        {
            Assert.Null(Core.Tts.TtsImporter.Import(@"{ ""ObjectStates"": [], ""Lighting"": { ""LightIntensity"": 0.54, ""AmbientIntensity"": 1.3,
                ""LightColor"": { ""r"": 1.0, ""g"": 0.9804, ""b"": 0.8902 } } }").State.Environment?.Lighting.LightScale);
            var dim = Core.Tts.TtsImporter.Import(@"{ ""ObjectStates"": [], ""Lighting"": { ""LightIntensity"": 0.27, ""AmbientIntensity"": 0.65 } }").State;
            Assert.Equal(0.5f, dim.Environment!.Lighting.LightScale!.Value, 3);
            Assert.Equal(0.5f, dim.Environment.Lighting.AmbientScale!.Value, 3);
            Assert.Null(dim.Environment.Lighting.LightTint);
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
            Assert.Equal("true Ready", said[2]);
        }

        [Fact]
        public void ZoneScripts_RunWithTheZoneAsSelf_AndHearZoneEvents()
        {
            var engine = EngineFixture.Create();
            engine.State.Zones.Add(new ScriptingZone
            {
                Id = "pool", Size = new Vec3(0.3f, 0.2f, 0.3f), Transform = new TableTransform { Position = new Vec3(0.6f, 0f, 0f) },
                Script = "function onLoad(saved) broadcastToAll('zone ' .. self.getGUID() .. ' ' .. saved .. ' ' .. self.getBounds().size.x) end " +
                         "function onObjectEnterZone(zone, obj) if zone == self then broadcastToAll('in me: ' .. obj.getGUID()) end end " +
                         "function onSave() return 'kept' end",
                ScriptState = "before",
            });
            var said = new System.Collections.Generic.List<string>();
            ScriptHost? host = null;
            host = new ScriptHost(() => engine, c => { var r = engine.Execute(c); host!.OnCommand(c, r, null); return r; });
            host.Broadcast += said.Add;
            host.Load(0);
            var move = new MoveCommand { ObjectId = "d6", Position = new Vec3(0.6f, 0f, 0f) };
            host.OnCommand(move, engine.Execute(move), null);
            host.Save();
            Assert.Equal(new[] { "zone pool before 10", "in me: d6" }, said);
            Assert.Equal("kept", engine.State.Zones.Single(z => z.Id == "pool").ScriptState);
        }

        [Fact]
        public void ScriptsInABag_StartWhenTakenOut_AndStopWhenPutBack()
        {
            var engine = EngineFixture.Create();
            engine.Find("t1")!.Script = "function onLoad() broadcastToAll('t1 alive') Wait.time(function() broadcastToAll('t1 tick') end, 1, -1) end";
            var said = new System.Collections.Generic.List<string>();
            ScriptHost? host = null;
            host = new ScriptHost(() => engine, c => { var r = engine.Execute(c); host!.OnCommand(c, r, null); return r; });
            host.Broadcast += said.Add;
            host.Load(0);
            Assert.Empty(said);
            void Run(Core.Engine.Command c) => host.OnCommand(c, engine.Execute(c), null);
            Run(new TakeCommand { ContainerId = "bag", EntryId = "t1", Position = new Vec3(0.5f, 0f, 0f) });
            Assert.Equal(new[] { "t1 alive" }, said);
            host.Tick(1.01);
            Assert.Equal("t1 tick", said.Last());
            Run(new InsertCommand { ObjectId = "t1", IntoId = "bag" });
            host.Tick(2.02);
            host.Tick(3.03);
            Assert.Equal(2, said.Count);
        }

        [Fact]
        public void SetCustomObject_PointsObjectsAtWebFiles_EachMachineFetches()
        {
            var (engine, _, said) = Load(@"
                function onLoad()
                  local m = spawnObject({ type = 'Custom_Model' })
                  m.setCustomObject({ mesh = 'https://cdn.test/pawn.obj', diffuse = 'https://cdn.test/pawn.png' })
                  getObjectFromGUID('loose').setCustomObject({ face = 'https://cdn.test/face.jpg' })
                  local c = m.getCustomObject()
                  broadcastToAll(c.mesh .. ' ' .. c.diffuse .. ' ' .. tostring(getObjectFromGUID('d6').setCustomObject({ image = 'not a url' })))
                end");
            Assert.Equal("https://cdn.test/pawn.obj https://cdn.test/pawn.png true", said.Single());
            var mesh = engine.State.Assets.Single(a => a.Url == "https://cdn.test/pawn.obj");
            Assert.Equal(AssetType.Model, mesh.Type);
            Assert.Equal(0.03f, mesh.MeshScale);
            Assert.StartsWith("urls/", mesh.Path);
            Assert.Equal(mesh.Id, ((Model)engine.State.Objects.OfType<Model>().Single(m => m.Asset == mesh.Id)).Asset);
            var face = engine.State.Assets.Single(a => a.Url == "https://cdn.test/face.jpg");
            Assert.EndsWith(".jpg", face.Path);
            Assert.Equal(face.Id, ((Card)engine.Find("loose")!).FaceAsset);
            var parsed = TableJson.Parse(TableJson.Serialize(engine.State));
            Assert.True(parsed.Ok, string.Join("; ", parsed.Errors));
        }

        [Fact]
        public void LogPrettyJsonAndSmoothMoving_Work()
        {
            var (_, host, said) = Load(@"
                log('a', 1)
                broadcastToAll(JSON.encode_pretty({1}) .. ' ' .. tostring(getObjectFromGUID('d6').isSmoothMoving()))");
            Assert.Equal("[1] false", said.Single());
        }

        [Fact]
        public void Global_HasUiAndCall_AndIsSelfInTheGlobalScript()
        {
            var (engine, _, said) = Load(@"
                function double(t) return t.n * 2 end
                function onLoad()
                    self.UI.setXml('<Panel id=""p"" active=""false""/>')
                    Global.UI.setAttribute('p', 'active', 'true')
                    broadcastToAll(Global.Call('double', { n = 3 }) .. ' ' .. self.getGUID() .. ' ' .. tostring(Global.UI.getAttribute('p', 'active')))
                end");
            Assert.Equal("6 -1 True", said.Single());
            Assert.True(engine.State.ScriptUi.Single().Active);
        }

        [Fact]
        public void Turns_AreReadAndSet()
        {
            var (engine, _, said) = Load(@"
                Turns.enable = true
                Turns.order = { 'Blue', 'Red' }
                Turns.turn_color = 'Red'
                broadcastToAll(tostring(Turns.enable) .. ' ' .. Turns.turn_color .. ' ' .. Turns.order[1] .. ' next ' .. Turns.getNextTurnColor())");
            Assert.Equal("true Red Blue next Blue", said.Single());
            Assert.Equal(new[] { "seat_2", "seat_1" }, engine.State.FreeTurns!.Seats);
        }

        [Fact]
        public void Grid_IsSetFieldByField_InTtsUnits()
        {
            var (engine, _, said) = Load(@"
                Grid.type = 2
                Grid.sizeX = 2
                Grid.snapping = 3
                Grid.show_lines = true
                broadcastToAll(Grid.type .. ' ' .. Grid.sizeX .. ' ' .. Grid.snapping)");
            Assert.Equal("2 2 3", said.Single());
            var grid = engine.State.Grid!;
            Assert.Equal(GridShape.Hex, grid.Shape);
            Assert.Equal(0.06f, grid.SizeX, 4);
            Assert.Equal(GridSnapping.Centers, grid.Snapping);
        }

        [Fact]
        public void MemberVariables_AreReadLive_AndRestingWaitsEnd()
        {
            var (_, host, said) = Load(@"
                local d = getObjectFromGUID('d6')
                broadcastToAll(type(d.is_face_down) .. ' ' .. tostring(d.resting) .. ' ' .. tostring(d.spawning) .. ' ' .. tostring(d.held_by_color))
                Wait.condition(function() broadcastToAll('settled') end, function() return d.resting end)");
            host.Tick(0.02);
            Assert.Equal(new[] { "boolean true false nil", "settled" }, said);
        }

        [Fact]
        public void MusicPlayer_LoadsPlaysAndPauses_TheTablesMusic()
        {
            // Pokemon Sinnoh's Music Box: a playlist from a cartridge, a random track, play; events set a clip directly.
            var (engine, _, said) = Load(@"
                MusicPlayer:setPlaylist({ { url = 'https://x/a.mp3', title = 'A' }, { url = 'https://x/b.mp3', title = 'B' } })
                MusicPlayer.shuffle = true
                MusicPlayer.playlistIndex = 1
                MusicPlayer:play()
                broadcastToAll(MusicPlayer.player_status .. ' ' .. MusicPlayer:getCurrentAudioclip().title .. ' ' .. #MusicPlayer:getPlaylist())
                MusicPlayer:setCurrentAudioclip({ url = 'https://x/event.mp3', title = 'Event' })
                MusicPlayer.repeat_track = true
                MusicPlayer:pause()
                broadcastToAll(MusicPlayer.player_status .. ' ' .. MusicPlayer.playlist_index)");
            Assert.Equal(new[] { "Play B 2", "Pause -1" }, said);
            var music = engine.State.Music!;
            Assert.Equal("https://x/event.mp3", music.Current!.Url);
            Assert.True(music.Repeat && music.Shuffle && !music.Playing);
        }

        [Fact]
        public void Music_MovesOn_RepeatingShufflingOrInOrder()
        {
            var music = new MusicState { Playlist = { new MusicTrack { Url = "https://x/1" }, new MusicTrack { Url = "https://x/2" } }, Index = 1, Current = new MusicTrack { Url = "https://x/2" } };
            var next = music.Next(new System.Random(1));
            Assert.Equal(0, next.Index);
            Assert.Equal(music.Starts + 1, next.Starts);
            music.Repeat = true;
            Assert.Equal(1, music.Next(new System.Random(1)).Index);
            music.Repeat = false;
            music.Shuffle = true;
            Assert.Equal(0, music.Next(new System.Random(1)).Index);
        }

        [Fact]
        public void Zones_AreMovedByScripts_AndObjectsAreDestroyedEitherWay()
        {
            var (engine, _, said) = Load(@"
                local hand = getObjectFromGUID('hand_1')
                hand.setPosition({ 10, 1.96, 0 })
                hand.setScale({ 20, 4, 5 })
                getObjectFromGUID('d6').destroyObject()
                broadcastToAll(hand.getPosition().x .. ' ' .. tostring(getObjectFromGUID('d6') == nil))");
            Assert.Equal("10 true", said.Single());
            var zone = engine.State.Zones.Single(z => z.Id == "hand_1");
            Assert.Equal(0.3f, zone.Transform.Position.X, 4);
            Assert.Equal(0.03f, zone.Transform.Position.Y, 4);
            Assert.Equal(0.6f, zone.Size.X, 4);
        }
    }
}
