using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using TableWorld.View.Settings;

namespace TableWorld.View
{
    /// <summary>
    /// The player's camera, two heights of one camera (docs/dev/camera.md). <b>Seated</b> (first person, the default):
    /// the eyes of your own chalker in its chair (<see cref="SetSeat"/>), right drag and Q/E turn the head, the wheel
    /// leans in over the table and, leaned all the way back, one more step out lifts you out of your head. <b>Lifted</b>:
    /// the orbit camera around a pivot on the table (TTS controls, docs/tts-interactions.md §1): right drag orbits, middle
    /// drag (or Shift + right drag) pans, wheel zooms, WASD/arrows pan, Q/E turn. Panning while seated lifts too. The
    /// interaction calls <see cref="ResetView"/> (Space: back to the seat), <see cref="ToggleSeated"/> (O),
    /// <see cref="ToggleQuickZoom"/> (Z, middle click), <see cref="ToggleTopDown"/> (P) and <see cref="Save"/>/
    /// <see cref="Load"/> (Ctrl/Shift + 1–9). With <see cref="FirstPerson"/> off (a setting, for motion sickness or taste)
    /// the camera stays lifted unless O sits it.
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
        /// <summary>Seated, the head starts this far down from level: the table in front and the faces across it.</summary>
        public const float SeatPitch = 28f;
        /// <summary>Seated, how far the head turns from the chair's facing and up and down, in degrees.</summary>
        public const float MaxHeadYaw = 110f;
        public const float MinHeadPitch = -35f;
        public const float MaxHeadPitch = 80f;
        /// <summary>Seated, each wheel step leans this much of the way in (0 sitting back, 1 over the table).</summary>
        public const float LeanStep = 0.25f;
        /// <summary>Seated and leaning all the way in, each wheel step in narrows the view by this much (an optical zoom)...</summary>
        public const float ZoomStep = 1.3f;
        /// <summary>...up to this many times closer: a card across the table reads like one in front of you.</summary>
        public const float MaxZoom = 4f;
        /// <summary>Sitting down and lifting off blend over this long, in seconds.</summary>
        public const float BlendSeconds = 0.4f;
        /// <summary>Seated, the near clip plane: the eyes are a hand's length from the cards they hold.</summary>
        public const float SeatedNearClip = 0.02f;
        /// <summary>Wheel steps out, in a row, that lift you out of your seat once you sit back; a pause longer than PullSettle forgets them.</summary>
        public const int LiftSteps = 3;
        public const float PullSettle = 0.6f;

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

        /// <summary>Seated in first person, at your chalker's eyes.</summary>
        public bool IsSeated => _seated;

        /// <summary>
        /// Player setting: Space and the start of a game sit you in first person. Off, the camera stays the free orbit camera
        /// (O still sits you).
        /// </summary>
        public bool FirstPerson { get; set; } = true;

        /// <summary>Seated, how far you lean in over the table (0–1): your chalker leans with you.</summary>
        public float Lean => _seated ? _lean : 0f;

        /// <summary>Seated, the head's turn from the chair's facing (degrees, right positive) and its pitch (down positive).</summary>
        /// <summary>Seated, how many times closer the view is than the eyes see (1: no zoom).</summary>
        public float Zoom => _seated ? _zoom : 1f;
        private float _zoom = 1f;
        /// <summary>The camera's own field of view, before any zoom.</summary>
        private float _baseFov = -1f;

        public float HeadYaw => _headYaw;
        public float HeadPitch => _headPitch;

        /// <summary>Seated, turns the head to <paramref name="yaw"/> and <paramref name="pitch"/> (as right drag does), within a neck's reach.</summary>
        public void TurnHead(float yaw, float pitch)
        {
            _headYaw = Mathf.Clamp(yaw, -MaxHeadYaw, MaxHeadYaw);
            _headPitch = Mathf.Clamp(pitch, MinHeadPitch, MaxHeadPitch);
        }

        /// <summary>Where your eyes are, or null without a seat (a spectator, no table).</summary>
        public Vector3? Eye => _hasSeat ? _eye : (Vector3?)null;

        private bool _seated;
        private bool _hasSeat;
        private Vector3 _eye;
        private float _seatYaw;
        private float _headYaw;
        private float _headPitch = SeatPitch;
        private float _lean;
        /// <summary>Space or a game's start asked to sit before the seat was known: sit as soon as it is.</summary>
        private bool _sitWhenSeated;
        private float _blend = 1f;
        private Vector3 _blendPosition;
        private Quaternion _blendRotation;
        private float _liftedNearClip = -1f;
        private bool _topDownFromSeat;
        private bool _quickZoomFromSeat;
        private int _pull;
        private float _pullAt;
        private float _pullShown;

