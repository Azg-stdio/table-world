using System.Linq;
using Core.Engine;
using Core.Net;
using Core.Rules;
using Core.Tests.Engine;
using Newtonsoft.Json.Linq;

namespace Core.Tests.Net
{
    /// <summary>G23: names, promotion, Game Master, teams and mute at a hosted table.</summary>
    public class PlayersTests
    {
        [Fact]
        public void Names_AreCleaned_Shortened_AndDefaultToPlayerN()
        {
            var roster = new Roster();
            Assert.Equal("Player 1", roster.Of(SessionHost.HostId).Name);
            Assert.Equal("Player 2", roster.Of("1").Name);
            Assert.True(roster.Rename("1", "  Ana\nMaría  "));
            Assert.Equal("Ana María", roster.Of("1").Name);
            Assert.False(roster.Rename("1", " \t "), "an empty name keeps the old one");
            Assert.Equal("Ana María", roster.Of("1").Name);
            roster.Rename("1", new string('x', 100));
            Assert.Equal(PlayerInfo.MaxNameLength, roster.Of("1").Name.Length);
        }

        [Fact]
        public void Roster_RoundTrips_AndHostileValuesAreIgnored()
        {
            var roster = new Roster();
            roster.Rename("1", "Ana");
            roster.Of("1").Promoted = true;
            roster.Of("1").Team = Team.Hearts;
            roster.Of("2").GameMaster = true;
            roster.Of("3").Muted = true;
            var read = Roster.Read(JObject.Parse(roster.ToJson().ToString()));
            Assert.Equal("Ana", read["1"].Name);
            Assert.True(read["1"].Promoted);
            Assert.Equal(Team.Hearts, read["1"].Team);
            Assert.True(read["2"].GameMaster);
            Assert.True(read["3"].Muted);

            var hostile = Roster.Read(JObject.Parse(@"{ ""1"": { ""name"": 5, ""promoted"": ""yes"", ""team"": ""Wizards"" }, ""2"": 7 }"));
            Assert.Equal("", hostile["1"].Name);
            Assert.False(hostile["1"].Promoted);
            Assert.Equal(Team.None, hostile["1"].Team);
            Assert.False(hostile["2"].GameMaster);
        }

        [Fact]
        public void OnlyTheHostAndPromotedPlayers_MayHost_AndTheGameMasterSeesEverything()
        {
            var roster = new Roster();
            roster.Of("1");
            Assert.True(roster.MayHost(SessionHost.HostId));
            Assert.False(roster.MayHost("1"));
            roster.Of("1").Promoted = true;
            Assert.True(roster.MayHost("1"));
            Assert.False(roster.MayHost("nobody"));
            Assert.False(roster.SeesEverything("1"));
            roster.Of("1").GameMaster = true;
            Assert.True(roster.SeesEverything("1"));
            roster.Leave("1");
            Assert.False(roster.MayHost("1"), "a player who left loses what they were given");
        }

        [Fact]
        public void HostOnlyCommands_FromAPromotedClient_AreAccepted()
        {
            var roster = new Roster();
            var host = new SessionHost(new GameEngine(TableJson.Parse(EngineFixture.Json).State!, 1)) { MayHost = roster.MayHost };
            var physics = new JObject { ["kind"] = "intent", ["seq"] = 1, ["command"] = CommandCodec.Encode(new SetPhysicsCommand { Mode = PhysicsMode.Locked }) };
            var refused = host.Receive("1", physics).Single().Message;
            Assert.False((bool)refused["ok"]!);
            Assert.Equal(ErrorCodes.HostOnly, (string)refused["error"]!["code"]!);

            roster.Of("1").Promoted = true;
            var accepted = host.Receive("1", physics).Single().Message;
            Assert.True((bool)accepted["ok"]!);
            Assert.Equal(PhysicsMode.Locked, host.Game.State.Physics);

            var client = new SessionClient("1");
            client.Receive(host.Snapshot("1").Message);
            Assert.Null(client.Submit(new SetPhysicsCommand()).Intent);
            client.MayHost = true;
            Assert.NotNull(client.Submit(new SetPhysicsCommand()).Intent);
        }

        [Fact]
        public void TeamChat_GoesToTheTeam_HistoryKeepsTeamsApart_AndMutedPlayersAreRefused()
        {
            var room = new ChatRoom(max => 0);
            var ana = new Speaker { Participant = "1", Seat = "north", Name = "Ana", Team = Team.Hearts };
            var said = room.Say(ana, "hello all", 0, out _);
            Assert.Equal("Ana", said!.From);
            Assert.Equal(Team.None, said.Team);
            var secret = room.Say(ana, "/t they have the ace", 1, out _);
            Assert.Equal(Team.Hearts, secret!.Team);
            Assert.Equal("they have the ace", secret.Text);
            Assert.Equal(Team.Hearts, ChatRoom.Read(ChatRoom.Message(new[] { secret })).Single().Team);

            Assert.Equal(2, ChatRoom.Read(room.History(Team.Hearts)).Count);
            Assert.Single(ChatRoom.Read(room.History(Team.Spades)));
            Assert.Single(ChatRoom.Read(room.History()));

            Assert.Null(room.Say(new Speaker { Participant = "2", Name = "Bo" }, "/t psst", 2, out var noTeam));
            Assert.Contains("not on a team", noTeam);
            Assert.Null(room.Say(new Speaker { Participant = "3", Name = "Cy", Muted = true }, "hi", 3, out var muted));
            Assert.Contains("muted", muted);
        }
    }
}
