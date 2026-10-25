using System.Collections;
using System.Linq;
using Core.Net;
using NUnit.Framework;
using TableWorld.View;
using TableWorld.View.Chalkers;
using TableWorld.View.Settings;
using TableWorld.View.Voice;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>
    /// T7.3 voice on one machine: with "Hear myself" on, this player's voice goes through the whole path (frames, Opus,
    /// the listener's jitter buffer, a streamed AudioSource) and a mirror chalker across the table talks with it. Two
    /// machines are checked with built players.
    /// </summary>
    public class VoiceTests
    {
        private VoiceChat _voice;
        private ChalkersView _chalkers;
        private int _toneAt;

        [UnitySetUp]
        public IEnumerator LoadTable()
        {
            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            Object.FindAnyObjectByType<TableInteraction>().IgnoreDevices = true;
            _voice = Object.FindAnyObjectByType<VoiceChat>();
            _chalkers = Object.FindAnyObjectByType<ChalkersView>();
            VoiceChat.Mode = VoiceMode.PushToTalk;
            VoiceChat.HearMyself = false;
            yield return new WaitForSeconds(0.3f);
        }

        [UnityTearDown]
        public IEnumerator Restore()
        {
            VoiceChat.HearMyself = false;
            VoiceChat.Mode = VoiceMode.PushToTalk;
            if (_voice != null)
            {
                _voice.ForceTalk = false;
                _voice.FakeMicrophone = null;
            }
            yield return null;
        }

        /// <summary>A voice-like tone, continuous across calls.</summary>
        private float[] Tone(int count)
        {
            var samples = new float[count];
            for (var i = 0; i < count; i++, _toneAt++)
            {
                var t = _toneAt / (double)VoiceFormat.SampleRate;
                samples[i] = (float)(0.25 * System.Math.Sin(2 * System.Math.PI * 220 * t) + 0.1 * System.Math.Sin(2 * System.Math.PI * 440 * t));
            }
            return samples;
        }

        [UnityTest]
        public IEnumerator HearMyself_MirrorChalkerTalks()
        {
            Assert.IsNotNull(_voice, "the table adds voice chat");
            Assert.IsNotNull(_chalkers, "the table adds the chalkers");
            _voice.FakeMicrophone = Tone;
            VoiceChat.HearMyself = true;
            yield return null;
            Assert.IsTrue(_chalkers.Shown.ContainsKey(ChalkersView.MirrorKey), "a mirror chalker sits across the table");
            var mirror = _chalkers.Shown[ChalkersView.MirrorKey];
            var camera = Camera.main.transform.position;
            Assert.Less(Vector3.Dot(new Vector3(camera.x, 0f, camera.z).normalized, mirror.Root.transform.position.normalized), -0.5f, "it sits on the far side from the camera");

            // Push to talk, not held: nothing is said.
            yield return new WaitForSeconds(0.3f);
            Assert.IsFalse(_voice.Talking);
            Assert.IsNull(_voice.SpeakerOf(VoiceChat.SelfSeat));
            Assert.Less(mirror.MouthOpen, 0.05f);

            _voice.ForceTalk = true;
            var opened = 0f;
            var level = 0f;
            for (var t = 0f; t < 1.5f; t += Time.unscaledDeltaTime)
            {
                yield return null;
                opened = Mathf.Max(opened, mirror.MouthOpen);
                level = Mathf.Max(level, _voice.LevelOf(VoiceChat.SelfSeat));
            }
            var speaker = _voice.SpeakerOf(VoiceChat.SelfSeat);
            Debug.Log($"[VoiceTests] level {level:F3}, mouth {opened:F2}, frames {_voice.Received}, concealed {speaker?.Stream.Concealed}, skipped {speaker?.Stream.Skipped}, late {speaker?.Stream.Late}");
            Assert.IsTrue(_voice.Talking);
            speaker = _voice.SpeakerOf(VoiceChat.SelfSeat);
            Assert.IsNotNull(speaker, "your own voice plays back");
            Assert.IsTrue(speaker.GetComponent<AudioSource>().isPlaying);
            Assert.Greater(level, 0.05f, "the decoded voice is loud enough");
            Assert.Greater(opened, 0.5f, "the mouth opens with the voice");
            Assert.Less(Vector3.Distance(speaker.transform.position, mirror.Mouth), 0.01f, "the voice comes from the chalker's mouth");

            // Release: the stream drains and the mouth closes.
            _voice.ForceTalk = false;
            yield return new WaitForSeconds(0.8f);
            Assert.IsFalse(_voice.Talking);
            Assert.Less(mirror.MouthOpen, 0.05f);

            VoiceChat.HearMyself = false;
            yield return null;
            yield return null;
            Assert.IsFalse(_chalkers.Shown.ContainsKey(ChalkersView.MirrorKey));
            Assert.IsNull(_voice.SpeakerOf(VoiceChat.SelfSeat));
        }

        [UnityTest]
        public IEnumerator VoiceActivation_OpensOnSpeech()
        {
            var silent = true;
            _voice.FakeMicrophone = n => silent ? new float[n] : Tone(n);
            VoiceChat.Mode = VoiceMode.VoiceActivation;
            VoiceChat.HearMyself = true;
            yield return new WaitForSeconds(0.3f);
            Assert.IsFalse(_voice.Talking, "silence keeps it closed");
            silent = false;
            yield return new WaitForSeconds(0.3f);
            Assert.IsTrue(_voice.Talking, "speech opens it");
            silent = true;
            yield return new WaitForSeconds(0.8f);
            Assert.IsFalse(_voice.Talking, "it closes after the hang time");
        }

        /// <summary>The real microphone starts only while someone could hear it, and stops after.</summary>
        [UnityTest]
        public IEnumerator Microphone_RunsOnlyWhenNeeded()
        {
            if (Microphone.devices.Length == 0)
                Assert.Ignore("No microphone on this machine.");
            yield return null;
            Assert.IsFalse(_voice.MicRunning, "playing alone, the microphone is off");
            VoiceChat.HearMyself = true;
            yield return new WaitForSeconds(0.5f);
            Assert.IsTrue(_voice.MicRunning, _voice.MicProblem);
            Assert.IsTrue(Microphone.IsRecording(null) || Microphone.devices.Any(Microphone.IsRecording));
            VoiceChat.HearMyself = false;
            yield return null;
            Assert.IsFalse(_voice.MicRunning);
            Assert.IsFalse(Microphone.devices.Any(Microphone.IsRecording));
        }
    }
}
