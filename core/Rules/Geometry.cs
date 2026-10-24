using System;

namespace Core.Rules
{
    /// <summary>
    /// The little 3D math core needs, matching Unity's conventions: Euler degrees applied Z, then X, then Y,
    /// left-handed axes, Y up. Unity reproduces the same numbers with <c>Quaternion.Euler(x, y, z) * v</c>.
    /// </summary>
    public static class Geometry
    {
        private const double DegToRad = Math.PI / 180.0;

        public static Vec3 Add(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 Subtract(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        /// <summary>Rotates <paramref name="v"/> by the Euler angles <paramref name="euler"/> (degrees).</summary>
        public static Vec3 Rotate(Vec3 euler, Vec3 v)
        {
            double x = v.X, y = v.Y, z = v.Z;
            RotateZ(euler.Z, ref x, ref y);
            RotateX(euler.X, ref y, ref z);
            RotateY(euler.Y, ref x, ref z);
            return new Vec3((float)x, (float)y, (float)z);
        }

        /// <summary>Inverse of <see cref="Rotate"/>: world direction → local direction.</summary>
        public static Vec3 InverseRotate(Vec3 euler, Vec3 v)
        {
            double x = v.X, y = v.Y, z = v.Z;
            RotateY(-euler.Y, ref x, ref z);
            RotateX(-euler.X, ref y, ref z);
            RotateZ(-euler.Z, ref x, ref y);
            return new Vec3((float)x, (float)y, (float)z);
        }

        /// <summary>
        /// True when the point lies inside the zone's box (size × scale, rotated with the zone). Snap points contain nothing.
        /// <paramref name="halfHeight"/> is the object's own half-height: TTS counts an object in a trigger when its collider
        /// overlaps it, so a card lying on a panel counts for a thin zone that starts just above the card's centre
        /// (Pokémon Sinnoh's arena zones).
        /// </summary>
        public static bool Contains(Zone zone, Vec3 point, float halfHeight = 0f)
        {
            if (zone is SnapPoint)
                return false;
            var t = zone.Transform;
            var local = InverseRotate(t.Rotation, Subtract(point, t.Position));
            const float epsilon = 1e-5f;
            return Math.Abs(local.X) <= zone.Size.X * t.Scale.X / 2 + epsilon
                && Math.Abs(local.Y) <= zone.Size.Y * t.Scale.Y / 2 + halfHeight + epsilon
                && Math.Abs(local.Z) <= zone.Size.Z * t.Scale.Z / 2 + epsilon;
        }

        /// <summary>Slack beyond an object's half-height for zone tests: TTS's physics keeps contact gaps of about this much.</summary>
        public const float ZoneSlack = 0.0015f;

        /// <summary>About half an object's height, for zone tests (thin pieces exactly, others roughly).</summary>
        public static float HalfHeight(TableObject obj)
        {
            var height = obj switch
            {
                Card _ => CardThickness,
                Deck deck => CardThickness * Math.Max(1, deck.Cards.Count),
                Tile tile => (tile.Thickness ?? 0.003f) * Math.Max(1, tile.Count),
                Token token => (token.Thickness ?? 0.003f) * Math.Max(1, token.Count),
                _ => 0.02f,
            };
            return height * Math.Abs(obj.Transform.Scale.Y) / 2f + ZoneSlack;
        }

        /// <summary>A card's thickness (the view draws cards this thick).</summary>
        public const float CardThickness = 0.0004f;

        /// <summary>
        /// The yaw of a card or tile that faces a zone's front (its −Z side, where a hand zone's owner sits): TTS draws
        /// images upright from +Z at yaw 0, so it is the zone's yaw turned 180° (ADR 0005).
        /// </summary>
        public static float FacingYaw(float zoneYaw) => NormalizeAngle(zoneYaw + 180f);

        /// <summary>Angle normalized to [0, 360).</summary>
        public static float NormalizeAngle(float degrees)
        {
            var a = degrees % 360f;
            return a < 0 ? a + 360f : a;
        }

        private static void RotateX(double degrees, ref double y, ref double z)
        {
            var (s, c) = SinCos(degrees);
            var ny = y * c - z * s;
            z = y * s + z * c;
            y = ny;
        }

        private static void RotateY(double degrees, ref double x, ref double z)
        {
            var (s, c) = SinCos(degrees);
            var nx = x * c + z * s;
            z = -x * s + z * c;
            x = nx;
        }

        private static void RotateZ(double degrees, ref double x, ref double y)
        {
            var (s, c) = SinCos(degrees);
            var nx = x * c - y * s;
            y = x * s + y * c;
            x = nx;
        }

        private static (double Sin, double Cos) SinCos(double degrees) => (Math.Sin(degrees * DegToRad), Math.Cos(degrees * DegToRad));
    }
}
