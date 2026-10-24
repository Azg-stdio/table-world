using Core.Rules;
using TableWorld.View.Meshes;
using UnityEngine;

namespace TableWorld.View
{
    /// <summary>
    /// The GameObject for one <see cref="TableObject"/> on the table. It only mirrors the core object: visuals are rebuilt
    /// by <see cref="ObjectViewFactory.Refresh"/> after each state event, and its pose is read from the state.
    /// </summary>
    public sealed class ObjectView : MonoBehaviour
    {
        public string Id { get; private set; }
        public TableObject Model { get; private set; }
        public Rigidbody Body { get; private set; }
        public MeshFilter Filter { get; private set; }
        public MeshRenderer Renderer { get; private set; }
        public Collider Collider { get; internal set; }

        /// <summary>Child holding the mesh when it is rotated relative to the object (standees).</summary>
        public Transform Visual { get; private set; }

        /// <summary>Extra renderer at the object's foot (a standee's stand), or null.</summary>
        public Renderer Base { get; internal set; }

        internal void Init(TableObject model, Transform visual, MeshFilter filter, MeshRenderer renderer, Rigidbody body)
        {
            Id = model.Id;
            Model = model;
            Visual = visual;
            Filter = filter;
            Renderer = renderer;
            Body = body;
        }

        /// <summary>True while the local player holds the object: the state then only drives rotation and visuals, not position.</summary>
        public bool Held { get; internal set; }

        /// <summary>True while an animation (a die roll) owns the transform.</summary>
        public bool Animating { get; internal set; }

        /// <summary>True while a card sits in a hand zone: it stands up facing its owner and physics leaves it alone.</summary>
        public bool InHand { get; internal set; }

        /// <summary>Set by <see cref="TableView"/> while the table's physics mode is locked (G19).</summary>
        internal bool TablePinned;

        /// <summary>
        /// Set by <see cref="TableView"/> on a thin piece resting in a pile of loose cards: held where it lies, because
        /// PhysX squeezes a pile of 0.4 mm cards into one layer. Let go when it is picked up or loses what it rests on.
        /// </summary>
        internal bool PileHeld;

        /// <summary>A tint at or below this alpha hides the shape (TTS mods draw click and drop areas that way).</summary>
        public const float InvisibleAlpha = 0.02f;

        /// <summary>The shape is not drawn: a script hid it, or its tint is fully transparent. Its UI, buttons and collider stay.</summary>
        public bool ShapeHidden => Model.MeshHidden || Model.Tint is { Length: > 3 } tint && tint[3] <= InvisibleAlpha;

        /// <summary>True when physics must leave the object where it is: it is locked, the whole table is, or it rests in a pile.</summary>
        public bool Pinned => Model.Locked || TablePinned || PileHeld;

        /// <summary>True for a card in someone else's hand: its face shows the back, and its name is not shown.</summary>
        public bool HideFace { get; internal set; }

        /// <summary>
        /// True while the object lies in a hidden zone this machine's player may not see into (G17): it is not drawn and
        /// cannot be pointed at, hovered or box selected. Physics still runs, so the table stays the same for everyone.
        /// </summary>
        public bool ZoneHidden { get; private set; }

        internal void SetZoneHidden(bool hidden)
        {
            ZoneHidden = hidden;
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
                renderer.forceRenderingOff = hidden || renderer == Renderer && ShapeHidden;
            // A notecard's printed face is a UI panel, not a renderer.
            var face = transform.Find(NotecardView.FaceName);
            if (face != null)
                face.gameObject.SetActive(!hidden);
        }

        /// <summary>Set by <see cref="TableView"/>: called when this object hits something hard enough to be heard.</summary>
        internal System.Action<ObjectView, float> ImpactHandler;
        /// <summary>Every collision start (true) and end (false), for table scripts (M5).</summary>
        internal System.Action<ObjectView, Collision, bool> CollisionHandler;

        /// <summary>Impacts slower than this are silent (resting contact, gentle sliding).</summary>
        public const float AudibleImpactSpeed = 0.12f;
        private float _lastImpact;

        private float _touchingUntil = float.NegativeInfinity;

        /// <summary>True while the body is in contact with something (the table, another object).</summary>
        public bool Touching => Time.fixedTime <= _touchingUntil;

        private void OnCollisionStay(Collision collision) => _touchingUntil = Time.fixedTime + Time.fixedDeltaTime * 2f;

        private void OnCollisionExit(Collision collision) => CollisionHandler?.Invoke(this, collision, false);

        private void OnCollisionEnter(Collision collision)
        {
            CollisionHandler?.Invoke(this, collision, true);
            _touchingUntil = Time.fixedTime + Time.fixedDeltaTime * 2f;
            var speed = collision.relativeVelocity.magnitude;
            if (speed < AudibleImpactSpeed || Time.time - _lastImpact < 0.06f || ImpactHandler == null)
                return;
            _lastImpact = Time.time;
            ImpactHandler(this, speed);
        }

        /// <summary>The seat of another player carrying this object right now (live presence), or null.</summary>
        public string RemoteHolder { get; internal set; }

        /// <summary>Where the hand presentation wants this card; <see cref="TableView"/> glides it there.</summary>
        internal Vector3 HandPosition;
        internal Quaternion HandRotation;

        /// <summary>Stands in a hand: kinematic, and its collider a trigger so it is still picked but never pushes anything.</summary>
        internal void EnterHand()
        {
            InHand = true;
            Body.isKinematic = true;
            if (Collider != null)
                Collider.isTrigger = true;
        }

        internal void LeaveHand()
        {
            InHand = false;
            if (Collider != null)
                Collider.isTrigger = false;
        }

        internal void Rebind(TableObject model) => Model = model;

        /// <summary>Moves the GameObject to the state's pose. A resting height of 0 means "on the table": it is lifted to sit on it.</summary>
        public void SnapToState()
        {
            var t = Model.Transform;
            var rotation = t.Rotation.ToRotation();
            if (Model is Die { Value: { } value } die)
            {
                // The engine owns the value; when the stored pose shows another face, the value wins (yaw is kept).
                var geometry = DiceGeometry.Get(die.Faces);
                if (geometry.ValueFor(rotation) != value)
                    rotation = geometry.RotationFor(value, t.Rotation.Y);
            }
            if (Held || Animating)
            {
                transform.rotation = rotation;
                return;
            }
            transform.SetPositionAndRotation(t.Position.ToUnity(), rotation);
            transform.localScale = t.Scale.ToUnity();
            Physics.SyncTransforms();
            if (Renderer != null)
            {
                var lowest = Base != null ? Mathf.Min(Renderer.bounds.min.y, Base.bounds.min.y) : Renderer.bounds.min.y;
                if (lowest < TableGeometry.Floor)
                    transform.position += Vector3.up * (TableGeometry.Floor - lowest);
            }
            if (Body != null && !Body.isKinematic)
            {
                Body.linearVelocity = Vector3.zero;
                Body.angularVelocity = Vector3.zero;
            }
        }
    }
}
