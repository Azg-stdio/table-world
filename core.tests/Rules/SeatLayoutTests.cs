using System;
using System.Linq;
using Core.Rules;

namespace Core.Tests.Rules
{
    /// <summary>New tables seat 1 to 12 players: hands on the edge, inside the table, apart from each other.</summary>
    public class SeatLayoutTests
    {
        [Fact]
        public void UpToFourSeats_KeepTheClassicSides_OnTheDefaultTable()
        {
            var (size, places) = SeatLayout.Arrange(4);
            Assert.Equal(TableLookDef.DefaultSize, size);
            Assert.Equal(new[] { "seat_south", "seat_north", "seat_east", "seat_west" }, places.Select(p => p.SeatId));
            Assert.Equal(new[] { "hand_south", "hand_north", "hand_east", "hand_west" }, places.Select(p => p.HandId));
            Assert.Equal(new Vec3(0f, 0.05f, -0.42f), places[0].Position);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        [InlineData(7)]
        [InlineData(8)]
        [InlineData(9)]
        [InlineData(10)]
        [InlineData(11)]
        [InlineData(12)]
        public void EveryHand_LiesOnTheTable_FacesItsEdge_AndOverlapsNoOther(int seats)
        {
            var (size, places) = SeatLayout.Arrange(seats);
            Assert.Equal(seats, places.Count);
            Assert.Equal(seats, places.Select(p => p.SeatId).Distinct().Count());
            Assert.Equal(seats, places.Select(p => p.Color).Distinct().Count());
            Assert.True(size.X / size.Y >= 1.59f && size.X / size.Y <= 1.61f, $"aspect {size.X}x{size.Y}");

            var boxes = places.Select(Footprint).ToList();
            foreach (var (minX, maxX, minZ, maxZ) in boxes)
            {
                Assert.True(minX >= -size.X / 2 && maxX <= size.X / 2 && minZ >= -size.Y / 2 && maxZ <= size.Y / 2,
                    $"{seats} seats: a hand leaves the {size.X}x{size.Y} table");
            }
            for (var i = 0; i < boxes.Count; i++)
            for (var j = i + 1; j < boxes.Count; j++)
            {
                var a = boxes[i];
                var b = boxes[j];
                var overlap = a.MinX < b.MaxX && b.MinX < a.MaxX && a.MinZ < b.MaxZ && b.MinZ < a.MaxZ;
                Assert.False(overlap, $"{seats} seats: {places[i].SeatId} overlaps {places[j].SeatId}");
            }
            foreach (var place in places)
            {
                // A hand faces the edge it sits on: its yaw points from the centre towards it.
                var outward = place.Yaw switch { 0f => -place.Position.Z, 180f => place.Position.Z, 90f => -place.Position.X, _ => place.Position.X };
                Assert.True(outward > 0, $"{place.SeatId} faces the wrong edge");
            }
        }

        [Fact]
        public void FiveOrMoreSeats_AreNumberedClockwise_AndTheTableGrows()
        {
            var (small, six) = SeatLayout.Arrange(6);
            Assert.Equal(TableLookDef.DefaultSize, small);
            Assert.Equal(Enumerable.Range(1, 6).Select(n => $"seat_{n}"), six.Select(p => p.SeatId));
            // South side first (east to west), then west, north, east.
            Assert.Equal(new[] { 0f, 0f, 90f, 180f, 180f, 270f }, six.Select(p => p.Yaw));
            Assert.True(six[0].Position.X > six[1].Position.X);

            var (big, twelve) = SeatLayout.Arrange(12);
            Assert.True(big.X > TableLookDef.DefaultSize.X && big.Y > TableLookDef.DefaultSize.Y);
            Assert.Equal(4, twelve.Count(p => p.Yaw == 0f));
            Assert.Equal(2, twelve.Count(p => p.Yaw == 90f));
        }

        [Fact]
        public void TheTable_NeverShrinks_AsSeatsAreAdded()
        {
            var previous = SeatLayout.Arrange(1).TableSize;
            for (var seats = 2; seats <= SeatLayout.MaxSeats; seats++)
            {
                var size = SeatLayout.Arrange(seats).TableSize;
                Assert.True(size.X >= previous.X && size.Y >= previous.Y, $"{seats} seats: {size.X}x{size.Y} < {previous.X}x{previous.Y}");
                previous = size;
            }
        }

        [Fact]
        public void RefusesNoSeatsOrMoreThanTwelve()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => SeatLayout.Arrange(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => SeatLayout.Arrange(SeatLayout.MaxSeats + 1));
        }

        private static (float MinX, float MaxX, float MinZ, float MaxZ) Footprint(SeatLayout.Place place)
        {
            const float depth = 0.12f;
            var sideways = place.Yaw == 90f || place.Yaw == 270f;
            var halfX = (sideways ? depth : place.Width) / 2;
            var halfZ = (sideways ? place.Width : depth) / 2;
            return (place.Position.X - halfX, place.Position.X + halfX, place.Position.Z - halfZ, place.Position.Z + halfZ);
        }
    }
}
