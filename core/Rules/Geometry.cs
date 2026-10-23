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

        /// <summary>True when the point lies inside the zone's box (size × scale, rotated with the zone). Snap points contain nothing.</summary>
        public static bool Contains(Zone zone, Vec3 point)
        {
            if (zone is SnapPoint)
                return false;
            var t = zone.Transform;
            var local = InverseRotate(t.Rotation, Subtract(point, t.Position));
            const float epsilon = 1e-5f;
            return Math.Abs(local.X) <= zone.Size.X * t.Scale.X / 2 + epsilon
                && Math.Abs(local.Y) <= zone.Size.Y * t.Scale.Y / 2 + epsilon
                && Math.Abs(local.Z) <= zone.Size.Z * t.Scale.Z / 2 + epsilon;
        }

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
