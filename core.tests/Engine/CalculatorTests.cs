using Core.Engine;
using Core.Rules;

namespace Core.Tests.Engine
{
    /// <summary>The table calculator: keys pressed by any player change one shared display.</summary>
    public class CalculatorTests
    {
        private static string Keys(TableEngine engine, string id, string keys)
        {
            foreach (var key in keys)
                engine.Ok(new PressCalculatorKeyCommand { ObjectId = id, Key = key.ToString(), Actor = "seat_1" });
            return ((Calculator)engine.Find(id)!).Display;
        }

        [Fact]
        public void APlayerPutsOneDown_AndKeysWorkLikeABasicCalculator()
        {
            var engine = EngineFixture.Create();
            var id = engine.Ok(new AddCalculatorCommand { Position = new Vec3(0.2f, 0, 0.1f), Yaw = 180f, Actor = "seat_2" }).CreatedId!;
            Assert.Equal("Calculator", engine.Find(id)!.Name);
            Assert.Equal("42", Keys(engine, id, "12+30="));
            Assert.Equal("84", Keys(engine, id, "*2="));
            Assert.Equal("17", Keys(engine, id, "3+4+10="));
            Assert.Equal("3.5", Keys(engine, id, "C7/2="));
            Assert.Equal("12", Keys(engine, id, "C123<"));
            Assert.Equal("-6", Keys(engine, id, "C4-10="));
            engine.Fails(new PressCalculatorKeyCommand { ObjectId = id, Key = "x" }, ErrorCodes.InvalidValue);
            engine.Fails(new PressCalculatorKeyCommand { ObjectId = "d6", Key = "1" }, ErrorCodes.InvalidValue);

            // Saved with the table, mid-sum.
            Keys(engine, id, "C5+");
            var again = (Calculator)TableJson.Parse(TableJson.Serialize(engine.State)).State!.Objects.Find(o => o.Id == id)!;
            Assert.Equal("5", again.Display);
            Assert.Equal("+", again.Op);
        }
    }
}
