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
            transform.SetPositionAndRotation(t.Position.ToUnity(), rotation);
            transform.localScale = t.Scale.ToUnity();
            Physics.SyncTransforms();
            if (Renderer != null)
            {
                var lowest = Base != null ? Mathf.Min(Renderer.bounds.min.y, Base.bounds.min.y) : Renderer.bounds.min.y;
                if (lowest < 0f)
                    transform.position += Vector3.up * -lowest;
            }
            if (Body != null && !Body.isKinematic)
            {
                Body.linearVelocity = Vector3.zero;
                Body.angularVelocity = Vector3.zero;
            }
        }
    }
}
