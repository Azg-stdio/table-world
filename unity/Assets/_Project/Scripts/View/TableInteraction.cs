using System;
using System.Collections.Generic;
using System.Linq;
using Core.Engine;
using Core.Rules;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TableWorld.View
{
    /// <summary>
    /// Mouse and keyboard on the table, following Tabletop Simulator (spec and bindings: docs/tts-interactions.md).
    /// Every gesture is a public method that ends in core commands sent through <see cref="TableView"/>; the input code in
    /// <see cref="Update"/> only maps devices to those methods, so tests and the context menu use exactly the same paths.
    /// </summary>
    public sealed class TableInteraction : MonoBehaviour
    {
        public const float HoldHeight = 0.04f;
        public const float TapDownHeight = 0.004f;
        public const float MinHoldHeight = 0.004f;
        public const float MaxHoldHeight = 0.3f;
        public const float SnapRadius = 0.04f;
        public const float RotateStep = 15f;
        public const float MaxThrowSpeed = 0.6f;
        public const float MaxDieThrowSpeed = 1.2f;
        public const float MaxDieSpin = 40f;
        public const float FollowRate = 25f;
        public const float ScaleStep = 1.1f;
        public const float ScaleRepeatSeconds = 0.12f;
        public const float DigitBufferSeconds = 1f;
        public const float ClickMaxSeconds = 0.3f;
        public const float ClickMaxPixels = 6f;
        public const float NudgeRadius = 0.08f;
        public const float NudgeSpeed = 0.8f;

        [SerializeField] private TableView _table;
        [SerializeField] private Camera _camera;
        [SerializeField] private TableCamera _tableCamera;

        private readonly RaycastHit[] _hits = new RaycastHit[64];
        private readonly List<ObjectView> _held = new List<ObjectView>();
        private readonly Dictionary<ObjectView, Vector3> _offsets = new Dictionary<ObjectView, Vector3>();
        private readonly List<ObjectView> _selection = new List<ObjectView>();
        private readonly List<string> _clipboard = new List<string>();
        private Vector3 _target;
        private Vector3 _velocity;
        private Vector3 _spin;
        private float _holdHeight = HoldHeight;
        private bool _tapDown;
        private float _nextScale;
        private string _digits = "";
        private float _digitTime;
        private ObjectView _digitTarget;
        private Vector2 _leftDown, _rightDown, _middleDown;
        private float _leftTime, _rightTime, _middleTime;
        private bool _boxing;
        private bool _measuring;
        private Vector3 _measureStart;
        private ObjectView _tooltipView;
        private float _tooltipSince;
        private ObjectView _looking;
        private bool _peeking;
        private bool _lookingByKey;

        // ---- state the HUD and tests read ---------------------------------------------------------------------------

        /// <summary>The primary held object (the one the pointer grabbed).</summary>
        public ObjectView Held => _held.Count > 0 ? _held[0] : null;
        public IReadOnlyList<ObjectView> HeldGroup => _held;
        public ObjectView Hovered { get; private set; }
        public IReadOnlyList<ObjectView> Selection => _selection;

        /// <summary>Last object clicked (the HUD shows its actions). Cleared by clicking empty table.</summary>
        public ObjectView Selected { get; private set; }

        /// <summary>Screen rectangle of a box selection in progress (pixels, origin bottom-left), or null.</summary>
        public Rect? BoxRect { get; private set; }

        /// <summary>The object whose tooltip should show, and for how long the pointer has rested on it.</summary>
        public ObjectView TooltipView => _tooltipView;
        public float TooltipSeconds => _tooltipView == null ? 0f : Time.unscaledTime - _tooltipSince;

        /// <summary>Digits typed over a deck or a multi-state object, waiting for the 1 s pause (TTS: "1" "2" = 12).</summary>
        public string PendingDigits => _digits;

        /// <summary>Distance being measured with Tab + drag, in metres, or null.</summary>
        public float? MeasuredDistance { get; private set; }

        public ObjectView Looking => _looking;
        public bool Peeking => _peeking;

        /// <summary>Set by the HUD: true when a screen point is over UI, so clicks there never reach the table.</summary>
        public Func<Vector2, bool> PointerBlocked { get; set; }

        /// <summary>Right click without dragging: on an object (or null for the table) at a screen point. The HUD opens a menu.</summary>
        public event Action<ObjectView, Vector2> ContextMenuRequested;

        /// <summary>Alt (look) or Alt + Shift (peek) over an object; null when released.</summary>
        public event Action<ObjectView, bool> LookChanged;

        /// <summary>Tab + click: a ping at a world point.</summary>
        public event Action<Vector3> Pinged;

        public void Configure(TableView table, Camera camera, TableCamera tableCamera)
        {
            _table = table;
            _camera = camera;
            _tableCamera = tableCamera;
        }

        // ---- device input ---------------------------------------------------------------------------------------

        private void Update()
        {
            if (_table == null || _table.Engine == null || _camera == null)
                return;
            DropDeadReferences();

            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            if (mouse == null || keyboard == null)
                return;

            var pointer = mouse.position.ReadValue();
            var overUi = Held == null && PointerBlocked != null && PointerBlocked(pointer);
            var ray = _camera.ScreenPointToRay(pointer);
            Hovered = overUi ? null : RaycastObject(ray, Held, out _);
            var surface = RaycastSurface(ray, Held, out var point);
            var alt = keyboard.altKey.isPressed;
            var shift = keyboard.shiftKey.isPressed;
            var ctrl = keyboard.ctrlKey.isPressed;
            var tab = keyboard.tabKey.isPressed;

            UpdateTooltip();
            UpdateLook(alt, shift);

            // Left button: grab, box select, or (with Tab) ping and measure.
            if (mouse.leftButton.wasPressedThisFrame && Held == null && !overUi)
            {
                _leftDown = pointer;
                _leftTime = Time.unscaledTime;
                if (tab && surface)
                {
                    _measuring = true;
                    _measureStart = point;
                }
                else if (Hovered != null)
                {
                    Selected = Hovered;
                    if (_selection.Contains(Hovered) && _selection.Count > 1)
                        GrabSelection(Hovered);
                    else
                    {
                        ClearSelection();
                        Grab(Hovered, moveContainer: shift);
                    }
                }
                else
                {
                    Selected = null;
                    _boxing = true;
                }
            }
            if (_measuring)
            {
                if (surface)
                    MeasuredDistance = Vector3.Distance(Flat(_measureStart), Flat(point));
                if (mouse.leftButton.wasReleasedThisFrame)
                {
                    if (IsClick(pointer, _leftDown, _leftTime))
                        Ping(_measureStart);
                    _measuring = false;
                    MeasuredDistance = null;
                }
            }
            if (_boxing)
            {
                BoxRect = RectBetween(_leftDown, pointer);
                if (mouse.leftButton.wasReleasedThisFrame)
                {
                    if (!IsClick(pointer, _leftDown, _leftTime))
                        SelectInScreenRect(BoxRect.Value);
                    else
                        ClearSelection();
                    _boxing = false;
                    BoxRect = null;
                }
            }

            if (Held != null)
            {
                if (surface)
                    DragTo(point);
                if (mouse.leftButton.wasReleasedThisFrame)
                    Release();
            }

            // Right button: while holding, add the hovered object to the group, or tap down; Alt drops the last one.
            if (mouse.rightButton.wasPressedThisFrame)
            {
                _rightDown = pointer;
                _rightTime = Time.unscaledTime;
                if (Held != null)
                {
                    if (alt)
                        DropLast();
                    else if (Hovered != null)
                        AddToHeld(Hovered);
                    else
                        _tapDown = true;
                }
            }
            if (mouse.rightButton.wasReleasedThisFrame)
            {
                _tapDown = false;
                if (Held == null && !overUi && IsClick(pointer, _rightDown, _rightTime))
                    ContextMenuRequested?.Invoke(Hovered, pointer);
            }

            // Middle button: a click flips the object under the pointer (Alt: other axis) or zooms onto the table; a drag pans.
            if (mouse.middleButton.wasPressedThisFrame)
            {
                _middleDown = pointer;
                _middleTime = Time.unscaledTime;
            }
            if (mouse.middleButton.wasReleasedThisFrame && !overUi && IsClick(pointer, _middleDown, _middleTime))
            {
                var target = Held != null ? Held : Hovered;
                if (target != null)
                    Flip(target, otherAxis: alt);
                else if (surface && _tableCamera != null)
                    _tableCamera.ToggleQuickZoom(point);
            }

            // Wheel while holding raises or lowers it; Alt + wheel rotates.
            var scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                var target = Held != null ? Held : Hovered;
                if (alt && target != null && _looking == null)
                    Rotate(target, RotateStep * Mathf.Sign(scroll), otherAxis: false);
                else if (Held != null)
                    RaiseHeld(0.01f * Mathf.Sign(scroll));
            }

            HandleKeys(keyboard, surface ? point : (Vector3?)null, alt, shift, ctrl);

            if (_tableCamera != null)
            {
                _tableCamera.SuppressTurnKeys = Held != null || Hovered != null || alt;
                _tableCamera.SuppressMouse = Held != null;
            }
        }

        private void HandleKeys(Keyboard keyboard, Vector3? point, bool alt, bool shift, bool ctrl)
        {
            var target = Held != null ? Held : Hovered;
            var targets = _selection.Count > 0 && (target == null || _selection.Contains(target)) ? _selection.ToList()
                : target != null ? new List<ObjectView> { target } : new List<ObjectView>();

            if (ctrl)
            {
                if (keyboard.zKey.wasPressedThisFrame) { if (shift) _table.Redo(); else _table.Undo(); }
                if (keyboard.yKey.wasPressedThisFrame) _table.Redo();
                if (keyboard.cKey.wasPressedThisFrame) Copy(targets);
                if (keyboard.vKey.wasPressedThisFrame && point is { } at) Paste(at);
                for (var n = 1; n <= 9; n++)
                    if (keyboard[Key.Digit1 + n - 1].wasPressedThisFrame)
                        _tableCamera?.Save(n);
                return;
            }

            if (Held == null && keyboard.f5Key.wasPressedThisFrame)
                Debug.Log($"[Table] Saved {_table.Save(TableView.SavePath(TableView.QuickSaveName))}");
            if (Held == null && keyboard.f9Key.wasPressedThisFrame)
                _table.LoadFile(TableView.SavePath(TableView.QuickSaveName));
            if (keyboard.spaceKey.wasPressedThisFrame)
                _tableCamera?.ResetView();
            if (keyboard.zKey.wasPressedThisFrame && point is { } zoomAt)
                _tableCamera?.ToggleQuickZoom(zoomAt);
            if (keyboard.pKey.wasPressedThisFrame)
                _tableCamera?.ToggleTopDown();
            if (keyboard.deleteKey.wasPressedThisFrame)
                Delete(targets);

            // Shift + digit loads a camera; digits alone draw or switch states after a 1 s pause (TTS).
            for (var n = 0; n <= 9; n++)
            {
                var key = n == 0 ? Key.Digit0 : Key.Digit1 + n - 1;
                if (!keyboard[key].wasPressedThisFrame)
                    continue;
                if (shift && n > 0)
                    _tableCamera?.Load(n);
                else if (target != null)
                    TypeDigit(target, n);
            }
            if (_digits.Length > 0 && Time.unscaledTime - _digitTime >= DigitBufferSeconds)
                FlushDigits();

            if (target == null)
                return;
            if (keyboard.fKey.wasPressedThisFrame) Flip(target, otherAxis: false);
            if (keyboard.rKey.wasPressedThisFrame) RollOrShuffle(target);
            if (keyboard.lKey.wasPressedThisFrame) ToggleLock(target);
            if (keyboard.gKey.wasPressedThisFrame) Group(targets.Count > 1 ? targets : HeldPlusHovered());
            if (keyboard.uKey.wasPressedThisFrame && Held != null && Hovered != null) PlaceUnder(Hovered);
            if (keyboard.qKey.wasPressedThisFrame && _looking == null) Rotate(target, -RotateStep, otherAxis: alt);
            if (keyboard.eKey.wasPressedThisFrame && _looking == null) Rotate(target, RotateStep, otherAxis: alt);
            if (keyboard.nKey.isPressed && point is { } nudgeAt) Nudge(nudgeAt);

            var grow = keyboard.equalsKey.isPressed || keyboard.numpadPlusKey.isPressed;
            var shrink = keyboard.minusKey.isPressed || keyboard.numpadMinusKey.isPressed;
            if ((grow || shrink) && Time.unscaledTime >= _nextScale)
            {
                _nextScale = Time.unscaledTime + ScaleRepeatSeconds;
                Scale(targets, grow ? ScaleStep : 1f / ScaleStep);
            }
        }

        private void FixedUpdate()
        {
            if (Held == null)
                return;
            var primary = Held.Body;
            var height = _tapDown ? TapDownHeight : _holdHeight;
            var target = _target + Vector3.up * (height - HoldHeight);
            var next = Vector3.Lerp(primary.position, target, 1f - Mathf.Exp(-FollowRate * Time.fixedDeltaTime));
            _velocity = (next - primary.position) / Time.fixedDeltaTime;
            foreach (var view in _held)
                view.Body.MovePosition(next + _offsets[view]);

            // Shaking a held die (TTS): it rolls in the direction it is moved, and keeps that spin when thrown.
            if (Held.Model is Die)
            {
                var planar = new Vector3(_velocity.x, 0f, _velocity.z);
                var radius = Mathf.Max(0.005f, Held.Renderer.bounds.extents.y);
                var spinTarget = planar.sqrMagnitude > 1e-4f
                    ? Vector3.Cross(Vector3.up, planar).normalized * Mathf.Min(planar.magnitude / radius, MaxDieSpin)
                    : Vector3.zero;
                _spin = Vector3.Lerp(_spin, spinTarget, 1f - Mathf.Exp(-8f * Time.fixedDeltaTime));
                if (_spin.sqrMagnitude > 1e-4f)
                    primary.MoveRotation(Quaternion.AngleAxis(_spin.magnitude * Mathf.Rad2Deg * Time.fixedDeltaTime, _spin.normalized) * primary.rotation);
            }
        }

        // ---- holding ----------------------------------------------------------------------------------------------

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
            else
            {
                _table.Checkpoint();
            }

            _holdHeight = HoldHeight;
            _target = view.transform.position + Vector3.up * HoldHeight;
            _velocity = Vector3.zero;
            _spin = Vector3.zero;
            Hold(view, Vector3.zero);
            return true;
        }

        /// <summary>Picks up every selected object (TTS: picking one of a box selection picks them all).</summary>
        public bool GrabSelection(ObjectView primary)
        {
            if (Held != null || !_selection.Contains(primary) || primary.Model.Locked)
                return false;
            _table.Checkpoint();
            _holdHeight = HoldHeight;
            _target = primary.transform.position + Vector3.up * HoldHeight;
            Hold(primary, Vector3.zero);
            foreach (var view in _selection.Where(v => v != primary && !v.Model.Locked))
                Hold(view, Flat(view.transform.position - primary.transform.position));
            ClearSelection();
            return true;
        }

        /// <summary>While holding, right-click another object: it joins the held group (TTS).</summary>
        public bool AddToHeld(ObjectView view)
        {
            if (Held == null || view == null || _held.Contains(view) || view.Model.Locked || view.Model.Container != null)
                return false;
            Hold(view, Flat(view.transform.position - Held.transform.position) + Vector3.up * (0.006f * _held.Count));
            return true;
        }

        private void Hold(ObjectView view, Vector3 offset)
        {
            _held.Add(view);
            _offsets[view] = offset;
            view.Held = true;
            view.Body.isKinematic = true;
        }

        /// <summary>Moves the held objects so they hover above <paramref name="surfacePoint"/>.</summary>
        public void DragTo(Vector3 surfacePoint)
        {
            if (Held == null)
                return;
            var halfHeight = Held.Renderer.bounds.extents.y;
            _target = surfacePoint + Vector3.up * (HoldHeight + halfHeight);
        }

        /// <summary>Tap down (RMB while holding): the held objects sink to the table while true.</summary>
        public void SetTapDown(bool down) => _tapDown = down;

        /// <summary>Raises (positive) or lowers the held objects, like the wheel while holding.</summary>
        public void RaiseHeld(float metres) => _holdHeight = Mathf.Clamp(_holdHeight + metres, MinHoldHeight, MaxHoldHeight);

        /// <summary>Drops the held objects: one alone may join what is below it or snap; a group just falls.</summary>
        public void Release()
        {
            if (Held == null)
                return;
            var group = _held.ToList();
            _held.Clear();
            _offsets.Clear();
            _tapDown = false;
            if (group.Count == 1)
                ReleaseOne(group[0], joinBelow: true, _velocity, _spin);
            else
                foreach (var view in group)
                    ReleaseOne(view, joinBelow: false, _velocity, Vector3.zero);
        }

        /// <summary>Alt + right click while holding: drop the last object added to the group, or the bottom card of a held deck.</summary>
        public bool DropLast()
        {
            if (Held == null)
                return false;
            if (_held.Count > 1)
            {
                var last = _held[_held.Count - 1];
                _held.RemoveAt(_held.Count - 1);
                _offsets.Remove(last);
                ReleaseOne(last, joinBelow: false, Vector3.zero, Vector3.zero);
                return true;
            }
            if (Held.Model is Deck { Cards: { Count: > 1 } } deck)
            {
                var below = new Vector3(Held.transform.position.x, 0.01f, Held.transform.position.z);
                return _table.Execute(new TakeCommand { ContainerId = deck.Id, FromBottom = true, Position = below.ToCore(), Rotation = deck.Transform.Rotation, Actor = _table.LocalSeat }).Ok;
            }
            Release();
            return true;
        }

        private void ReleaseOne(ObjectView view, bool joinBelow, Vector3 velocity, Vector3 spin)
        {
            view.Held = false;
            if (joinBelow)
            {
                var below = ObjectBelow(view);
                if (below != null && CanDropInto(view.Model, below.Model))
                {
                    var insert = _table.Execute(new InsertCommand { ObjectId = view.Id, IntoId = below.Id, Actor = _table.LocalSeat });
                    if (insert.Ok)
                        return;
                }
                if (NearestSnapPoint(view.transform.position) is { } snap)
                {
                    var p = SnapWorldPosition(snap);
                    view.transform.position = new Vector3(p.x, view.transform.position.y, p.z);
                    if (snap.RotationSnap)
                    {
                        var e = view.transform.eulerAngles;
                        view.transform.rotation = Quaternion.Euler(e.x, snap.Transform.Rotation.Y, e.z);
                    }
                    velocity = Vector3.zero;
                }
            }
            view.Body.isKinematic = view.Model.Locked;
            if (!view.Body.isKinematic)
            {
                var isDie = view.Model is Die;
                view.Body.linearVelocity = Vector3.ClampMagnitude(velocity, isDie ? MaxDieThrowSpeed : MaxThrowSpeed);
                view.Body.angularVelocity = isDie ? spin : Vector3.zero;
                view.Body.WakeUp();
            }
        }

        // ---- object actions -------------------------------------------------------------------------------------

        /// <summary>F / middle click flips; with <paramref name="otherAxis"/> (Alt) it turns over end to end instead of side to side.</summary>
        public bool Flip(ObjectView view, bool otherAxis = false)
        {
            if (view.Held)
                _table.ReportPose(view);
            if (!otherAxis)
                return _table.Execute(new FlipCommand { ObjectId = view.Id, Actor = _table.LocalSeat }).Ok;
            return Turn(view, Quaternion.AngleAxis(180f, view.transform.right));
        }

        /// <summary>Q/E: turn about the vertical by <paramref name="degrees"/>; Alt: tilt about the camera's right axis.</summary>
        public bool Rotate(ObjectView view, float degrees, bool otherAxis = false)
        {
            var axis = otherAxis ? (_camera != null ? _camera.transform.right : Vector3.right) : Vector3.up;
            return Turn(view, Quaternion.AngleAxis(degrees, axis));
        }

        private bool Turn(ObjectView view, Quaternion delta)
        {
            if (view.Model.Locked)
                return false;
            var rotation = delta * view.transform.rotation;
            if (view.Held)
            {
                view.Body.MoveRotation(rotation);
                view.transform.rotation = rotation;
                return true;
            }
            var result = _table.Execute(new MoveCommand
            {
                ObjectId = view.Id,
                Position = view.transform.position.ToCore(),
                Rotation = rotation.eulerAngles.ToCore(),
                Actor = _table.LocalSeat,
            });
            if (result.Ok)
                view.Body.WakeUp();
            return result.Ok;
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

        /// <summary>+ / −: scale every target by <paramref name="factor"/>.</summary>
        public bool Scale(IEnumerable<ObjectView> views, float factor)
        {
            var ok = true;
            foreach (var view in views.ToList())
                ok &= _table.Execute(new ScaleCommand { ObjectId = view.Id, Factor = factor, Actor = _table.LocalSeat }).Ok;
            return ok;
        }

        /// <summary>
        /// U while holding over <paramref name="target"/>: slide the held object under it. A card or deck goes to the
        /// bottom of a deck (or under a card, forming a deck); anything else is set down where the target is and the target
        /// is lifted onto it.
        /// </summary>
        public bool PlaceUnder(ObjectView target)
        {
            var held = Held;
            if (held == null || target == null || target == held || _held.Count > 1)
                return false;
            if ((held.Model is Card || held.Model is Deck) && (target.Model is Deck || target.Model is Card))
            {
                _held.Clear();
                _offsets.Clear();
                held.Held = false;
                held.Body.isKinematic = held.Model.Locked;
                var result = target.Model is Deck
                    ? _table.Execute(new InsertCommand { ObjectId = held.Id, IntoId = target.Id, AtBottom = true, Actor = _table.LocalSeat })
                    : _table.Execute(new InsertCommand { ObjectId = held.Id, IntoId = target.Id, AtBottom = true, Actor = _table.LocalSeat });
                return result.Ok;
            }
            if (target.Model.Locked)
                return false;
            var spot = target.transform.position;
            var lift = held.Renderer.bounds.size.y + 0.002f;
            _table.Execute(new MoveCommand { ObjectId = target.Id, Position = (spot + Vector3.up * lift).ToCore(), Rotation = target.transform.rotation.eulerAngles.ToCore(), Actor = _table.LocalSeat });
            _held.Clear();
            _offsets.Clear();
            held.Held = false;
            held.transform.position = new Vector3(spot.x, held.Renderer.bounds.extents.y + 0.001f, spot.z);
            held.Body.isKinematic = held.Model.Locked;
            held.Body.WakeUp();
            target.Body.WakeUp();
            return true;
        }

        /// <summary>N: push free objects near <paramref name="point"/> away from it.</summary>
        public int Nudge(Vector3 point)
        {
            var pushed = 0;
            foreach (var view in _table.Views.Values)
            {
                if (view == null || view.Held || view.Body.isKinematic)
                    continue;
                var away = Flat(view.transform.position - point);
                if (away.magnitude > NudgeRadius)
                    continue;
                var direction = away.sqrMagnitude < 1e-6f ? Vector3.forward : away.normalized;
                view.Body.linearVelocity = direction * NudgeSpeed + Vector3.up * 0.05f;
                view.Body.WakeUp();
                pushed++;
            }
            return pushed;
        }

        /// <summary>G: cards and decks into one deck (top to bottom in the given order).</summary>
        public bool Group(IReadOnlyList<ObjectView> views)
        {
            var groupable = views.Where(v => v != null && (v.Model is Card || v.Model is Deck)).ToList();
            if (groupable.Count < 2)
                return false;
            if (groupable.Any(v => v.Held))
                Release();
            var result = _table.Execute(new GroupCommand { ObjectIds = groupable.Select(v => v.Id).ToList(), Actor = _table.LocalSeat });
            if (result.Ok)
                ClearSelection();
            return result.Ok;
        }

        /// <summary>Del: delete every target (and a bag's contents).</summary>
        public bool Delete(IEnumerable<ObjectView> views)
        {
            var list = views.Where(v => v != null).ToList();
            if (list.Count == 0)
                return false;
            if (list.Any(v => v.Held))
                Release();
            var ok = true;
            foreach (var view in list)
                ok &= _table.Execute(new DeleteCommand { ObjectId = view.Id, Actor = _table.LocalSeat }).Ok;
            ClearSelection();
            return ok;
        }

        /// <summary>Ctrl+C: remember the targets for pasting.</summary>
        public int Copy(IEnumerable<ObjectView> views)
        {
            _clipboard.Clear();
            _clipboard.AddRange(views.Where(v => v != null).Select(v => v.Id));
            return _clipboard.Count;
        }

        /// <summary>Ctrl+V: copies of the clipboard objects at <paramref name="point"/>, keeping their arrangement.</summary>
        public List<string> Paste(Vector3 point)
        {
            var created = new List<string>();
            var originals = _clipboard.Select(id => _table.Engine.Find(id)).Where(o => o != null && o.Container == null).ToList();
            if (originals.Count == 0)
                return created;
            var anchor = originals[0].Transform.Position.ToUnity();
            foreach (var original in originals)
            {
                var at = Flat(point) + Flat(original.Transform.Position.ToUnity() - anchor) + Vector3.up * 0.03f;
                var result = _table.Execute(new CloneCommand { ObjectId = original.Id, Position = at.ToCore(), Actor = _table.LocalSeat });
                if (result.Ok)
                    created.Add(result.CreatedId);
            }
            return created;
        }

        /// <summary>
        /// Number row over a target: digits typed within 1 s form one number. Over an object with states it switches to
        /// that state; over a deck it deals that many cards to the local seat; over a bag it takes that many objects out.
        /// </summary>
        public void TypeDigit(ObjectView target, int digit)
        {
            if (_digits.Length == 0 || _digitTarget != target)
            {
                _digits = "";
                _digitTarget = target;
            }
            _digits += digit.ToString();
            _digitTime = Time.unscaledTime;
        }

        /// <summary>Acts on the buffered digits now (normally after the 1 s pause).</summary>
        public bool FlushDigits()
        {
            var target = _digitTarget;
            var text = _digits;
            _digits = "";
            _digitTarget = null;
            if (target == null || !int.TryParse(text, out var n) || n <= 0)
                return false;
            return DrawOrSwitch(target, n);
        }

        public bool DrawOrSwitch(ObjectView target, int n)
        {
            var model = target.Model;
            if (model.States.ContainsKey(n.ToString()))
                return _table.Execute(new SetStateCommand { ObjectId = model.Id, State = n.ToString(), Actor = _table.LocalSeat }).Ok;
            if (model is Deck)
                return _table.LocalSeat != null && _table.Execute(new DealCommand { DeckId = model.Id, Count = n, Seats = { _table.LocalSeat }, Actor = _table.LocalSeat }).Ok;
            if (model is Bag && HandOfLocalSeat() is { } hand)
            {
                var ok = true;
                for (var i = 0; i < n && ok; i++)
                    ok = _table.Execute(new TakeCommand { ContainerId = model.Id, Position = hand.Transform.Position, Actor = _table.LocalSeat }).Ok;
                return ok;
            }
            return false;
        }

        public bool DealToLocalSeat(ObjectView view, int count) => view.Model is Deck && DrawOrSwitch(view, count);

        /// <summary>Tab + click: a ping in the local seat's colour.</summary>
        public void Ping(Vector3 point) => Pinged?.Invoke(point);

        // ---- selection --------------------------------------------------------------------------------------------

        public void Select(ObjectView view) => Selected = view;

        /// <summary>Box select: every free object whose centre is inside the screen rectangle.</summary>
        public int SelectInScreenRect(Rect rect)
        {
            ClearSelection();
            foreach (var view in _table.Views.Values)
            {
                if (view == null)
                    continue;
                var screen = _camera.WorldToScreenPoint(view.transform.position);
                if (screen.z > 0 && rect.Contains(new Vector2(screen.x, screen.y)))
                    _selection.Add(view);
            }
            SelectionMarkers.Show(_selection);
            return _selection.Count;
        }

        public void SetSelection(IEnumerable<ObjectView> views)
        {
            ClearSelection();
            _selection.AddRange(views.Where(v => v != null));
            SelectionMarkers.Show(_selection);
        }

        public void ClearSelection()
        {
            _selection.Clear();
            SelectionMarkers.Show(_selection);
        }

        // ---- looking ----------------------------------------------------------------------------------------------

        /// <summary>Alt starts a look at the hovered object and releasing Alt ends it; looks opened by code stay until closed.</summary>
        private void UpdateLook(bool alt, bool shift)
        {
            if (!alt && !_lookingByKey)
                return;
            var target = alt ? (Hovered != null ? Hovered : _looking) : null;
            var peek = alt && shift;
            _lookingByKey = alt && target != null;
            if (target == _looking && peek == _peeking)
                return;
            Look(target, peek);
        }

        /// <summary>Alt (look) / Alt + Shift (peek) without devices: shows or hides the close-up.</summary>
        public void Look(ObjectView view, bool peek)
        {
            _looking = view;
            _peeking = view != null && peek;
            LookChanged?.Invoke(view, _peeking);
        }

        private void UpdateTooltip()
        {
            if (Hovered != _tooltipView)
            {
                _tooltipView = Hovered;
                _tooltipSince = Time.unscaledTime;
            }
        }

        // ---- queries ----------------------------------------------------------------------------------------------

        private static bool CanDropInto(TableObject dropped, TableObject target) =>
            target is Bag || ((dropped is Card || dropped is Deck) && (target is Card || target is Deck));

        private List<ObjectView> HeldPlusHovered()
        {
            var list = new List<ObjectView>();
            if (Hovered != null) list.Add(Hovered);
            list.AddRange(_held);
            return list;
        }

        private Zone HandOfLocalSeat()
        {
            var seat = _table.Engine.State.Players.Seats.FirstOrDefault(s => s.Id == _table.LocalSeat);
            return seat == null ? null : _table.Engine.State.Zones.FirstOrDefault(z => z.Id == seat.HandZone);
        }

        private ObjectView ObjectBelow(ObjectView view)
        {
            var ray = new Ray(view.transform.position, Vector3.down);
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
                if (view == null || view == ignore || _held.Contains(view) || _hits[i].distance >= bestDistance)
                    continue;
                best = view;
                bestDistance = _hits[i].distance;
                hit = _hits[i];
            }
            return best;
        }

        /// <summary>First surface (table or object) under the ray that is not held.</summary>
        private bool RaycastSurface(Ray ray, ObjectView ignore, out Vector3 point)
        {
            var count = Physics.RaycastNonAlloc(ray, _hits, 50f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            point = default;
            var bestDistance = float.MaxValue;
            for (var i = 0; i < count; i++)
            {
                var view = _hits[i].collider.GetComponentInParent<ObjectView>();
                if (view != null && (view == ignore || _held.Contains(view)))
                    continue;
                if (_hits[i].distance < bestDistance)
                {
                    bestDistance = _hits[i].distance;
                    point = _hits[i].point;
                }
            }
            return bestDistance < float.MaxValue;
        }

        private void DropDeadReferences()
        {
            // Views destroyed by the table (inserted, deleted, undone) compare equal to null.
            if (_held.Any(v => v == null))
            {
                foreach (var view in _held.Where(v => v != null))
                    view.Held = false;
                _held.Clear();
                _offsets.Clear();
            }
            _selection.RemoveAll(v => v == null);
            if (Selected == null)
                Selected = null;
            if (_looking == null && !ReferenceEquals(_looking, null))
                Look(null, false);
        }

        private static bool IsClick(Vector2 now, Vector2 down, float downTime) =>
            Time.unscaledTime - downTime <= ClickMaxSeconds && Vector2.Distance(now, down) <= ClickMaxPixels;

        private static Rect RectBetween(Vector2 a, Vector2 b) =>
            Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
