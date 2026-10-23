using Core.Rules;
using UnityEngine;

namespace TableWorld.View
{
    /// <summary>Core ↔ Unity value conversions. Core uses the same axes and Euler order as Unity, so these are copies.</summary>
    public static class CoreConversions
    {
        public static Vector3 ToUnity(this Vec3 v) => new Vector3(v.X, v.Y, v.Z);
        public static Vec3 ToCore(this Vector3 v) => new Vec3(v.x, v.y, v.z);
        public static Vector2 ToUnity(this Vec2 v) => new Vector2(v.X, v.Y);
        public static Quaternion ToRotation(this Vec3 euler) => Quaternion.Euler(euler.X, euler.Y, euler.Z);

        public static Color ToColor(this float[] tint, Color fallback)
        {
            if (tint == null || tint.Length < 3)
                return fallback;
            return new Color(tint[0], tint[1], tint[2], tint.Length > 3 ? tint[3] : 1f);
        }
    }
}
