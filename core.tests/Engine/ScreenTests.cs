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

        /// <summary>T12.12: the song queue: anyone adds, you or the host take out, the next plays from the start and leaves the queue.</summary>
        [Fact]
        public void TheSongQueue_AddsTakesOutAndPlaysNext()
        {
            var table = Table();
            Assert.True(table.Execute(new QueueSongCommand { Actor = "red", VideoId = "M7lc1UVf-VE", Title = "  Bohemian\u0007 Rhapsody (karaoke)  " }).Ok);
            Assert.True(table.Execute(new QueueSongCommand { Actor = "blue", VideoId = "aqz-KE-bpKQ", Title = new string('x', 300) }).Ok);
            Assert.True(table.Execute(new QueueSongCommand { Actor = "blue", VideoId = "dQw4w9WgXcQ" }).Ok);
            Assert.False(table.Execute(new QueueSongCommand { Actor = "blue", VideoId = "not a video" }).Ok);
            var queue = table.State.SongQueue;
            Assert.Equal(3, queue.Count);
            Assert.Equal("Bohemian Rhapsody (karaoke)", queue[0].Title);
            Assert.Equal(QueuedSong.MaxTitle, queue[1].Title!.Length);
            Assert.Null(queue[2].Title);
            Assert.Equal("red", queue[0].Seat);

            var refused = table.Execute(new RemoveSongCommand { Actor = "red", Index = 2 });
            Assert.False(refused.Ok, "only who added it");
            Assert.Equal(ErrorCodes.NotYourSeat, refused.Error!.Code);
            Assert.True(table.Execute(new RemoveSongCommand { Actor = "blue", Index = 2 }).Ok);
            Assert.True(table.Execute(new RemoveSongCommand { Index = 1 }).Ok, "the host takes any out");
            Assert.False(table.Execute(new RemoveSongCommand { Index = 5 }).Ok);

            Assert.True(table.Execute(new PlayNextSongCommand { At = 5_000 }).Ok);
            Assert.Equal("M7lc1UVf-VE", table.State.Screen!.VideoId);
            Assert.True(table.State.Screen.Playing);
            Assert.Equal(0, table.State.Screen.Position, 6);
            Assert.Equal(5_000, table.State.Screen.At, 6);
            Assert.Empty(table.State.SongQueue);
            Assert.True(table.Execute(new PlayNextSongCommand { At = 9_000 }).Ok);
            Assert.Null(table.State.Screen); // nothing queued: the screen goes off

            for (var i = 0; i < TableEngine.MaxQueuedSongs; i++)
                Assert.True(table.Execute(new QueueSongCommand { VideoId = "M7lc1UVf-VE" }).Ok);
            Assert.False(table.Execute(new QueueSongCommand { VideoId = "M7lc1UVf-VE" }).Ok, "the queue has a limit");
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
