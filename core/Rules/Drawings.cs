using System.Collections.Generic;

namespace Core.Rules
{
    /// <summary>
    /// A line players drew on the table (G28, TTS draw and line tools, saved as <c>VectorLines</c>): points on the table
    /// in order, a colour, a thickness, and an arrowhead at the end when it is an arrow.
    /// </summary>
    public sealed class DrawingDef
    {
        public const int MaxPoints = 2000;
        public const float MinThickness = 0.001f;
        public const float MaxThickness = 0.02f;

        public string Id { get; set; } = "";
        public List<Vec3> Points { get; set; } = new List<Vec3>();
        /// <summary>RGB 0–1.</summary>
        public float[] Color { get; set; } = { 1f, 1f, 1f };
        [System.ComponentModel.DefaultValue(0.004f)]
        public float Thickness { get; set; } = 0.004f;
        public bool Arrow { get; set; }
        /// <summary>The seat that drew it, or null.</summary>
        public string? Seat { get; set; }

        /// <summary>Distance from (<paramref name="x"/>, <paramref name="z"/>) to the nearest part of the line, ignoring height.</summary>
        public float DistanceTo(float x, float z)
        {
            var best = float.MaxValue;
            for (var i = 0; i < Points.Count; i++)
            {
                var a = Points[i];
                var b = i + 1 < Points.Count ? Points[i + 1] : a;
                var dx = b.X - a.X;
                var dz = b.Z - a.Z;
                var length = dx * dx + dz * dz;
                var t = length < 1e-12f ? 0f : System.Math.Max(0f, System.Math.Min(1f, ((x - a.X) * dx + (z - a.Z) * dz) / length));
                var px = a.X + t * dx - x;
                var pz = a.Z + t * dz - z;
                best = System.Math.Min(best, (float)System.Math.Sqrt(px * px + pz * pz));
            }
            return best;
        }
    }
}
