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

        /// <summary>T12.10: a walking body travels in the frame (position, facing, sofa place, moving); nonsense is refused.</summary>
        [Fact]
        public void Bodies_RoundTrip_AndNonsenseIsRefused()
        {
            var frame = Frame("seat_north", 4);
            frame.Body = new AvatarPose { Position = new Vec3(1.5f, -0.76f, -2f), Yaw = 135f, Place = 3, Moving = true };
            var body = PresenceCodec.Decode(JObject.Parse(PresenceCodec.Encode(frame).ToString()))!.Body!;
            Assert.Equal(new Vec3(1.5f, -0.76f, -2f), body.Position);
            Assert.Equal(135f, body.Yaw);
            Assert.Equal(3, body.Place);
            Assert.True(body.Moving);

            frame.Body = new AvatarPose { Position = new Vec3(0f, -0.76f, 0f), Yaw = 0f };
            var standing = PresenceCodec.Decode(PresenceCodec.Encode(frame))!.Body!;
            Assert.Null(standing.Place);
            Assert.False(standing.Moving);
            Assert.Null(PresenceCodec.Decode(PresenceCodec.Encode(Frame("seat_north", 5)))!.Body);

            var bad = PresenceCodec.Encode(frame);
            bad["body"]!["place"] = -1;
            Assert.Null(PresenceCodec.Decode(bad));
            bad = PresenceCodec.Encode(frame);
            bad["body"]!["p"] = new JArray(0f, 5000f, 0f);
            Assert.Null(PresenceCodec.Decode(bad));
            bad = PresenceCodec.Encode(frame);
            bad["body"]!["yaw"] = "north";
            Assert.Null(PresenceCodec.Decode(bad));
        }

        /// <summary>First person: a raised hand of cards and leaning in travel; a lean outside 0–1 is refused.</summary>
        [Fact]
        public void HandAndLean_RoundTrip()
        {
            var frame = Frame("seat_north", 2);
            frame.HandUp = true;
            frame.Lean = 0.5f;
            var decoded = PresenceCodec.Decode(JObject.Parse(PresenceCodec.Encode(frame).ToString()))!;
            Assert.True(decoded.HandUp);
            Assert.Equal(0.5f, decoded.Lean);
            var plain = PresenceCodec.Decode(PresenceCodec.Encode(Frame("seat_north", 3)))!;
            Assert.False(plain.HandUp);
            Assert.Equal(0f, plain.Lean);
            var bad = PresenceCodec.Encode(frame);
            bad["lean"] = 3f;
            Assert.Null(PresenceCodec.Decode(bad));
        }

        [Fact]
        public void Emotes_RoundTrip()
        {
            var frame = Frame("seat_north", 3);
            frame.Emote = "Wave";
            frame.EmoteCount = 4;
            var decoded = PresenceCodec.Decode(JObject.Parse(PresenceCodec.Encode(frame).ToString()))!;
            Assert.Equal("Wave", decoded.Emote);
            Assert.Equal(4, decoded.EmoteCount);
            Assert.Null(PresenceCodec.Decode(PresenceCodec.Encode(Frame("s", 1)))!.Emote);
        }

        [Fact]
        public void Hover_RoundTrips()
        {
            var frame = Frame("seat_north", 3);
            frame.Hover = "d6";
            Assert.Equal("d6", PresenceCodec.Decode(JObject.Parse(PresenceCodec.Encode(frame).ToString()))!.Hover);
            Assert.Null(PresenceCodec.Decode(PresenceCodec.Encode(Frame("s", 1)))!.Hover);
        }

        [Fact]
        public void Selections_RoundTrip_AndHugeOnesAreRefused()
        {
            var frame = Frame("seat_north", 3);
            frame.Selected.AddRange(new[] { "a", "b" });
            Assert.Equal(new[] { "a", "b" }, PresenceCodec.Decode(JObject.Parse(PresenceCodec.Encode(frame).ToString()))!.Selected);
            Assert.Empty(PresenceCodec.Decode(PresenceCodec.Encode(Frame("s", 1)))!.Selected);
            var huge = JObject.Parse(@"{ ""kind"": ""presence"", ""seat"": ""s"" }");
            huge["sel"] = new JArray(Enumerable.Range(0, PresenceCodec.MaxSelected + 1).Select(i => "o" + i));
            Assert.Null(PresenceCodec.Decode(huge));
        }

        [Theory]
        [InlineData(@"{ ""kind"": ""order"" }")]
        [InlineData(@"{ ""kind"": ""presence"" }")]
        [InlineData(@"{ ""kind"": ""presence"", ""seat"": ""s"", ""pointer"": [1, 2] }")]
        [InlineData(@"{ ""kind"": ""presence"", ""seat"": ""s"", ""pointer"": [1, ""x"", 3] }")]
        [InlineData(@"{ ""kind"": ""presence"", ""seat"": ""s"", ""pointer"": [1e9, 0, 0] }")]
        [InlineData(@"{ ""kind"": ""presence"", ""seat"": ""s"", ""gaze"": [0, ""x"", 0] }")]
        [InlineData(@"{ ""kind"": ""presence"", ""seat"": ""s"", ""emote"": { ""name"": ""<script>"", ""n"": 1 } }")]
        [InlineData(@"{ ""kind"": ""presence"", ""seat"": ""s"", ""emote"": { ""name"": ""Wave"" } }")]
        [InlineData(@"{ ""kind"": ""presence"", ""seat"": ""s"", ""hover"": """" }")]
        [InlineData(@"{ ""kind"": ""presence"", ""seat"": ""s"", ""sel"": [ 3 ] }")]
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
