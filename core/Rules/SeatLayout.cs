using System;
using System.Collections.Generic;

namespace Core.Rules
{
    /// <summary>
    /// Where seats sit around a new table: each seat gets a colour, a hand zone on the table's edge facing it, and the
    /// table grows when the hands no longer fit. Up to four seats keep the classic sides (<c>seat_south</c>,
    /// <c>seat_north</c>, <c>seat_east</c>, <c>seat_west</c>) on the default 1.6 × 1.0 m table; five or more are numbered
    /// <c>seat_1</c>… in clockwise order seen from above, starting at the east end of the south side, so list order is
    /// turn order. More people than seats can still join a session: they watch, talk and take a seat that frees up.
    /// </summary>
    public static class SeatLayout
    {
        public const int MaxSeats = 12;

        /// <summary>Seat colours in seat order (names the game draws; TTS's player colours plus two).</summary>
        public static readonly string[] Colors =
            { "red", "blue", "green", "yellow", "orange", "purple", "pink", "teal", "white", "brown", "lime", "maroon" };

        /// <summary>Hand width for five seats or more, metres, and the distance between neighbouring hands' centres.</summary>
        public const float HandWidth = 0.5f;
        public const float HandPitch = 0.55f;
        /// <summary>
        /// Room left at each end of a side so hands on adjacent sides do not meet at the corner. The long sides need more:
        /// a short side's hands reach further in from their edge than a long side's.
        /// </summary>
        public const float LongCornerRoom = 0.25f;
        public const float ShortCornerRoom = 0.2f;
        /// <summary>How far in from the long and short edges the hands' centres lie.</summary>
        public const float LongInset = 0.08f;
        public const float ShortInset = 0.12f;

        public sealed class Place
        {
            public string SeatId { get; set; } = "";
            public string HandId { get; set; } = "";
            public string Color { get; set; } = "";
            /// <summary>Hand zone centre on the table, metres (y = 0.05 keeps cards inside the zone box).</summary>
            public Vec3 Position { get; set; }
            /// <summary>Degrees about the vertical; 0 faces a seat on the south edge.</summary>
            public float Yaw { get; set; }
            public float Width { get; set; }
        }

        /// <summary>
        /// <paramref name="players"/> of <paramref name="seats"/> (in seat order, which goes around the table) spread as
        /// evenly as they go: 3 players at a 6-seat table sit at every other seat, not side by side.
        /// </summary>
        public static List<string> Spread(IReadOnlyList<string> seats, int players)
        {
            var chosen = new List<string>();
            players = Math.Max(0, Math.Min(players, seats.Count));
            for (var i = 0; i < players; i++)
                chosen.Add(seats[(int)Math.Floor(i * (double)seats.Count / players)]);
            return chosen;
        }

        /// <summary>The table size (width × depth, metres) and the places for <paramref name="seats"/> seats (1 to <see cref="MaxSeats"/>).</summary>
        public static (Vec2 TableSize, List<Place> Places) Arrange(int seats)
        {
            if (seats < 1 || seats > MaxSeats)
                throw new ArgumentOutOfRangeException(nameof(seats), $"1 to {MaxSeats} seats.");
            var places = new List<Place>();
            if (seats <= 4)
            {
                var sides = new[] { ("south", 0f, -0.42f, 0f, 0.6f), ("north", 0f, 0.42f, 180f, 0.6f), ("east", 0.68f, 0f, 270f, 0.5f), ("west", -0.68f, 0f, 90f, 0.5f) };
                for (var i = 0; i < seats; i++)
                {
                    var (where, x, z, yaw, handWidth) = sides[i];
                    places.Add(new Place { SeatId = $"seat_{where}", HandId = $"hand_{where}", Color = Colors[i], Position = new Vec3(x, 0.05f, z), Yaw = yaw, Width = handWidth });
                }
                return (TableLookDef.DefaultSize, places);
            }

            // One hand on each short side, two from nine seats (so the table never shrinks as seats are added); the rest split between the long sides, south first.
            var shortSide = seats >= 9 ? 2 : 1;
            var rest = seats - 2 * shortSide;
            var south = (rest + 1) / 2;
            var north = rest - south;
            var defaultSize = TableLookDef.DefaultSize;
            var aspect = defaultSize.X / defaultSize.Y;
            var width = Math.Max(defaultSize.X, south * HandPitch + 2 * LongCornerRoom);
            var depth = Math.Max(Math.Max(defaultSize.Y, width / aspect), shortSide * HandPitch + 2 * ShortCornerRoom);
            width = Math.Max(width, depth * aspect);
            width = (float)Math.Round(width, 2);
            depth = (float)Math.Round(depth, 2);

            // Clockwise from above: south east→west, west south→north, north west→east, east north→south.
            void Side(int count, Func<float, (float X, float Z)> at, float yaw)
            {
                for (var i = 0; i < count; i++)
                {
                    var along = (i - (count - 1) / 2f) * HandPitch;
                    var (x, z) = at(along);
                    var n = places.Count + 1;
                    places.Add(new Place
                    {
                        SeatId = $"seat_{n}", HandId = $"hand_{n}", Color = Colors[n - 1],
                        Position = new Vec3((float)Math.Round(x, 3), 0.05f, (float)Math.Round(z, 3)), Yaw = yaw, Width = HandWidth,
                    });
                }
            }
            var halfW = width / 2f;
            var halfD = depth / 2f;
            Side(south, a => (-a, -(halfD - LongInset)), 0f);
            Side(shortSide, a => (-(halfW - ShortInset), a), 90f);
            Side(north, a => (a, halfD - LongInset), 180f);
            Side(shortSide, a => (halfW - ShortInset, -a), 270f);
            return (new Vec2(width, depth), places);
        }
    }
}
