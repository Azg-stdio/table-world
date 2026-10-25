using System.Linq;
using Core.Net;
using Core.Rules;
using Newtonsoft.Json.Linq;

namespace Core.Tests.Net
{
    /// <summary>Live presence: pointers and carried objects, newest frame wins, silent players fade out.</summary>
    public class PresenceTests
    {
        private static PresenceFrame Frame(string seat, long seq, params string[] held) => new PresenceFrame
        {
            Seat = seat,
            Sequence = seq,
            Pointer = new Vec3(0.1f, 0f, -0.2f),
            Gaze = new Vec3(0f, 0f, 0.3f),
            Held = held.Select((id, i) => new HeldPose { Id = id, Position = new Vec3(i, 0.05f, 0f), Rotation = new Vec3(0f, 90f, 180f) }).ToList(),
        };

        [Fact]
        public void Frames_RoundTrip()
        {
            var frame = Frame("seat_north", 7, "ace", "king");
            var decoded = PresenceCodec.Decode(JObject.Parse(PresenceCodec.Encode(frame).ToString()))!;
            Assert.Equal("seat_north", decoded.Seat);
            Assert.Equal(7, decoded.Sequence);
            Assert.Equal(new Vec3(0.1f, 0f, -0.2f), decoded.Pointer);
            Assert.Equal(new Vec3(0f, 0f, 0.3f), decoded.Gaze);
            Assert.Equal(new[] { "ace", "king" }, decoded.Held.Select(h => h.Id));
            Assert.Equal(new Vec3(0f, 90f, 180f), decoded.Held[1].Rotation);

            var noPointer = new PresenceFrame { Seat = "seat_south", Sequence = 1 };
            Assert.Null(PresenceCodec.Decode(PresenceCodec.Encode(noPointer))!.Pointer);
            Assert.Null(PresenceCodec.Decode(PresenceCodec.Encode(noPointer))!.Gaze);
        }

        [Theory]
        [InlineData(@"{ ""kind"": ""order"" }")]
        [InlineData(@"{ ""kind"": ""presence"" }")]
        [InlineData(@"{ ""kind"": ""presence"", ""seat"": ""s"", ""pointer"": [1, 2] }")]
        [InlineData(@"{ ""kind"": ""presence"", ""seat"": ""s"", ""pointer"": [1, ""x"", 3] }")]
        [InlineData(@"{ ""kind"": ""presence"", ""seat"": ""s"", ""pointer"": [1e9, 0, 0] }")]
        [InlineData(@"{ ""kind"": ""presence"", ""seat"": ""s"", ""gaze"": [0, ""x"", 0] }")]
        [InlineData(@"{ ""kind"": ""presence"", ""seat"": ""s"", ""held"": [ { ""id"": """", ""p"": [0, 0, 0], ""r"": [0, 0, 0] } ] }")]
        [InlineData(@"{ ""kind"": ""presence"", ""seat"": ""s"", ""held"": [ { ""id"": ""a"", ""p"": [0, 0], ""r"": [0, 0, 0] } ] }")]
        public void MalformedFrames_AreRefused(string json)
        {
            Assert.Null(PresenceCodec.Decode(JObject.Parse(json)));
        }

        [Fact]
        public void TooManyHeldObjects_AreRefused()
        {
            var frame = Frame("s", 1, Enumerable.Range(0, PresenceCodec.MaxHeld + 1).Select(i => "o" + i).ToArray());
            Assert.Null(PresenceCodec.Decode(PresenceCodec.Encode(frame)));
        }

        [Fact]
        public void Board_KeepsTheNewestFrame_AndDropsLateOnes()
        {
            var board = new PresenceBoard();
            Assert.True(board.Update("2", Frame("seat_north", 5, "ace"), 0));
            Assert.False(board.Update("2", Frame("seat_north", 4, "king"), 0.1), "a late frame is ignored");
            Assert.Equal("seat_north", board.HolderOf("ace", 0.2));
            Assert.Null(board.HolderOf("king", 0.2));
            Assert.True(board.Update("2", Frame("seat_north", 6), 0.3), "dropped the ace");
            Assert.Null(board.HolderOf("ace", 0.3));
        }

        [Fact]
        public void SilentPlayers_FadeOut()
        {
            var board = new PresenceBoard();
            board.Update("2", Frame("seat_north", 1, "ace"), 10);
            board.Update("3", Frame("seat_east", 1, "die"), 10);
            Assert.Equal(2, board.Active(10.5).Count());
            board.Update("3", Frame("seat_east", 2, "die"), 11);
            var later = 10 + PresenceBoard.TimeoutSeconds + 0.1;
            Assert.Equal(new[] { "3" }, board.Active(later).Select(a => a.Peer));
            Assert.Null(board.HolderOf("ace", later));
            Assert.Equal("seat_east", board.HolderOf("die", later));
            board.Remove("3");
            Assert.Empty(board.Active(later));
        }
    }
}
