using System.Linq;
using Core.Net;
using Newtonsoft.Json.Linq;

namespace Core.Tests.Net
{
    /// <summary>G11 chat and G24 /roll: the host stamps senders, cleans text, rate limits and rolls.</summary>
    public class ChatTests
    {
        private static ChatRoom Room(int die = 3) => new ChatRoom(max => (die - 1) % max);

        [Fact]
        public void Say_StampsTheSenderSeat_AndSpectatorsSpeakAsSpectator()
        {
            var room = Room();
            var said = room.Say("1", "north", "  hello table  ", 0, out var reply);
            Assert.Null(reply);
            Assert.Equal(ChatKind.Say, said!.Kind);
            Assert.Equal("north", said.Seat);
            Assert.Equal("north", said.From);
            Assert.Equal("hello table", said.Text);

            var watching = room.Say("2", null, "hi", 0, out _);
            Assert.Null(watching!.Seat);
            Assert.Equal("Spectator", watching.From);
            Assert.NotEqual(said.Id, watching.Id);
            Assert.Equal(2, room.Log.Entries.Count);
        }

        [Fact]
        public void Clean_DropsControlCharacters_TrimsAndCutsLongLines()
        {
            Assert.Null(ChatRoom.Clean("   \n\t "));
            Assert.Equal("a b", ChatRoom.Clean("a\nb"));
            Assert.Equal(ChatRoom.MaxLength, ChatRoom.Clean(new string('x', 5000))!.Length);
            // A cut never leaves half of an emoji behind.
            var emoji = new string('x', ChatRoom.MaxLength - 1) + "😀";
            Assert.False(char.IsHighSurrogate(ChatRoom.Clean(emoji)!.Last()));
            Assert.Null(Room().Say("1", "north", "\n\n", 0, out var reply));
            Assert.Null(reply);
        }

        [Fact]
        public void Say_TooFast_IsRefusedPrivately_ThenAllowedAgain()
        {
            var room = Room();
            for (var i = 0; i < ChatRoom.BurstLines; i++)
                Assert.NotNull(room.Say("1", "north", $"line {i}", 0.1 * i, out _));
            Assert.Null(room.Say("1", "north", "spam", 1, out var reply));
            Assert.Contains("too fast", reply);
            Assert.NotNull(room.Say("2", "south", "others still talk", 1, out _));
            Assert.NotNull(room.Say("1", "north", "later", ChatRoom.BurstSeconds + 1, out _));
        }

        [Fact]
        public void Roll_RollsOnTheHost_AndEveryoneSeesTheDice()
        {
            var room = Room(die: 4);
            var roll = room.Say("1", "north", "/roll 3d6+2", 0, out var reply);
            Assert.Null(reply);
            Assert.Equal(ChatKind.Roll, roll!.Kind);
            Assert.Equal(new[] { 4, 4, 4 }, roll.Dice);
            Assert.Equal("rolled 3d6+2: 4 + 4 + 4 + 2 = 14", roll.Text);

            Assert.Equal("rolled 1d20: 4", room.Say("1", "north", "/r d20", 1, out _)!.Text);
            Assert.Equal("rolled 1d6: 4", room.Say("1", "north", "/ROLL", 2, out _)!.Text);
            Assert.Equal("rolled 2d10−3: 4 + 4 − 3 = 5", room.Say("1", "north", "/roll 2d10-3", 3, out _)!.Text);
        }

        [Theory]
        [InlineData("/roll banana", "not a roll")]
        [InlineData("/roll 0d6", "between 1 and")]
        [InlineData("/roll 1000d6", "between 1 and")]
        [InlineData("/roll 1d1", "faces")]
        [InlineData("/roll 1d99999999999", "faces")]
        [InlineData("/roll 1d6+5000", "at most")]
        [InlineData("/dance", "Unknown command")]
        [InlineData("/help", "/roll")]
        public void BadCommandsAndHelp_AnswerOnlyTheSender(string text, string expected)
        {
            var room = Room();
            Assert.Null(room.Say("1", "north", text, 0, out var reply));
            Assert.Contains(expected, reply);
            Assert.Empty(room.Log.Entries);
        }

        [Fact]
        public void Messages_RoundTrip_AndMalformedEntriesAreSkipped()
        {
            var room = Room();
            room.Say("1", "north", "/roll 2d6", 0, out _);
            room.Notice("south joined");
            var read = ChatRoom.Read(JObject.Parse(room.History().ToString()));
            Assert.Equal(2, read.Count);
            Assert.Equal(ChatKind.Roll, read[0].Kind);
            Assert.Equal(new[] { 3, 3 }, read[0].Dice);
            Assert.Equal("north", read[0].Seat);
            Assert.Equal(ChatKind.Notice, read[1].Kind);
            Assert.Equal("", read[1].From);

            var hostile = JObject.Parse(@"{ ""kind"": ""chat"", ""entries"": [ 5, { ""type"": ""say"" }, { ""type"": ""shout"", ""text"": ""x"" },
                { ""type"": ""say"", ""text"": ""ok\nfine"", ""seat"": 3, ""dice"": [1, ""x""] } ] }");
            var entry = Assert.Single(ChatRoom.Read(hostile));
            Assert.Equal("ok fine", entry.Text);
            Assert.Null(entry.Seat);
            Assert.Equal(new[] { 1 }, entry.Dice);
        }

        [Fact]
        public void Log_SkipsEntriesItAlreadyHas_AndKeepsTheNewest()
        {
            var log = new ChatLog();
            log.Add(new ChatEntry { Id = 1, Text = "a" });
            log.Add(new ChatEntry { Id = 1, Text = "a again" });
            log.Add(new ChatEntry { Text = "local, no id" });
            log.Add(new ChatEntry { Text = "local, no id" });
            Assert.Equal(3, log.Entries.Count);
            for (var i = 0; i < ChatLog.Capacity; i++)
                log.Add(new ChatEntry { Id = 100 + i, Text = $"{i}" });
            Assert.Equal(ChatLog.Capacity, log.Entries.Count);
            Assert.Equal($"{ChatLog.Capacity - 1}", log.Entries.Last().Text);
        }

        [Fact]
        public void History_SendsOnlyTheLastLines()
        {
            var room = Room();
            for (var i = 0; i < ChatRoom.HistorySize + 20; i++)
                room.Notice($"n{i}");
            var history = ChatRoom.Read(room.History());
            Assert.Equal(ChatRoom.HistorySize, history.Count);
            Assert.Equal($"n{ChatRoom.HistorySize + 19}", history.Last().Text);
        }
    }
}
