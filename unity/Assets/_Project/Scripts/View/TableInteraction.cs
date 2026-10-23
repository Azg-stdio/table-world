using System.Linq;
using Core.Engine;
using Core.Rules;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TableWorld.View
{
    /// <summary>
    /// Mouse and keyboard on the table. Every gesture ends in a <see cref="Command"/> sent through <see cref="TableView"/>:
    /// <list type="bullet">
    /// <item>Left drag: grab. On a deck or bag it takes one item; Shift + left drag moves the deck or bag itself.</item>
    /// <item>Release over a deck, card or bag: insert into it. Near a snap point: snap. Otherwise physics settles it and
    /// <see cref="TableView"/> reports the final pose.</item>
    /// <item>F flip, R roll a die / shuffle a deck or bag, L lock/unlock, 1–9 over a deck deal to the local seat, Q/E rotate
    /// the held object. F5 quick save, F9 quick load.</item>
    /// </list>
    /// The public methods are the same gestures without input devices, for tests and tools.
    /// </summary>
    public sealed class TableInteraction : MonoBehaviour
    {
        public const float HoldHeight = 0.04f;
        public const float SnapRadius = 0.04f;
        public const float RotateStep = 15f;
        public const float MaxThrowSpeed = 0.6f;
        public const float FollowRate = 25f;

        [SerializeField] private TableView _table;
        [SerializeField] private Camera _camera;
        [SerializeField] private TableCamera _tableCamera;

        private readonly RaycastHit[] _hits = new RaycastHit[32];
        private Vector3 _target;
        private Vector3 _velocity;

        public ObjectView Held { get; private set; }
        public ObjectView Hovered { get; private set; }

        public void Configure(TableView table, Camera camera, TableCamera tableCamera)
        {
            _table = table;
            _camera = camera;
            _tableCamera = tableCamera;
        }

        private void Update()
        {
            if (_table == null || _table.Engine == null || _camera == null)
                return;
            if (Held == null && _tableCamera != null)
                _tableCamera.SuppressTurnKeys = false;

            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            if (mouse == null)
                return;

            var ray = _camera.ScreenPointToRay(mouse.position.ReadValue());
            Hovered = RaycastObject(ray, Held, out _);

            if (mouse.leftButton.wasPressedThisFrame && Held == null && Hovered != null)
                Grab(Hovered, moveContainer: keyboard != null && keyboard.shiftKey.isPressed);

            if (Held != null)
            {
                if (RaycastSurface(ray, Held, out var point))
                    DragTo(point);
                if (mouse.leftButton.wasReleasedThisFrame)
                    Release();
            }

            if (keyboard != null)
                HandleKeys(keyboard);
            if (_tableCamera != null)
                _tableCamera.SuppressTurnKeys = Held != null;
        }

        private void FixedUpdate()
        {
            if (Held == null)
                return;
            var body = Held.Body;
            var next = Vector3.Lerp(body.position, _target, 1f - Mathf.Exp(-FollowRate * Time.fixedDeltaTime));
            _velocity = (next - body.position) / Time.fixedDeltaTime;
            body.MovePosition(next);
        }

        private void HandleKeys(Keyboard keyboard)
        {
            if (Held == null && keyboard.f5Key.wasPressedThisFrame)
                Debug.Log($"[Table] Saved {_table.Save(TableView.SavePath(TableView.QuickSaveName))}");
            if (Held == null && keyboard.f9Key.wasPressedThisFrame)
                _table.LoadFile(TableView.SavePath(TableView.QuickSaveName));

            var target = Held != null ? Held : Hovered;
            if (target == null)
                return;
            if (keyboard.fKey.wasPressedThisFrame)
                Flip(target);
            if (keyboard.rKey.wasPressedThisFrame)
                RollOrShuffle(target);
            if (keyboard.lKey.wasPressedThisFrame)
                ToggleLock(target);
            if (Held != null && keyboard.qKey.wasPressedThisFrame)
                RotateHeld(-RotateStep);
            if (Held != null && keyboard.eKey.wasPressedThisFrame)
                RotateHeld(RotateStep);
            for (var n = 1; n <= 9; n++)
            {
                if (keyboard[Key.Digit1 + n - 1].wasPressedThisFrame || keyboard[Key.Numpad1 + n - 1].wasPressedThisFrame)
                    DealToLocalSeat(target, n);
            }
        }

        // ---- gestures ---------------------------------------------------------------------------------------------

        /// <summary>Starts holding <paramref name="view"/>, or one item taken out of it when it is a deck or bag.</summary>
        public bool Grab(ObjectView view, bool moveContainer)
        {
            if (Held != null || view == null)
                return false;

            var model = view.Model;
            var isFullContainer = model is Deck { Cards: { Count: > 0 } } || model is Bag;
            if (isFullContainer && !moveContainer)
            {
                var lift = view.transform.position + Vector3.up * HoldHeight;
                var result = _table.Execute(new TakeCommand { ContainerId = view.Id, Position = lift.ToCore(), Actor = _table.LocalSeat });
                if (!result.Ok || !(_table.ViewOf(result.CreatedId) is { } taken))
                    return false;
                view = taken;
            }
            else if (model.Locked)
            {
                return false;
            }

            Held = view;
            view.Held = true;
            view.Body.isKinematic = true;
            _target = view.transform.position + Vector3.up * HoldHeight;
            _velocity = Vector3.zero;
            return true;
        }

        /// <summary>Moves the held object so it hovers above <paramref name="surfacePoint"/>.</summary>
        public void DragTo(Vector3 surfacePoint)
        {
            if (Held == null)
                return;
            var halfHeight = Held.Renderer.bounds.extents.y;
            _target = surfacePoint + Vector3.up * (HoldHeight + halfHeight);
        }

        /// <summary>Drops the held object: insert into what is below it, snap to a snap point, or let physics settle it.</summary>
        public void Release()
        {
            var view = Held;
            if (view == null)
                return;
            Held = null;
            view.Held = false;

            var below = ObjectBelow(view);
            if (below != null && CanDropInto(view.Model, below.Model))
            {
                var insert = _table.Execute(new InsertCommand { ObjectId = view.Id, IntoId = below.Id, Actor = _table.LocalSeat });
                if (insert.Ok)
                    return;
            }

            if (NearestSnapPoint(view.transform.position) is { } snap)
            {
                var p = snap.Transform.Position.ToUnity();
                view.transform.position = new Vector3(p.x, view.transform.position.y, p.z);
                if (snap.RotationSnap)
                {
                    var e = view.transform.eulerAngles;
                    view.transform.rotation = Quaternion.Euler(e.x, snap.Transform.Rotation.Y, e.z);
                }
                _velocity = Vector3.zero;
            }

            view.Body.isKinematic = view.Model.Locked;
            if (!view.Body.isKinematic)
            {
                view.Body.linearVelocity = Vector3.ClampMagnitude(_velocity, MaxThrowSpeed);
                view.Body.angularVelocity = Vector3.zero;
                view.Body.WakeUp();
            }
        }

        public bool Flip(ObjectView view)
        {
            // A held object's yaw lives only in the view until it is dropped; record it before flipping.
            if (view.Held)
                _table.ReportPose(view);
            return _table.Execute(new FlipCommand { ObjectId = view.Id, Actor = _table.LocalSeat }).Ok;
        }

        public bool RollOrShuffle(ObjectView view)
        {
            switch (view.Model)
            {
                case Die _:
                    if (view == Held)
                        Release();
                    return _table.Execute(new RollCommand { ObjectId = view.Id, Actor = _table.LocalSeat }).Ok;
                case Deck _:
                case Bag _:
                    return _table.Execute(new ShuffleCommand { ObjectId = view.Id, Actor = _table.LocalSeat }).Ok;
                default:
                    return false;
            }
        }

        public bool ToggleLock(ObjectView view)
        {
            if (view == Held)
                Release();
            return _table.Execute(new LockCommand { ObjectId = view.Id, Locked = !view.Model.Locked, Actor = _table.LocalSeat }).Ok;
        }

        public bool DealToLocalSeat(ObjectView view, int count)
        {
            if (!(view.Model is Deck) || _table.LocalSeat == null)
                return false;
            return _table.Execute(new DealCommand { DeckId = view.Id, Count = count, Seats = { _table.LocalSeat }, Actor = _table.LocalSeat }).Ok;
        }

        public void RotateHeld(float degrees)
        {
            if (Held != null)
                Held.transform.rotation = Quaternion.AngleAxis(degrees, Vector3.up) * Held.transform.rotation;
        }

        // ---- queries ----------------------------------------------------------------------------------------------

        private static bool CanDropInto(TableObject dropped, TableObject target) =>
            target is Bag || ((dropped is Card || dropped is Deck) && (target is Card || target is Deck));

        private ObjectView ObjectBelow(ObjectView view)
        {
            var origin = view.transform.position;
            var ray = new Ray(origin, Vector3.down);
            return RaycastObject(ray, view, out _);
        }

        private SnapPoint NearestSnapPoint(Vector3 position)
        {
            SnapPoint best = null;
            var bestDistance = SnapRadius;
            foreach (var snap in _table.Engine.State.Zones.OfType<SnapPoint>())
            {
                var p = SnapWorldPosition(snap);
                var distance = Vector2.Distance(new Vector2(position.x, position.z), new Vector2(p.x, p.z));
                if (distance <= bestDistance)
                {
                    best = snap;
                    bestDistance = distance;
                }
            }
            return best;
        }

        /// <summary>Snap points attached to an object move with it (TTS AttachedSnapPoints): their position is local to the parent.</summary>
        private Vector3 SnapWorldPosition(SnapPoint snap)
        {
            var local = snap.Transform.Position.ToUnity();
            var parent = _table.ViewOf(snap.Parent);
            return parent != null ? parent.transform.TransformPoint(local) : local;
        }

        private ObjectView RaycastObject(Ray ray, ObjectView ignore, out RaycastHit hit)
        {
            var count = Physics.RaycastNonAlloc(ray, _hits, 50f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
            hit = default;
            ObjectView best = null;
            var bestDistance = float.MaxValue;
            for (var i = 0; i < count; i++)
            {
                var view = _hits[i].collider.GetComponentInParent<ObjectView>();
                if (view == null || view == ignore || _hits[i].distance >= bestDistance)
                    continue;
                best = view;
                bestDistance = _hits[i].distance;
                hit = _hits[i];
            }
            return best;
        }

        /// <summary>First surface (table or object) under the ray that is not <paramref name="ignore"/>.</summary>
        private bool RaycastSurface(Ray ray, ObjectView ignore, out Vector3 point)
        {
            var count = Physics.RaycastNonAlloc(ray, _hits, 50f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            point = default;
            var bestDistance = float.MaxValue;
            for (var i = 0; i < count; i++)
            {
                var view = _hits[i].collider.GetComponentInParent<ObjectView>();
                if (view != null && view == ignore)
                    continue;
                if (_hits[i].distance < bestDistance)
                {
                    bestDistance = _hits[i].distance;
                    point = _hits[i].point;
                }
            }
            return bestDistance < float.MaxValue;
        }
    }
}
