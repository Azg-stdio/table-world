using System.Collections.Generic;
using Core.Net;
using UnityEngine;

namespace TableWorld.View.Chalkers
{
    /// <summary>
    /// One chalker (T7.3, simple shapes until an artist's model): a figure in a seat colour seated on a
    /// <see cref="ChalkerChair"/>, hands on the table, with a chalkboard head. Its face rests in the player's <see cref="Personality"/>; <see cref="Play"/> runs an emote (face
    /// and body for a couple of seconds); <see cref="Pose"/> seats it, turns its head to a gaze point, its pupils to an
    /// eye target, and opens its mouth with a voice level. Everything is driven from code: no Animator.
    /// </summary>
    public sealed class Chalker
    {
        public const float MaxHeadYaw = 75f;
        public const float MaxHeadPitch = 40f;
        public const float HeadTurnRate = 8f;
        /// <summary>
        /// The torso's height above the origin (table-top height): seated on a <see cref="ChalkerChair"/>, the shoulders
        /// clear the table so the hands rest on it (T11.10).
        /// </summary>
        public const float TorsoHeight = 0.14f;
        /// <summary>The thighs' axis below the origin, resting on the chair's cushion.</summary>
        public const float HipHeight = ChalkerChair.SeatTop + 0.05f;
        /// <summary>How far forward the knees reach, under the table's edge.</summary>
        public const float KneeForward = 0.24f;

        private readonly GameObject _root;
        private readonly Transform _torso;
        private readonly Transform _head;
        private readonly Transform _headTurn;
        private readonly Transform _mouth;
        private readonly Transform[] _shoulders = new Transform[2];
        private readonly Transform[] _elbows = new Transform[2];
        private readonly ChalkerFace _face;
        private readonly MouthFollower _mouthFollower = new MouthFollower();
        private readonly List<Material> _materials = new List<Material>();
        private readonly List<Material> _tinted = new List<Material>();
        private float _nextBlink = Random.Range(2f, 5f);
        private float _blinkUntil;
        private float _winkUntil;
        private bool _winkLeft = true;
        private float _sleep;
        private bool _placed;
        private float _emoteSeconds;

        public ChalkerPersonality Personality { get; set; }

        /// <summary>The emote playing, or <see cref="ChalkerEmote.None"/>.</summary>
        public ChalkerEmote Emote { get; private set; }

        /// <summary>Freezes the emote where it is (the lab, to look at one moment of it).</summary>
        public bool Hold { get; set; }

        /// <summary>
        /// Asleep for an eyes-closed phase (ADR 0012 §3): both eyes shut and the head lowered, eased in and out, on every
        /// screen; no blinking meanwhile.
        /// </summary>
        public bool EyesClosed { get; set; }

        /// <summary>How long a wink keeps one eye shut, in seconds.</summary>
        public const float WinkSeconds = 0.45f;

        /// <summary>The eye a wink is shutting now (true: its left), or null.</summary>
        public bool? Winking => Time.time < _winkUntil ? _winkLeft : (bool?)null;

        /// <summary>Shuts one eye for <see cref="WinkSeconds"/>: its left by default (on the right as you face it).</summary>
        public void Wink(bool left = true)
        {
            _winkLeft = left;
            _winkUntil = Time.time + WinkSeconds;
        }

        /// <summary>The eyes as last drawn (tests): each shut or not.</summary>
        public (bool Left, bool Right) EyesShut { get; private set; }

        /// <summary>The face as last drawn (tests).</summary>
        public FaceShape Face { get; private set; } = FaceShape.Neutral;

        public float MouthOpen => _mouthFollower.Openness;
        public Transform Head => _headTurn;
        public Vector3 Mouth => _mouth.position;
        public GameObject Root => _root;
        public Transform RightHand { get; }

