using System.Collections.Generic;
using UnityEngine;

namespace TableWorld.View.Chalkers
{
    /// <summary>
    /// A wooden chair at a seat (T11.10, ADR 0012 §7), built from boxes. Placed like a <see cref="Chalker"/>: its origin is
    /// the sitter's place at table-top height, facing the table; the seat sits <see cref="SeatTop"/> below that and the
    /// legs reach the room's floor. Empty seats keep their chair, so the table shows how many can play.
    /// </summary>
    public sealed class ChalkerChair
    {
        /// <summary>The top of the cushion, below the table top (the room's floor is at <see cref="EnvironmentView.FloorY"/>).</summary>
        public const float SeatTop = -0.3f;
        public const float SeatWidth = 0.42f;
        public const float SeatDepth = 0.4f;
        /// <summary>The seat's centre, behind the sitter's origin (the hips sit on its front half).</summary>
        public const float SeatCentre = -0.04f;
        public const float BackHeight = 0.42f;

        private readonly GameObject _root;
        private readonly List<Material> _materials = new List<Material>();

        public GameObject Root => _root;

        /// <summary>World position of the cushion's top centre.</summary>
        public Vector3 Seat => _root.transform.TransformPoint(new Vector3(0f, SeatTop, SeatCentre));

        public ChalkerChair(string key, Transform parent)
        {
            _root = new GameObject($"Chair {key}");
            _root.transform.SetParent(parent, false);
            var wood = Material(new Color(0.36f, 0.22f, 0.12f), 0.35f);
            var cushion = Material(new Color(0.2f, 0.13f, 0.09f), 0.15f);

            const float board = 0.035f;
            const float leg = 0.04f;
            Box("Seat", new Vector3(0f, SeatTop - board / 2f - 0.02f, SeatCentre), new Vector3(SeatWidth, board, SeatDepth), wood);
            Box("Cushion", new Vector3(0f, SeatTop - 0.01f, SeatCentre), new Vector3(SeatWidth - 0.03f, 0.02f, SeatDepth - 0.03f), cushion);
            var underSeat = SeatTop - board - 0.02f;
            var legHeight = underSeat - EnvironmentView.FloorY;
            var back = SeatCentre - SeatDepth / 2f + leg / 2f;
            var front = SeatCentre + SeatDepth / 2f - leg / 2f;
            foreach (var x in new[] { -SeatWidth / 2f + leg / 2f, SeatWidth / 2f - leg / 2f })
            {
                Box("Leg", new Vector3(x, EnvironmentView.FloorY + legHeight / 2f, front), new Vector3(leg, legHeight, leg), wood);
                // The back legs carry on up as the backrest's posts.
                var postTop = SeatTop + BackHeight;
                Box("Back leg", new Vector3(x, (EnvironmentView.FloorY + postTop) / 2f, back), new Vector3(leg, postTop - EnvironmentView.FloorY, leg), wood);
            }
            Box("Back rail", new Vector3(0f, SeatTop + BackHeight - 0.06f, back), new Vector3(SeatWidth - leg, 0.1f, 0.025f), wood);
            Box("Back slat", new Vector3(0f, SeatTop + BackHeight * 0.45f, back), new Vector3(SeatWidth - leg, 0.06f, 0.025f), wood);
        }

        /// <summary>Puts the chair at the sitter's <paramref name="position"/> (table-top height), facing <paramref name="facing"/>.</summary>
        public void Place(Vector3 position, Vector3 facing)
        {
            facing.y = 0f;
            _root.transform.SetPositionAndRotation(position, Quaternion.LookRotation(facing.sqrMagnitude < 1e-4f ? Vector3.forward : facing, Vector3.up));
        }

        private Material Material(Color color, float smoothness)
        {
            var material = new Material(RequiredShaders.Lit) { name = "Chair" };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            _materials.Add(material);
            return material;
        }

        private void Box(string name, Vector3 position, Vector3 scale, Material material)
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(part.GetComponent<Collider>());
            part.name = name;
            part.transform.SetParent(_root.transform, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            part.GetComponent<Renderer>().sharedMaterial = material;
        }

        public void Destroy()
        {
            foreach (var material in _materials)
                Object.Destroy(material);
            Object.Destroy(_root);
        }
    }
}
