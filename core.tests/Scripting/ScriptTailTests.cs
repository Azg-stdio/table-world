using System.Collections.Generic;
using System.Linq;
using Core.Engine;
using Core.Rules;
using Core.Scripting;
using Core.Tests.Engine;

namespace Core.Tests.Scripting
{
    /// <summary>
    /// M5 (T6.3): the tail of TTS calls the local mods still made (lua_coverage.py): object data and spawning from it,
    /// spawn and destroy events, onUpdate, bag reset and laying out a layout zone.
    /// </summary>
    public class ScriptTailTests
    {
        private static (TableEngine Engine, ScriptHost Host, List<string> Said) Load(string global, System.Action<TableEngine>? prepare = null)
        {
            var engine = EngineFixture.Create();
            prepare?.Invoke(engine);
            engine.State.Scripts[ScriptHost.GlobalName] = global;
            var said = new List<string>();
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
        public void GetData_ThenSpawnObjectData_ClonesADeck_WithItsChanges()
        {
            // Riftbound copies a deck this way: read its data, spawn it again higher up.
            var (engine, host, said) = Load(@"
                local data = getObjectFromGUID('deck').getData()
                broadcastToAll(data.GUID .. ' ' .. #data.cards)
                data.Nickname = 'Copy'
                spawnObjectData({ data = data, position = { 0, 3, 0 }, callback_function = function(o) broadcastToAll('got ' .. o.getName()) end })");
            host.Tick(0.1);
            Assert.Equal("deck 10", said[0]);
            var copy = engine.State.Objects.OfType<Deck>().Single(d => d.Id != "deck");
            Assert.Equal("Copy", copy.Name);
            Assert.Equal(10, copy.Cards.Count);
            Assert.Empty(copy.Cards.Select(c => c.Id).Where(id => id != null).Intersect(((Deck)engine.Find("deck")!).Cards.Select(c => c.Id)));
            Assert.Equal("got Copy", said[1]);
        }

        [Fact]
        public void SpawnObjectData_RefusesTtsDataItCannotRead()
        {
            var (engine, _, said) = Load(@"broadcastToAll(tostring(spawnObjectData({ data = { Name = 'Custom_Model', Nickname = 'x' } })))");
            Assert.Equal("nil", said.Single());
            Assert.DoesNotContain(engine.State.Objects, o => o.Name == "x");
        }

        [Fact]
        public void SpawnAndDestroy_RaiseTheirEvents()
        {
            var (_, _, said) = Load(@"
                function onObjectSpawn(o) broadcastToAll('spawn ' .. o.getGUID()) end
                function onObjectDestroy(o) broadcastToAll('destroy ' .. o.getGUID()) end
                function onLoad()
                    spawnObject({ type = 'Custom_Token' })
                    destroyObject(getObjectFromGUID('d6'))
                end");
            Assert.StartsWith("spawn token", said[0]);
            Assert.Equal("destroy d6", said[1]);
        }

        [Fact]
        public void OnUpdate_RunsEveryTick()
        {
            var (_, host, said) = Load("n = 0 function onUpdate() n = n + 1 if n == 3 then broadcastToAll('three') end end");
            host.Tick(0.02);
            host.Tick(0.04);
            Assert.Empty(said);
            host.Tick(0.06);
            Assert.Equal("three", said.Single());
        }

        [Fact]
        public void Reset_EmptiesABag()
        {
            var (engine, _, said) = Load("broadcastToAll(tostring(getObjectFromGUID('bag').reset()) .. ' ' .. getObjectFromGUID('bag').getQuantity())");
            Assert.Equal("true 0", said.Single());
            Assert.Empty(engine.ContentsOf("bag"));
            Assert.Null(engine.Find("t1"));
        }

        [Fact]
        public void LayoutZone_LaysOut_FromItsComponent()
        {
            var (_, _, said) = Load("local z = getObjectFromGUID('lay') broadcastToAll(tostring(z.LayoutZone.layout()) .. ' ' .. tostring(z.LayoutZone:layout()))",
                engine => engine.State.Zones.Add(new LayoutZone { Id = "lay", Size = new Vec3(0.4f, 0.1f, 0.3f) }));
            Assert.Equal("true true", said.Single());
        }
    }
}
