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
    /// Round, every seat is numbered, clockwise from the south (<see cref="ArrangeRound"/>).
    /// </summary>
    public static class SeatLayout
    {
        public const int MaxSeats = 12;

        /// <summary>Seat colours in seat order (names the game draws; TTS's player colours plus two).</summary>
        public static readonly string[] Colors =
            { "red", "blue", "green", "yellow", "orange", "purple", "pink", "teal", "white", "brown", "lime", "maroon" };

        /// <summary>RGB (0–1) of a seat colour name, as the game draws it; white for an unknown name.</summary>
        public static float[] Rgb(string color) => color.ToLowerInvariant() switch
        {
            "red" => new[] { 0.86f, 0.2f, 0.18f },
            "blue" => new[] { 0.2f, 0.42f, 0.9f },
            "green" => new[] { 0.25f, 0.75f, 0.3f },
            "yellow" => new[] { 0.95f, 0.85f, 0.2f },
            "orange" => new[] { 0.95f, 0.55f, 0.15f },
            "purple" => new[] { 0.6f, 0.3f, 0.8f },
            "pink" => new[] { 0.95f, 0.5f, 0.7f },
            "teal" => new[] { 0.15f, 0.7f, 0.7f },
            "brown" => new[] { 0.5f, 0.32f, 0.18f },
            "lime" => new[] { 0.65f, 0.85f, 0.15f },
            "maroon" => new[] { 0.5f, 0.08f, 0.15f },
            _ => new[] { 0.95f, 0.95f, 0.95f },
        };

        /// <summary>
        /// Where a seat's own pieces go: in front of its hand, towards the centre, <paramref name="along"/> metres to the
        /// player's right of the hand's middle.
        /// </summary>
        public static Vec3 InFrontOf(Place place, float along, float inward = 0.13f)
        {
            var yaw = place.Yaw * (float)Math.PI / 180f;
            var inX = (float)Math.Sin(yaw);
            var inZ = (float)Math.Cos(yaw);
            return new Vec3(place.Position.X + inX * inward + inZ * along, 0f, place.Position.Z + inZ * inward - inX * along);
        }

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

        /// <summary>Smallest round table, metres across (as wide as the default table).</summary>
        public const float RoundMinDiameter = 1.6f;
        /// <summary>How far in from a round table's edge the hands' centres lie.</summary>
        public const float RoundInset = 0.12f;

        /// <summary>
        /// The table size (width × depth, metres) and the places for <paramref name="seats"/> seats (1 to
        /// <see cref="MaxSeats"/>), round the table's <paramref name="shape"/>.
        /// </summary>
        public static (Vec2 TableSize, List<Place> Places) Arrange(int seats, TableShape shape = TableShape.Rectangle)
        {
            if (seats < 1 || seats > MaxSeats)
                throw new ArgumentOutOfRangeException(nameof(seats), $"1 to {MaxSeats} seats.");
            if (shape == TableShape.Round)
                return ArrangeRound(seats);
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

        /// <summary>
        /// Seats evenly round a round table, <c>seat_1</c>… clockwise seen from above starting at the south, each hand
        /// facing the centre. The table grows past <see cref="RoundMinDiameter"/> when neighbouring hands would be closer
        /// than <see cref="HandPitch"/> (ten seats: about 2 m across).
        /// </summary>
        public static (Vec2 TableSize, List<Place> Places) ArrangeRound(int seats)
        {
            if (seats < 1 || seats > MaxSeats)
                throw new ArgumentOutOfRangeException(nameof(seats), $"1 to {MaxSeats} seats.");
            var places = new List<Place>();
            // The chord between neighbours' centres is at least a hand's pitch.
            var handRadius = seats < 2 ? 0f : HandPitch / 2f / (float)Math.Sin(Math.PI / seats);
            var diameter = (float)Math.Round(Math.Max(RoundMinDiameter, 2f * (handRadius + RoundInset)), 2);
            handRadius = diameter / 2f - RoundInset;
            var chord = seats < 2 ? HandWidth + 0.05f : 2f * handRadius * (float)Math.Sin(Math.PI / seats);
            var width = (float)Math.Round(Math.Min(HandWidth, chord - 0.05f), 2);
            for (var i = 0; i < seats; i++)
            {
                var yaw = 360f * i / seats;
                var rad = yaw * (float)Math.PI / 180f;
                // Yaw 0 faces a seat on the south edge: a hand lies opposite the way it faces, seen from the centre.
                var x = -handRadius * (float)Math.Sin(rad);
                var z = -handRadius * (float)Math.Cos(rad);
                places.Add(new Place
                {
                    SeatId = $"seat_{i + 1}", HandId = $"hand_{i + 1}", Color = Colors[i],
                    Position = new Vec3((float)Math.Round(x, 3), 0.05f, (float)Math.Round(z, 3)), Yaw = (float)Math.Round(yaw, 2), Width = width,
                });
            }
            return (new Vec2(diameter, diameter), places);
        }
    }
}