        /// <summary>Set each frame by the interaction: Q/E rotate a held or hovered object instead of turning.</summary>
        public bool SuppressTurnKeys { get; set; }

        /// <summary>Set each frame by the interaction: the wheel raises a held object, the right button taps it down.</summary>
        public bool SuppressMouse { get; set; }

        /// <summary>Set each frame by the interaction while the pointer is over the HUD: the wheel scrolls the panel instead.</summary>
        public bool SuppressWheel { get; set; }

        /// <summary>Set each frame by the interaction while the player types in the HUD: keys do not move the camera.</summary>
        public bool SuppressKeys { get; set; }

        /// <summary>Player settings (G31): multipliers on the mouse orbit, wheel zoom and keyboard pan speeds, and an inverted orbit.</summary>
        public float OrbitScale { get; set; } = 1f;
        public float ZoomScale { get; set; } = 1f;
        public float PanScale { get; set; } = 1f;
        public bool InvertOrbitY { get; set; }

        /// <summary>
        /// Lets the camera reach all of a table of this size (big TTS tables need more than the default), and starts the
        /// view further back on a bigger table (a 12-seat table is 2.7 m wide), up to twice the scene's distance.
        /// </summary>
        public void FitTable(Vector2 size)
        {
            _pivotExtents = new Vector2(Mathf.Max(1.2f, size.x / 2f + 0.4f), Mathf.Max(0.9f, size.y / 2f + 0.4f));
            _maxDistance = Mathf.Max(4f, Mathf.Max(size.x, size.y) * 1.3f);
            var scale = Mathf.Max(size.x / TableGeometry.DefaultSize.x, size.y / TableGeometry.DefaultSize.y);
            _home.Distance = Mathf.Min(_maxDistance, _sceneDistance * Mathf.Clamp(scale, 1f, 2f));
        }

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

        /// <summary>
        /// The game holds this player in their chalker's eyes (a seated phase, M12): every way out of the seat (O, the
        /// wheel, Space, a quick zoom, top-down) waits until it lets go.
        /// </summary>
        public bool HoldSeated { get; set; }

        public void SetView(View view)
        {
            if (HoldSeated && _seated)
                return;
            if (_seated)
            {
                StartBlend();
                _seated = false;
            }
            _pivot = view.Pivot;
            _yaw = view.Yaw;
            _pitch = view.Pitch;
            _distance = view.Distance;
            Apply();
        }

        /// <summary>Space: back to the starting view, seated in first person when <see cref="FirstPerson"/> is on.</summary>
        public void ResetView()
        {
            _beforeQuickZoom = null;
            _beforeTopDown = null;
            _topDownFromSeat = _quickZoomFromSeat = false;
            if (FirstPerson && _hasSeat)
            {
                Sit();
                return;
            }
            _sitWhenSeated = FirstPerson;
            SetView(_home);
        }

        /// <summary>
        /// Where your eyes are this frame (your chalker's, set after it is posed) and which way its chair faces; null when
        /// you have no seat, which lifts a seated camera to the starting view.
        /// </summary>
        public void SetSeat(Vector3? eye, Vector3 facing)
        {
            if (eye is not { } at)
            {
                _hasSeat = false;
                // A table loading drops the seat for a frame or two: sit again when it is back.
                if (_seated)
                {
                    _sitWhenSeated = true;
                    SetView(_home);
                }
                return;
            }
            var yaw = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg;
            // A new chair (another seat, a hot seat's turn): the head starts straight again, and the view glides there.
            if (_hasSeat && _seated && (Vector3.Distance(at, _eye) > 0.3f || Mathf.Abs(Mathf.DeltaAngle(yaw, _seatYaw)) > 20f))
            {
                StartBlend();
                _headYaw = 0f;
                _eye = at;
                _headPitch = StartPitch();
            }
            _eye = at;
            _seatYaw = yaw;
            _hasSeat = true;
            if (_sitWhenSeated)
            {
                _sitWhenSeated = false;
                if (FirstPerson)
                    Sit();
            }
            if (_seated)
                Apply();
        }

