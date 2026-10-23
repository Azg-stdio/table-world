using System.Linq;
using Core.Engine;
using Core.Rules;

namespace Core.Tests.Engine
{
    public class ZoneTests
    {
        private static readonly Vec3 InHand1 = new Vec3(0.1f, 0.01f, -0.5f);
        private static readonly Vec3 Elsewhere = new Vec3(0.3f, 0f, 0.2f);

        [Fact]
        public void Geometry_Contains_RespectsRotationScaleAndSnapPoints()
        {
            var zone = new ScriptingZone { Size = new Vec3(0.4f, 0.1f, 0.1f) };
            zone.Transform.Position = new Vec3(1, 0, 0);
            zone.Transform.Rotation = new Vec3(0, 90, 0);

            Assert.True(Geometry.Contains(zone, new Vec3(1, 0, 0.19f)));   // long axis now along Z
            Assert.False(Geometry.Contains(zone, new Vec3(1.19f, 0, 0)));
            zone.Transform.Scale = new Vec3(1, 1, 3);
            Assert.True(Geometry.Contains(zone, new Vec3(1.14f, 0, 0)));
            Assert.False(Geometry.Contains(new SnapPoint(), Vec3.Zero));
        }

        [Fact]
        public void Geometry_RotateAndInverse_MatchUnityConventions()
        {
            // Quaternion.Euler(0, 90, 0) * forward == right in Unity.
            var v = Geometry.Rotate(new Vec3(0, 90, 0), new Vec3(0, 0, 1));
            Assert.Equal(1f, v.X, 5);
            Assert.Equal(0f, v.Z, 5);

            var euler = new Vec3(30, 45, 60);
            var back = Geometry.InverseRotate(euler, Geometry.Rotate(euler, new Vec3(0.2f, -0.5f, 0.7f)));
            Assert.Equal(0.2f, back.X, 4);
            Assert.Equal(-0.5f, back.Y, 4);
            Assert.Equal(0.7f, back.Z, 4);
        }

        [Fact]
        public void Move_IntoAndOutOfAZone_EmitsEnterAndLeave()
        {
            var engine = EngineFixture.Create();

            var enter = engine.Ok(new MoveCommand { ObjectId = "d6", Position = InHand1 });
            var leave = engine.Ok(new MoveCommand { ObjectId = "d6", Position = Elsewhere });

            var entered = Assert.Single(enter.Events.OfType<ZoneEntered>());
            Assert.Equal(("d6", "hand_1"), (entered.ObjectId, entered.ZoneId));
            Assert.Equal("hand_1", Assert.Single(leave.Events.OfType<ZoneLeft>()).ZoneId);
            Assert.Empty(engine.ZonesOf("d6"));
        }

        [Fact]
        public void Deal_EntersTheHand_AndInsertIntoABag_LeavesIt()
        {
            var engine = EngineFixture.Create();

            var deal = engine.Ok(new DealCommand { DeckId = "deck", Count = 2, Seats = { "seat_1" } });
            var card = deal.Dealt!["seat_1"][0];
            Assert.Equal(2, deal.Events.OfType<ZoneEntered>().Count(e => e.ZoneId == "hand_1"));
            Assert.Equal(new[] { "c0", "c1" }, engine.ObjectsIn("hand_1").Select(o => o.Id));

            var insert = engine.Ok(new InsertCommand { ObjectId = card, IntoId = "bag" });

            Assert.Equal("hand_1", Assert.Single(insert.Events.OfType<ZoneLeft>()).ZoneId);
            Assert.Equal(new[] { "c1" }, engine.ObjectsIn("hand_1").Select(o => o.Id));
        }

        [Fact]
        public void Hands_AreLaidOutAgain_WhenACardLeaves_KeepingTheOrder()
        {
            var engine = EngineFixture.Create();
            var dealt = engine.Ok(new DealCommand { DeckId = "deck", Count = 3, Seats = { "seat_1" } }).Dealt!["seat_1"];
            var xs = dealt.Select(id => engine.Find(id)!.Transform.Position.X).ToList();

            var left = engine.Ok(new MoveCommand { ObjectId = dealt[1], Position = Elsewhere });

            Assert.Equal(xs[0], engine.Find(dealt[0])!.Transform.Position.X, 4);
            Assert.Equal(xs[1], engine.Find(dealt[2])!.Transform.Position.X, 4);   // closed the gap
            Assert.Contains(left.Events, e => e is ObjectUpdated && e.ObjectId == dealt[2]);
            engine.AssertValid();
        }

        [Fact]
        public void Hands_SqueezeToFit_AndADroppedCardTakesItsPlaceInOrder()
        {
            var engine = EngineFixture.Create();
            var dealt = engine.Ok(new DealCommand { DeckId = "deck", Count = 8, Seats = { "seat_1" } }).Dealt!["seat_1"];
            var hand = engine.State.Zones.Single(z => z.Id == "hand_1");
            Assert.All(dealt, id => Assert.True(Geometry.Contains(hand, engine.Find(id)!.Transform.Position)));

            // Drop the loose card between the first and second card: it becomes second.
            var between = (engine.Find(dealt[0])!.Transform.Position.X + engine.Find(dealt[1])!.Transform.Position.X) / 2f;
            engine.Ok(new MoveCommand { ObjectId = "loose", Position = new Vec3(between, 0.01f, -0.5f) });
            var order = engine.ObjectsIn("hand_1").OrderBy(o => o.Transform.Position.X).Select(o => o.Id).ToList();
            Assert.Equal(new[] { dealt[0], "loose", dealt[1] }, order.Take(3));
        }

        [Fact]
        public void MembershipIsComputedOnLoad()
        {
            var state = TableJson.Parse(EngineFixture.Json).State!;
            state.Objects.Single(o => o.Id == "d6").Transform.Position = InHand1;

            var engine = new TableEngine(state, 1);

            Assert.Equal(new[] { "hand_1" }, engine.ZonesOf("d6"));
        }

        [Fact]
        public void FailedCommands_EmitNoZoneEvents()
        {
            var engine = EngineFixture.Create();

            var result = engine.Execute(new MoveCommand { ObjectId = "locked_die", Position = InHand1 });

            Assert.False(result.Ok);
            Assert.Empty(result.Events);
        }

        [Fact]
        public void HandContents_AreHiddenFromOtherSeats_AndHiddenZonesFollowVisibleTo()
        {
            var state = TableJson.Parse(EngineFixture.Json).State!;
            state.Zones.Add(new HiddenZone { Id = "gm", Size = new Vec3(0.1f, 0.1f, 0.1f), VisibleTo = { "host" } });
            state.Objects.Single(o => o.Id == "d6").Transform.Position = InHand1;
            var engine = new TableEngine(state, 1);

            Assert.False(engine.IsHiddenFrom("d6", "seat_1"));
            Assert.True(engine.IsHiddenFrom("d6", "seat_2"));
            Assert.True(engine.IsHiddenFrom("d6", null));
            Assert.True(engine.IsHiddenFrom("locked_die", "seat_1"));   // at the origin, inside the gm zone
            Assert.False(engine.IsHiddenFrom("locked_die", null));
            Assert.False(engine.IsHiddenFrom("loose", "seat_2"));
        }
    }
}
