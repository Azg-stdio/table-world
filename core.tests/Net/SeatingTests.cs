using Core.Net;

namespace Core.Tests.Net
{
    /// <summary>G10: choosing seats at a hosted table.</summary>
    public class SeatingTests
    {
        private static readonly string[] Seats = { "south", "north", "east" };

        [Fact]
        public void Newcomers_TakeFreeSeatsInOrder_ThenWatch()
        {
            var seating = new Seating();
            Assert.Equal("south", seating.Join(SessionHost.HostId, Seats));
            Assert.Equal("north", seating.Join("1", Seats));
            Assert.Equal("east", seating.Join("2", Seats));
            Assert.Null(seating.Join("3", Seats));
            seating.Leave("1");
            Assert.False(seating.IsTaken("north"));
            Assert.Equal("north", seating.Join("4", Seats));
        }

        [Fact]
        public void Sit_MovesToAFreeSeat_RefusesTakenOrUnknownOnes_AndSpectatingFreesTheSeat()
        {
            var seating = new Seating();
            seating.Join(SessionHost.HostId, Seats);
            seating.Join("1", Seats);

            Assert.False(seating.Sit("1", "south", Seats, out var taken));
            Assert.Contains("already", taken);
            Assert.False(seating.Sit("1", "west", Seats, out var unknown));
            Assert.Contains("no seat", unknown);
            Assert.Equal("north", seating.Seat("1"));

            Assert.True(seating.Sit("1", "north", Seats, out _), "sitting where you are is fine");
            Assert.True(seating.Sit("1", null, Seats, out _));
            Assert.Null(seating.Seat("1"));
            Assert.True(seating.Sit(SessionHost.HostId, "north", Seats, out _));
            Assert.True(seating.Sit("1", "south", Seats, out _));

            var read = Seating.Read(seating.Message());
            Assert.Equal(SessionHost.HostId, read["north"]);
            Assert.Equal("1", read["south"]);
            Assert.Equal(2, read.Count);
        }

        [Fact]
        public void AnotherTable_SendsPlayersOfVanishedSeatsToWatch()
        {
            var seating = new Seating();
            seating.Join(SessionHost.HostId, Seats);
            seating.Join("1", Seats);
            seating.Keep(new[] { "south" });
            Assert.Equal("south", seating.Seat(SessionHost.HostId));
            Assert.Null(seating.Seat("1"));
        }
    }
}