        /// <summary>
        /// Where the head starts, degrees down: <see cref="SeatPitch"/>, or on a big table (a round one for ten) a little
        /// above its middle, so the faces across it stay clear of the top bar.
        /// </summary>
        private float StartPitch()
        {
            var toMiddle = new Vector2(_eye.x, _eye.z).magnitude;
            if (toMiddle < 0.05f || _eye.y <= 0f)
                return SeatPitch;
            var middle = Mathf.Atan2(_eye.y, toMiddle) * Mathf.Rad2Deg;
            return Mathf.Clamp(middle - MiddleMargin, 12f, SeatPitch);
        }

        /// <summary>Seated, how far above the table's middle the head starts on a big table, in degrees.</summary>
        public const float MiddleMargin = 4f;

        /// <summary>Sits you in your chalker's eyes, the head straight, leaning back. Without a seat yet, as soon as there is one.</summary>
        public void Sit()
        {
            if (!_hasSeat)
            {
                _sitWhenSeated = true;
                return;
            }
            if (!_seated)
                StartBlend();
            _seated = true;
            _headYaw = 0f;
            _headPitch = StartPitch();
            _lean = 0f;
            _zoom = 1f;
            _beforeQuickZoom = null;
            _beforeTopDown = null;
            Apply();
        }

        /// <summary>
        /// Out of your head into the orbit camera: it orbits the point you were looking at on the table, from a little
        /// higher and further back than your eyes, so the view rises rather than jumps.
        /// </summary>
        public void Lift()
        {
            if (!_seated || HoldSeated)
                return;
            var rotation = Quaternion.Euler(_headPitch, _seatYaw + _headYaw, 0f);
            var from = _eye;
            var forward = rotation * Vector3.forward;
            // Where the view meets the table, or a point ahead when looking up.
            var pivot = forward.y < -0.15f ? from + forward * (-from.y / forward.y) : from + Vector3.ProjectOnPlane(forward, Vector3.up).normalized * 0.8f;
            pivot.y = 0f;
            var flat = new Vector3(pivot.x - from.x, 0f, pivot.z - from.z);
            if (flat.magnitude > 2f)
                pivot = new Vector3(from.x, 0f, from.z) + flat.normalized * 2f;
            SetView(new View
            {
                Pivot = pivot,
                Yaw = _seatYaw + _headYaw,
                Pitch = Mathf.Clamp(Mathf.Max(_headPitch, 50f), _minPitch, _maxPitch),
                Distance = Mathf.Clamp(Vector3.Distance(from, pivot) + 0.6f, _minDistance, _maxDistance),
            });
        }

        /// <summary>O: seated or lifted, whichever you are not.</summary>
        public void ToggleSeated()
        {
            if (_seated)
                Lift();
            else
                Sit();
        }

        private void StartBlend()
        {
            _blendPosition = transform.position;
            _blendRotation = transform.rotation;
            _blend = 0f;
        }

        /// <summary>
        /// Turns the starting view to look across the table from <paramref name="seatPoint"/> (a seat's hand zone). Space
        /// returns here. With the hand's <paramref name="handYaw"/> the view faces straight out from the seat's edge, as in
        /// TTS, instead of at the centre (they differ for seats away from the middle of a side, on tables of five or more);
        /// a yaw that points well away from the table (an imported hand turned its own way) is ignored.
        /// </summary>
        public void FaceFrom(Vector3 seatPoint, float? handYaw = null)
        {
            var across = new Vector3(_home.Pivot.x - seatPoint.x, 0f, _home.Pivot.z - seatPoint.z);
            if (across.sqrMagnitude < 1e-6f)
                return;
            var towardCentre = Mathf.Atan2(across.x, across.z) * Mathf.Rad2Deg;
            _home.Yaw = handYaw is { } yaw && Mathf.Abs(Mathf.DeltaAngle(yaw, towardCentre)) < 60f ? yaw : towardCentre;
            ResetView();
        }

        /// <summary>Z or middle click on the table: zoom onto a point; again to go back.</summary>
        public void ToggleQuickZoom(Vector3 point)
        {
            if (_beforeQuickZoom is { } back)
            {
                _beforeQuickZoom = null;
                if (_quickZoomFromSeat)
                    Sit();
                else
                    SetView(back);
                return;
            }
            _quickZoomFromSeat = _seated;
            _beforeQuickZoom = Current;
            SetView(new View { Pivot = point, Yaw = _yaw, Pitch = Mathf.Max(_pitch, 50f), Distance = QuickZoomDistance });
        }

