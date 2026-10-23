using UnityEngine;

namespace TableWorld.View
{
    /// <summary>The physical table, shared by the scene builder (Editor) and the runtime failsafes. Top surface at Y = 0.</summary>
    public static class TableGeometry
    {
        /// <summary>Playing surface in metres.</summary>
        public static readonly Vector2 Size = new Vector2(1.6f, 1.0f);
        public const float Thickness = 0.04f;
        public const float RimHeight = 0.02f;
        public const float RimWidth = 0.04f;

        /// <summary>Held objects are kept this far inside the edge, so a drop never lands on the rim.</summary>
        public const float DropMargin = 0.04f;

        /// <summary>Below this height (or this far past an edge) an object has left the table and is put back.</summary>
        public const float LostBelow = -0.05f;
        public const float LostBeyond = 0.15f;

        public static Vector3 ClampToDropArea(Vector3 point)
        {
            var hx = Size.x / 2f - DropMargin;
            var hz = Size.y / 2f - DropMargin;
            return new Vector3(Mathf.Clamp(point.x, -hx, hx), point.y, Mathf.Clamp(point.z, -hz, hz));
        }

        public static bool IsLost(Vector3 position) =>
            position.y < LostBelow ||
            Mathf.Abs(position.x) > Size.x / 2f + LostBeyond ||
            Mathf.Abs(position.z) > Size.y / 2f + LostBeyond;
    }
}
