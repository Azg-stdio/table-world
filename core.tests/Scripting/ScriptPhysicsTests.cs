using System.Collections.Generic;
using System.Linq;
using Core.Rules;
using Core.Scripting;
using Core.Tests.Engine;

namespace Core.Tests.Scripting
{
    /// <summary>M5 (T6.3): Physics.cast through the game's physics, and collision events.</summary>
    public class ScriptPhysicsTests
    {
        [Fact]
        public void Cast_AsksTheGame_InMetres_AndAnswersInTtsUnits()
        {
            var engine = EngineFixture.Create();
            engine.State.Scripts[ScriptHost.GlobalName] = @"
                local hits = Physics.cast({ origin = { 0, 10, 0 }, direction = { 0, -1, 0 }, type = 2, size = { 2, 2, 2 }, max_distance = 20 })
                broadcastToAll(#hits .. ' ' .. hits[1].hit_object.getGUID() .. ' ' .. hits[1].distance .. ' ' .. hits[1].point.y)";
            var said = new List<string>();
            PhysicsCastQuery? asked = null;
            var host = new ScriptHost(() => engine, c => engine.Execute(c))
            {
                PhysicsCast = q =>
                {
                    asked = q;
                    return new[]
                    {
                        new PhysicsCastHit { ObjectId = "d6", Point = new Vec3(0f, 0f, 0f), Distance = 0.27f },
                        new PhysicsCastHit { ObjectId = "nothing-here", Distance = 0.3f },
                    };
                },
            };
            host.Broadcast += said.Add;
            host.Load(0);
            Assert.Equal(PhysicsCastQuery.Shape.Sphere, asked!.Type);
            Assert.Equal((10 - 0.96f) * 0.03f, asked.Origin.Y, 4);
            Assert.Equal(0.06f, asked.Size.X, 4);
            Assert.Equal(0.6f, asked.MaxDistance, 4);
            Assert.Equal("1 d6 9 0.96", said.Single());
        }

        [Fact]
        public void Collisions_ReachTheObjectsScript_AndRegisteredGlobalListeners()
        {
            var engine = EngineFixture.Create();
            engine.Find("d6")!.Script = "function onCollisionEnter(info) broadcastToAll('own ' .. info.collision_object.getGUID()) end";
            engine.State.Scripts[ScriptHost.GlobalName] = @"
                function onLoad() getObjectFromGUID('locked_die').registerCollisions() end
                function onObjectCollisionEnter(obj, info) broadcastToAll('global ' .. obj.getGUID() .. ' ' .. tostring(info.collision_object)) end";
            var said = new List<string>();
            var host = new ScriptHost(() => engine, c => engine.Execute(c));
            host.Broadcast += said.Add;
            host.Load(0);
            Assert.True(host.ListensToCollisions("d6"));
            Assert.True(host.ListensToCollisions("locked_die"));
            Assert.False(host.ListensToCollisions("loose"));
            host.OnCollision("d6", "locked_die", new Vec3(0f, 0f, 0f), new Vec3(0f, -1f, 0f), enter: true);
            host.OnCollision("locked_die", null, new Vec3(0f, 0f, 0f), new Vec3(0f, -1f, 0f), enter: true);
            Assert.Equal(new[] { "own locked_die", "global locked_die nil" }, said);
        }
    }
}
