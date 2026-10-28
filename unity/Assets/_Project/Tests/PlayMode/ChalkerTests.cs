using System.Collections;
using NUnit.Framework;
using TableWorld.View.Chalkers;
using UnityEngine;
using UnityEngine.TestTools;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>T7.3/T7.4 chalkers on their own: personalities change the resting face, emotes move the body and face and end by themselves, Hold freezes them.</summary>
    public class ChalkerTests
    {
        private GameObject _parent;
        private Chalker _chalker;

        [SetUp]
        public void Create()
        {
            _parent = new GameObject("Chalker test");
            _chalker = new Chalker("test", _parent.transform, Color.red);
        }

        [TearDown]
        public void Destroy()
        {
            _chalker.Destroy();
            Object.Destroy(_parent);
        }

        private IEnumerator Run(float seconds, float level = 0f)
        {
            for (var t = 0f; t < seconds; t += Time.deltaTime)
            {
                _chalker.Pose(Vector3.zero, Vector3.forward, new Vector3(0f, 0.4f, 1f), null, level, Time.deltaTime);
                yield return null;
            }
        }

        /// <summary>
        /// The rigged model follows the points: its hands reach the chalker's hands on the table, and the voice drops its jaw.
        /// </summary>
        [UnityTest]
        public IEnumerator Model_ReachesTheHandsAndTalks()
        {
            var model = _chalker.Model;
            Assume.That(model, Is.Not.Null, "no chalker model in the project");
            yield return Run(0.3f);
            foreach (var (point, bone) in new[] { ("leftHand", ChalkerModel.Rig.Unreal.LeftHand), ("rightHand", ChalkerModel.Rig.Unreal.RightHand) })
                Assert.Less(Vector3.Distance(_chalker.Points[point].position, model.Find(bone).position), 0.06f, point);
            var head = model.Find(ChalkerModel.Rig.Unreal.Head);
            var jaw = model.Find(ChalkerModel.Rig.Unreal.Jaw);
            var shut = Quaternion.Inverse(head.rotation) * jaw.rotation;
            yield return Run(0.4f, 0.3f);
            Assert.Greater(Quaternion.Angle(shut, Quaternion.Inverse(head.rotation) * jaw.rotation), 15f, "talking opens the jaw");
            Assert.Greater(_chalker.MouthOpen, 0.5f);
        }

        /// <summary>M10 T11.11: the named points an artist's model binds to exist, and the pose moves them.</summary>
        [UnityTest]
        public IEnumerator NamedPoints_ExistAndFollowThePose()
        {
            var names = new[] { "root", "torso", "neck", "head", "face", "mouth", "leftShoulder", "rightShoulder", "leftElbow", "rightElbow", "leftHand", "rightHand", "leftHip", "rightHip" };
            CollectionAssert.AreEquivalent(names, _chalker.Points.Keys);
            foreach (var name in names)
                Assert.IsNotNull(_chalker.Points[name], name);
            yield return Run(0.2f);
            var hand = _chalker.Points["rightHand"].position;
            _chalker.Play(ChalkerEmote.Wave);
            yield return Run(0.6f);
            Assert.Greater(Vector3.Distance(hand, _chalker.Points["rightHand"].position), 0.1f, "a wave moves the right hand point");
        }

        /// <summary>M10: a wink shuts one eye and opens it again; eyes closed shuts both and lowers the head until woken.</summary>
        [UnityTest]
        public IEnumerator AWink_ShutsOneEye_AndEyesClosed_PutsItToSleep()
        {
            yield return Run(0.2f);
            _chalker.Wink();
            yield return Run(0.1f);
            Assert.AreEqual((true, false), _chalker.EyesShut, "only the left eye");
            yield return Run(Chalker.WinkSeconds);
            Assert.IsNull(_chalker.Winking);

            var awakeHead = _chalker.Mouth.y;
            _chalker.EyesClosed = true;
            yield return Run(0.8f);
            Assert.AreEqual((true, true), _chalker.EyesShut);
            Assert.Less(_chalker.Mouth.y, awakeHead - 0.01f, "the head drops");
            _chalker.EyesClosed = false;
            yield return Run(0.8f);
            Assert.AreEqual(awakeHead, _chalker.Mouth.y, 0.003f, "and comes back up");
        }

        [UnityTest]
        public IEnumerator Personalities_SetTheRestingFace()
        {
            _chalker.Personality = ChalkerPersonality.Grumpy;
            yield return Run(0.05f);
            Assert.Greater(_chalker.Face.BrowTilt, 0.5f, "grumpy brows slant down to the nose");
            Assert.Less(_chalker.Face.Smile, 0f);
            _chalker.Personality = ChalkerPersonality.Sleepy;
            yield return Run(0.05f);
            Assert.Less(_chalker.Face.EyeOpen, 0.5f);
            Assert.AreEqual(FaceExtra.Zzz, _chalker.Face.Extra);
        }

        [UnityTest]
        public IEnumerator Wave_RaisesTheRightHand_ThenEnds()
        {
            yield return Run(0.1f);
            var restingHand = _chalker.RightHand.position.y;
            Assert.Less(restingHand, _chalker.Head.position.y);
            _chalker.Play(ChalkerEmote.Wave);
            yield return Run(0.8f);
            Assert.AreEqual(ChalkerEmote.Wave, _chalker.Emote);
            Assert.Greater(_chalker.RightHand.position.y, _chalker.Head.position.y, "the hand is up by the head");
            yield return Run(ChalkerEmotes.Duration(ChalkerEmote.Wave));
            Assert.AreEqual(ChalkerEmote.None, _chalker.Emote, "an emote ends by itself");
            yield return Run(0.1f);
            Assert.AreEqual(restingHand, _chalker.RightHand.position.y, 0.01f, "back at rest");
        }

        [UnityTest]
        public IEnumerator Sad_ShowsATear_AndHoldFreezesIt()
        {
            _chalker.Play(ChalkerEmote.Sad);
            yield return Run(1f);
            Assert.AreEqual(FaceExtra.Tear, _chalker.Face.Extra);
            Assert.Less(_chalker.Face.Smile, -0.5f);
            _chalker.Hold = true;
            yield return Run(ChalkerEmotes.Duration(ChalkerEmote.Sad) + 0.5f);
            Assert.AreEqual(ChalkerEmote.Sad, _chalker.Emote, "held, it does not end");
            _chalker.Hold = false;
            yield return Run(ChalkerEmotes.Duration(ChalkerEmote.Sad));
            Assert.AreEqual(ChalkerEmote.None, _chalker.Emote);
        }

        [UnityTest]
        public IEnumerator Voice_OpensTheMouth_OverAnyFace()
        {
            _chalker.Personality = ChalkerPersonality.Grumpy;
            yield return Run(0.2f);
            Assert.Less(_chalker.MouthOpen, 0.05f);
            yield return Run(0.3f, level: 0.2f);
            Assert.Greater(_chalker.MouthOpen, 0.8f);
        }
    }
}