        /// <summary>P: top-down view of the whole table; again to go back (to the seat, when you were seated).</summary>
        public void ToggleTopDown()
        {
            if (_beforeTopDown is { } back)
            {
                _beforeTopDown = null;
                if (_topDownFromSeat)
                    Sit();
                else
                    SetView(back);
                return;
            }
            _topDownFromSeat = _seated;
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

        /// <summary>The starting distance the scene was built with, for the default table.</summary>
        private float _sceneDistance;

        private void Awake()
        {
            _home = Current;
            _sceneDistance = _distance;
            Apply();
        }

        private void LateUpdate()
        {
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            if (_blend < 1f)
                _blend = Mathf.Min(1f, _blend + Time.unscaledDeltaTime / BlendSeconds);
            if (_seated && Seated(mouse, keyboard))
            {
                Apply();
                return;
            }

            if (mouse != null && !SuppressMouse)
            {
                var delta = mouse.delta.ReadValue();
                var shift = keyboard != null && keyboard.shiftKey.isPressed;
                if (mouse.middleButton.isPressed || (mouse.rightButton.isPressed && shift))
                    PanByPixels(delta);
                else if (mouse.rightButton.isPressed)
                {
                    var degrees = _orbitDegreesPerPixel * OrbitScale;
                    _yaw += delta.x * degrees;
                    _pitch = Mathf.Clamp(_pitch - delta.y * degrees * (InvertOrbitY ? -1f : 1f), _minPitch, _maxPitch);
                }

                var scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f && !SuppressWheel && (keyboard == null || !keyboard.altKey.isPressed))
                    _distance = Mathf.Clamp(_distance * Mathf.Pow(1f - Mathf.Min(0.6f, _zoomPerWheelStep * ZoomScale), Mathf.Sign(scroll)), _minDistance, _maxDistance);
            }

            if (keyboard != null && !keyboard.ctrlKey.isPressed && !SuppressKeys)
            {
                var move = Vector2.zero;
                if (KeyBindings.Held(keyboard, GameAction.PanForward) || keyboard.upArrowKey.isPressed) move.y += 1;
                if (KeyBindings.Held(keyboard, GameAction.PanBack) || keyboard.downArrowKey.isPressed) move.y -= 1;
                if (KeyBindings.Held(keyboard, GameAction.PanRight) || keyboard.rightArrowKey.isPressed) move.x += 1;
                if (KeyBindings.Held(keyboard, GameAction.PanLeft) || keyboard.leftArrowKey.isPressed) move.x -= 1;
                if (move != Vector2.zero)
                    PanBy(move.normalized * (_keyPanSpeed * PanScale * _distance * Time.unscaledDeltaTime));
                if (!SuppressTurnKeys && KeyBindings.Held(keyboard, GameAction.RotateLeft)) _yaw -= _keyTurnSpeed * Time.unscaledDeltaTime;
                if (!SuppressTurnKeys && KeyBindings.Held(keyboard, GameAction.RotateRight)) _yaw += _keyTurnSpeed * Time.unscaledDeltaTime;
            }

            Apply();
        }

