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
        public const int Resolution = 1024;

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
            if (IsFlat(_target.Model))
            {
                // Cards, decks, tokens, tiles, boards: always square on to their face, whatever their pose (a card
                // standing in a hand, a tilted token), top edge up. Peek looks at the other side.
                var normal = _target.transform.up;
                var side = normal.y >= -0.01f ? normal : -normal;
                var direction = _peek ? -side : side;
                // The image's top edge is the drawing's −Z side (TTS's convention): the drawing, not the pose, since a card
                // may be drawn turned toward this player (ObjectView.TurnQuarters), and the close-up shows it as they see it.
                var drawn = _target.transform.rotation * Quaternion.Euler(0f, 90f * _target.TurnQuarters, 0f);
                var top = Quaternion.AngleAxis(_yaw, direction) * (drawn * Vector3.back);
                var local = _target.Filter.sharedMesh.bounds.size;
                var scale = _target.transform.lossyScale;
                var size = Mathf.Max(local.x * scale.x, local.z * scale.z, 0.02f);
                var distance = size / (2f * Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad)) * 1.15f * _zoom;
                transform.position = bounds.center + direction * (distance + local.y * scale.y);
                transform.rotation = Quaternion.LookRotation(-direction, _peek ? -top : top);
                return;
            }
            var extent = Mathf.Max(bounds.size.x, bounds.size.z, bounds.size.y * 0.5f, 0.02f);
            var away = extent / (2f * Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad)) * 1.15f * _zoom;
            // Seen from straight above (or below for a peek), with the object's yaw so it reads upright.
            var yaw = _target.transform.eulerAngles.y + _yaw;
            var vertical = _peek ? Vector3.down : Vector3.up;
            var up = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            transform.position = bounds.center + vertical * (away + bounds.extents.y);
            transform.rotation = Quaternion.LookRotation(-vertical, _peek ? -up : up);
        }

        private static bool IsFlat(Core.Rules.TableObject model) =>
            model is Core.Rules.Card || model is Core.Rules.Deck || model is Core.Rules.Token || model is Core.Rules.Tile || model is Core.Rules.Board
            || model is Core.Rules.Pdf;

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
