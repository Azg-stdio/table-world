using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TableWorld.View
{
    /// <summary>
    /// Orbit camera around a pivot on the table (TTS controls, docs/tts-interactions.md §1): right drag orbits, middle
    /// drag (or Shift + right drag) pans, wheel zooms, WASD/arrows pan, Q/E turn. The interaction calls
    /// <see cref="ResetView"/> (Space), <see cref="ToggleQuickZoom"/> (Z, middle click), <see cref="ToggleTopDown"/> (P)
    /// and <see cref="Save"/>/<see cref="Load"/> (Ctrl/Shift + 1–9).
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class TableCamera : MonoBehaviour
    {
        [SerializeField] private Vector3 _pivot = Vector3.zero;
        [SerializeField] private float _yaw;
        [SerializeField] private float _pitch = 55f;
        [SerializeField] private float _distance = 1.4f;

        [Header("Limits")]
        [SerializeField] private float _minDistance = 0.15f;
        [SerializeField] private float _maxDistance = 4f;
        [SerializeField] private float _minPitch = 10f;
        [SerializeField] private float _maxPitch = 89f;
        [SerializeField] private Vector2 _pivotExtents = new Vector2(1.2f, 0.9f);

        [Header("Speeds")]
        [SerializeField] private float _orbitDegreesPerPixel = 0.25f;
        [SerializeField] private float _zoomPerWheelStep = 0.12f;
        [SerializeField] private float _keyPanSpeed = 0.8f;
        [SerializeField] private float _keyTurnSpeed = 90f;

        public const float QuickZoomDistance = 0.3f;
        public const float TopDownPitch = 89f;

        private readonly Dictionary<int, View> _saved = new Dictionary<int, View>();
        private View _home;
        private View? _beforeQuickZoom;
        private View? _beforeTopDown;

        /// <summary>A camera pose: pivot, yaw, pitch, distance.</summary>
        public struct View
        {
            public Vector3 Pivot;
            public float Yaw;
            public float Pitch;
            public float Distance;
        }

        public Vector3 Pivot => _pivot;
        public float Distance => _distance;
        public float Pitch => _pitch;
        public bool IsTopDown => _beforeTopDown.HasValue;
        public bool IsQuickZoomed => _beforeQuickZoom.HasValue;

        /// <summary>Set each frame by the interaction: Q/E rotate a held or hovered object instead of turning.</summary>
        public bool SuppressTurnKeys { get; set; }

        /// <summary>Set each frame by the interaction: the wheel raises a held object, the right button taps it down.</summary>
        public bool SuppressMouse { get; set; }

        public void Configure(Vector3 pivot, float yaw, float pitch, float distance, Vector2 pivotExtents)
        {
            _pivot = pivot;
            _yaw = yaw;
            _pitch = pitch;
            _distance = distance;
            _pivotExtents = pivotExtents;
            _home = Current;
            Apply();
        }

        public View Current => new View { Pivot = _pivot, Yaw = _yaw, Pitch = _pitch, Distance = _distance };

        public void SetView(View view)
        {
            _pivot = view.Pivot;
            _yaw = view.Yaw;
            _pitch = view.Pitch;
            _distance = view.Distance;
            Apply();
        }

        /// <summary>Space: back to the starting view.</summary>
        public void ResetView()
        {
            _beforeQuickZoom = null;
            _beforeTopDown = null;
            SetView(_home);
        }

        /// <summary>Z or middle click on the table: zoom onto a point; again to go back.</summary>
        public void ToggleQuickZoom(Vector3 point)
        {
            if (_beforeQuickZoom is { } back)
            {
                _beforeQuickZoom = null;
                SetView(back);
                return;
            }
            _beforeQuickZoom = Current;
            SetView(new View { Pivot = point, Yaw = _yaw, Pitch = Mathf.Max(_pitch, 50f), Distance = QuickZoomDistance });
        }

        /// <summary>P: top-down view of the whole table; again to go back. (TTS also has first person: M6.)</summary>
        public void ToggleTopDown()
        {
            if (_beforeTopDown is { } back)
            {
                _beforeTopDown = null;
                SetView(back);
                return;
            }
            _beforeTopDown = Current;
            SetView(new View { Pivot = new Vector3(_pivot.x, 0f, _pivot.z), Yaw = _yaw, Pitch = TopDownPitch, Distance = Mathf.Max(_distance, 1.2f) });
        }

        /// <summary>Ctrl + 1–9.</summary>
        public void Save(int slot) => _saved[slot] = Current;

        /// <summary>Shift + 1–9. Returns false for an empty slot.</summary>
        public bool Load(int slot)
        {
            if (!_saved.TryGetValue(slot, out var view))
                return false;
            SetView(view);
            return true;
        }

        private void Awake()
        {
            _home = Current;
            Apply();
        }

        private void LateUpdate()
        {
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;

            if (mouse != null && !SuppressMouse)
            {
                var delta = mouse.delta.ReadValue();
                var shift = keyboard != null && keyboard.shiftKey.isPressed;
                if (mouse.middleButton.isPressed || (mouse.rightButton.isPressed && shift))
                    PanByPixels(delta);
                else if (mouse.rightButton.isPressed)
                {
                    _yaw += delta.x * _orbitDegreesPerPixel;
                    _pitch = Mathf.Clamp(_pitch - delta.y * _orbitDegreesPerPixel, _minPitch, _maxPitch);
                }

                var scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f && (keyboard == null || !keyboard.altKey.isPressed))
                    _distance = Mathf.Clamp(_distance * Mathf.Pow(1f - _zoomPerWheelStep, Mathf.Sign(scroll)), _minDistance, _maxDistance);
            }

            if (keyboard != null && !keyboard.ctrlKey.isPressed)
            {
                var move = Vector2.zero;
                if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) move.y += 1;
                if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) move.y -= 1;
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) move.x += 1;
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) move.x -= 1;
                if (move != Vector2.zero)
                    PanBy(move.normalized * (_keyPanSpeed * _distance * Time.unscaledDeltaTime));
                if (!SuppressTurnKeys && keyboard.qKey.isPressed) _yaw -= _keyTurnSpeed * Time.unscaledDeltaTime;
                if (!SuppressTurnKeys && keyboard.eKey.isPressed) _yaw += _keyTurnSpeed * Time.unscaledDeltaTime;
            }

            Apply();
        }

        private void PanByPixels(Vector2 pixels)
        {
            // One screen height of drag moves the pivot by roughly what the camera sees vertically at the pivot.
            var cam = GetComponent<Camera>();
            var worldPerPixel = 2f * _distance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1, Screen.height);
            PanBy(-pixels * worldPerPixel);
        }

        private void PanBy(Vector2 planar)
        {
            var rotation = Quaternion.Euler(0f, _yaw, 0f);
            var offset = rotation * new Vector3(planar.x, 0f, planar.y);
            _pivot += offset;
        }

        private void Apply()
        {
            _pivot.x = Mathf.Clamp(_pivot.x, -_pivotExtents.x, _pivotExtents.x);
            _pivot.z = Mathf.Clamp(_pivot.z, -_pivotExtents.y, _pivotExtents.y);
            var rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            transform.SetPositionAndRotation(_pivot - rotation * Vector3.forward * _distance, rotation);
        }
    }
}
