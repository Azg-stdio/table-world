using Core.Rules;
using TableWorld.View.Meshes;
using UnityEngine;

namespace TableWorld.View
{
    /// <summary>
    /// Flat markers for zones, drawn just above the felt and without colliders (they never block a grab or a drop):
    /// hand zones as an outline in the seat colour, hidden zones as a grey outline, snap points as a small dark disc.
    /// Scripting zones stay invisible, as in TTS. Zones with a parent object move with it.
    /// </summary>
    public static class ZoneViewFactory
    {
        public const float LineWidth = 0.004f;
        public const float LineHeight = 0.0003f;
        public const float SnapRadius = 0.009f;

        private static readonly Color HiddenColor = new Color(0.25f, 0.25f, 0.28f);
        private static readonly Color SnapColor = new Color(0.05f, 0.16f, 0.11f);

        public static GameObject Create(Zone zone, Color seatColor, AssetLibrary assets, Transform parent)
        {
            if (zone is ScriptingZone)
                return null;

            var root = new GameObject($"{zone.Type} {zone.Id}");
            root.transform.SetParent(parent, false);
            var t = zone.Transform;
            // Markers lie on the felt: only the zone's position on the table and its yaw matter.
            root.transform.localPosition = new Vector3(t.Position.X, LineHeight / 2f, t.Position.Z);
            root.transform.localRotation = Quaternion.Euler(0f, t.Rotation.Y, 0f);

            if (zone is SnapPoint)
            {
                AddPiece(root, MeshFactory.Cylinder(SnapRadius, LineHeight, 20), Vector3.zero, assets.Plain(SnapColor, 0.1f));
                return root;
            }

            var color = zone is HandZone ? seatColor : HiddenColor;
            var material = assets.Plain(color, 0.1f);
            var w = zone.Size.X * t.Scale.X;
            var d = zone.Size.Z * t.Scale.Z;
            AddPiece(root, MeshFactory.Box(new Vector3(w, LineHeight, LineWidth)), new Vector3(0f, 0f, (d - LineWidth) / 2f), material);
            AddPiece(root, MeshFactory.Box(new Vector3(w, LineHeight, LineWidth)), new Vector3(0f, 0f, -(d - LineWidth) / 2f), material);
            AddPiece(root, MeshFactory.Box(new Vector3(LineWidth, LineHeight, d)), new Vector3((w - LineWidth) / 2f, 0f, 0f), material);
            AddPiece(root, MeshFactory.Box(new Vector3(LineWidth, LineHeight, d)), new Vector3(-(w - LineWidth) / 2f, 0f, 0f), material);
            return root;
        }

        /// <summary>Seat colour names ("red", "blue", "#3366ff", ...) to a colour; unknown names fall back to white.</summary>
        public static Color SeatColor(string name)
        {
            switch (name?.ToLowerInvariant())
            {
                case "red": return new Color(0.86f, 0.2f, 0.18f);
                case "blue": return new Color(0.2f, 0.42f, 0.9f);
                case "green": return new Color(0.25f, 0.75f, 0.3f);
                case "yellow": return new Color(0.95f, 0.85f, 0.2f);
                case "orange": return new Color(0.95f, 0.55f, 0.15f);
                case "purple": return new Color(0.6f, 0.3f, 0.8f);
                case "pink": return new Color(0.95f, 0.5f, 0.7f);
                case "teal": return new Color(0.15f, 0.7f, 0.7f);
                case "white": return new Color(0.95f, 0.95f, 0.95f);
                case "brown": return new Color(0.5f, 0.32f, 0.18f);
            }
            return ColorUtility.TryParseHtmlString(name ?? "", out var parsed) ? parsed : Color.white;
        }

        private static void AddPiece(GameObject root, Mesh mesh, Vector3 position, Material material)
        {
            var piece = new GameObject("Line");
            piece.transform.SetParent(root.transform, false);
            piece.transform.localPosition = position;
            piece.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = piece.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { material, material, material };
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}
