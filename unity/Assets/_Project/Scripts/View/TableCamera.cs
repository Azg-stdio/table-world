using UnityEngine;
using UnityEngine.InputSystem;

namespace TableWorld.View
{
    /// <summary>
    /// Orbit camera around a pivot on the table. Right drag orbits, middle drag (or Shift + right drag) pans,
    /// wheel zooms, WASD/arrows pan, Q/E turn. Left mouse is left free for grabbing objects.
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

        public Vector3 Pivot => _pivot;

        public void Configure(Vector3 pivot, float yaw, float pitch, float distance, Vector2 pivotExtents)
        {
            _pivot = pivot;
            _yaw = yaw;
            _pitch = pitch;
            _distance = distance;
            _pivotExtents = pivotExtents;
            Apply();
        }

        private void Awake()
        {
            Apply();
        }

        private void LateUpdate()
        {
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;

            if (mouse != null)
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
                if (Mathf.Abs(scroll) > 0.01f)
                    _distance = Mathf.Clamp(_distance * Mathf.Pow(1f - _zoomPerWheelStep, Mathf.Sign(scroll)), _minDistance, _maxDistance);
            }

            if (keyboard != null)
            {
                var move = Vector2.zero;
                if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) move.y += 1;
                if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) move.y -= 1;
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) move.x += 1;
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) move.x -= 1;
                if (move != Vector2.zero)
                    PanBy(move.normalized * (_keyPanSpeed * _distance * Time.unscaledDeltaTime));
                if (keyboard.qKey.isPressed) _yaw -= _keyTurnSpeed * Time.unscaledDeltaTime;
                if (keyboard.eKey.isPressed) _yaw += _keyTurnSpeed * Time.unscaledDeltaTime;
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
