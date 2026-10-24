using UnityEngine;

namespace TableWorld.View
{
    /// <summary>
    /// The physical table, shared by the scene builder (Editor) and the runtime failsafes. Top surface at Y = 0. Its size
    /// (and whether there is one) comes from the loaded table's environment: TTS games need far more room than our
    /// default, and some bring their own surface.
    /// </summary>
    public static class TableGeometry
    {
        /// <summary>The scene's table as built by the Editor, metres.</summary>
        public static readonly Vector2 DefaultSize = new Vector2(1.6f, 1.0f);

        /// <summary>Playing surface of the loaded table, metres.</summary>
        public static Vector2 Size { get; private set; } = DefaultSize;

        /// <summary>No table: the game's own objects are the surface (TTS <c>Table_None</c>).</summary>
        public static bool Hidden { get; private set; }

        private static float _lostBelow = LostBelow;

        /// <summary>The lowest an object may be placed: the table top, or with no table the game's lowest level.</summary>
        public static float Floor => Hidden ? _lostBelow : 0f;

        /// <summary>Takes the loaded table's size and table settings (before its objects are placed).</summary>
        public static void Use(Core.Rules.EnvironmentDef environment)
        {
            var look = environment?.Table;
            Size = look?.Size is { } size ? new Vector2(size.X, size.Y) : DefaultSize;
            Hidden = look?.Hidden ?? false;
            _lostBelow = look?.LowestY ?? LostBelow;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => Use(null);
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
            position.y < _lostBelow ||
            Mathf.Abs(position.x) > Size.x / 2f + LostBeyond ||
            Mathf.Abs(position.z) > Size.y / 2f + LostBeyond;
    }
}
