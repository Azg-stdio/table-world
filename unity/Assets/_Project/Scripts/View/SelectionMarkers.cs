using System.Collections.Generic;
using TableWorld.View.Meshes;
using UnityEngine;

namespace TableWorld.View
{
    /// <summary>Thin cyan outlines on the felt under box-selected objects (TTS highlights selections).</summary>
    public static class SelectionMarkers
    {
        private static readonly Color Highlight = new Color(0.2f, 0.85f, 1f);
        private static readonly List<GameObject> Pool = new List<GameObject>();
        private static Material _material;

        public static void Show(IReadOnlyList<ObjectView> views)
        {
            for (var i = 0; i < Pool.Count; i++)
                if (Pool[i] != null)
                    Pool[i].SetActive(false);
            for (var i = 0; i < views.Count; i++)
            {
                var view = views[i];
                if (view == null || view.Renderer == null)
                    continue;
                var marker = MarkerAt(i);
                var bounds = view.Renderer.bounds;
                marker.transform.position = new Vector3(bounds.center.x, 0.0006f, bounds.center.z);
                marker.transform.localScale = new Vector3(bounds.size.x + 0.01f, 1f, bounds.size.z + 0.01f);
                marker.SetActive(true);
            }
        }

        private static GameObject MarkerAt(int index)
        {
            while (Pool.Count <= index)
                Pool.Add(null);
            if (Pool[index] != null)
                return Pool[index];
            if (_material == null)
            {
                _material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Selection" };
                _material.SetColor("_BaseColor", Highlight);
            }
            // A unit frame scaled to the object's footprint: four strips around a hole.
            var root = new GameObject("Selection marker");
            const float w = 0.06f;
            AddStrip(root, new Vector3(0f, 0f, 0.5f - w / 2f), new Vector3(1f, 0.0004f, w));
            AddStrip(root, new Vector3(0f, 0f, -0.5f + w / 2f), new Vector3(1f, 0.0004f, w));
            AddStrip(root, new Vector3(0.5f - w / 2f, 0f, 0f), new Vector3(w, 0.0004f, 1f));
            AddStrip(root, new Vector3(-0.5f + w / 2f, 0f, 0f), new Vector3(w, 0.0004f, 1f));
            Pool[index] = root;
            return root;
        }

        private static void AddStrip(GameObject root, Vector3 position, Vector3 size)
        {
            var strip = new GameObject("Strip");
            strip.transform.SetParent(root.transform, false);
            strip.transform.localPosition = position;
            strip.AddComponent<MeshFilter>().sharedMesh = MeshFactory.Box(size);
            var renderer = strip.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { _material, _material, _material };
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}
