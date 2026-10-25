using TableWorld.View.Meshes;
using UnityEngine;

namespace TableWorld.View
{
    /// <summary>
    /// A die thrown by a roll (R, the menu, a script), as in TTS: physics tosses it up with a random spin, and every new
    /// roll while it is up adds to its motion, so pressing R again and again sends it higher. The engine has already
    /// chosen the value (every machine shows the same one): on its last fall near the table the die eases into a short
    /// landing on that face, where it falls.
    /// </summary>
    [RequireComponent(typeof(ObjectView))]
    public sealed class DieToss : MonoBehaviour
    {
        /// <summary>Upward speed one roll adds (about 6 cm high from rest), and the most it can build up to.</summary>
        public const float TossSpeed = 1.1f;
        public const float MaxUpSpeed = 3.5f;
        public const float MaxSpin = 25f;
        /// <summary>Falling below this height over the table, the die starts its landing.</summary>
        public const float LandingHeight = 0.035f;
        public const float LandingSeconds = 0.16f;

        private ObjectView _view;
        private int _value;
        private bool _flying;
        private bool _landing;
        private float _landingT;
        private Vector3 _fromPosition, _toPosition;
        private Quaternion _fromRotation, _toRotation;
        private float _groundY;
        /// <summary>Its centre's height over the ground at rest (the same on every face of a regular die); measured before the first toss.</summary>
        private float _restLift = -1f;
        private System.Action<ObjectView> _landed;

        /// <summary>In the air or landing: the roll is still showing.</summary>
        public bool Busy => _flying || _landing;

        /// <summary>Tosses <paramref name="view"/> to land on <paramref name="value"/>; <paramref name="landed"/> hears when it is down.</summary>
        public static void Toss(ObjectView view, int value, System.Action<ObjectView> landed)
        {
            var toss = view.GetComponent<DieToss>() ?? view.gameObject.AddComponent<DieToss>();
            toss._landed = landed;
            toss.Throw(value);
        }

        private void Awake() => _view = GetComponent<ObjectView>();

        private void Throw(int value)
        {
            _value = value;
            _landing = false;
            _view.Animating = false;
            var body = _view.Body;
            if (!Busy && body.linearVelocity.sqrMagnitude < 1e-4f)
                _restLift = transform.position.y - GroundBelow();
            body.isKinematic = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            // Added to what it already does: a second R while it is still going up sends it higher.
            var v = body.linearVelocity;
            v.y = Mathf.Min(Mathf.Max(v.y, 0f) + TossSpeed, MaxUpSpeed);
            v += new Vector3(Random.Range(-0.12f, 0.12f), 0f, Random.Range(-0.12f, 0.12f));
            body.linearVelocity = v;
            body.angularVelocity = Vector3.ClampMagnitude(body.angularVelocity + Random.onUnitSphere * Random.Range(12f, 20f), MaxSpin);
            body.WakeUp();
            _groundY = GroundBelow();
            _flying = true;
        }

        /// <summary>The top of what is under the die (table or a board), ignoring the die itself.</summary>
        private float GroundBelow()
        {
            var bottom = _view.Renderer.bounds.min.y;
            var origin = new Vector3(transform.position.x, bottom + 0.001f, transform.position.z);
            var ground = float.NegativeInfinity;
            foreach (var hit in Physics.RaycastAll(origin, Vector3.down, 2f, ~0, QueryTriggerInteraction.Ignore))
                if (hit.rigidbody != _view.Body && hit.point.y > ground)
                    ground = hit.point.y;
            return float.IsNegativeInfinity(ground) ? 0f : ground;
        }

        private void FixedUpdate()
        {
            if (!_flying || _view == null)
                return;
            var body = _view.Body;
            var height = _view.Renderer.bounds.min.y - _groundY;
            if (body.linearVelocity.y >= 0f || height > LandingHeight)
                return;
            // The last fall: ease onto the chosen face where it is coming down.
            _flying = false;
            _landing = true;
            _landingT = 0f;
            _view.Animating = true;
            body.isKinematic = true;
            _fromPosition = transform.position;
            _fromRotation = transform.rotation;
            var faces = ((Core.Rules.Die)_view.Model).Faces;
            _toRotation = DiceGeometry.Get(faces).RotationFor(_value, transform.eulerAngles.y);
            var lift = _restLift > 0f ? _restLift
                : _view.Filter.sharedMesh != null ? _view.Filter.sharedMesh.bounds.extents.y * transform.lossyScale.y : 0.008f;
            _toPosition = new Vector3(transform.position.x, _groundY + lift, transform.position.z);
        }

        private void Update()
        {
            if (!_landing || _view == null)
                return;
            _landingT += Time.deltaTime / LandingSeconds;
            var k = Mathf.Clamp01(_landingT);
            var eased = 1f - (1f - k) * (1f - k);
            transform.SetPositionAndRotation(Vector3.Lerp(_fromPosition, _toPosition, eased), Quaternion.Slerp(_fromRotation, _toRotation, eased));
            if (k < 1f)
                return;
            _landing = false;
            _view.Animating = false;
            _view.Body.isKinematic = _view.Pinned;
            _view.Body.linearVelocity = Vector3.zero;
            _view.Body.angularVelocity = Vector3.zero;
            _landed?.Invoke(_view);
        }
    }
}
