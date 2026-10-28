using System.Collections.Generic;
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

        /// <summary>How far the jaw drops with the mouth fully open, in degrees.</summary>
        public const float JawDegrees = 36f;
        /// <summary>A shut eye's height against an open one: cartoon eyes without lids blink by squashing flat.</summary>
        public const float ShutEye = 0.12f;

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
        /// <summary>Per eye (left, right), the bone's local axis that points up the face, which a blink squashes.</summary>
        private readonly int[] _eyeUp = new int[2];

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
            }
            for (var side = 0; side < 2; side++)
            {
                var eye = Bone(side == 0 ? rig.LeftEye : rig.RightEye);
                if (eye == null)
                    continue;
                var axes = new[] { eye.right, eye.up, eye.forward };
                for (var i = 1; i < 3; i++)
                    if (Mathf.Abs(Vector3.Dot(axes[i], _container.up)) > Mathf.Abs(Vector3.Dot(axes[_eyeUp[side]], _container.up)))
                        _eyeUp[side] = i;
            }

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
                var holds = chalker.CardHold is { } hold && (side == 0 || chalker.ReachFor == null && !chalker.Microphone);
                if (holds)
                {
                    var grip = chalker.CardHold.Value;
                    // The wrist well below the fan's foot and a little behind it: the fingers close on the cards from their
                    // backs, the faces left to read.
                    target = (side == 0 ? grip.LeftHand : grip.RightHand) - grip.Up * (_handLength[side] * 0.8f) - grip.Face * 0.025f;
                }
                Reach(upper, lower, end, target, elbow - root.forward * 0.1f);
                if (holds && Bone(end) is { } palm)
                {
                    var along = chalker.CardHold.Value;
                    var want = (along.Up - along.Face * 0.25f).normalized;
                    palm.rotation = Quaternion.FromToRotation(palm.TransformDirection(_fingers[side]), want) * palm.rotation;
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
        /// First person: the model draws only its shadow (you look out from inside it, and its head and shoulders would
        /// fill the view) except its own forearms and hands (<see cref="FirstPersonArms"/>), which hold the cards in view.
        /// </summary>
        public void HideBody(bool hide)
        {
            var mode = hide ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly : UnityEngine.Rendering.ShadowCastingMode.On;
            foreach (var renderer in _renderers)
                renderer.shadowCastingMode = mode;
            if (hide && _arms == null)
                _arms = BuildArms();
            foreach (var arm in _arms ?? new List<GameObject>())
                arm.SetActive(hide);
        }

        /// <summary>Whether first person shows this model's own forearms and hands (false: none were found in its rig).</summary>
        public bool FirstPersonArms => _arms is { Count: > 0 };

        private SkinnedMeshRenderer[] _renderers = new SkinnedMeshRenderer[0];
        private List<GameObject> _arms;

        /// <summary>
        /// The triangles of each skinned mesh that its forearm and hand bones carry (both sides, the fingers included),
        /// as meshes of their own on the same skeleton: posed with the body, drawn without it. Built the first time.
        /// </summary>
        private List<GameObject> BuildArms()
        {
            var arms = new List<GameObject>();
            var carried = new HashSet<Transform>();
            foreach (var name in new[] { _rig.LeftForearm, _rig.RightForearm })
                if (Bone(name) is { } forearm)
                    carried.UnionWith(forearm.GetComponentsInChildren<Transform>(true));
            if (carried.Count == 0)
                return arms;
            foreach (var renderer in _renderers)
            {
                var source = renderer.sharedMesh;
                // An imported model needs Read/Write on to be cut; without it, the shapes' arms stand in.
                if (source == null || !source.isReadable)
                    continue;
                var bones = renderer.bones;
                var weights = source.boneWeights;
                if (weights.Length != source.vertexCount)
                    continue;
                bool Arm(int vertex)
                {
                    var w = weights[vertex];
                    float On(int bone, float weight) => bone >= 0 && bone < bones.Length && carried.Contains(bones[bone]) ? weight : 0f;
                    return On(w.boneIndex0, w.weight0) + On(w.boneIndex1, w.weight1) + On(w.boneIndex2, w.weight2) + On(w.boneIndex3, w.weight3) > 0.5f;
                }
                var subMeshes = new List<int[]>();
                var any = false;
                for (var sub = 0; sub < source.subMeshCount; sub++)
                {
                    var triangles = source.GetTriangles(sub);
                    var kept = new List<int>();
                    for (var i = 0; i + 2 < triangles.Length; i += 3)
                        if (Arm(triangles[i]) && Arm(triangles[i + 1]) && Arm(triangles[i + 2]))
                            kept.AddRange(new[] { triangles[i], triangles[i + 1], triangles[i + 2] });
                    any |= kept.Count > 0;
                    subMeshes.Add(kept.ToArray());
                }
                if (!any)
                    continue;
                var mesh = new Mesh
                {
                    name = source.name + " arms",
                    indexFormat = source.indexFormat,
                    vertices = source.vertices,
                    normals = source.normals,
                    tangents = source.tangents,
                    uv = source.uv,
                    boneWeights = weights,
                    bindposes = source.bindposes,
                    subMeshCount = subMeshes.Count,
                };
                for (var sub = 0; sub < subMeshes.Count; sub++)
                    mesh.SetTriangles(subMeshes[sub], sub);
                mesh.RecalculateBounds();
                var go = new GameObject(renderer.name + " (first-person arms)");
                go.transform.SetParent(renderer.transform.parent, false);
                go.transform.SetLocalPositionAndRotation(renderer.transform.localPosition, renderer.transform.localRotation);
                go.transform.localScale = renderer.transform.localScale;
                var arm = go.AddComponent<SkinnedMeshRenderer>();
                arm.sharedMesh = mesh;
                arm.bones = bones;
                arm.rootBone = renderer.rootBone;
                arm.sharedMaterials = renderer.sharedMaterials;
                arm.updateWhenOffscreen = true;
                arm.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _meshes.Add(mesh);
                go.SetActive(false);
                arms.Add(go);
            }
            return arms;
        }

        private readonly List<Mesh> _meshes = new List<Mesh>();

        private void Face(FaceShape face, float mouth, Vector2 pupils, bool leftShut, bool rightShut, Transform head)
        {
            var open = Mathf.Clamp01(Mathf.Max(mouth, face.MouthOpen));
            var across = head.right;
            var jaw = Bone(_rig.Jaw);
            if (jaw != null)
                jaw.rotation = Quaternion.AngleAxis(open * JawDegrees, across) * jaw.rotation;

            // Eyes: as tall as the face opens them (half shut, wide); a blink, wink or sleep squashes them flat.
            var height = Mathf.Clamp(face.EyeOpen, ShutEye, 1.25f);
            Eye(0, leftShut ? ShutEye : height);
            Eye(1, rightShut ? ShutEye : height);

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

        private void Eye(int side, float height)
        {
            var bone = Bone(side == 0 ? _rig.LeftEye : _rig.RightEye);
            if (bone == null)
                return;
            var scale = Vector3.one;
            scale[_eyeUp[side]] = height;
            bone.localScale = scale;
        }

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
            foreach (var skin in _skins)
                if (skin.Painted != null)
                    Object.Destroy(skin.Painted);
            Object.Destroy(_container.gameObject);
        }
    }
}
