using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Core.Engine;
using Core.Rules;
using Core.Scripting;
using Core.Tests.Engine;

namespace Core.Tests.Scripting
{
    /// <summary>M5 (T6.1, T6.2): the Lua sandbox survives hostile scripts; the API reads the table and changes it by commands.</summary>
    public class ScriptHostTests
    {
        private sealed class Harness
        {
            public readonly TableEngine Engine = EngineFixture.Create();
            public readonly ScriptHost Host;
            public readonly List<string> Said = new List<string>();
            public readonly List<string> Log = new List<string>();

            public Harness(string? global, params (string Id, string Script)[] objects)
            {
                if (global != null)
                    Engine.State.Scripts[ScriptHost.GlobalName] = global;
                foreach (var (id, script) in objects)
                    Engine.Find(id)!.Script = script;
                Host = new ScriptHost(() => Engine, Run);
                Host.Broadcast += Said.Add;
                Host.Log += Log.Add;
                Host.Load(0);
            }

            /// <summary>The host's route: execute, then tell the scripts.</summary>
            public CommandResult Run(Command command)
            {
                var result = Engine.Execute(command);
                Host.OnCommand(command, result, command.Actor == "seat_1" ? "red" : null);
                return result;
            }
        }

        [Fact]
        public void Sandbox_HasNoFilesOsOrLoading()
        {
            var h = new Harness(@"
                broadcastToAll(tostring(io) .. ' ' .. tostring(os.execute) .. ' ' .. tostring(load) .. ' ' .. tostring(dofile) .. ' ' .. tostring(require))");
            Assert.Equal("nil nil nil nil nil", h.Said.Single());
        }

        [Fact]
        public void EndlessLoops_AreStopped_AndTheHostGoesOn()
        {
            var clock = Stopwatch.StartNew();
            var h = new Harness("function onLoad() while true do end end");
            Assert.True(clock.Elapsed.TotalSeconds < 10, "stopped in time");
            Assert.Contains(h.Log, l => l.Contains("ran more than"));
            // The next event still runs.
            var h2 = new Harness("function onLoad() local x = 0 while true do x = x + 1 end end\nfunction onObjectFlip(color, obj) broadcastToAll('flipped') end");
            h2.Run(new FlipCommand { ObjectId = "loose" });
            Assert.Equal("flipped", h2.Said.Single());
        }

        [Fact]
        public void Handlers_RunAgainAfterAnError_AndCallsIntoTheSameScriptNest()
        {
            // Handlers share one runner per script: an error, a call back into the script while it runs, and the next
            // event must each behave as if every call had its own coroutine.
            var h = new Harness(@"
                local flips = 0
                function onObjectFlip(color, obj)
                    flips = flips + 1
                    if flips == 1 then error('first flip fails') end
                    broadcastToAll('flip ' .. flips .. ' ' .. Global.call('twice', { n = flips }))
                end
                function twice(t) return t.n * 2 end");
            h.Run(new FlipCommand { ObjectId = "loose" });
            Assert.Contains(h.Log, l => l.Contains("error:") && l.Contains("first flip fails"));
            h.Run(new FlipCommand { ObjectId = "loose" });
            h.Run(new FlipCommand { ObjectId = "loose" });
            Assert.Equal(new[] { "flip 2 4", "flip 3 6" }, h.Said);
        }

        [Fact]
        public void HugeStrings_AreRefused()
        {
            var h = new Harness("local s = string.rep('x', 2000000000) broadcastToAll('built')");
            Assert.Empty(h.Said);
            Assert.Contains(h.Log, l => l.Contains("string.rep"));
            var ok = new Harness("broadcastToAll(string.rep('ab', 3))");
            Assert.Equal("ababab", ok.Said.Single());
        }

        [Fact]
        public void Errors_AreLogged_NotThrown()
        {
            var h = new Harness("function onLoad() error('boom') end", ("d6", "this is not lua"));
            Assert.Contains(h.Log, l => l.Contains("boom"));
            Assert.Contains(h.Log, l => l.Contains("does not load"));
        }

        [Fact]
        public void Objects_AreReadInTtsUnits_AndChangedByCommands()
        {
            var h = new Harness(@"
                function onLoad()
                  local card = getObjectFromGUID('loose')
                  local p = card.getPosition()
                  broadcastToAll(card.getGUID() .. ' at ' .. p.x .. ',' .. p.y)
                  card.setName('Ace')
                  card.setPosition({x = 0, y = 1, z = -10})
                  card.flip()
                  broadcastToAll(tostring(getObjectFromGUID('nothing')))
                  broadcastToAll(#getObjects() .. ' objects')
                end");
            Assert.Equal("loose at 10,0.96", h.Said[0]);
            var card = h.Engine.Find("loose")!;
            Assert.Equal("Ace", card.Name);
            Assert.Equal(-0.3f, card.Transform.Position.Z, 4);
            Assert.False(card.IsFaceUp);
            Assert.Equal("nil", h.Said[1]);
            Assert.Equal($"{h.Engine.State.Objects.Count(o => o.Container == null)} objects", h.Said[2]);
            Assert.Equal(3, h.Host.CommandsIssued);
        }

        [Fact]
        public void Decks_GiveTheirCards_AndTakeObjectDeals()
        {
            var h = new Harness(@"
                function onLoad()
                  local deck = getObjectFromGUID('deck')
                  broadcastToAll(deck.getQuantity() .. ' ' .. deck.getObjects()[1].guid)
                  local card = deck.takeObject({ position = {x = 5, y = 1, z = 0} })
                  broadcastToAll(card.getGUID() .. ' ' .. deck.getQuantity())
                end");
            Assert.Equal("10 c0", h.Said[0]);
            Assert.Equal("c0 9", h.Said[1]);
            Assert.Equal(0.15f, h.Engine.Find("c0")!.Transform.Position.X, 4);
        }

        [Fact]
        public void Events_Drop_Zones_Flip_Randomize_AndSelfEvents()
        {
            var h = new Harness(@"
                function onObjectDrop(color, obj) broadcastToAll('drop ' .. color .. ' ' .. obj.getGUID()) end
                function onObjectEnterZone(zone, obj) broadcastToAll('enter ' .. zone.getGUID() .. ' ' .. obj.getGUID()) end
                function onObjectRandomize(obj, color) broadcastToAll('random ' .. obj.getGUID()) end",
                ("d6", "function onDrop(color) broadcastToAll('my drop ' .. color) end"));
            h.Run(new DropCommand { ObjectId = "d6", Actor = "seat_1" });
            h.Run(new MoveCommand { ObjectId = "loose", Position = new Vec3(0f, 0.05f, -0.5f) });
            h.Run(new RollCommand { ObjectId = "d6" });
            Assert.Contains("drop Red d6", h.Said);
            Assert.Contains("my drop Red", h.Said);
            Assert.Contains("enter hand_1 loose", h.Said);
            Assert.Contains("random d6", h.Said);
        }

        [Fact]
        public void Wait_Time_Frames_Condition_AndStop()
        {
            var h = new Harness(@"
                count = 0
                Wait.time(function() broadcastToAll('later') end, 2)
                local id = Wait.time(function() count = count + 1 end, 1, 3)
                Wait.frames(function() broadcastToAll('next frame') end, 1)
                Wait.condition(function() broadcastToAll('ready') end, function() return count >= 2 end)
                local never = Wait.time(function() broadcastToAll('never') end, 1)
                Wait.stop(never)");
            h.Host.Tick(0.5);
            Assert.Equal(new[] { "next frame" }, h.Said);
            h.Host.Tick(1.0);
            h.Host.Tick(2.0);
            Assert.Contains("later", h.Said);
            h.Host.Tick(2.1);
            Assert.Contains("ready", h.Said);
            h.Host.Tick(3.0);
            Assert.DoesNotContain("never", h.Said);
        }

        [Fact]
        public void Scripts_CallEachOther_AndShareVariables_ButNotTables()
        {
            var h = new Harness(@"
                function double(n) return n * 2 end
                function onLoad()
                  local d = getObjectFromGUID('d6')
                  broadcastToAll('d6 says ' .. d.call('hello', 'world'))
                  d.setVar('mark', 7)
                  broadcastToAll('mark ' .. d.getVar('mark'))
                end",
                ("d6", "function hello(who) return 'hi ' .. who .. ' ' .. Global.call('double', 21) end"));
            Assert.Contains("d6 says hi world 42", h.Said);
            Assert.Contains("mark 7", h.Said);
        }

        [Fact]
        public void ColonAndDotCalls_BothWork_AsInTts()
        {
            var h = new Harness(@"
                local hand = getObjectFromGUID('hand_1')
                getObjectFromGUID('loose'):setName('Colon')
                broadcastToAll(getObjectFromGUID('loose'):getName() .. ' ' .. getObjectFromGUID('loose').getName() .. ' ' .. #hand:getObjects() .. ' ' .. #hand.getObjects())");
            Assert.Equal("Colon Colon 0 0", h.Said.Single());
        }

        [Fact]
        public void SaveAndLoad_KeepScriptState()
        {
            var h = new Harness(@"
count = 0
function onLoad(saved) if saved ~= '' then count = tonumber(saved) end broadcastToAll('count ' .. count) end
function onSave() return tostring(count + 5) end",
                ("d6", @"
function onSave() return 'die state' end
function onLoad(s) broadcastToAll('die got ' .. s) end"));
            Assert.Contains("count 0", h.Said);
            h.Host.Save();
            Assert.Equal("5", h.Engine.State.GlobalScriptState);
            Assert.Equal("die state", h.Engine.Find("d6")!.ScriptState);
            var json = TableJson.Serialize(h.Engine.State);
            Assert.Contains("globalScriptState", json);
            h.Said.Clear();
            h.Host.Load(0);
            Assert.Contains("count 5", h.Said);
            Assert.Contains("die got die state", h.Said);
        }

        [Fact]
        public void Coroutines_RunAFrameAtATime()
        {
            var h = new Harness(@"
                function steps()
                  for i = 1, 3 do broadcastToAll('step ' .. i) coroutine.yield(0) end
                  return 1
                end
                function onLoad() startLuaCoroutine(Global, 'steps') end");
            Assert.Empty(h.Said);
            h.Host.Tick(0.1);
            h.Host.Tick(0.2);
            Assert.Equal(new[] { "step 1", "step 2" }, h.Said);
            h.Host.Tick(0.3);
            h.Host.Tick(0.4);
            Assert.Equal(3, h.Said.Count);
            Assert.Equal(0, h.Host.CoroutineCount);
        }

        [Fact]
        public void SetLuaScript_ReplacesAndReloadsAnObjectsScript()
        {
            var h = new Harness(@"function onLoad() getObjectFromGUID('d6').setLuaScript('function onLoad() broadcastToAll(""new script loaded"") end') end");
            Assert.Contains("new script loaded", h.Said);
            Assert.Contains("new script loaded", h.Engine.Find("d6")!.Script);
            Assert.Equal(2, h.Host.ScriptCount);
        }

        [Fact]
        public void TintScaleBoundsZonesAndLocalPositions()
        {
            var h = new Harness(@"
                local card = getObjectFromGUID('loose')
                card.setColorTint({r = 1, g = 0, b = 0})
                card.setScale({2, 2, 2})
                local b = card.getBounds()
                broadcastToAll(card.getColorTint().g .. ' ' .. card.getScale().x .. ' ' .. b.size.x)
                card.setPosition({0, 1, -16.6667})
                broadcastToAll(#card.getZones() .. ' zone')
                local w = card.positionToWorld({1, 0, 0})
                broadcastToAll(string.format('%.2f', w.x - card.getPosition().x))
                card.scale(1.5)
                broadcastToAll(string.format('%.1f', card.getScale().x))
                card.playLoopingEffect(1)");
            Assert.Equal("0 2 4.2333", h.Said[0]);
            Assert.Equal("1 zone", h.Said[1]);
            Assert.Equal("2.00", h.Said[2]); // one local unit across, scaled twice
            Assert.Equal("3.0", h.Said[3]); // scale(1.5) multiplies
        }

        /// <summary>Pokémon Master Trainer spawns a figure only on a face-up chip, tested with getTransformUp()[2] > 0.</summary>
        [Fact]
        public void TransformAxes_FollowTheRotation()
        {
            var h = new Harness(@"
                local card = getObjectFromGUID('loose')
                local function axes() local u, f, r = card.getTransformUp(), card.getTransformForward(), card.getTransformRight()
                    return string.format('%.0f %.0f %.0f / %.0f %.0f %.0f', u[2], f.z, r.x, u.y, f[3], r[1]) end
                broadcastToAll(axes())
                card.flip()
                broadcastToAll(string.format('%.0f', card.getTransformUp()[2]))
                card.setRotation({0, 90, 0})
                local f = card.getTransformForward()
                broadcastToAll(string.format('%.0f %.0f', f.x, f.z))");
            Assert.Equal("1 1 1 / 1 1 1", h.Said[0]);
            Assert.Equal("-1", h.Said[1]); // face down: up points down
            Assert.Equal("1 0", h.Said[2]); // a quarter turn about y sends forward to +x, as in Unity and TTS
        }

        [Fact]
        public void PickUp_IsHeard()
        {
            var h = new Harness("function onObjectPickUp(color, obj) broadcastToAll(color .. ' took ' .. obj.getGUID()) end");
            h.Run(new PickUpCommand { ObjectId = "d6", Actor = "seat_1" });
            Assert.Equal("Red took d6", h.Said.Single());
        }

        [Fact]
        public void Json_RoundTrips()
        {
            var h = new Harness("local t = JSON.decode('{\"a\": [1, 2, 3]}') broadcastToAll(#t.a .. ' ' .. JSON.encode({x = 1}))");
            Assert.Equal("3 {\"x\":1}", h.Said.Single());
        }

        [Fact]
        public void ScriptsReactingToThemselves_StopAtTheDepthLimit()
        {
            var h = new Harness("function onObjectFlip(color, obj) obj.flip() end");
            h.Run(new FlipCommand { ObjectId = "loose" });
            Assert.Contains(h.Log, l => l.Contains("deep"));
        }
    }
}
