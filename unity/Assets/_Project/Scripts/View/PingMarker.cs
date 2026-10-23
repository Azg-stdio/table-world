using TableWorld.View.Meshes;
using UnityEngine;

namespace TableWorld.View
{
    /// <summary>TTS ping (Tab + click): a ring in the player's colour that grows and fades on the table for 1.5 s.</summary>
    public sealed class PingMarker : MonoBehaviour
    {
        public const float Seconds = 1.5f;

        private Material _material;
        private Color _color;
        private float _born;

        public static PingMarker Spawn(Vector3 point, Color color)
        {
            var go = new GameObject("Ping");
            go.transform.position = new Vector3(point.x, 0.002f, point.z);
            var marker = go.AddComponent<PingMarker>();
            marker._color = color;
            marker._born = Time.unscaledTime;
            marker._material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Ping" };
            marker._material.SetColor("_BaseColor", color);
            go.AddComponent<MeshFilter>().sharedMesh = MeshFactory.Cylinder(0.02f, 0.002f, 32);
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { marker._material, marker._material, marker._material };
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return marker;
        }

        private void Update()
        {
            var k = (Time.unscaledTime - _born) / Seconds;
            if (k >= 1f)
            {
                Destroy(gameObject);
                return;
            }
            var s = 1f + 2f * k;
            transform.localScale = new Vector3(s, 1f, s);
            _material.SetColor("_BaseColor", Color.Lerp(_color, Color.white, k));
        }

        private void OnDestroy()
        {
            if (_material != null)
                Destroy(_material);
        }
    }
}
