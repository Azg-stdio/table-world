using System.Collections.Generic;
using UnityEngine;

namespace TableWorld.View.Chalkers
{
    /// <summary>
    /// A sofa along one side of the table in the karaoke room (T12.6), built from boxes: one cushion per place on it, arms
    /// at both ends and a back. Placed like a <see cref="ChalkerChair"/>: its origin is the sitters' line at table-top
    /// height, facing the table, and its cushions' tops are at <see cref="ChalkerChair.SeatTop"/>, so a chalker sits on it
    /// as on a chair.
    /// </summary>
    public sealed class ChalkerSofa
    {
        /// <summary>The room one sitter takes along a sofa.</summary>
        public const float PlaceWidth = 0.5f;
        public const float ArmWidth = 0.1f;
        public const float SeatDepth = 0.42f;
        /// <summary>The front of the cushions, ahead of the sitters' line (the knees reach past it).</summary>
        public const float Front = 0.16f;
        public const float BackHeight = 0.48f;
        public const float BackThickness = 0.16f;

        private readonly GameObject _root;
        private readonly List<Material> _materials = new List<Material>();

        public GameObject Root => _root;
        public int Places { get; }
        /// <summary>Its length from arm to arm, in metres.</summary>
        public float Length { get; }

        public ChalkerSofa(string key, Transform parent, int places)
        {
            Places = Mathf.Max(1, places);
            Length = Places * PlaceWidth + 2f * ArmWidth;
            _root = new GameObject($"Sofa {key}");
            _root.transform.SetParent(parent, false);
            var fabric = Material(new Color(0.36f, 0.08f, 0.13f), 0.25f);
            var cushionFabric = Material(new Color(0.42f, 0.1f, 0.15f), 0.2f);
            var foot = Material(new Color(0.08f, 0.06f, 0.05f), 0.4f);

            const float cushion = 0.1f;
            const float legHeight = 0.06f;
            var floor = EnvironmentView.FloorY;
            var back = Front - SeatDepth;
            var baseTop = ChalkerChair.SeatTop - cushion;
            var inner = Places * PlaceWidth;
            // The frame under the cushions, from the feet up.
            Box("Base", new Vector3(0f, (floor + legHeight + baseTop) / 2f, (Front + back) / 2f), new Vector3(inner, baseTop - floor - legHeight, SeatDepth), fabric);
            for (var i = 0; i < Places; i++)
            {
                var x = -inner / 2f + PlaceWidth * (i + 0.5f);
                Box("Cushion", new Vector3(x, baseTop + cushion / 2f, (Front + back) / 2f + 0.01f), new Vector3(PlaceWidth - 0.015f, cushion, SeatDepth - 0.02f), cushionFabric);
                Box("Back cushion", new Vector3(x, ChalkerChair.SeatTop + BackHeight * 0.45f, back + 0.06f), new Vector3(PlaceWidth - 0.015f, BackHeight * 0.75f, 0.12f), cushionFabric);
            }
            var backTop = ChalkerChair.SeatTop + BackHeight;
            Box("Back", new Vector3(0f, (floor + legHeight + backTop) / 2f, back - BackThickness / 2f), new Vector3(Length, backTop - floor - legHeight, BackThickness), fabric);
            var armTop = ChalkerChair.SeatTop + 0.2f;
            foreach (var side in new[] { -1f, 1f })
            {
                var x = side * (inner / 2f + ArmWidth / 2f);
                Box("Arm", new Vector3(x, (floor + legHeight + armTop) / 2f, (Front + back - BackThickness) / 2f), new Vector3(ArmWidth, armTop - floor - legHeight, SeatDepth + BackThickness), fabric);
                foreach (var z in new[] { Front - 0.04f, back - BackThickness + 0.04f })
                    Box("Foot", new Vector3(x, floor + legHeight / 2f, z), new Vector3(0.05f, legHeight, 0.05f), foot);
            }
        }

        /// <summary>Puts the sofa's centre at <paramref name="position"/> (the sitters' line, table-top height), facing <paramref name="facing"/>.</summary>
        public void Place(Vector3 position, Vector3 facing)
        {
            facing.y = 0f;
            _root.transform.SetPositionAndRotation(position, Quaternion.LookRotation(facing.sqrMagnitude < 1e-4f ? Vector3.forward : facing, Vector3.up));
        }

        /// <summary>World position of the top of place <paramref name="index"/>'s cushion.</summary>
        public Vector3 Seat(int index)
        {
            var inner = Places * PlaceWidth;
            return _root.transform.TransformPoint(new Vector3(-inner / 2f + PlaceWidth * (index + 0.5f), ChalkerChair.SeatTop, (Front + Front - SeatDepth) / 2f));
        }

        private Material Material(Color color, float smoothness)
        {
            var material = new Material(RequiredShaders.Lit) { name = "Sofa" };
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
