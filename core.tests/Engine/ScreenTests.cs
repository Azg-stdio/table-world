using Core.Engine;
using Core.Net;
using Core.Rules;

namespace Core.Tests.Engine
{
    /// <summary>T12.11: the karaoke room's big screen is shared table state: a video, playing or paused, where it is at a host time.</summary>
    public class ScreenTests
    {
        private static TableEngine Table() => new TableEngine(TableJson.Parse("{\"schemaVersion\":\"0.1\",\"objects\":[]}").State!, 1);

        [Fact]
        public void ThePosition_RunsWithTheHostsClock_OnlyWhilePlaying()
        {
            var playing = new ScreenState { VideoId = "M7lc1UVf-VE", Playing = true, Position = 30, At = 10_000 };
            Assert.Equal(30, playing.PositionAt(10_000), 6);
            Assert.Equal(42.5, playing.PositionAt(22_500), 6);
            Assert.Equal(0, new ScreenState { Playing = true, Position = 0, At = 5_000 }.PositionAt(1_000), 6);
            var paused = new ScreenState { VideoId = "M7lc1UVf-VE", Playing = false, Position = 30, At = 10_000 };
            Assert.Equal(30, paused.PositionAt(99_000), 6);
        }

        [Fact]
        public void SettingTheScreen_IsCheckedAndShared()
        {
            var table = Table();
            var result = table.Execute(new SetScreenCommand { VideoId = "M7lc1UVf-VE", Playing = true, Position = 0, At = 1_234 });
            Assert.True(result.Ok, result.Error?.ToString());
            Assert.Contains(result.Events, e => e is ScreenChanged);
            Assert.Equal("M7lc1UVf-VE", table.State.Screen!.VideoId);
            Assert.Equal(1_234, table.State.Screen.At);

            Assert.False(table.Execute(new SetScreenCommand { VideoId = "https://youtu.be/M7lc1UVf-VE", Playing = true }).Ok, "an id, not a link");
            Assert.False(table.Execute(new SetScreenCommand { VideoId = "M7lc1UVf-VE", Position = -1 }).Ok);
            Assert.False(table.Execute(new SetScreenCommand { VideoId = "M7lc1UVf-VE", At = double.NaN }).Ok);
            Assert.Equal(1_234, table.State.Screen!.At, 6);

            // A save keeps it; a command crosses the network as itself.
            var again = TableJson.Parse(TableJson.Serialize(table.State)).State!;
            Assert.Equal("M7lc1UVf-VE", again.Screen!.VideoId);
            Assert.True(again.Screen.Playing);
            var sent = (SetScreenCommand)CommandCodec.Decode(CommandCodec.Encode(new SetScreenCommand { VideoId = "M7lc1UVf-VE", Playing = false, Position = 61.5, At = 9_000 }));
            Assert.Equal(61.5, sent.Position, 6);
            Assert.False(sent.Playing);

            Assert.True(table.Execute(new SetScreenCommand { VideoId = null }).Ok);
            Assert.Null(table.State.Screen);
        }
    }
}
