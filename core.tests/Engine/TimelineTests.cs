using Core.Engine;

namespace Core.Tests.Engine
{
    /// <summary>G26: rewind keeps a point every few seconds of change, steps back and forth, and forgets the old future.</summary>
    public class TimelineTests
    {
        [Fact]
        public void Record_KeepsChangedTables_AtMostEveryInterval()
        {
            var t = new Timeline();
            Assert.True(t.Record("a", 0));
            Assert.False(t.Record("a", 100), "an unchanged table is not kept");
            Assert.False(t.Record("b", 5), "too soon");
            Assert.True(t.Record("b", 10));
            Assert.True(t.Record("c", 11, force: true), "forced before a rewind");
            Assert.Equal(3, t.Count);
        }

        [Fact]
        public void BackAndForward_MoveTheCursor_AndANewChangeDropsTheFuture()
        {
            var t = new Timeline();
            t.Record("a", 0);
            t.Record("b", 10);
            t.Record("c", 20);
            Assert.False(t.CanGoForward);
            Assert.Equal("b", t.Back()!.Value.Json);
            Assert.Equal("a", t.Back()!.Value.Json);
            Assert.Null(t.Back());
            Assert.Equal("b", t.Forward()!.Value.Json);
            Assert.Equal(20, t.AgeOf(1, 20));

            Assert.True(t.Record("x", 40));
            Assert.Equal(3, t.Count);
            Assert.False(t.CanGoForward, "after a change there is no way forward to c");
            Assert.Equal("b", t.Back()!.Value.Json);
        }

        [Fact]
        public void BigTables_KeepFewerPoints()
        {
            var t = new Timeline();
            var big = new string('x', (int)(Timeline.MaxCharacters / 4));
            for (var i = 0; i < 6; i++)
                t.Record(big + i, i * Timeline.IntervalSeconds);
            Assert.Equal(3, t.Count);
            var huge = new string('y', (int)Timeline.MaxCharacters + 10);
            t.Record(huge, 100);
            Assert.Equal(1, t.Count);
            Assert.False(t.CanGoBack);
        }

        [Fact]
        public void OldPoints_FallOff()
        {
            var t = new Timeline();
            for (var i = 0; i < Timeline.Capacity + 5; i++)
                t.Record($"s{i}", i * Timeline.IntervalSeconds);
            Assert.Equal(Timeline.Capacity, t.Count);
            string? oldest = null;
            while (t.CanGoBack)
                oldest = t.Back()!.Value.Json;
            Assert.Equal("s5", oldest);
        }
    }
}
