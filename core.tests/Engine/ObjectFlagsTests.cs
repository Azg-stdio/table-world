using System.Linq;
using Core.Engine;
using Core.Rules;

namespace Core.Tests.Engine
{
    /// <summary>G39: the per-object TTS flags Hands and HideWhenFaceDown.</summary>
    public class ObjectFlagsTests
    {
        [Fact]
        public void HandsFalse_ObjectsDoNotJoinAHand()
        {
            var engine = EngineFixture.Create();
            var loose = engine.Find("loose")!;
            loose.Hands = false;
            engine.Ok(new MoveCommand { ObjectId = "loose", Position = new Vec3(0f, 0.05f, -0.5f) });
            Assert.DoesNotContain("hand_1", engine.ZonesOf("loose"));
            Assert.False(engine.IsHiddenFrom("loose", "seat_2"), "it lies in the hand zone, open to all");
            Assert.Equal(0f, loose.Transform.Position.X, 4);

            var json = TableJson.Serialize(engine.State);
            Assert.Contains(@"""hands"": false", json);
            Assert.False(TableJson.Parse(json).State!.Objects.Single(o => o.Id == "loose").Hands);
            Assert.DoesNotContain(@"""hands""", TableJson.Serialize(EngineFixture.Create().State));
        }

        [Fact]
        public void FaceDownCards_HideTheirName_UnlessTheyOptOut_AndOtherObjectsOnlyWhenTheyOptIn()
        {
            var engine = EngineFixture.Create();
            var card = (Card)engine.Find("loose")!;
            card.Name = "Ace of Spades";
            Assert.False(card.NameHidden);
            engine.Ok(new FlipCommand { ObjectId = "loose" });
            Assert.True(card.NameHidden);
            Assert.Equal("a card", ActionLog.NameFor(engine, "loose", "seat_1"));
            card.HideWhenFaceDown = false;
            Assert.False(card.NameHidden);
            Assert.Equal("Ace of Spades", ActionLog.NameFor(engine, "loose", "seat_1"));

            var die = engine.Find("d6")!;
            die.Name = "Lucky";
            die.Transform.Rotation = new Vec3(0f, 0f, 180f);
            Assert.False(die.NameHidden, "a die does not hide by default");
            die.HideWhenFaceDown = true;
            Assert.True(die.NameHidden);
            Assert.Equal("a d6", ActionLog.NameFor(engine, "d6", "seat_1"));
        }

        [Fact]
        public void Toggles_SetEachFlag_AndHandsMovesTheObjectInOrOutOfTheHand()
        {
            var engine = EngineFixture.Create();
            engine.Ok(new MoveCommand { ObjectId = "loose", Position = new Vec3(0f, 0.05f, -0.5f) });
            Assert.Contains("hand_1", engine.ZonesOf("loose"));
            var off = engine.Ok(new SetPropertiesCommand { ObjectId = "loose", Hands = false, Snap = false, Tooltip = false, Grid = false, HideWhenFaceDown = false });
            var loose = engine.Find("loose")!;
            Assert.False(loose.Hands || loose.Snap || loose.Tooltip || loose.Grid);
            Assert.False(loose.HideWhenFaceDown);
            Assert.DoesNotContain("hand_1", engine.ZonesOf("loose"));
            Assert.Contains(off.Events, e => e is ZoneLeft l && l.ZoneId == "hand_1");
            engine.Ok(new SetPropertiesCommand { ObjectId = "loose", Hands = true });
            Assert.Contains("hand_1", engine.ZonesOf("loose"));
            var json = TableJson.Serialize(engine.State);
            Assert.Contains(@"""snap"": false", json);
            Assert.Contains(@"""tooltip"": false", json);
            engine.AssertValid();
        }
    }
}