        /// <summary>
        /// The seated controls: right drag and Q/E turn the head, the wheel leans in and out. Panning (middle drag, Shift +
        /// right drag, WASD) or the wheel out while sitting back lifts you into the orbit camera: false then, and the orbit
        /// controls take this frame's input.
        /// </summary>
        private bool Seated(Mouse mouse, Keyboard keyboard)
        {
            if (_pull > 0 && Time.unscaledTime - _pullAt > PullSettle)
                _pull = 0;
            _pullShown = Mathf.MoveTowards(_pullShown, _pull, Time.unscaledDeltaTime * 8f);
            if (mouse != null && !SuppressMouse)
            {
                var delta = mouse.delta.ReadValue();
                var shift = keyboard != null && keyboard.shiftKey.isPressed;
                if (mouse.middleButton.isPressed || (mouse.rightButton.isPressed && shift))
                {
                    Lift();
                    return false;
                }
                if (mouse.rightButton.isPressed)
                {
                    var degrees = _orbitDegreesPerPixel * OrbitScale;
                    _headYaw = Mathf.Clamp(_headYaw + delta.x * degrees, -MaxHeadYaw, MaxHeadYaw);
                    _headPitch = Mathf.Clamp(_headPitch - delta.y * degrees * (InvertOrbitY ? -1f : 1f), MinHeadPitch, MaxHeadPitch);
                }
                var scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f && !SuppressWheel && (keyboard == null || !keyboard.altKey.isPressed))
                {
                    if (scroll < 0f && _lean <= 0f)
                    {
                        // Resistance: sitting back, the view gives a little with each step out and lifts only after
                        // LiftSteps in a row; a pause lets it settle back.
                        _pull++;
                        _pullAt = Time.unscaledTime;
                        if (_pull >= LiftSteps)
                        {
                            _pull = 0;
                            Lift();
                        }
                        return true;
                    }
                    _pull = 0;
                    // In: lean over the table, then zoom toward the pointer. Out: unzoom first, then sit back.
                    if (scroll > 0f && _lean >= 1f)
                        ZoomToward(mouse.position.ReadValue());
                    else if (scroll < 0f && _zoom > 1f)
                        _zoom = Mathf.Max(1f, _zoom / ZoomStep);
                    else
                        _lean = Mathf.Clamp01(_lean + LeanStep * Mathf.Sign(scroll));
                }
            }
            if (keyboard != null && !keyboard.ctrlKey.isPressed && !SuppressKeys)
            {
                if (KeyBindings.Held(keyboard, GameAction.PanForward) || keyboard.upArrowKey.isPressed ||
                    KeyBindings.Held(keyboard, GameAction.PanBack) || keyboard.downArrowKey.isPressed ||
                    KeyBindings.Held(keyboard, GameAction.PanRight) || keyboard.rightArrowKey.isPressed ||
                    KeyBindings.Held(keyboard, GameAction.PanLeft) || keyboard.leftArrowKey.isPressed)
                {
                    Lift();
                    return false;
                }
                if (!SuppressTurnKeys && KeyBindings.Held(keyboard, GameAction.RotateLeft))
                    _headYaw = Mathf.Max(-MaxHeadYaw, _headYaw - _keyTurnSpeed * Time.unscaledDeltaTime);
                if (!SuppressTurnKeys && KeyBindings.Held(keyboard, GameAction.RotateRight))
                    _headYaw = Mathf.Min(MaxHeadYaw, _headYaw + _keyTurnSpeed * Time.unscaledDeltaTime);
            }
            return true;
        }

        /// <summary>
        /// Seated: one step of optical zoom toward <paramref name="screen"/> (pixels): the view narrows and the head turns
        /// toward the pointer by as much, so what is under the pointer stays under it.
        /// </summary>
        public void ZoomToward(Vector2 screen)
        {
            if (!_seated || _zoom >= MaxZoom || !TryGetComponent<Camera>(out var cam))
                return;
            var step = Mathf.Min(ZoomStep, MaxZoom / _zoom);
            var aim = Quaternion.LookRotation(cam.ScreenPointToRay(screen).direction).eulerAngles;
            var share = 1f - 1f / step;
            _headYaw = Mathf.Clamp(Mathf.LerpAngle(_headYaw, Mathf.DeltaAngle(_seatYaw, aim.y), share), -MaxHeadYaw, MaxHeadYaw);
            _headPitch = Mathf.Clamp(Mathf.LerpAngle(_headPitch, Mathf.DeltaAngle(0f, aim.x), share), MinHeadPitch, MaxHeadPitch);
            _zoom *= step;
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
            Vector3 position;
            Quaternion rotation;
            if (_seated)
            {
                // The eyes are your chalker's, which leans with you (it is posed from Lean before it reports them).
                rotation = Quaternion.Euler(_headPitch, _seatYaw + _headYaw, 0f);
                // Pulling out against the resistance eases the eyes back and up a little.
                position = _eye + Quaternion.Euler(0f, _seatYaw, 0f) * new Vector3(0f, 0.02f, -0.035f) * _pullShown;
            }
            else
            {
                _pivot.x = Mathf.Clamp(_pivot.x, -_pivotExtents.x, _pivotExtents.x);
                _pivot.z = Mathf.Clamp(_pivot.z, -_pivotExtents.y, _pivotExtents.y);
                rotation = Quaternion.Euler(_pitch, _yaw, 0f);
                position = _pivot - rotation * Vector3.forward * _distance;
            }
            if (_blend < 1f)
            {
                var t = Mathf.SmoothStep(0f, 1f, _blend);
                position = Vector3.Lerp(_blendPosition, position, t);
                rotation = Quaternion.Slerp(_blendRotation, rotation, t);
            }
            transform.SetPositionAndRotation(position, rotation);
            if (TryGetComponent<Camera>(out var cam))
            {
                if (_liftedNearClip < 0f)
                    _liftedNearClip = cam.nearClipPlane;
                cam.nearClipPlane = _seated || _blend < 1f ? Mathf.Min(SeatedNearClip, _liftedNearClip) : _liftedNearClip;
                if (_baseFov < 0f)
                    _baseFov = cam.fieldOfView;
                if (!_seated)
                    _zoom = 1f;
                cam.fieldOfView = _baseFov / _zoom;
            }
        }
    }
}
