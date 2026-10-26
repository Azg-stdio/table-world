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
        /// <summary>
        /// Below this hand speed a released piece is placed, not thrown (TTS): it drops straight down. Moving the hand
        /// and letting go carried the hand's speed, so pieces slid on 5 cm and more (Miguel, 2026-10-25). A flick throws.
        /// </summary>
        public const float FlickSpeed = 1f;
        public const float MaxDieThrowSpeed = 2f;
        public const float MaxDieSpin = 40f;
        public const float FollowRate = 25f;
        public const float ScaleStep = 1.1f;
        public const float ScaleRepeatSeconds = 0.12f;
        public const float DigitBufferSeconds = 1f;
        public const float ClickMaxSeconds = 0.3f;
        public const float ClickMaxPixels = 6f;
        public const float NudgeRadius = 0.08f;
        public const float NudgeSpeed = 0.8f;

        /// <summary>
        /// TTS press timing on a deck or bag: drag right away to pull out its top item; press and keep still this long to
        /// lift the whole container; release before either to just select it. Shift lifts the container at once.
        /// </summary>
        public const float HoldToLiftSeconds = 0.35f;

        private ObjectView _pressedContainer;

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
        /// <summary>What the left button went down on, to tell a click on it from a drag.</summary>
        private ObjectView _leftPressed;
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

        /// <summary>Where the pointer touches the table (null when it is off the table or over the HUD); other players see it.</summary>
        public Vector3? PointerOnTable { get; private set; }

        /// <summary>Sets the pointer from code (automated sessions without a mouse, with <see cref="IgnoreDevices"/>).</summary>
        public void OverridePointer(Vector3? point) => PointerOnTable = point;
        public ObjectView Hovered { get; private set; }
        /// <summary>A script input on an object was clicked (M5): the HUD opens its editor at that screen point.</summary>
        public event System.Action<ScriptInputView, Vector2> InputEditRequested;

        /// <summary>The script button under the pointer (M5), or null.</summary>
        public ScriptButtonView HoveredButton { get; private set; }
        private bool _rightOnButton;
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

        /// <summary>Set by the HUD: true while the player types in a text field (chat, rename), so keys are text, not shortcuts.</summary>
        public Func<bool> KeyboardBlocked { get; set; }

        /// <summary>
        /// Set by the HUD's hand bar: for a release at a screen point, where across the bar it lands (0..1), or null when
        /// the pointer is not over the bar.
        /// </summary>
        public Func<Vector2, float?> HandDrop { get; set; }

        /// <summary>Set by the HUD's hand bar: the id of the hand card drawn under a screen point, or null.</summary>
        public Func<Vector2, string> HandCardAt { get; set; }

        private Vector2 _lastPointer;

        /// <summary>Layer of held objects: it collides with nothing, so a carried object never shoves dice or cards aside.</summary>
        public const int HeldLayer = 30;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ConfigureHeldLayer()
        {
            for (var layer = 0; layer < 32; layer++)
                Physics.IgnoreLayerCollision(HeldLayer, layer, true);
        }

        private readonly Dictionary<GameObject, int> _layerBeforeHold = new Dictionary<GameObject, int>();

        private void SetHeldLayer(ObjectView view, bool held)
        {
            foreach (var t in view.GetComponentsInChildren<Transform>(true))
            {
                var go = t.gameObject;
                if (held)
                {
                    _layerBeforeHold[go] = go.layer;
                    go.layer = HeldLayer;
                }
                else if (_layerBeforeHold.TryGetValue(go, out var before))
                {
                    go.layer = before;
                    _layerBeforeHold.Remove(go);
                }
            }
        }

        /// <summary>Ignore the real mouse and keyboard: tests that drive gestures from code set it, or a cursor over the Game view would steer held objects.</summary>
        public bool IgnoreDevices { get; set; }

        /// <summary>Right click without dragging: on an object (or null for the table) at a screen point. The HUD opens a menu.</summary>
        public event Action<ObjectView, Vector2> ContextMenuRequested;

        /// <summary>Alt (look) or Alt + Shift (peek) over an object; null when released.</summary>
        public event Action<ObjectView, bool> LookChanged;

        /// <summary>Tab + click: a ping at a world point.</summary>
        public event Action<Vector3> Pinged;

        /// <summary>What the left button does (G28): grab (TTS's default), draw freehand, draw an arrow, or erase drawings.</summary>
        public enum Tool { Grab, Draw, Arrow, Erase, Gizmo }

        /// <summary>The Gizmo tool clicked an object: the HUD opens its exact-position panel.</summary>
        public event Action<ObjectView> GizmoRequested;

        /// <summary>A PDF was clicked: the HUD opens it to read (M7 T8.8).</summary>
        public event Action<ObjectView> ReadRequested;

        private Tool _tool;

        /// <summary>The active tool; changing it drops any drawing in progress.</summary>
        public Tool ActiveTool
        {
            get => _tool;
            set
            {
                _tool = value;
                EndStroke(commit: false);
            }
        }

        /// <summary>Freehand points closer together than this are merged.</summary>
        public const float DrawStep = 0.004f;
        /// <summary>The eraser removes drawings within this distance of the pointer.</summary>
        public const float EraseRadius = 0.012f;

        private List<Vec3> _stroke;
        private GameObject _strokePreview;

        /// <summary>The local player picked something up (for its sound).</summary>
        public event Action<ObjectView> PickedUp;

        // ---- drawing tools (G28) ----------------------------------------------------------------------------------

        /// <summary>One frame of the draw, arrow or erase tool: pressed this frame, held, and where the pointer touches the table.</summary>
        public void HandleDrawingTool(bool pressed, bool held, Vector3? at)
        {
            if (_table.Spectating)
                return;
            if (_tool == Tool.Erase)
            {
                if (held && at is { } p)
                    EraseAt(p);
                return;
            }
            if (pressed && at is { } start)
            {
                _stroke = new List<Vec3> { start.ToCore() };
                return;
            }
            if (_stroke == null)
                return;
            if (held)
            {
                if (at is { } p)
                {
                    if (_tool == Tool.Arrow)
                    {
                        if (_stroke.Count == 1)
                            _stroke.Add(p.ToCore());
                        else
                            _stroke[1] = p.ToCore();
                    }
                    else if (Vector3.Distance(p, _stroke[_stroke.Count - 1].ToUnity()) >= DrawStep && _stroke.Count < DrawingDef.MaxPoints)
                        _stroke.Add(p.ToCore());
                    ShowStroke();
                }
                return;
            }
            EndStroke(commit: true);
        }

        /// <summary>Removes every drawing under the pointer (one command each).</summary>
        public int EraseAt(Vector3 at)
        {
            var hits = _table.Engine.State.Drawings.Where(d => d.DistanceTo(at.x, at.z) <= EraseRadius).Select(d => d.Id).ToList();
            foreach (var id in hits)
                _table.Execute(new EraseDrawingCommand { DrawingId = id, Actor = _table.LocalSeat });
            return hits.Count;
        }

        private DrawingDef StrokeDrawing()
        {
            var seat = _table.Engine.State.Players.Seats.FirstOrDefault(s => s.Id == _table.LocalSeat);
            var color = seat != null ? ZoneViewFactory.SeatColor(seat.Color) : Color.white;
            return new DrawingDef
            {
                Points = new List<Vec3>(_stroke),
                Color = new[] { color.r, color.g, color.b },
                Arrow = _tool == Tool.Arrow,
                Seat = _table.LocalSeat,
            };
        }

        private void ShowStroke()
        {
            if (_strokePreview != null)
                Destroy(_strokePreview);
            _strokePreview = DrawingView.Build(StrokeDrawing(), _table.transform);
        }

        private void EndStroke(bool commit)
        {
            if (_strokePreview != null)
                Destroy(_strokePreview);
            _strokePreview = null;
            var stroke = _stroke;
            _stroke = null;
            if (!commit || stroke == null || stroke.Count < 2)
                return;
            var length = 0f;
            for (var i = 1; i < stroke.Count; i++)
                length += Vector3.Distance(stroke[i - 1].ToUnity(), stroke[i].ToUnity());
            if (length < 0.01f)
                return;
            _stroke = stroke;
            var drawing = StrokeDrawing();
            _stroke = null;
            _table.Execute(new AddDrawingCommand { Drawing = drawing, Actor = _table.LocalSeat });
        }

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

            _selection.RemoveAll(v => v == null);
            ShowSelection();

            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            if (mouse == null || keyboard == null || IgnoreDevices)
                return;

            var pointer = mouse.position.ReadValue();
            _lastPointer = pointer;
            var overUi = Held == null && PointerBlocked != null && PointerBlocked(pointer);
            var ray = _camera.ScreenPointToRay(pointer);
            var hoverHit = default(RaycastHit);
            Hovered = overUi ? null : RaycastObject(ray, Held, out hoverHit);
            var button = Hovered != null && Held == null && hoverHit.collider != null && hoverHit.collider.TryGetComponent(out ScriptButtonView hit) ? hit : null;
            if (button != HoveredButton)
            {
                if (HoveredButton != null)
                    HoveredButton.SetHovered(false);
                if (button != null)
                    button.SetHovered(true);
                HoveredButton = button;
            }
            var surface = RaycastSurface(ray, Held, out var point);
            PointerOnTable = surface && !overUi ? point : (Vector3?)null;
            var typing = KeyboardBlocked != null && KeyboardBlocked();
            var alt = !typing && keyboard.altKey.isPressed;
            var shift = !typing && keyboard.shiftKey.isPressed;
            var ctrl = !typing && keyboard.ctrlKey.isPressed;
            var tab = !typing && keyboard.tabKey.isPressed;

            UpdateTooltip();
            UpdateLook(alt, shift);

            if (_tool == Tool.Gizmo && !tab && Held == null)
            {
                if (mouse.leftButton.wasPressedThisFrame && !overUi && Hovered != null && !Hovered.HideFace)
                {
                    Selected = Hovered;
                    GizmoRequested?.Invoke(Hovered);
                }
            }
            else if (_tool != Tool.Grab && !tab && Held == null)
                HandleDrawingTool(mouse.leftButton.wasPressedThisFrame && !overUi, mouse.leftButton.isPressed, surface && !overUi ? point : (Vector3?)null);
            // Left button on a card in the hand bar: pick that card up (it then follows the pointer like any grab).
            else if (mouse.leftButton.wasPressedThisFrame && Held == null && HandCardAt?.Invoke(pointer) is { } handCard && _table.ViewOf(handCard) is { } fromBar)
            {
                _leftDown = pointer;
                _leftTime = Time.unscaledTime;
                ClearSelection();
                Grab(fromBar, moveContainer: false);
            }
            // Left button on a script button: click it (M5).
            else if (mouse.leftButton.wasPressedThisFrame && Held == null && !overUi && _tool == Tool.Grab && !tab && HoveredButton != null)
                ClickButton(HoveredButton, alt: false);
            // Left button on a script input: type into it (M5).
            else if (mouse.leftButton.wasPressedThisFrame && Held == null && !overUi && _tool == Tool.Grab && !tab && Hovered != null &&
                     hoverHit.collider != null && hoverHit.collider.TryGetComponent(out ScriptInputView input))
                InputEditRequested?.Invoke(input, pointer);
            // Left button on a clickable part of a script's UI on an object (M5).
            else if (mouse.leftButton.wasPressedThisFrame && Held == null && !overUi && _tool == Tool.Grab && !tab && UseObjectUi(ray, "-1"))
            {
            }
            // Left button: grab, box select, or (with Tab) ping and measure.
            else if (mouse.leftButton.wasPressedThisFrame && Held == null && !overUi && (_tool == Tool.Grab || tab))
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
                    _leftPressed = Hovered;
                    if (_selection.Contains(Hovered) && _selection.Count > 1)
                        GrabSelection(Hovered);
                    else
                    {
                        // The object pressed is the selection now (TTS): keys act on it until something else is clicked.
                        SetSelection(new[] { Hovered });
                        if (IsFullContainer(Hovered.Model) && !shift)
                            _pressedContainer = Hovered;
                        else
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

            if (_pressedContainer != null)
            {
                if (!mouse.leftButton.isPressed)
                    _pressedContainer = null;
                else if (Vector2.Distance(pointer, _leftDown) > ClickMaxPixels)
                    PressedContainerResolved(liftWhole: false);
                else if (Time.unscaledTime - _leftTime >= HoldToLiftSeconds)
                    PressedContainerResolved(liftWhole: true);
            }

            if (Held != null)
            {
                if (surface)
                    DragTo(point);
                if (mouse.leftButton.wasReleasedThisFrame)
                    Release();
            }
            // A click on a PDF (no drag) opens it to read (M7 T8.8), locked or not.
            if (mouse.leftButton.wasReleasedThisFrame)
            {
                if (_leftPressed != null && _leftPressed.Model is Pdf && !overUi && IsClick(pointer, _leftDown, _leftTime))
                    ReadRequested?.Invoke(_leftPressed);
                _leftPressed = null;
            }

            // Right button: while holding, add the hovered object to the group, or tap down; Alt drops the last one.
            if (mouse.rightButton.wasPressedThisFrame)
            {
                _rightDown = pointer;
                _rightTime = Time.unscaledTime;
                // A right click on a script button is its alt click (TTS), not the context menu.
                _rightOnButton = Held == null && !overUi && HoveredButton != null;
                if (_rightOnButton)
                    ClickButton(HoveredButton, alt: true);
                else if (Held == null && !overUi && UseObjectUi(ray, "-2"))
                    _rightOnButton = true;
                else if (Held != null)
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
                if (Held == null && !overUi && !_rightOnButton && IsClick(pointer, _rightDown, _rightTime))
                    ContextMenuRequested?.Invoke(Hovered, pointer);
                _rightOnButton = false;
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

            if (!typing)
                HandleKeys(keyboard, surface ? point : (Vector3?)null, alt, shift, ctrl);

            if (_tableCamera != null)
            {
                _tableCamera.SuppressKeys = typing;
                _tableCamera.SuppressTurnKeys = Held != null || Hovered != null || _selection.Count > 0 || alt;
                _tableCamera.SuppressMouse = Held != null;
                // The wheel over a panel scrolls the panel (help, search), not the camera.
                _tableCamera.SuppressWheel = PointerBlocked != null && PointerBlocked(pointer);
            }
        }

        /// <summary>
        /// What the keys act on (TTS, G46): what is held, else the selection, else what is under the pointer. The
        /// selection keeps the keys until a click elsewhere clears it; with nothing at all, Q / E turn the camera.
        /// </summary>
        public List<ObjectView> KeyTargets()
        {
            _selection.RemoveAll(v => v == null);
            if (Held != null)
                return new List<ObjectView> { Held };
            if (_selection.Count > 0)
                return _selection.ToList();
            return Hovered != null ? new List<ObjectView> { Hovered } : new List<ObjectView>();
        }

        private void HandleKeys(Keyboard keyboard, Vector3? point, bool alt, bool shift, bool ctrl)
        {
            var targets = KeyTargets();
            var target = targets.Count > 0 ? targets[0] : null;

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
                _table.LoadFileInSteps(TableView.SavePath(TableView.QuickSaveName), "Quicksave");
            if (keyboard.spaceKey.wasPressedThisFrame)
                _tableCamera?.ResetView();
            if (keyboard.zKey.wasPressedThisFrame && point is { } zoomAt)
                _tableCamera?.ToggleQuickZoom(zoomAt);
            if (keyboard.bKey.wasPressedThisFrame)
                ToggleBlindfold();
            if (keyboard.pKey.wasPressedThisFrame)
                _tableCamera?.ToggleTopDown();
            if (keyboard.deleteKey.wasPressedThisFrame)
                Delete(targets);

            ScriptingKeys(keyboard);

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

            // R rolls (TTS): what is targeted, or, just after a roll, the same dice again even once they have jumped out
            // from under the pointer, so pressing R again and again sends them higher.
            if (keyboard.rKey.wasPressedThisFrame && !_table.Spectating)
            {
                if (RollDice(Held != null ? _held.ToList() : targets) == 0)
                {
                    if (Time.unscaledTime - _lastRollAt < RecentRollSeconds && _lastRolled.Any(v => v != null))
                        RollDice(_lastRolled);
                    else if (target != null)
                        RollOrShuffle(target);
                }
            }
            if (target == null || _table.Spectating)
                return;
            if (keyboard.fKey.wasPressedThisFrame) Flip(target, otherAxis: false);
            if (keyboard.lKey.wasPressedThisFrame) ToggleLock(target);
            if (keyboard.gKey.wasPressedThisFrame) Group(targets.Count > 1 ? targets : HeldPlusHovered());
            if (keyboard.uKey.wasPressedThisFrame && Held != null && Hovered != null) PlaceUnder(Hovered);
            if (_looking == null)
            {
                if (keyboard.qKey.wasPressedThisFrame) BeginRotation(target, -1, alt);
                if (keyboard.eKey.wasPressedThisFrame) BeginRotation(target, +1, alt);
            }
            UpdateRotation(_rotSign < 0 ? keyboard.qKey.isPressed : keyboard.eKey.isPressed);
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
            // Auto raise (TTS): hover above whatever lies under the held object, never through it.
            var halfHeight = Held.Renderer.bounds.extents.y;
            var floor = HighestBelow(new Vector3(target.x, 0f, target.z), primary.position);
            target.y = Mathf.Max(target.y, floor + halfHeight + (_tapDown ? 0.002f : AutoRaiseClearance));
            // Lift before travelling: while it is still below its hover height it barely moves sideways, so a card
            // picked up and dragged at once does not plough through its neighbours.
            var k = 1f - Mathf.Exp(-FollowRate * Time.fixedDeltaTime);
            var rising = primary.position.y < target.y - LiftFirstMargin;
            var kSide = rising ? 1f - Mathf.Exp(-FollowRate * LiftFirstSideFactor * Time.fixedDeltaTime) : k;
            var p0 = primary.position;
            var next = new Vector3(Mathf.Lerp(p0.x, target.x, kSide), Mathf.Lerp(p0.y, target.y, k), Mathf.Lerp(p0.z, target.z, kSide));
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
                // Shaken hard, it rattles: a random axis that changes every few hundredths of a second, faster the faster
                // the hand moves (TTS).
                var speed = planar.magnitude;
                if (speed > RattleSpeed)
                {
                    if (Time.time >= _nextRattle)
                    {
                        _rattleAxis = UnityEngine.Random.onUnitSphere;
                        _rattleAxes.Clear();
                        _nextRattle = Time.time + UnityEngine.Random.Range(0.03f, 0.08f);
                    }
                    spinTarget += _rattleAxis * Mathf.Min((speed - RattleSpeed) / radius * 1.5f, MaxDieSpin);
                }
                _spin = Vector3.Lerp(_spin, spinTarget, 1f - Mathf.Exp(-(speed > RattleSpeed ? 20f : 8f) * Time.fixedDeltaTime));
                // Every held die turns, each on its own rattle axis, so a handful of dice clatters together.
                if (_spin.sqrMagnitude > 1e-4f)
                    foreach (var die in _held.Where(v => v != null && v.Model is Die))
                    {
                        var axis = die == Held || speed <= RattleSpeed ? _spin.normalized : RattleAxisOf(die);
                        die.Body.MoveRotation(Quaternion.AngleAxis(_spin.magnitude * Mathf.Rad2Deg * Time.fixedDeltaTime, axis) * die.Body.rotation);
                    }
            }
        }

        /// <summary>Hand speed above which a held die rattles at random (m/s).</summary>
        public const float RattleSpeed = 0.25f;
        private Vector3 _rattleAxis = Vector3.up;
        private float _nextRattle;
        private readonly Dictionary<ObjectView, Vector3> _rattleAxes = new Dictionary<ObjectView, Vector3>();

        private Vector3 RattleAxisOf(ObjectView die)
        {
            if (!_rattleAxes.TryGetValue(die, out var axis))
                _rattleAxes[die] = axis = UnityEngine.Random.onUnitSphere;
            return axis;
        }

        /// <summary>Gap kept between a held object and the top of what is under it.</summary>
        public const float AutoRaiseClearance = 0.012f;
        /// <summary>While a held object is more than this below its hover height, it moves sideways at <see cref="LiftFirstSideFactor"/> speed.</summary>
        public const float LiftFirstMargin = 0.006f;
        public const float LiftFirstSideFactor = 0.12f;
        private readonly RaycastHit[] _below = new RaycastHit[32];

        /// <summary>Top of the highest thing (table, objects) under the held object's footprint at <paramref name="at"/>, ignoring what is held.</summary>
        private float HighestBelow(Vector3 at, Vector3 heldPosition)
        {
            var extents = Held.Renderer.bounds.extents;
            var top = 0f;
            var origin = new Vector3(at.x, heldPosition.y + 0.5f, at.z);
            var count = Physics.BoxCastNonAlloc(origin, new Vector3(extents.x, 0.0005f, extents.z), Vector3.down, _below, Quaternion.identity,
                heldPosition.y + 1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < count; i++)
            {
                var hit = _below[i];
                if (hit.distance <= 0f || (hit.rigidbody != null && _held.Any(v => v.Body == hit.rigidbody)))
                    continue;
                top = Mathf.Max(top, hit.point.y);
            }
            return top;
        }

        // ---- holding ----------------------------------------------------------------------------------------------

        /// <summary>A deck or bag, or a counted stack (G14): a quick drag takes one off, a long press lifts it whole.</summary>
        private static bool IsFullContainer(TableObject model) => model is Deck { Cards: { Count: > 0 } } || model is Bag || Stacks.CountOf(model) > 1;

        /// <summary>The press on a deck or bag turned into a drag (take its top item) or a long hold (lift it whole).</summary>
        private void PressedContainerResolved(bool liftWhole)
        {
            var container = _pressedContainer;
            _pressedContainer = null;
            if (container != null)
                Grab(container, moveContainer: liftWhole);
        }

        /// <summary>Starts holding <paramref name="view"/>, or one item taken out of it when it is a deck or bag.</summary>
        public bool Grab(ObjectView view, bool moveContainer)
        {
            if (Held != null || view == null || _table.Spectating)
                return false;
            // An attached piece is part of what it is attached to (TTS addAttachment): that is what the hand takes.
            if (view.Model.Joint is { Attached: true } attached && _table.ViewOf(attached.To) is { } whole)
                view = whole;

            var model = view.Model;
            var isFullContainer = IsFullContainer(model);
            _playFromHand = null;
            if (view.HideFace)
            {
                _table.Tell("That card is in another player's hand.");
                return false;
            }
            if (view.RemoteHolder != null)
            {
                _table.Tell($"{view.RemoteHolder} is holding that.");
                return false;
            }
            if (isFullContainer && !moveContainer && RulesTake(view))
                return false;
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
                // During a game that plays cards from hands, a card picked from your hand is played when dropped outside it.
                if (model is Card && Gestures.PlaysObjects(_table.Game) && HandOfLocalSeat() is { } hand && _table.Engine.ZonesOf(model.Id).Contains(hand.Id))
                    _playFromHand = hand;
            }

            _holdHeight = HoldHeight;
            _target = view.transform.position + Vector3.up * HoldHeight;
            _velocity = Vector3.zero;
            _spin = Vector3.zero;
            Hold(view, Vector3.zero);
            // Table scripts hear the pick up (onObjectPickUp, M5); it changes nothing by itself.
            _table.Execute(new PickUpCommand { ObjectId = view.Id, Actor = _table.LocalSeat });
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
            _table.NotifyTouched(view.Id);
            // Taken out of a pile: physics carries it again once it is let go.
            view.PileHeld = false;
            if (view.InHand)
            {
                // Out of the hand's standing pose: it is carried flat and face up, as the state has it.
                view.LeaveHand();
                var flat = view.Model.Transform.Rotation.ToRotation();
                view.Body.rotation = flat;
                view.transform.rotation = flat;
            }
            _table.ForgetRest(view.Id);
            PickedUp?.Invoke(view);
            _held.Add(view);
            _offsets[view] = offset;
            view.Held = true;
            view.Body.isKinematic = true;
            SetHeldLayer(view, true);
        }

        /// <summary>Moves the held objects so they hover above <paramref name="surfacePoint"/>.</summary>
        public void DragTo(Vector3 surfacePoint)
        {
            if (Held == null)
                return;
            var halfHeight = Held.Renderer.bounds.extents.y;
            _target = TableGeometry.ClampToDropArea(surfacePoint) + Vector3.up * (HoldHeight + halfHeight);
            TrackShake(surfacePoint);
        }

        // ---- shaking a held deck or bag shuffles it (G32) --------------------------------------------------------

        /// <summary>Direction changes needed within <see cref="ShakeWindow"/> seconds for a shake.</summary>
        public const int ShakeReversals = 4;
        public const float ShakeWindow = 1.2f;
        /// <summary>A sideways move shorter than this is a tremble, not a shake.</summary>
        public const float ShakeStroke = 0.03f;
        public const float ShakeCooldown = 1.5f;

        private readonly List<float> _shakeTurns = new List<float>();
        private Vector3 _shakeAnchor;
        private Vector3 _shakeDirection;
        private float _lastShake = float.NegativeInfinity;
        private ObjectView _shaking;

        /// <summary>Raised when a shake shuffled what is held (the HUD says so).</summary>
        public event Action<ObjectView> Shaken;

        private void TrackShake(Vector3 point)
        {
            var held = Held;
            if (held == null || _held.Count != 1 || !(held.Model is Deck { Cards: { Count: > 1 } } || held.Model is Bag && !(held.Model is InfiniteBag)))
            {
                _shaking = null;
                return;
            }
            var now = Time.unscaledTime;
            if (_shaking != held)
            {
                _shaking = held;
                _shakeAnchor = point;
                _shakeDirection = Vector3.zero;
                _shakeTurns.Clear();
            }
            var move = point - _shakeAnchor;
            move.y = 0f;
            if (move.magnitude < ShakeStroke)
                return;
            var direction = move.normalized;
            // A stroke back against the previous one is a reversal.
            if (_shakeDirection != Vector3.zero && Vector3.Dot(direction, _shakeDirection) < -0.5f)
                _shakeTurns.Add(now);
            _shakeDirection = direction;
            _shakeAnchor = point;
            _shakeTurns.RemoveAll(t => now - t > ShakeWindow);
            if (_shakeTurns.Count >= ShakeReversals && now - _lastShake > ShakeCooldown)
            {
                _lastShake = now;
                _shakeTurns.Clear();
                if (_table.Execute(new ShuffleCommand { ObjectId = held.Id, Actor = _table.LocalSeat }).Ok)
                    Shaken?.Invoke(held);
            }
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
                    ReleaseOne(view, joinBelow: false, _velocity, view.Model is Die ? Quaternion.AngleAxis(UnityEngine.Random.Range(-40f, 40f), Vector3.up) * _spin : Vector3.zero);
            // Table scripts hear the drop (onObjectDrop, M5) once the release is done (it changes nothing by itself), with
            // the object's pose where it was let go; one that joined a deck or stack is gone and has no drop.
            foreach (var view in group)
                if (view != null && _table.Engine.Find(view.Id) != null && !view.InHand)
                {
                    _table.ReportPose(view);
                    _table.Execute(new DropCommand { ObjectId = view.Id, Actor = _table.LocalSeat });
                }
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
            SetHeldLayer(view, false);
            _table.NotifyTouched(view.Id);
            // Into your own hand (over the hand bar, or over your hand zone): straight into its slot, no fall.
            if (view.Model is Card && HandOfLocalSeat() is { } ownHand)
            {
                var slot = IgnoreDevices ? null : HandDrop?.Invoke(_lastPointer);
                var at = view.transform.position;
                var overZone = Geometry.Contains(ownHand, new Vec3(at.x, ownHand.Transform.Position.Y, at.z));
                if (slot != null || overZone)
                {
                    _playFromHand = null;
                    if (PutInHand(view, ownHand, slot))
                        return;
                }
            }
            if (_playFromHand is { } hand)
            {
                _playFromHand = null;
                var p = view.transform.position;
                if (!Geometry.Contains(hand, new Vec3(p.x, hand.Transform.Position.Y, p.z)) && PlayFromHand(view))
                    return;
            }
            if (joinBelow)
            {
                var below = ObjectBelow(view);
                if (below == null || !CanDropInto(view.Model, below.Model))
                    below = MostOverlapped(view);
                if (below != null && CanDropInto(view.Model, below.Model))
                {
                    var insert = _table.Execute(new InsertCommand { ObjectId = view.Id, IntoId = below.Id, Actor = _table.LocalSeat });
                    if (insert.Ok)
                        return;
                }
                if (NearestSnapPoint(view.transform.position, view) is { } snap)
                {
                    var p = SnapWorldPosition(snap);
                    // The body too: a released kinematic body otherwise keeps its own pose.
                    view.transform.position = new Vector3(p.x, view.transform.position.y, p.z);
                    view.Body.position = view.transform.position;
                    if (snap.RotationSnap)
                    {
                        // A snap point attached to an object turns with it (G39).
                        var e = view.transform.eulerAngles;
                        var parentYaw = _table.ViewOf(snap.Parent) is { } parent ? parent.transform.eulerAngles.y : 0f;
                        view.transform.rotation = Quaternion.Euler(e.x, snap.Transform.Rotation.Y + parentYaw, e.z);
                        view.Body.rotation = view.transform.rotation;
                    }
                    velocity = Vector3.zero;
                }
                else if (_table.Engine.State.Grid is { Snapping: not GridSnapping.Off } grid && view.Model.Grid)
                {
                    // Grid snapping (G28): the drop lands on the nearest cell centre or corner.
                    var (x, z) = grid.Snap(view.transform.position.x, view.transform.position.z);
                    var snapped = new Vector3(x, view.transform.position.y, z);
                    view.transform.position = snapped;
                    view.Body.position = snapped;
                    velocity = Vector3.zero;
                }
            }
            if (_table.PhysicsMode == PhysicsMode.Locked && !view.Model.Locked)
            {
                // Locked physics (G19): nothing falls; the object is set down right where it was let go.
                var rest = _table.RestingSpot(view);
                view.Body.isKinematic = true;
                view.Body.position = rest;
                view.transform.position = rest;
                _table.Execute(new MoveCommand
                {
                    ObjectId = view.Id,
                    Position = rest.ToCore(),
                    Rotation = view.transform.rotation.eulerAngles.ToCore(),
                    Actor = _table.LocalSeat,
                });
                return;
            }
            view.Body.isKinematic = view.Pinned;
            if (!view.Body.isKinematic)
            {
                var isDie = view.Model is Die;
                // Dice keep the hand's motion (that is the roll); other pieces only when flicked.
                if (!isDie && velocity.magnitude < FlickSpeed)
                    velocity = Vector3.zero;
                // A die thrown along the table tumbles over and over as in TTS: it rolls the way it is thrown, with a
                // little hop and some randomness (it stopped dead in half a second).
                var planar = new Vector3(velocity.x, 0f, velocity.z);
                if (isDie && planar.magnitude > 0.3f)
                {
                    var radius = Mathf.Max(0.005f, view.Renderer.bounds.extents.y);
                    spin += Vector3.Cross(Vector3.up, planar.normalized) * (planar.magnitude / radius * 0.5f)
                            + UnityEngine.Random.onUnitSphere * (planar.magnitude * 8f);
                    velocity.y = Mathf.Max(velocity.y, 0.25f * planar.magnitude);
                }
                view.Body.linearVelocity = Vector3.ClampMagnitude(velocity, isDie ? MaxDieThrowSpeed : MaxThrowSpeed);
                view.Body.angularVelocity = isDie ? spin : Vector3.zero;
                view.Body.WakeUp();
                _table.ReportWhenResting(view);
            }
        }

        /// <summary>
        /// Moves <paramref name="card"/> into <paramref name="hand"/>, flat and face up in the state, at <paramref name="slot"/>
        /// (0..1 across the hand) or where it is. The view then glides to its standing place in the hand.
        /// </summary>
        private bool PutInHand(ObjectView card, Zone hand, float? slot)
        {
            var yaw = Quaternion.Euler(0f, hand.Transform.Rotation.Y, 0f);
            var center = hand.Transform.Position.ToUnity();
            var width = hand.Size.X * hand.Transform.Scale.X;
            var local = Quaternion.Inverse(yaw) * (card.transform.position - center);
            var x = slot is { } s ? (s - 0.5f) * width : Mathf.Clamp(local.x, -width / 2f, width / 2f);
            var position = center + yaw * new Vector3(x, 0f, 0f);
            var result = _table.Execute(new MoveCommand
            {
                ObjectId = card.Id,
                Position = new Vector3(position.x, 0f, position.z).ToCore(),
                Rotation = new Vec3(0f, Geometry.FacingYaw(hand.Transform.Rotation.Y), 0f),
                Actor = _table.LocalSeat,
            });
            if (!result.Ok)
                return false;
            card.Body.isKinematic = true;
            _table.PresentHands();
            return true;
        }

        // ---- rules gestures -------------------------------------------------------------------------------------

        /// <summary>The hand a card was picked from, while it may be played by dropping it outside.</summary>
        private Zone _playFromHand;

        /// <summary>
        /// Dragging from a pile the rules draw from performs the draw (it is refused, with the reason, when not legal now).
        /// Returns true when the rules handled the gesture, so nothing is picked up.
        /// </summary>
        private bool RulesTake(ObjectView pile)
        {
            var game = _table.Game;
            var seat = _table.LocalSeat;
            var draws = Gestures.Draws(game, seat, pile.Id);
            if (draws.Count > 0)
            {
                _table.Execute(draws[0]);
                return true;
            }
            if (!Gestures.RulesTakeFrom(game, seat, pile.Id))
                return false;
            _table.Tell(game.Session.CurrentSeat != seat
                ? $"It is {game.Session.CurrentSeat}'s turn."
                : $"The rules do not let you take from {pile.Model.Name ?? pile.Id} now.");
            return true;
        }

        /// <summary>
        /// A card dropped outside the hand it came from: perform the action that plays it, or send it back to the hand
        /// with the reason. Returns true when the drop was handled (played or sent back).
        /// </summary>
        private bool PlayFromHand(ObjectView card)
        {
            var game = _table.Game;
            var seat = _table.LocalSeat;
            var plays = Gestures.Plays(game, seat, card.Id);
            if (plays.Count > 0 && _table.Execute(plays[0]).Ok)
                return true;
            if (plays.Count == 0)
                _table.Tell(game.Session.CurrentSeat != seat
                    ? $"It is {game.Session.CurrentSeat}'s turn."
                    : $"{card.Model.Name ?? card.Id} cannot be played now.");
            // Back to its slot in the hand: the state never moved it.
            card.SnapToState();
            _table.PresentHands();
            return true;
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

        // ---- Q/E rotation (TTS): tap = next 15° step, snapped; hold = continuous, stops anywhere ---------------------

        /// <summary>A press shorter than this is a tap (one snapped step); longer turns continuously while held.</summary>
        public const float RotateTapSeconds = 0.18f;
        /// <summary>Continuous turning speed while Q/E is held.</summary>
        public const float RotateDegreesPerSecond = 120f;
        /// <summary>How fast the shown rotation catches up with the target (a tap glides in ≈0.1 s).</summary>
        public const float RotateFollowRate = 30f;

        private ObjectView _rotView;
        private int _rotSign;
        private bool _rotAltAxis;
        private Vector3 _rotAxis;
        private Quaternion _rotBase;
        private float _rotTarget;
        private float _rotShown;
        private float _rotPressedAt;
        private bool _rotKeyDown;

        /// <summary>The object being turned by Q/E, if a rotation is in progress.</summary>
        public ObjectView Rotating => _rotView;

        /// <summary>Q/E pressed over (or while holding) <paramref name="view"/>.</summary>
        public void BeginRotation(ObjectView view, int sign, bool altAxis)
        {
            if (view == null || view.Model.Locked)
                return;
            // A new press on the object still gliding from the last one continues from where that one was heading.
            if (_rotView != view || _rotAltAxis != altAxis)
            {
                FinishRotation();
                _rotView = view;
                _rotAltAxis = altAxis;
                _rotAxis = altAxis ? (_camera != null ? _camera.transform.right : Vector3.right) : Vector3.up;
                _rotBase = view.transform.rotation;
                _rotTarget = 0f;
                _rotShown = 0f;
                if (!view.Held)
                    view.Body.isKinematic = true;
            }
            _rotSign = sign;
            _rotPressedAt = Time.unscaledTime;
            _rotKeyDown = true;
        }

        /// <summary>Called every frame with whether the rotation key is still down.</summary>
        public void UpdateRotation(bool keyDown)
        {
            if (_rotView == null)
                return;
            var held = Time.unscaledTime - _rotPressedAt;
            if (_rotKeyDown && keyDown && held > RotateTapSeconds)
                _rotTarget += _rotSign * RotateDegreesPerSecond * Time.unscaledDeltaTime;
            if (_rotKeyDown && !keyDown)
            {
                _rotKeyDown = false;
                if (held <= RotateTapSeconds)
                    _rotTarget += SnapStep(_rotSign);
            }
            _rotShown = Mathf.Lerp(_rotShown, _rotTarget, 1f - Mathf.Exp(-RotateFollowRate * Time.unscaledDeltaTime));
            if (!_rotKeyDown && Mathf.Abs(_rotTarget - _rotShown) < 0.05f)
                _rotShown = _rotTarget;
            var rotation = Quaternion.AngleAxis(_rotShown, _rotAxis) * _rotBase;
            _rotView.Body.MoveRotation(rotation);
            _rotView.transform.rotation = rotation;
            if (!_rotKeyDown && _rotShown == _rotTarget)
                FinishRotation();
        }

        /// <summary>
        /// A tap's turn: to the next multiple of <see cref="RotateStep"/> in that direction (from 67° E goes to 75°, Q to
        /// 60°); a full step when already on one. About the camera's right axis (Alt) it is a plain step.
        /// </summary>
        private float SnapStep(int sign)
        {
            if (_rotAltAxis)
                return sign * RotateStep;
            var forward = Quaternion.AngleAxis(_rotTarget, _rotAxis) * _rotBase * Vector3.forward;
            var heading = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            const float onStep = 0.5f;
            var next = sign > 0
                ? Mathf.Floor((heading + onStep) / RotateStep) * RotateStep + RotateStep
                : Mathf.Ceil((heading - onStep) / RotateStep) * RotateStep - RotateStep;
            return next - heading;
        }

        /// <summary>Records the final rotation as one command (a held object reports its pose when dropped).</summary>
        private void FinishRotation()
        {
            var view = _rotView;
            _rotView = null;
            _rotKeyDown = false;
            if (view == null || view.Held)
                return;
            var rotation = Quaternion.AngleAxis(_rotTarget, _rotAxis) * _rotBase;
            view.transform.rotation = rotation;
            view.Body.rotation = rotation;
            view.Body.isKinematic = view.Pinned || view.InHand;
            var result = _table.Execute(new MoveCommand
            {
                ObjectId = view.Id,
                Position = view.transform.position.ToCore(),
                Rotation = rotation.eulerAngles.ToCore(),
                Actor = _table.LocalSeat,
            });
            if (result.Ok && !view.Body.isKinematic)
                view.Body.WakeUp();
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

        /// <summary>
        /// R on several dice (selected or held together): every unlocked die rolls at once (TTS). Held dice are let go first,
        /// so they tumble where they were carried. Returns how many dice rolled.
        /// </summary>
        /// <summary>For this long after a roll, R rolls the same dice again wherever they are (TTS spam-rolling).</summary>
        public const float RecentRollSeconds = 1.5f;
        private List<ObjectView> _lastRolled = new List<ObjectView>();
        private float _lastRollAt = float.NegativeInfinity;

        public int RollDice(IReadOnlyCollection<ObjectView> views)
        {
            var dice = views.Where(v => v != null && v.Model is Die && !v.Model.Locked).ToList();
            if (dice.Count == 0)
                return 0;
            _lastRolled = dice;
            _lastRollAt = Time.unscaledTime;
            if (dice.Any(d => _held.Contains(d)))
                Release();
            return dice.Count(d => _table.Execute(new RollCommand { ObjectId = d.Id, Actor = _table.LocalSeat }).Ok);
        }

        /// <summary>Starts a stopped clock, or pauses a running one on the reading this machine shows (G27).</summary>
        public bool StartOrPause(ObjectView view)
        {
            if (!(view.Model is Clock clock) || clock.Mode == ClockMode.Clock)
                return false;
            var shown = view.TryGetComponent(out Readout readout) ? readout.CurrentSeconds : clock.Seconds;
            return _table.Execute(new SetClockCommand { ObjectId = view.Id, Seconds = shown, Running = !clock.Running, Actor = _table.LocalSeat }).Ok;
        }

        /// <summary>A stopwatch back to zero, a timer back to what it was set to; both stopped.</summary>
        public bool ResetClock(ObjectView view) =>
            view.Model is Clock clock && _table.Execute(new SetClockCommand
            {
                ObjectId = view.Id, Seconds = clock.Mode == ClockMode.Timer ? clock.Duration : 0f, Running = false, Actor = _table.LocalSeat,
            }).Ok;

        /// <summary>Sets a timer to <paramref name="seconds"/>, stopped, ready to start.</summary>
        public bool SetTimer(ObjectView view, float seconds) =>
            _table.Execute(new SetClockCommand
            {
                ObjectId = view.Id, Mode = ClockMode.Timer, Seconds = seconds, Duration = seconds, Running = false, Actor = _table.LocalSeat,
            }).Ok;

        public bool AddToCounter(ObjectView view, int amount) =>
            _table.Execute(new SetCounterCommand { ObjectId = view.Id, Add = amount, Actor = _table.LocalSeat }).Ok;

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
                held.Body.isKinematic = held.Pinned;
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
            // Teleport the body itself (not only the transform) and drop the speed it had while following the cursor,
            // or the object can shoot out from under its new top before the physics step catches up.
            var below = new Vector3(spot.x, held.Renderer.bounds.extents.y + 0.001f, spot.z);
            held.Body.isKinematic = held.Pinned;
            held.Body.position = below;
            held.transform.position = below;
            foreach (var body in new[] { held.Body, target.Body })
            {
                if (!body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
                body.WakeUp();
            }
            _table.ReportWhenResting(held);
            _table.ReportWhenResting(target);
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

        /// <summary>TTS scripting buttons: numpad 1–9 are buttons 1–9 and numpad 0 is 10.</summary>
        public static readonly Key[] ScriptingKeyMap = { Key.Numpad1, Key.Numpad2, Key.Numpad3, Key.Numpad4, Key.Numpad5, Key.Numpad6, Key.Numpad7, Key.Numpad8, Key.Numpad9, Key.Numpad0 };

        /// <summary>Numpad presses and releases go to the host's scripts (onScriptingButtonDown / Up) when the table has any.</summary>
        private void ScriptingKeys(Keyboard keyboard)
        {
            for (var i = 0; i < ScriptingKeyMap.Length; i++)
            {
                var key = keyboard[ScriptingKeyMap[i]];
                if (key.wasPressedThisFrame)
                    PressScriptingKey(i + 1, down: true);
                else if (key.wasReleasedThisFrame)
                    PressScriptingKey(i + 1, down: false);
            }
        }

        public bool PressScriptingKey(int index, bool down)
        {
            if (_table.LocalSeat == null || !HasScripts())
                return false;
            return _table.Execute(new ScriptingKeyCommand { Index = index, Down = down, Actor = _table.LocalSeat }).Ok;
        }

        private bool HasScripts() =>
            _table.Engine.State.Scripts.Count > 0 || _table.Engine.State.Objects.Any(o => !string.IsNullOrEmpty(o.Script));

        /// <summary>Acts on the buffered digits now (normally after the 1 s pause).</summary>
        public bool FlushDigits()
        {
            var target = _digitTarget;
            var text = _digits;
            _digits = "";
            _digitTarget = null;
            if (target == null || !int.TryParse(text, out var n))
                return false;
            // The object's script hears the number first (TTS onNumberTyped); the host then draws or switches unless it says not to.
            if (target.Model.Script is { } script && script.Contains("onNumberTyped") && _table.LocalSeat != null)
                return _table.Execute(new TypeNumberCommand { ObjectId = target.Id, Number = n, Actor = _table.LocalSeat }).Ok;
            // Over a counter the typed number is its new value (G27).
            if (target.Model is Counter)
                return _table.Execute(new SetCounterCommand { ObjectId = target.Id, Value = n, Actor = _table.LocalSeat }).Ok;
            return n > 0 && DrawOrSwitch(target, n);
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
                if (view == null || view.ZoneHidden)
                    continue;
                var screen = _camera.WorldToScreenPoint(view.transform.position);
                if (screen.z > 0 && rect.Contains(new Vector2(screen.x, screen.y)))
                    _selection.Add(view);
            }
            ShowSelection();
            return _selection.Count;
        }

        private readonly HashSet<ObjectView> _outlined = new HashSet<ObjectView>();

        /// <summary>The selection is outlined in this player's seat colour (cyan when watching), around its 3D shape (G46).</summary>
        private void ShowSelection()
        {
            var seat = _table.Engine?.State.Players.Seats.FirstOrDefault(s => s.Id == _table.LocalSeat);
            var color = seat != null ? ZoneViewFactory.SeatColor(seat.Color) : new Color(0.2f, 0.85f, 1f);
            foreach (var view in _outlined.Where(v => !_selection.Contains(v)).ToList())
            {
                Outline.Set(view, "select", null);
                _outlined.Remove(view);
            }
            foreach (var view in _selection)
                if (view != null && (_outlined.Add(view) || Outline.ColorOf(view) != color))
                    Outline.Set(view, "select", color);
        }

        public void SetSelection(IEnumerable<ObjectView> views)
        {
            ClearSelection();
            _selection.AddRange(views.Where(v => v != null));
            ShowSelection();
        }

        public void ClearSelection()
        {
            _selection.Clear();
            ShowSelection();
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
            var wasPeekingAt = _peeking ? _looking : null;
            _looking = view;
            _peeking = view != null && peek;
            // Peeking at what is face down (or under someone's hand) is announced to everyone (G15), once per peek.
            if (_peeking && view != wasPeekingAt && (!view.Model.IsFaceUp || view.HideFace) && !_table.Spectating)
                _table.Execute(new PeekCommand { ObjectId = view.Id, Actor = _table.LocalSeat });
            LookChanged?.Invoke(view, _peeking);
        }

        /// <summary>True while this player wears a blindfold (G15): the screen is dark, and everyone was told.</summary>
        public bool Blindfolded { get; private set; }

        /// <summary>Raised when the blindfold goes on or off (the HUD darkens the screen).</summary>
        public event Action<bool> BlindfoldChanged;

        /// <summary>B: puts the blindfold on or takes it off.</summary>
        public void ToggleBlindfold()
        {
            Blindfolded = !Blindfolded;
            if (!_table.Spectating)
                _table.Execute(new BlindfoldCommand { On = Blindfolded, Actor = _table.LocalSeat });
            BlindfoldChanged?.Invoke(Blindfolded);
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
            target is Bag || ((dropped is Card || dropped is Deck) && (target is Card || target is Deck)) || Stacks.SameKind(dropped, target);

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

        /// <summary>A card or deck released over at least this share of another card or deck joins it (TTS joins on any real overlap).</summary>
        public const float JoinOverlap = 0.15f;

        /// <summary>Anything released over at least this share of a bag goes into it (TTS).</summary>
        public const float BagOverlap = 0.3f;

        /// <summary>
        /// What <paramref name="view"/> joins when released over it without its centre being above it: the card or deck it
        /// covers at least <see cref="JoinOverlap"/> of (cards and decks only), or the bag it covers at least
        /// <see cref="BagOverlap"/> of. The largest overlap wins.
        /// </summary>
        private ObjectView MostOverlapped(ObjectView view)
        {
            var mine = view.Collider.bounds;
            var area = mine.size.x * mine.size.z;
            var isCard = view.Model is Card || view.Model is Deck;
            ObjectView best = null;
            var bestShare = 0f;
            foreach (var other in _table.Views.Values)
            {
                if (other == null || other == view || other.Held || other.InHand || other.Model.Locked && !(other.Model is Bag) ||
                    !CanDropInto(view.Model, other.Model) || other.transform.position.y > view.transform.position.y)
                    continue;
                var threshold = other.Model is Bag ? BagOverlap : isCard && (other.Model is Card || other.Model is Deck) ? JoinOverlap : float.MaxValue;
                if (threshold > 1f)
                    continue;
                var b = other.Collider.bounds;
                var dx = Mathf.Min(mine.max.x, b.max.x) - Mathf.Max(mine.min.x, b.min.x);
                var dz = Mathf.Min(mine.max.z, b.max.z) - Mathf.Max(mine.min.z, b.min.z);
                if (dx <= 0f || dz <= 0f)
                    continue;
                // Measured against the smaller footprint, so a big object over a small bag counts too.
                var share = dx * dz / Mathf.Min(area, b.size.x * b.size.z);
                if (share >= threshold && share > bestShare)
                {
                    bestShare = share;
                    best = other;
                }
            }
            return best;
        }

        private ObjectView ObjectBelow(ObjectView view)
        {
            var ray = new Ray(view.transform.position, Vector3.down);
            return RaycastObject(ray, view, out _);
        }

        private SnapPoint NearestSnapPoint(Vector3 position, ObjectView dropped = null)
        {
            SnapPoint best = null;
            var bestDistance = SnapRadius;
            foreach (var snap in _table.Engine.State.Zones.OfType<SnapPoint>())
            {
                // An object never snaps to a point it carries itself, nor at all when its snapping is off (G16).
                if (dropped != null && (snap.Parent == dropped.Id || !dropped.Model.Snap))
                    continue;
                // A snap point of a state not shown (or of something in a bag) is not on the table.
                if (snap.Parent != null && _table.ViewOf(snap.Parent) == null)
                    continue;
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

        /// <summary>Opens the editor of a script input, as a click on it does (also for tests and the CLI).</summary>
        public void EditInput(ScriptInputView input)
        {
            if (input != null && _camera != null)
                InputEditRequested?.Invoke(input, _camera.WorldToScreenPoint(input.transform.position));
        }

        /// <summary>
        /// A click on a script's UI drawn on an object or at a zone (M5): the nearest UI surface under the pointer, unless
        /// something solid is in front of it; true when an element there took the click.
        /// </summary>
        private bool UseObjectUi(Ray ray, string button)
        {
            var count = Physics.RaycastNonAlloc(ray, _hits, 50f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
            TableWorld.View.ScriptUi.ObjectUiSurface best = null;
            var bestDistance = float.MaxValue;
            var point = Vector3.zero;
            var solid = float.MaxValue;
            for (var i = 0; i < count; i++)
            {
                var hit = _hits[i];
                if (hit.collider.TryGetComponent(out TableWorld.View.ScriptUi.ObjectUiSurface surface))
                {
                    if (hit.distance < bestDistance)
                    {
                        best = surface;
                        bestDistance = hit.distance;
                        point = hit.point;
                    }
                }
                else if (!hit.collider.isTrigger && hit.distance < solid)
                    solid = hit.distance;
            }
            return best != null && bestDistance <= solid + 0.001f && best.TryUse(point, button);
        }

        /// <summary>Clicks a script button (M5): the host's scripts run its function with this seat's colour.</summary>
        public void ClickButton(ScriptButtonView button, bool alt)
        {
            if (button == null || button.Owner == null)
                return;
            _table.Execute(new ClickButtonCommand { ObjectId = button.Owner.Id, Index = button.Index, Alt = alt, Actor = _table.LocalSeat });
        }

        private ObjectView RaycastObject(Ray ray, ObjectView ignore, out RaycastHit hit)
        {
            var count = Physics.RaycastNonAlloc(ray, _hits, 50f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
            hit = default;
            ObjectView best = null;
            var bestDistance = float.MaxValue;
            for (var i = 0; i < count; i++)
            {
                var view = ObjectView.Of(_hits[i].collider);
                if (view == null || view == ignore || _held.Contains(view) || view.ZoneHidden || _hits[i].distance >= bestDistance)
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
                var view = ObjectView.Of(_hits[i].collider);
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
