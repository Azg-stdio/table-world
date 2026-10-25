using System.Collections;
using System.Linq;
using Core.Net;
using NUnit.Framework;
using TableWorld.View;
using TableWorld.View.Chalkers;
using TableWorld.View.Hud;
using TableWorld.View.Net;
using TableWorld.View.Settings;
using TableWorld.View.Voice;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>
    /// T7.4 on one machine: the emote wheel picks by direction, an emote and the chosen personality reach the session
    /// (and so presence and the roster), and the mirror chalker shows them. Two machines are checked with built players.
    /// </summary>
    public class EmoteTests
    {
        private NetworkTableSession _session;
        private HudController _hud;
        private ChalkersView _chalkers;

        [UnitySetUp]
        public IEnumerator LoadTable()
        {
            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            Object.FindAnyObjectByType<TableInteraction>().IgnoreDevices = true;
            _session = Object.FindAnyObjectByType<NetworkTableSession>();
            _hud = Object.FindAnyObjectByType<HudController>();
            _chalkers = Object.FindAnyObjectByType<ChalkersView>();
            yield return new WaitForSeconds(0.3f);
        }

        [UnityTearDown]
        public IEnumerator Restore()
        {
            _hud.Escape.Set(s => { s.Personality = nameof(ChalkerPersonality.Cheerful); s.HearMyself = false; });
            _session.Stop();
            yield return null;
        }

        [Test]
        public void Wheel_PicksByDirection()
        {
            var wheel = _hud.Emotes;
            Assert.IsNotNull(wheel);
            Assert.IsNull(wheel.Pick(Vector2.zero), "the centre plays nothing");
            Assert.AreEqual(ChalkerEmote.Wave, wheel.Pick(new Vector2(0f, -120f)), "the first emote is at the top");
            var emotes = ChalkerLab.Emotes().ToList();
            var angle = (-90f + 360f / emotes.Count) * Mathf.Deg2Rad;
            Assert.AreEqual(emotes[1], wheel.Pick(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 120f), "then clockwise");
        }

        [UnityTest]
        public IEnumerator Emote_AndPersonality_ReachTheMirrorAndTheRoster()
        {
            _voiceOff();
            _hud.Escape.Set(s => { s.Personality = nameof(ChalkerPersonality.Grumpy); s.HearMyself = true; });
            yield return null;
            Assert.AreEqual("Grumpy", _session.LocalLook);
            yield return null;
            var mirror = _chalkers.Shown[ChalkersView.MirrorKey];
            Assert.AreEqual(ChalkerPersonality.Grumpy, mirror.Personality);

            _session.Emote(nameof(ChalkerEmote.Cheer));
            yield return null;
            Assert.AreEqual(ChalkerEmote.Cheer, mirror.Emote, "your own emote shows on the mirror");

            // Hosting: the roster carries the personality to everyone.
            Assert.IsTrue(_session.StartHost(7793));
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual("Grumpy", _session.Players[SessionHost.HostId].Look);
            _hud.Escape.Set(s => s.Personality = nameof(ChalkerPersonality.Shy));
            yield return null;
            Assert.AreEqual("Shy", _session.Players[SessionHost.HostId].Look);
        }

        /// <summary>No real microphone in this test.</summary>
        private void _voiceOff() => Object.FindAnyObjectByType<VoiceChat>().FakeMicrophone = n => new float[n];
    }
}
