using System.Collections.Generic;
using System.Linq;
using Core.Engine;
using Core.Rules;

namespace Core.Tests.Engine
{
    /// <summary>G28: lines and arrows drawn on the table are saved, erased one by one or cleared, and validated.</summary>
    public class DrawingTests
    {
        private static DrawingDef Line(string? seat = "seat_1", params float[] xz)
        {
            var d = new DrawingDef { Seat = seat, Color = new[] { 1f, 0f, 0f } };
            for (var i = 0; i < xz.Length; i += 2)
                d.Points.Add(new Vec3(xz[i], 0f, xz[i + 1]));
            return d;
        }

        [Fact]
        public void Add_GivesIds_SavesWithTheTable_AndLogs()
        {
            var engine = EngineFixture.Create();
            var first = engine.Ok(new AddDrawingCommand { Drawing = Line("seat_1", 0f, 0f, 0.1f, 0f) });
            var arrow = Line("seat_2", 0f, 0.1f, 0.2f, 0.1f);
            arrow.Arrow = true;
            var second = engine.Ok(new AddDrawingCommand { Drawing = arrow });
            Assert.Equal("drawing-1", first.CreatedId);
            Assert.Equal("drawing-2", second.CreatedId);
            Assert.Contains(first.Events, e => e is DrawingsChanged);

            var back = TableJson.Parse(TableJson.Serialize(engine.State)).State!;
            Assert.Equal(2, back.Drawings.Count);
            Assert.True(back.Drawings[1].Arrow);
            Assert.Equal(0.004f, back.Drawings[0].Thickness);
            Assert.DoesNotContain("drawings", TableJson.Serialize(EngineFixture.Create().State));
            Assert.Equal("seat_2 drew an arrow", ActionLog.Describe(engine, new AddDrawingCommand { Drawing = arrow }, second, "seat_2", null).Single().Text);
        }

        [Fact]
        public void Erase_Clear_AndClearOneSeat()
        {
            var engine = EngineFixture.Create();
            engine.Ok(new AddDrawingCommand { Drawing = Line("seat_1", 0f, 0f, 0.1f, 0f) });
            engine.Ok(new AddDrawingCommand { Drawing = Line("seat_2", 0f, 0f, 0.1f, 0f) });
            engine.Ok(new AddDrawingCommand { Drawing = Line("seat_2", 0f, 0f, 0.1f, 0f) });
            engine.Ok(new EraseDrawingCommand { DrawingId = "drawing-1" });
            engine.Fails(new EraseDrawingCommand { DrawingId = "drawing-1" }, ErrorCodes.UnknownObject);
            engine.Ok(new ClearDrawingsCommand { Seat = "seat_1" });
            Assert.Equal(2, engine.State.Drawings.Count);
            engine.Ok(new ClearDrawingsCommand { Seat = "seat_2" });
            Assert.Empty(engine.State.Drawings);
        }

        [Fact]
        public void BadDrawings_AreRefused_AndTheLimitHolds()
        {
            var engine = EngineFixture.Create();
            engine.Fails(new AddDrawingCommand { Drawing = Line() }, ErrorCodes.InvalidValue);
            engine.Fails(new AddDrawingCommand { Drawing = Line("seat_1", 0f, 0f, 50f, 0f) }, ErrorCodes.InvalidValue);
            var thick = Line("seat_1", 0f, 0f, 0.1f, 0f);
            thick.Thickness = 1f;
            engine.Fails(new AddDrawingCommand { Drawing = thick }, ErrorCodes.InvalidValue);
            var tooLong = Line("seat_1");
            tooLong.Points = Enumerable.Range(0, DrawingDef.MaxPoints + 1).Select(i => new Vec3(i * 0.0001f, 0f, 0f)).ToList();
            engine.Fails(new AddDrawingCommand { Drawing = tooLong }, ErrorCodes.InvalidValue);
            for (var i = 0; i < TableEngine.MaxDrawings; i++)
                engine.State.Drawings.Add(new DrawingDef { Id = $"d{i}", Points = new List<Vec3> { new Vec3() } });
            engine.Fails(new AddDrawingCommand { Drawing = Line("seat_1", 0f, 0f) }, ErrorCodes.LimitReached);
        }

        [Fact]
        public void DistanceTo_MeasuresToTheNearestSegment()
        {
            var d = Line(null, 0f, 0f, 0.1f, 0f, 0.1f, 0.1f);
            Assert.Equal(0.02f, d.DistanceTo(0.05f, 0.02f), 4);
            Assert.Equal(0.03f, d.DistanceTo(0.13f, 0.05f), 4);
            Assert.Equal(0.05f, d.DistanceTo(-0.05f, 0f), 4);
        }
    }
}
