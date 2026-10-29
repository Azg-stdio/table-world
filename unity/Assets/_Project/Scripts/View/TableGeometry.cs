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

        /// <summary>A round table (M12): a disc <see cref="Size"/>.x across, with a ring for a rim.</summary>
        public static bool Round { get; private set; }

        /// <summary>Half the width of a round table, metres.</summary>
        public static float Radius => Size.x / 2f;

        /// <summary>No table: the game's own objects are the surface (TTS <c>Table_None</c>), or a room people walk in.</summary>
        public static bool Hidden { get; private set; }

        /// <summary>
        /// A room people walk in instead of sitting at a table (the karaoke room, ADR 0013 §5): no table, first-person
        /// walking, sofas anyone can sit on.
        /// </summary>
        public static bool Walk { get; private set; }

        private static float _lostBelow = LostBelow;

        /// <summary>The lowest an object may be placed: the table top, or with no table the game's lowest level.</summary>
        public static float Floor => Hidden ? _lostBelow : 0f;

        /// <summary>Takes the loaded table's size and table settings (before its objects are placed).</summary>
        public static void Use(Core.Rules.EnvironmentDef environment)
        {
            var look = environment?.Table;
            Round = look != null && look.Shape == Core.Rules.TableShape.Round;
            Size = look != null ? new Vector2(look.SurfaceSize.X, look.SurfaceSize.Y) : DefaultSize;
            Walk = environment != null && environment.Room == Core.Rules.RoomPreset.Karaoke && environment.RoomAsset == null;
            Hidden = (look?.Hidden ?? false) || Walk;
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
            if (Round && !Hidden)
            {
                var flat = new Vector2(point.x, point.z);
                var room = Radius - DropMargin;
                if (flat.magnitude > room)
                    flat = flat.normalized * room;
                return new Vector3(flat.x, point.y, flat.y);
            }
            var hx = Size.x / 2f - DropMargin;
            var hz = Size.y / 2f - DropMargin;
            return new Vector3(Mathf.Clamp(point.x, -hx, hx), point.y, Mathf.Clamp(point.z, -hz, hz));
        }

        public static bool IsLost(Vector3 position) =>
            position.y < _lostBelow ||
            (Round && !Hidden
                ? new Vector2(position.x, position.z).magnitude > Radius + LostBeyond
                : Mathf.Abs(position.x) > Size.x / 2f + LostBeyond || Mathf.Abs(position.z) > Size.y / 2f + LostBeyond);

        /// <summary>Whether a point (x, z) lies on the playing surface.</summary>
        public static bool OnSurface(Vector3 point) =>
            Round ? new Vector2(point.x, point.z).magnitude <= Radius : Mathf.Abs(point.x) <= Size.x / 2f && Mathf.Abs(point.z) <= Size.y / 2f;
    }
}
