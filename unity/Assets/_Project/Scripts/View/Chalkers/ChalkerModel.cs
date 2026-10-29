using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TableWorld.View.Chalkers
{
    /// <summary>
    /// A rigged model worn by a <see cref="Chalker"/> instead of its shapes (docs/dev/chalker-model.md). Every frame the
    /// skeleton starts from its bind pose and follows the chalker's points: the hips sit where the chalker sits, the spine,
    /// neck and head turn as the torso, neck and head points do, and the arms and legs reach the chalker's hands and feet
    /// with two-bone IK, so a model whose limbs are longer or shorter than the shapes still puts its hands on the table.
    /// The face is blend shapes and jaw, lid and brow bones, set from the chalker's <see cref="FaceShape"/>, the voice's
    /// mouth opening, the pupils and the blinks. Bones are found by name (<see cref="Rig"/>), never by their axes.
    /// </summary>
    public sealed class ChalkerModel
    {
        /// <summary>The model the chalkers wear while it exists (a test placeholder, see its NOTICE.txt).</summary>
        public const string ResourcePath = "TomLizard/TomLizard";

        /// <summary>The bone names of a model, for the points the chalker drives.</summary>
        public sealed class Rig
        {
            public string Pelvis, SpineLow, SpineHigh, Neck, Head, Jaw;
            public string LeftUpperArm, LeftForearm, LeftHand, RightUpperArm, RightForearm, RightHand;
            public string LeftThigh, LeftCalf, LeftFoot, RightThigh, RightCalf, RightFoot;
            public string LeftEye, RightEye;
            public string[] InnerBrows, OuterBrows, Tail;
            /// <summary>Materials (by a part of their name) whose skin takes the seat's colour.</summary>
            public string[] Skin;
            /// <summary>The skin's own hue (0–1), the one repainted, and its typical saturation and value.</summary>
            public float SkinHue, SkinSaturation, SkinValue;
            /// <summary>The hands' size against the file's (a cartoon's fists dwarf real-size cards).</summary>
            public float HandScale = 1f;
            /// <summary>The whole body's size against the one that puts its shoulders where the shapes' are.</summary>
            public float BodyScale = 1f;

            /// <summary>Unreal-style names (pelvis, spine_01, upperarm_l…), as on the placeholder lizard.</summary>
            public static readonly Rig Unreal = new Rig
            {
                Pelvis = "pelvis", SpineLow = "spine_01", SpineHigh = "spine_03", Neck = "neck_01", Head = "head", Jaw = "C_jaw",
                LeftUpperArm = "upperarm_l", LeftForearm = "lowerarm_l", LeftHand = "hand_l",
                RightUpperArm = "upperarm_r", RightForearm = "lowerarm_r", RightHand = "hand_r",
                LeftThigh = "thigh_l", LeftCalf = "calf_l", LeftFoot = "foot_l",
                RightThigh = "thigh_r", RightCalf = "calf_r", RightFoot = "foot_r",
                LeftEye = "L_eye", RightEye = "R_eye",
                InnerBrows = new[] { "C_brow_mid", "L_brow_mid", "R_brow_mid" },
                OuterBrows = new[] { "L_brow_outer", "R_brow_outer" },
                Tail = new[] { "tail1", "tail2", "tail3", "tail4" },
                Skin = new[] { "Body", "Head" },
                SkinHue = 0.3f, SkinSaturation = 0.45f, SkinValue = 0.7f,
                HandScale = 0.75f, BodyScale = 0.9f,
            };
        }

        /// <summary>A seated chalker's hand lower than this above its root (the table top) is resting on the table, in metres.</summary>
        public const float RestHeight = 0.07f;

        /// <summary>How far the jaw drops with the mouth fully open, in degrees.</summary>
        public const float JawDegrees = 36f;

        private readonly Transform _container;
        private readonly Transform _model;
        private readonly Transform[] _bones;
        private readonly Vector3[] _restPositions;
        private readonly Quaternion[] _restRotations;
        private readonly Dictionary<string, Transform> _byName = new Dictionary<string, Transform>();
        private readonly List<(SkinnedMeshRenderer Renderer, Dictionary<string, int> Shapes)> _faces = new List<(SkinnedMeshRenderer, Dictionary<string, int>)>();
        private readonly List<Material> _materials = new List<Material>();
        /// <summary>The skin materials, their painted textures, and the texture each is painted from.</summary>
        private readonly List<(Material Material, Texture Source, RenderTexture Painted)> _skins = new List<(Material, Texture, RenderTexture)>();
        private readonly Rig _rig;
        private readonly Transform _pelvis;
        private readonly float _legLength;
        private readonly float _ankleHeight;
        private readonly float _footSpread;
        private float _tailPhase;
        private readonly float[] _handLength = new float[2];
        private readonly Vector3[] _fingers = { Vector3.up, Vector3.up };
        /// <summary>Per hand, its local axis out of the back of the hand (up, in the bind pose's flat-palmed arms).</summary>
        private readonly Vector3[] _backOfHand = { Vector3.forward, Vector3.forward };

        /// <summary>The model's scale against its file, chosen so its shoulders are as high above its hips as the shapes'.</summary>
        public float Scale { get; }

        /// <summary>Loads the placeholder model, or null when it is not in the project.</summary>
        public static ChalkerModel Load(Transform parent, Color color)
        {
            var prefab = Resources.Load<GameObject>(ResourcePath);
            return prefab == null ? null : new ChalkerModel(prefab, parent, color, Rig.Unreal);
        }

        public ChalkerModel(GameObject prefab, Transform parent, Color color, Rig rig)
        {
            _rig = rig;
            _container = new GameObject("Model").transform;
            _container.SetParent(parent, false);
            _model = Object.Instantiate(prefab, _container, false).transform;
            _model.name = prefab.name;
            foreach (var animator in _model.GetComponentsInChildren<Animator>())
                Object.Destroy(animator);
            foreach (var t in _model.GetComponentsInChildren<Transform>(true))
                _byName.TryAdd(t.name, t);

            // The file opens on the first frame of its animation: put the skeleton back in its bind pose (the skinned
            // meshes' bind matrices), parents first, and keep that as the rest every frame starts from.
            var renderers = _model.GetComponentsInChildren<SkinnedMeshRenderer>();
            var bind = new Dictionary<Transform, Matrix4x4>();
            foreach (var renderer in renderers)
            {
                var poses = renderer.sharedMesh.bindposes;
                var bones = renderer.bones;
                for (var i = 0; i < bones.Length && i < poses.Length; i++)
                    if (bones[i] != null)
                        bind[bones[i]] = renderer.transform.localToWorldMatrix * poses[i].inverse;
            }
            // Everything under the model's own root, which turns to face the table and is never reset.
            _bones = System.Array.FindAll(_model.GetComponentsInChildren<Transform>(true), t => t != _model);
            foreach (var bone in _bones)
                if (bind.TryGetValue(bone, out var m))
                    bone.SetPositionAndRotation(m.GetColumn(3), m.rotation);
            _restPositions = new Vector3[_bones.Length];
            _restRotations = new Quaternion[_bones.Length];
            for (var i = 0; i < _bones.Length; i++)
            {
                _restPositions[i] = _bones[i].localPosition;
                _restRotations[i] = _bones[i].localRotation;
            }

            // Facing: the model's forward is from its head toward its eyes, its right from the left arm to the right.
            // Measured in the container's space (unscaled until the end), so the parent's own turn does not matter.
            _pelvis = Bone(rig.Pelvis);
            Vector3 At(string bone) => _container.InverseTransformPoint(Bone(bone).position);
            var head = At(rig.Head);
            var eyes = (At(rig.LeftEye) + At(rig.RightEye)) / 2f;
            var right = At(rig.RightUpperArm) - At(rig.LeftUpperArm);
            var up = Vector3.ProjectOnPlane(head - At(rig.Pelvis), right).normalized;
            var forward = Vector3.Cross(right.normalized, up);
            if (Vector3.Dot(forward, eyes - head) < 0f)
                forward = -forward;
            _model.localRotation = Quaternion.Inverse(Quaternion.LookRotation(forward, up)) * _model.localRotation;

            var pelvis = At(rig.Pelvis);
            var shoulders = (At(rig.LeftUpperArm).y + At(rig.RightUpperArm).y) / 2f - pelvis.y;
            Scale = (Chalker.TorsoHeight + 0.06f - Chalker.HipHeight) / Mathf.Max(shoulders, 0.01f) * rig.BodyScale;
            var thigh = At(rig.LeftThigh);
            var calf = At(rig.LeftCalf);
            var foot = At(rig.LeftFoot);
            _legLength = (Vector3.Distance(thigh, calf) + Vector3.Distance(calf, foot)) * Scale;
            _ankleHeight = Mathf.Max(0.02f, (foot.y - MinY(renderers, _container)) * Scale);
            _footSpread = Mathf.Abs(thigh.x - pelvis.x) * Scale;
            _container.localScale = Vector3.one * Scale;
            // Each hand: its size, and the way and distance from the wrist to the farthest fingertip, in the hand's own
            // axes (holding cards, the fingers lie up the cards' backs).
            for (var side = 0; side < 2; side++)
            {
                var hand = Bone(side == 0 ? rig.LeftHand : rig.RightHand);
                if (hand == null)
                    continue;
                hand.localScale = Vector3.one * rig.HandScale;
                var tip = hand;
                foreach (var t in hand.GetComponentsInChildren<Transform>())
                    if (Vector3.Distance(t.position, hand.position) > Vector3.Distance(tip.position, hand.position))
                        tip = t;
                _handLength[side] = Vector3.Distance(tip.position, hand.position);
                _fingers[side] = tip == hand ? Vector3.up : hand.InverseTransformDirection(tip.position - hand.position).normalized;
                _backOfHand[side] = Vector3.ProjectOnPlane(hand.InverseTransformDirection(_container.up), _fingers[side]).normalized;
            }
            BuildLids(renderers);

            // The eyes are mapped onto the head's texture (their pupils are painted there).
            var headTexture = Resources.Load<Texture2D>(ResourcePath + "_Head");
            var textures = new Dictionary<string, Texture2D>
            {
                ["Body"] = Resources.Load<Texture2D>(ResourcePath + "_Body"),
                ["Head"] = headTexture,
                ["Eyes"] = headTexture,
            };
            foreach (var renderer in renderers)
            {
                var shared = renderer.sharedMaterials;
                var own = new Material[shared.Length];
                for (var i = 0; i < shared.Length; i++)
                {
                    var name = shared[i] != null ? shared[i].name : "";
                    var material = new Material(RequiredShaders.Lit) { name = "ChalkerModel " + name };
                    material.SetFloat("_Smoothness", 0.25f);
                    foreach (var pair in textures)
                        if (pair.Value != null && name.Contains(pair.Key))
                            material.SetTexture("_BaseMap", pair.Value);
                    var skin = material.GetTexture("_BaseMap");
                    if (skin != null && System.Array.Exists(rig.Skin ?? new string[0], part => name.Contains(part)))
                        _skins.Add((material, skin, null));
                    _materials.Add(material);
                    own[i] = material;
                }
                renderer.sharedMaterials = own;
                renderer.updateWhenOffscreen = true;
                var mesh = renderer.sharedMesh;
                var shapes = new Dictionary<string, int>();
                for (var i = 0; i < mesh.blendShapeCount; i++)
                    shapes[mesh.GetBlendShapeName(i)] = i;
                if (shapes.Count > 0)
                    _faces.Add((renderer, shapes));
            }
            _renderers = renderers;
            SetColor(color);
        }

        public GameObject Root => _container.gameObject;

        /// <summary>
        /// Paints the skin in the seat's colour, so players tell each other apart: its hue becomes the colour's, its shading
        /// and painted detail stay, and the eyes and mouth keep theirs. Done on the GPU once per colour change.
        /// </summary>
        public void SetColor(Color color)
        {
            var shader = Shader.Find(RequiredShaders.RecolorName);
            if (shader == null)
                return;
            Color.RGBToHSV(color, out var hue, out var saturation, out var value);
            // The lids in the painted skin's colour: its hue, and its saturation and value as the paint scales them.
            if (_lidMaterial != null)
                _lidMaterial.SetColor("_BaseColor", Color.HSVToRGB(hue, Mathf.Clamp01(saturation),
                    // a shade darker: the painted skin has darker speckles the plain lid lacks.
                    0.82f * _rig.SkinValue * Mathf.Clamp(value / _rig.SkinValue, 0.45f, 1.4f)));
            var paint = new Material(shader);
            paint.SetFloat("_FromHue", _rig.SkinHue);
            paint.SetFloat("_Band", 0.15f);
            paint.SetFloat("_Hue", hue);
            paint.SetFloat("_SaturationScale", saturation / _rig.SkinSaturation);
            paint.SetFloat("_ValueScale", Mathf.Clamp(value / _rig.SkinValue, 0.45f, 1.4f));
            for (var i = 0; i < _skins.Count; i++)
            {
                var (material, source, painted) = _skins[i];
                if (painted == null)
                {
                    painted = new RenderTexture(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                    {
                        name = "ChalkerModel skin", useMipMap = true, autoGenerateMips = true, anisoLevel = 4,
                    };
                    _skins[i] = (material, source, painted);
                    material.SetTexture("_BaseMap", painted);
                }
                Graphics.Blit(source, painted, paint);
            }
            Object.Destroy(paint);
        }

        /// <summary>The lowest point of the skinned meshes as posed, in <paramref name="space"/>.</summary>
        private static float MinY(SkinnedMeshRenderer[] renderers, Transform space)
        {
            var min = float.MaxValue;
            foreach (var renderer in renderers)
            {
                var mesh = new Mesh();
                renderer.BakeMesh(mesh, true);
                foreach (var v in mesh.vertices)
                    min = Mathf.Min(min, space.InverseTransformPoint(renderer.transform.TransformPoint(v)).y);
                Object.Destroy(mesh);
            }
            return min;
        }

        /// <summary>The model's bone or part named <paramref name="name"/>, or null.</summary>
        public Transform Find(string name) => name != null && _byName.TryGetValue(name, out var t) ? t : null;

        private Transform Bone(string name) => Find(name);

        private void Reset()
        {
            for (var i = 0; i < _bones.Length; i++)
                _bones[i].SetLocalPositionAndRotation(_restPositions[i], _restRotations[i]);
        }

        /// <summary>The skeleton in the chalker's pose this frame (called at the end of <see cref="Chalker.Pose"/>).</summary>
        public void Follow(Chalker chalker, FaceShape face, float mouth, Vector2 pupils, bool leftShut, bool rightShut, float dt)
        {
            var points = chalker.Points;
            var root = points["root"];
            var torso = points["torso"];
            var neck = points["neck"];
            var head = points["head"];
            Reset();
            _container.SetPositionAndRotation(root.position, root.rotation);

            // The hips: on the cushion seated; standing, as high as the legs reach with the knees a little soft.
            var hips = root.TransformPoint(new Vector3(0f, Chalker.HipHeight, 0f));
            if (chalker.Standing)
            {
                var floor = root.position.y - Chalker.StandingLift;
                hips.y = floor + _ankleHeight + _legLength * 0.97f + (torso.localPosition.y - Chalker.TorsoHeight);
            }
            _container.position += hips - _pelvis.position;

            // Spine, neck and head turn as the chalker's points turn (each point's own turn, in world space).
            var lean = torso.rotation * Quaternion.Inverse(root.rotation);
            var half = Quaternion.Slerp(Quaternion.identity, lean, 0.5f);
            Turn(_rig.SpineLow, half);
            Turn(_rig.SpineHigh, half);
            Turn(_rig.Neck, neck.rotation * Quaternion.Inverse(torso.rotation));
            Turn(_rig.Head, head.rotation * Quaternion.Inverse(neck.rotation));

            // Arms reach the chalker's hands, bending toward its elbows. Seated the hands land where the chalker's do (on the
            // table); standing, the reach stretches to this model's arm length so hanging arms hang straight.
            var lift = root.up * 0.025f;
            for (var side = 0; side < 2; side++)
            {
                var upper = side == 0 ? _rig.LeftUpperArm : _rig.RightUpperArm;
                var lower = side == 0 ? _rig.LeftForearm : _rig.RightForearm;
                var end = side == 0 ? _rig.LeftHand : _rig.RightHand;
                var hand = points[side == 0 ? "leftHand" : "rightHand"].position;
                var elbow = points[side == 0 ? "leftElbow" : "rightElbow"].position;
                var target = hand + lift;
                if (chalker.Standing && Bone(upper) is { } shoulderBone)
                {
                    var shoulder = points[side == 0 ? "leftShoulder" : "rightShoulder"].position;
                    var reach = Vector3.Distance(shoulderBone.position, Bone(lower).position) + Vector3.Distance(Bone(lower).position, Bone(end).position);
                    var span = Vector3.Distance(shoulder, elbow) + Vector3.Distance(elbow, hand);
                    target = shoulderBone.position + (hand - shoulder) * (reach / Mathf.Max(span, 0.01f));
                }
                // Holding cards: the fingers at the fan's foot (the wrist below it), lying up the cards' backs. The hold's own
                // places, not the shapes' hands: this model's arms may reach where the shapes' stop short.
                // Holding cards: in the left hand only; the right hand
                // is free (it rests, reaches, holds a microphone or raises a thumb).
                var holds = chalker.CardHold is { } hold && side == 0;
                if (holds)
                {
                    var grip = chalker.CardHold.Value;
                    // The wrist up at chest height on the hand's own side, the elbow below: the forearm rises, so the hand,
                    // left as the model was made, points its fingers up. The cards are then put in those fingers
                    // (CardsInHand), wherever the hand is.
                    target = grip.Grip + root.up * 0.08f - root.forward * 0.05f;
                }
                // Resting on the table (seated, the hand down at the table's height, doing nothing else): the hand lies flat,
                // palm down and fingers forward, lifted by its own thickness so it does not sink into the table.
                var rests = !holds && !chalker.Standing && !(side == 1 && (chalker.ReachFor != null || chalker.Microphone || chalker.ThumbUp)) &&
                            !TableGeometry.Hidden && root.InverseTransformPoint(hand).y < RestHeight;
                if (rests)
                    target = hand + root.up * (_handLength[side] * 0.3f) + root.forward * 0.015f;
                Reach(upper, lower, end, target, elbow - root.forward * 0.1f);
                // A holding hand keeps its wrist in line with the forearm, as the model was made: turning it to fit the fan
                // twisted it.
                if (rests && Bone(end) is { } flat)
                {
                    var outward = side == 0 ? -root.right : root.right;
                    var fingers = (root.forward + outward * 0.2f - root.up * 0.12f).normalized;
                    flat.rotation = Quaternion.LookRotation(fingers, root.up) * Quaternion.Inverse(Quaternion.LookRotation(_fingers[side], _backOfHand[side]));
                }
            }

            // Legs: seated, the feet on the floor under the knees; standing, swung from the chalker's hips.
            for (var side = 0; side < 2; side++)
            {
                var thigh = side == 0 ? _rig.LeftThigh : _rig.RightThigh;
                var calf = side == 0 ? _rig.LeftCalf : _rig.RightCalf;
                var foot = side == 0 ? _rig.LeftFoot : _rig.RightFoot;
                var x = side == 0 ? -_footSpread : _footSpread;
                Vector3 target, knee;
                if (chalker.Standing)
                {
                    var hip = points[side == 0 ? "leftHip" : "rightHip"];
                    var start = Bone(thigh).position;
                    target = start + hip.rotation * Vector3.down * (_legLength * 0.97f);
                    knee = start + hip.rotation * (Vector3.down * _legLength * 0.5f + Vector3.forward * 0.3f);
                }
                else
                {
                    var floor = root.position.y + EnvironmentView.FloorY;
                    target = root.TransformPoint(new Vector3(x, 0f, Chalker.KneeForward + 0.02f));
                    target.y = floor + _ankleHeight;
                    knee = root.TransformPoint(new Vector3(x, Chalker.HipHeight + 0.3f, Chalker.KneeForward + 0.4f));
                }
                Reach(thigh, calf, foot, target, knee);
            }

            // The tail sways, more while walking.
            _tailPhase += dt * (chalker.Walking ? Chalker.StepsPerSecond * Mathf.PI : 1.3f);
            if (_rig.Tail != null)
                for (var i = 0; i < _rig.Tail.Length; i++)
                {
                    var bone = Bone(_rig.Tail[i]);
                    if (bone != null)
                        bone.rotation = Quaternion.AngleAxis(Mathf.Sin(_tailPhase - i * 0.6f) * (chalker.Walking ? 9f : 5f), root.up) * bone.rotation;
                }

            Face(face, mouth, pupils, leftShut, rightShut, head);
            FaceDepth = root.InverseTransformPoint(Eyes).z;
        }

        /// <summary>How far ahead of the chalker's root the eyes are (as last posed): a raised hand of cards goes in front.</summary>
        public float FaceDepth { get; private set; }

        private void Turn(string name, Quaternion delta)
        {
            var bone = Bone(name);
            if (bone != null)
                bone.rotation = delta * bone.rotation;
        }

        /// <summary>Two-bone IK: the end of the chain to <paramref name="target"/>, the middle joint toward <paramref name="pole"/>.</summary>
        private void Reach(string upperName, string lowerName, string endName, Vector3 target, Vector3 pole)
        {
            var upper = Bone(upperName);
            var lower = Bone(lowerName);
            var end = Bone(endName);
            if (upper != null && lower != null && end != null)
                TwoBoneIk.Solve(upper, lower, end, target, pole, -_container.up, _container.forward);
        }

        /// <summary>
        /// The hold moved into the left hand as posed (after <see cref="Follow"/>): the cards' foot between its fingers,
        /// standing up along them, their faces still toward the holder's eyes. Null when the rig has no left hand.
        /// </summary>
        public HandHold? CardsInHand(HandHold hold)
        {
            var palm = Bone(_rig.LeftHand);
            if (palm == null)
                return null;
            var fingers = palm.TransformDirection(_fingers[0]).normalized;
            // A card stands more upright than the fingers point: half of up mixed in.
            var up = (fingers + _container.up * 0.8f).normalized;
            // On the side of the fingers that faces the holder: the fingers behind the cards, not through them.
            var toward = Vector3.ProjectOnPlane(hold.Face, fingers).normalized;
            var grip = palm.position + fingers * (_handLength[0] * 0.55f) + toward * (_handLength[0] * 0.22f);
            return new HandHold(grip, up, hold.Face);
        }

        /// <summary>Midway between the eyes as posed (the first-person camera sits there).</summary>
        public Vector3 Eyes
        {
            get
            {
                var left = Bone(_rig.LeftEye);
                var right = Bone(_rig.RightEye);
                return left != null && right != null ? (left.position + right.position) / 2f : Bone(_rig.Head).position;
            }
        }

        /// <summary>
        /// First person: the model draws only its shadow (you look out from inside it); nothing of it stands between your
        /// eyes and the table.
        /// </summary>
        public void HideBody(bool hide)
        {
            var mode = hide ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly : UnityEngine.Rendering.ShadowCastingMode.On;
            foreach (var renderer in _renderers)
                renderer.shadowCastingMode = mode;
            foreach (var lid in _lids.Concat(_lowerLids))
                if (lid != null)
                    lid.shadowCastingMode = mode;
        }

        private SkinnedMeshRenderer[] _renderers = new SkinnedMeshRenderer[0];

        private void Face(FaceShape face, float mouth, Vector2 pupils, bool leftShut, bool rightShut, Transform head)
        {
            var open = Mathf.Clamp01(Mathf.Max(mouth, face.MouthOpen));
            var across = head.right;
            var jaw = Bone(_rig.Jaw);
            if (jaw != null)
                jaw.rotation = Quaternion.AngleAxis(open * JawDegrees, across) * jaw.rotation;

            // Eyes: the lids come down as far as the face shuts them (half shut, asleep); a blink, a wink or sleep closes them.
            var shut = 1f - Mathf.Clamp01(face.EyeOpen);
            Lid(0, leftShut ? 1f : shut);
            Lid(1, rightShut ? 1f : shut);

            // Brows: up and down, the inner ends lower when angry and higher when worried.
            var up = head.up * Scale;
            var raise = face.BrowRaise * 0.012f;
            foreach (var name in _rig.InnerBrows ?? new string[0])
            {
                var bone = Bone(name);
                if (bone != null)
                    bone.position += up * (raise - face.BrowTilt * 0.01f);
            }
            foreach (var name in _rig.OuterBrows ?? new string[0])
            {
                var bone = Bone(name);
                if (bone != null)
                    bone.position += up * (raise + (name.StartsWith("L") ? face.BrowAsym * 0.01f : 0f));
            }

            var smile = Mathf.Clamp01(face.Smile);
            var frown = Mathf.Clamp01(-face.Smile);
            var smirk = face.Smirk;
            Shape("jaw_open_pose", open);
            Shape("L_smile_pose", smile + Mathf.Clamp01(smirk));
            Shape("R_smile_pose", smile + Mathf.Clamp01(-smirk));
            Shape("L_frown_pose", frown);
            Shape("R_frown_pose", frown);
            Shape("L_squeeze_pose", face.EyeSmile);
            Shape("R_squeeze_pose", face.EyeSmile);
            Shape("L_cheek_up_pose", Mathf.Max(face.EyeSmile, smile * 0.5f));
            Shape("R_cheek_up_pose", Mathf.Max(face.EyeSmile, smile * 0.5f));
            Shape("L_lip_corner_narrow_pose", Mathf.Clamp01(1f - face.MouthWidth) * 1.5f);
            Shape("R_lip_corner_narrow_pose", Mathf.Clamp01(1f - face.MouthWidth) * 1.5f);
            Shape("L_lip_corner_wide_pose", Mathf.Clamp01(face.MouthWidth - 1f) * 2f);
            Shape("R_lip_corner_wide_pose", Mathf.Clamp01(face.MouthWidth - 1f) * 2f);
            Shape("look_left_pose", Mathf.Clamp01(-pupils.x));
            Shape("look_right_pose", Mathf.Clamp01(pupils.x));
            Shape("look_up_pose", Mathf.Clamp01(pupils.y));
            Shape("look_down_pose", Mathf.Clamp01(-pupils.y));
        }

        /// <summary>
        /// Lids for a model that has none (the placeholder lizard's lid bones move no skin): over each eyeball a dome in
        /// the skin's colour, a little bigger than the eye, which rolls from behind the eye over its top to cover its front.
        /// Its size is measured from the eye's own vertices.
        /// </summary>
        private void BuildLids(SkinnedMeshRenderer[] renderers)
        {
            var mesh = Dome();
            _meshes.Add(mesh);
            _lidMaterial = new Material(RequiredShaders.Lit) { name = "ChalkerModel lid" };
            _lidMaterial.SetFloat("_Smoothness", 0.25f);
            _materials.Add(_lidMaterial);
            for (var side = 0; side < 2; side++)
            {
                var eye = Bone(side == 0 ? _rig.LeftEye : _rig.RightEye);
                if (eye == null || eye.parent == null)
                    continue;
                // The eyeball's own vertices (those its bone carries): where they are as posed places the lid.
                foreach (var renderer in renderers)
                {
                    var source = renderer.sharedMesh;
                    var index = source != null && source.isReadable ? System.Array.IndexOf(renderer.bones, eye) : -1;
                    if (index < 0)
                        continue;
                    var weights = source.boneWeights;
                    var mine = new List<int>();
                    for (var i = 0; i < weights.Length; i++)
                        if (weights[i].boneIndex0 == index && weights[i].weight0 > 0.5f)
                            mine.Add(i);
                    if (mine.Count == 0)
                        continue;
                    _eyeRenderer = renderer;
                    _eyeVertices[side] = mine.ToArray();
                    break;
                }
                if (_eyeVertices[side] == null)
                    continue;
                MeshRenderer Half(string name)
                {
                    var half = new GameObject(name);
                    half.transform.SetParent(eye.parent, false);
                    half.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var drawn = half.AddComponent<MeshRenderer>();
                    drawn.sharedMaterial = _lidMaterial;
                    drawn.enabled = false;
                    return drawn;
                }
                _lids[side] = Half(side == 0 ? "Left lid" : "Right lid");
                _lowerLids[side] = Half(side == 0 ? "Left lower lid" : "Right lower lid");
                // The face's forward, up and right in the space the eyes hang from: the lid turns with the face.
                _lidAxes[side] = (eye.parent.InverseTransformDirection(_container.forward), eye.parent.InverseTransformDirection(_container.up),
                    eye.parent.InverseTransformDirection(_container.right));
            }
        }

        /// <summary>A half sphere of radius 1, bulging along +Z, its rim in the XY plane.</summary>
        private static Mesh Dome()
        {
            const int around = 24, down = 8;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (var r = 0; r <= down; r++)
            {
                var polar = Mathf.PI / 2f * r / down;
                for (var a = 0; a <= around; a++)
                {
                    var angle = 2f * Mathf.PI * a / around;
                    vertices.Add(new Vector3(Mathf.Sin(polar) * Mathf.Cos(angle), Mathf.Sin(polar) * Mathf.Sin(angle), Mathf.Cos(polar)));
                }
            }
            for (var r = 0; r < down; r++)
                for (var a = 0; a < around; a++)
                {
                    var i = r * (around + 1) + a;
                    var j = i + around + 1;
                    triangles.AddRange(new[] { i, j, i + 1, i + 1, j, j + 1 });
                }
            var mesh = new Mesh { name = "Lid" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Shuts a lid to <paramref name="shut"/> (0 open, 1 closed). The upper half comes down over the eyeball from the
        /// back of its top (sleepy, half shut) to cover its whole top; shut, the lower half closes it from below and the
        /// eye is a smooth ball of skin. Open, neither is drawn. (The placeholder's eyes bulge out of the head, so a single
        /// dome could not cover them.)
        /// </summary>
        private void Lid(int side, float shut)
        {
            var upper = _lids[side];
            var lower = _lowerLids[side];
            if (upper == null || lower == null)
                return;
            var (forward, up, right) = _lidAxes[side];
            upper.enabled = shut > 0.05f;
            lower.enabled = shut > ClosedFrom;
            if (!upper.enabled)
                return;
            // Over the eyeball as it is now (the face's shapes move it: looking around, squinting), a little bigger.
            if (_bakedFrame != Time.frameCount)
            {
                _bakedFrame = Time.frameCount;
                _bakedEyes ??= new Mesh { name = "Eyes as posed" };
                _eyeRenderer.BakeMesh(_bakedEyes);
                _bakedEyes.GetVertices(_bakedVertices);
            }
            var t = _eyeRenderer.transform;
            var min = Vector3.positiveInfinity;
            var max = Vector3.negativeInfinity;
            foreach (var i in _eyeVertices[side])
            {
                var point = _bakedVertices[i];
                min = Vector3.Min(min, point);
                max = Vector3.Max(max, point);
            }
            var middle = (min + max) / 2f;
            var radius = 0f;
            foreach (var i in _eyeVertices[side])
                radius = Mathf.Max(radius, (_bakedVertices[i] - middle).magnitude);
            var centre = t.position + t.rotation * middle;
            var parentScale = Mathf.Max(1e-6f, upper.transform.parent.lossyScale.x);
            foreach (var half in new[] { upper, lower })
            {
                half.transform.position = centre;
                half.transform.localScale = Vector3.one * (radius * 1.06f / parentScale);
            }
            if (upper.enabled)
            {
                var dome = Vector3.Slerp((up - forward).normalized, up, Mathf.Clamp01(shut / ClosedFrom));
                upper.transform.localRotation = Quaternion.LookRotation(dome, Vector3.Cross(dome, right));
            }
            if (lower.enabled)
                lower.transform.localRotation = Quaternion.LookRotation(-up, Vector3.Cross(-up, right));
        }

        /// <summary>From this much shut, an eye is closed: the upper lid is all the way down and the lower one meets it.</summary>
        public const float ClosedFrom = 0.75f;

        private readonly MeshRenderer[] _lids = new MeshRenderer[2];
        private readonly MeshRenderer[] _lowerLids = new MeshRenderer[2];
        private SkinnedMeshRenderer _eyeRenderer;
        private readonly int[][] _eyeVertices = new int[2][];
        private Mesh _bakedEyes;
        private readonly List<Vector3> _bakedVertices = new List<Vector3>();
        private int _bakedFrame = -1;
        private Material _lidMaterial;
        private readonly List<Mesh> _meshes = new List<Mesh>();
        private readonly (Vector3 Forward, Vector3 Up, Vector3 Right)[] _lidAxes = new (Vector3, Vector3, Vector3)[2];

        private void Shape(string name, float weight)
        {
            foreach (var (renderer, shapes) in _faces)
                if (shapes.TryGetValue(name, out var index))
                    renderer.SetBlendShapeWeight(index, Mathf.Clamp01(weight) * 100f);
        }

        public void Destroy()
        {
            foreach (var material in _materials)
                Object.Destroy(material);
            foreach (var mesh in _meshes)
                Object.Destroy(mesh);
            if (_bakedEyes != null)
                Object.Destroy(_bakedEyes);
            foreach (var skin in _skins)
                if (skin.Painted != null)
                    Object.Destroy(skin.Painted);
            Object.Destroy(_container.gameObject);
        }
    }
}
