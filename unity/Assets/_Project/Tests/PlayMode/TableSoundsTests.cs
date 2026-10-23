using System.Collections;
using System.Linq;
using Core.Engine;
using NUnit.Framework;
using TableWorld.View;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>The table makes the right sound for each thing that happens, and none while a table settles after loading.</summary>
    public class TableSoundsTests
    {
        private TableView _table;
        private TableInteraction _interaction;
        private TableSounds _sounds;

        [UnitySetUp]
        public IEnumerator LoadTable()
        {
            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            _table = Object.FindAnyObjectByType<TableView>();
            _interaction = Object.FindAnyObjectByType<TableInteraction>();
            _interaction.IgnoreDevices = true;
            _sounds = Object.FindAnyObjectByType<TableSounds>();
            yield return new WaitForSeconds(0.8f);
        }

        private int Count(string bank) => _sounds.Played.TryGetValue(bank, out var n) ? n : 0;

        private bool AnyVoicePlaying() => _sounds.GetComponentsInChildren<AudioSource>().Any(s => s.isPlaying);

        [UnityTest]
        public IEnumerator Loading_IsSilent()
        {
            Assert.AreEqual(0, Count("CardPlace") + Count("DieImpact") + Count("ChipLay") + Count("WoodImpact"), "objects settling after a load make no sound");
            yield return null;
        }

        [UnityTest]
        public IEnumerator PickUpAndDrop_SoundByKind()
        {
            Assert.IsTrue(_interaction.Grab(_table.ViewOf("ace_spades"), false));
            Assert.AreEqual(1, Count("CardSlide"), "picking up a card");
            Assert.IsTrue(AnyVoicePlaying(), "a voice is really playing");
            _interaction.RaiseHeld(0.15f);
            _interaction.DragTo(new Vector3(0.2f, 0f, 0.05f));
            yield return new WaitForSeconds(0.6f);
            _interaction.Release();
            yield return new WaitForSeconds(1f);
            Assert.GreaterOrEqual(Count("CardPlace"), 1, "a card landing");

            Assert.IsTrue(_interaction.Grab(_table.ViewOf("d6"), false));
            Assert.AreEqual(1, Count("DiceGrab"));
            _interaction.RaiseHeld(0.15f);
            yield return new WaitForSeconds(0.4f);
            _interaction.Release();
            yield return new WaitForSeconds(1f);
            var d6 = _table.ViewOf("d6");
            Assert.GreaterOrEqual(Count("DieImpact"), 1, $"a die landing (played: {string.Join(", ", _sounds.Played.Select(p => p.Key + "=" + p.Value))}; die at {d6.transform.position:F3}, layer {d6.gameObject.layer}, kinematic {d6.Body.isKinematic})");
        }

        [UnityTest]
        public IEnumerator Commands_Sound()
        {
            Assert.IsTrue(_table.Execute(new ShuffleCommand { ObjectId = "deck_main" }).Ok);
            Assert.AreEqual(1, Count("CardShuffle"));
            Assert.IsTrue(_table.Execute(new RollCommand { ObjectId = "d20" }).Ok);
            Assert.AreEqual(1, Count("DiceThrow"));
            Assert.IsTrue(_table.Execute(new FlipCommand { ObjectId = "king_hearts" }).Ok);
            Assert.AreEqual(1, Count("CardFan"));
            _interaction.Ping(new Vector3(0f, 0f, 0f));
            Assert.AreEqual(1, Count("Ping"));
            yield return null;
        }
    }
}