        public Chalker(string key, Transform parent, Color color, ChalkerPersonality personality = ChalkerPersonality.Cheerful)
        {
            Personality = personality;
            _root = new GameObject($"Chalker {key}");
            _root.transform.SetParent(parent, false);
            _torso = new GameObject("Torso").transform;
            _torso.SetParent(_root.transform, false);

            var bodyMaterial = Lit(color, 0.35f, tinted: true);
            var limbMaterial = Lit(color * 0.85f, 0.3f, tinted: true);
            Part(PrimitiveType.Capsule, "Body", _torso, new Vector3(0f, -0.12f, 0f), new Vector3(0.34f, 0.3f, 0.24f), bodyMaterial);
            // Seated legs, on the root so emotes move only the upper body: thighs forward on the cushion, shins down to the floor.
            var footY = EnvironmentView.FloorY + 0.025f;
            foreach (var x in new[] { -0.085f, 0.085f })
            {
                var thigh = Part(PrimitiveType.Capsule, "Thigh", _root.transform, new Vector3(x, HipHeight, KneeForward / 2f), new Vector3(0.1f, KneeForward / 2f + 0.04f, 0.1f), limbMaterial);
                thigh.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                Part(PrimitiveType.Capsule, "Shin", _root.transform, new Vector3(x, (HipHeight + footY) / 2f, KneeForward), new Vector3(0.085f, (HipHeight - footY) / 2f, 0.085f), limbMaterial);
                Part(PrimitiveType.Cube, "Foot", _root.transform, new Vector3(x, footY, KneeForward + 0.04f), new Vector3(0.09f, 0.05f, 0.15f), bodyMaterial);
            }
            for (var side = 0; side < 2; side++)
            {
                var x = side == 0 ? -0.165f : 0.165f;
                var shoulder = new GameObject(side == 0 ? "Left shoulder" : "Right shoulder").transform;
                shoulder.SetParent(_torso, false);
                shoulder.localPosition = new Vector3(x, 0.06f, 0f);
                Part(PrimitiveType.Capsule, "Upper arm", shoulder, new Vector3(0f, -0.09f, 0f), new Vector3(0.075f, 0.09f, 0.075f), limbMaterial);
                var elbow = new GameObject("Elbow").transform;
                elbow.SetParent(shoulder, false);
                elbow.localPosition = new Vector3(0f, -0.17f, 0f);
                Part(PrimitiveType.Capsule, "Forearm", elbow, new Vector3(0f, -0.08f, 0f), new Vector3(0.065f, 0.08f, 0.065f), limbMaterial);
                var hand = Part(PrimitiveType.Sphere, "Hand", elbow, new Vector3(0f, -0.17f, 0f), Vector3.one * 0.08f, bodyMaterial);
                if (side == 1)
                    RightHand = hand.transform;
                _shoulders[side] = shoulder;
                _elbows[side] = elbow;
            }

            // The head turns to the gaze (_headTurn); emotes nod and tilt it on top (_head).
            _headTurn = new GameObject("Head").transform;
            _headTurn.SetParent(_torso, false);
            _headTurn.localPosition = new Vector3(0f, 0.22f, 0f);
            _head = new GameObject("Emote").transform;
            _head.SetParent(_headTurn, false);
            Part(PrimitiveType.Cylinder, "Neck", _head, new Vector3(0f, 0.01f, 0f), new Vector3(0.06f, 0.03f, 0.06f), limbMaterial);
            // The chalkboard: a dark slab in a wooden frame.
            Part(PrimitiveType.Cube, "Frame", _head, new Vector3(0f, 0.17f, 0f), new Vector3(0.3f, 0.26f, 0.05f), Lit(new Color(0.42f, 0.27f, 0.15f), 0.3f, tinted: false));
            _face = new ChalkerFace();
            var faceMaterial = new Material(RequiredShaders.Unlit) { name = "ChalkerFace" };
            faceMaterial.SetTexture("_BaseMap", _face.Texture);
            faceMaterial.SetColor("_BaseColor", Color.white);
            _materials.Add(faceMaterial);
            // A Unity quad faces -Z; turned around so it faces the way the head looks.
            var face = Part(PrimitiveType.Quad, "Face", _head, new Vector3(0f, 0.17f, 0.0255f), new Vector3(0.27f, 0.23f, 1f), faceMaterial);
            face.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            _mouth = new GameObject("Mouth").transform;
            _mouth.SetParent(_head, false);
            _mouth.localPosition = new Vector3(0f, 0.11f, 0.04f);
        }

        /// <summary>Repaints the body in another seat colour.</summary>
        public void SetColor(Color color)
        {
            _tinted[0].SetColor("_BaseColor", color);
            _tinted[1].SetColor("_BaseColor", color * 0.85f);
        }

        /// <summary>Starts <paramref name="emote"/> from its beginning (a new one replaces the one playing).</summary>
        public void Play(ChalkerEmote emote)
        {
            Emote = emote;
            _emoteSeconds = 0f;
        }

        private Material Lit(Color color, float smoothness, bool tinted)
        {
            var material = new Material(RequiredShaders.Lit) { name = "Chalker" };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            _materials.Add(material);
            if (tinted)
                _tinted.Add(material);
            return material;
        }

        private static GameObject Part(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            var part = GameObject.CreatePrimitive(type);
            Object.Destroy(part.GetComponent<Collider>());
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            part.GetComponent<Renderer>().sharedMaterial = material;
            return part;
        }

