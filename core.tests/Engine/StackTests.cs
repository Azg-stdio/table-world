using System.Linq;
using Core.Engine;
using Core.Rules;

namespace Core.Tests.Engine
{
    /// <summary>G14: identical stackable pieces merge into a counted stack; pieces come off the top.</summary>
    public class StackTests
    {
        private const string Json = @"{
  ""schemaVersion"": ""0.1"",
  ""objects"": [
    { ""id"": ""gold"", ""type"": ""Token"", ""name"": ""Gold"", ""shape"": ""round"", ""stackable"": true, ""tint"": [1, 0.8, 0.2] },
    { ""id"": ""gold2"", ""type"": ""Token"", ""name"": ""Gold"", ""shape"": ""round"", ""stackable"": true, ""tint"": [1, 0.8, 0.2], ""count"": 4, ""transform"": { ""position"": [0.2, 0, 0] } },
    { ""id"": ""silver"", ""type"": ""Token"", ""name"": ""Silver"", ""shape"": ""round"", ""stackable"": true },
    { ""id"": ""loner"", ""type"": ""Token"", ""name"": ""Gold"", ""shape"": ""round"", ""tint"": [1, 0.8, 0.2] },
    { ""id"": ""wound"", ""type"": ""Tile"", ""shape"": ""hex"", ""stackable"": true, ""count"": 3 },
    { ""id"": ""bag"", ""type"": ""Bag"" }
  ],
  ""players"": { ""seats"": [ { ""id"": ""south"", ""color"": ""red"" } ] }
}";

        private static TableEngine Create()
        {
            var parsed = TableJson.Parse(Json);
            Assert.True(parsed.Ok, string.Join("\n", parsed.Errors));
            return new TableEngine(parsed.State!, 1);
        }

        [Fact]
        public void SamePieces_Merge_CountsAddUp_AndTheDroppedOneIsGone()
        {
            var engine = Create();
            var result = engine.Ok(new InsertCommand { ObjectId = "gold", IntoId = "gold2" });
            Assert.Null(engine.Find("gold"));
            Assert.Equal(5, Stacks.CountOf(engine.Find("gold2")!));
            Assert.Contains(result.Events, e => e is ObjectRemoved r && r.ObjectId == "gold");
            Assert.Equal("south stacked Gold (5)", ActionLog.Describe(engine, new InsertCommand { ObjectId = "gold", IntoId = "gold2" }, result, "south", "south").Single().Text);
            engine.AssertValid();
        }

        [Fact]
        public void DifferentPieces_OrNotStackable_DoNotMerge()
        {
            var engine = Create();
            engine.Fails(new InsertCommand { ObjectId = "silver", IntoId = "gold2" }, ErrorCodes.InvalidTarget);
            engine.Fails(new InsertCommand { ObjectId = "loner", IntoId = "gold2" }, ErrorCodes.InvalidTarget);
            engine.Fails(new InsertCommand { ObjectId = "wound", IntoId = "gold2" }, ErrorCodes.InvalidTarget);
            // A locked stack still takes pieces, and its own lock is not part of being "the same piece".
            engine.Ok(new LockCommand { ObjectId = "gold2" });
            engine.Ok(new InsertCommand { ObjectId = "gold", IntoId = "gold2" });
            Assert.Equal(5, Stacks.CountOf(engine.Find("gold2")!));
            // Bags still take stacks whole.
            engine.Ok(new InsertCommand { ObjectId = "wound", IntoId = "bag" });
            Assert.Equal(3, Stacks.CountOf(engine.Find("wound")!));
        }

        [Fact]
        public void Take_SplitsPiecesOffTheTop_AsANewObject()
        {
            var engine = Create();
            var one = engine.Ok(new TakeCommand { ContainerId = "gold2", Position = new Vec3(0.5f, 0.1f, 0f) });
            var piece = engine.Find(one.CreatedId!)!;
            Assert.Equal(1, Stacks.CountOf(piece));
            Assert.Equal(3, Stacks.CountOf(engine.Find("gold2")!));
            Assert.Equal(0.5f, piece.Transform.Position.X);
            Assert.True(Stacks.SameKind(piece, engine.Find("gold2")!), "the piece is still the same kind");

            var two = engine.Ok(new TakeCommand { ContainerId = "gold2", Count = 2 });
            Assert.Equal(2, Stacks.CountOf(engine.Find(two.CreatedId!)!));
            Assert.Equal(1, Stacks.CountOf(engine.Find("gold2")!));
            Assert.Contains("(1 left)", ActionLog.Describe(engine, new TakeCommand { ContainerId = "gold2", Count = 2 }, two, "south", "south").Single().Text);

            engine.Fails(new TakeCommand { ContainerId = "gold2" }, ErrorCodes.WrongObjectType);
            engine.Fails(new TakeCommand { ContainerId = "wound", Count = 3 }, ErrorCodes.InvalidValue);
            engine.Fails(new TakeCommand { ContainerId = "wound", Count = 0 }, ErrorCodes.InvalidValue);
            engine.AssertValid();
        }

        [Fact]
        public void Merge_StopsAtTheLimit()
        {
            var engine = Create();
            Stacks.SetCount(engine.Find("gold2")!, Stacks.MaxCount);
            engine.Fails(new InsertCommand { ObjectId = "gold", IntoId = "gold2" }, ErrorCodes.LimitReached);
        }

        [Theory]
        [InlineData(@"{ ""id"": ""x"", ""type"": ""Token"", ""count"": 3 }", "not stackable")]
        [InlineData(@"{ ""id"": ""x"", ""type"": ""Token"", ""stackable"": true, ""count"": 0 }", "a stack holds")]
        [InlineData(@"{ ""id"": ""x"", ""type"": ""Tile"", ""stackable"": true, ""count"": 501 }", "a stack holds")]
        public void Validation_RefusesBadCounts(string obj, string message)
        {
            var parsed = TableJson.Parse(@"{ ""schemaVersion"": ""0.1"", ""objects"": [" + obj + "] }");
            Assert.False(parsed.Ok);
            Assert.Contains(parsed.Errors, e => e.Path.EndsWith(".count") && e.Message.Contains(message));
        }

        [Fact]
        public void Count_IsOmittedFromJsonForSinglePieces()
        {
            var json = TableJson.Serialize(Create().State);
            Assert.Contains(@"""count"": 4", json);
            Assert.Equal(2, json.Split(@"""count""").Length - 1);
        }
    }
}
