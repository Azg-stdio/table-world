using UnityEngine;

namespace TableWorld.View
{
    /// <summary>
    /// TTS Alt zoom and Alt+Shift peek: a second camera frames the looked-at object and renders it into a texture the HUD
    /// shows large. Peek looks from below (the underside of a card is its face when it lies face down). Wheel zooms and
    /// Q/E turn the view while looking.
    /// </summary>
    public sealed class LookCamera : MonoBehaviour
    {
        public const int Resolution = 768;

        /// <summary>Unnamed layer used only while looking: the camera renders the target alone, so a peek from under the table works.</summary>
        public const int LookLayer = 31;

        private readonly System.Collections.Generic.Dictionary<GameObject, int> _layers = new System.Collections.Generic.Dictionary<GameObject, int>();

        private Camera _camera;
        private ObjectView _target;
        private bool _peek;
        private float _zoom = 1f;
        private float _yaw;

        public RenderTexture Texture { get; private set; }
        public ObjectView Target => _target;

        public static LookCamera Create(Transform parent)
        {
            var go = new GameObject("Look Camera");
            go.transform.SetParent(parent, false);
            var look = go.AddComponent<LookCamera>();
            look._camera = go.AddComponent<Camera>();
            look._camera.enabled = false;
            look._camera.nearClipPlane = 0.005f;
            look._camera.farClipPlane = 5f;
            look._camera.fieldOfView = 30f;
            look._camera.clearFlags = CameraClearFlags.SolidColor;
            look._camera.backgroundColor = new Color(0.06f, 0.06f, 0.07f);
            look._camera.cullingMask = 1 << LookLayer;
            look.Texture = new RenderTexture(Resolution, Resolution, 24) { name = "Look" };
            look._camera.targetTexture = look.Texture;
            return look;
        }

        public void Show(ObjectView target, bool peek)
        {
            if (target != _target)
            {
                _zoom = 1f;
                _yaw = 0f;
            }
            RestoreLayers();
            _target = target;
            _peek = peek;
            _camera.enabled = target != null;
            if (target != null)
                foreach (var child in target.GetComponentsInChildren<Transform>())
                {
                    _layers[child.gameObject] = child.gameObject.layer;
                    child.gameObject.layer = LookLayer;
                }
        }

        /// <summary>Wheel while looking: positive zooms in.</summary>
        public void Zoom(float steps) => _zoom = Mathf.Clamp(_zoom * Mathf.Pow(0.85f, steps), 0.3f, 2f);

        /// <summary>Q/E while looking.</summary>
        public void Turn(float degrees) => _yaw += degrees;

        private void LateUpdate()
        {
            if (_target == null)
            {
                if (_camera.enabled)
                    Show(null, false);
                return;
            }
            var bounds = _target.Renderer.bounds;
            var size = Mathf.Max(bounds.size.x, bounds.size.z, bounds.size.y * 0.5f, 0.02f);
            var distance = size / (2f * Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad)) * 1.15f * _zoom;
            // Seen from straight above (or below for a peek), with the object's yaw so cards read upright.
            var yaw = _target.transform.eulerAngles.y + _yaw;
            var direction = _peek ? Vector3.down : Vector3.up;
            var up = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            transform.position = bounds.center + direction * (distance + bounds.extents.y);
            transform.rotation = Quaternion.LookRotation(-direction, _peek ? -up : up);
        }

        private void RestoreLayers()
        {
            foreach (var pair in _layers)
                if (pair.Key != null)
                    pair.Key.layer = pair.Value;
            _layers.Clear();
        }

        private void OnDestroy()
        {
            RestoreLayers();
            if (Texture != null)
                Texture.Release();
        }
    }
}