        /// <summary>
        /// Seats the chalker at <paramref name="position"/> facing <paramref name="facing"/> (the table's centre when null),
        /// turns its head to <paramref name="gaze"/>, its pupils to <paramref name="eyes"/>, opens its mouth with the voice
        /// <paramref name="level"/> (RMS), and plays the current emote.
        /// </summary>
        public void Pose(Vector3 position, Vector3? facing, Vector3 gaze, Vector3? eyes, float level, float dt)
        {
            var forward = facing ?? new Vector3(-position.x, 0f, -position.z);
            forward.y = 0f;
            _root.transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward.sqrMagnitude < 1e-4f ? Vector3.forward : forward, Vector3.up));

            // The emote, eased in and out over the resting face and pose.
            var resting = ChalkerEmotes.Face(Personality);
            var face = resting;
            var pose = ChalkerEmotes.Rest;
            Vector2? emoteLook = null;
            if (Emote != ChalkerEmote.None)
            {
                if (!Hold)
                    _emoteSeconds += dt;
                var seconds = _emoteSeconds;
                var duration = ChalkerEmotes.Duration(Emote);
                if (seconds >= duration)
                    Emote = ChalkerEmote.None;
                else
                {
                    var weight = ChalkerEmotes.Weight(seconds / duration);
                    var (emoteFace, emotePose, look) = ChalkerEmotes.Evaluate(Emote, seconds, resting);
                    face = FaceShape.Lerp(resting, emoteFace, weight);
                    pose = BodyPose.Lerp(ChalkerEmotes.Rest, emotePose, weight);
                    if (weight > 0.5f)
                        emoteLook = look;
                }
            }
            // Asleep: the head drops and the shoulders round, eased so it reads as falling asleep, not a jump.
            _sleep = Mathf.MoveTowards(_sleep, EyesClosed ? 1f : 0f, dt * 2.5f);
            pose.Head += new Vector3(28f * _sleep, 0f, 0f);
            pose.Lean += 6f * _sleep;
            Apply(pose);

            // The head turns toward the gaze, as far as a neck goes.
            var local = _torso.InverseTransformDirection(gaze - _headTurn.position);
            var yaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -MaxHeadYaw, MaxHeadYaw);
            var pitch = Mathf.Clamp(-Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg, -MaxHeadPitch, MaxHeadPitch);
            var target = Quaternion.Euler(pitch, yaw, 0f);
            _headTurn.localRotation = _placed ? Quaternion.Slerp(_headTurn.localRotation, target, 1f - Mathf.Exp(-HeadTurnRate * dt)) : target;
            _placed = true;

            // Pupils: what the head still has to turn to reach the eye target.
            var pupils = Vector2.zero;
            if (emoteLook is { } fixedLook)
                pupils = fixedLook;
            else if (eyes is { } e)
            {
                var inHead = _head.InverseTransformDirection(e - _head.position);
                pupils = new Vector2(Mathf.Atan2(inHead.x, inHead.z) * Mathf.Rad2Deg / 30f,
                    Mathf.Atan2(inHead.y, new Vector2(inHead.x, inHead.z).magnitude) * Mathf.Rad2Deg / 25f);
            }

            var now = Time.time;
            if (now >= _nextBlink)
            {
                _blinkUntil = now + 0.12f;
                _nextBlink = now + Random.Range(2.5f, 6f);
            }
            Face = face;
            var blink = now < _blinkUntil;
            var asleep = EyesClosed || _sleep > 0.5f;
            var wink = Winking;
            EyesShut = (asleep || blink || wink == true, asleep || blink || wink == false);
            _face.Draw(face, _mouthFollower.Update(level, dt), pupils, EyesShut.Left, EyesShut.Right);
        }

        private void Apply(BodyPose pose)
        {
            _torso.localPosition = new Vector3(0f, TorsoHeight + pose.Bob, 0f);
            _torso.localRotation = Quaternion.Euler(pose.Lean, 0f, pose.Sway);
            _head.localRotation = Quaternion.Euler(pose.Head);
            // Left is side 0 (-x): raising it out to the side turns it the other way round Z.
            _shoulders[0].localRotation = Quaternion.Euler(-pose.LeftForward, 0f, -pose.LeftRaise);
            _shoulders[1].localRotation = Quaternion.Euler(-pose.RightForward, 0f, pose.RightRaise);
            _elbows[0].localRotation = Quaternion.Euler(-pose.LeftElbow, 0f, 0f);
            _elbows[1].localRotation = Quaternion.Euler(-pose.RightElbow, 0f, 0f);
        }

        public void Destroy()
        {
            _face.Destroy();
            foreach (var material in _materials)
                Object.Destroy(material);
            Object.Destroy(_root);
        }
    }
}
