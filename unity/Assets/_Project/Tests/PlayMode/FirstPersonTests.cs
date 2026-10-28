using System.Collections;
using System.Linq;
using NUnit.Framework;
using TableWorld.View;
using TableWorld.View.Chalkers;
using TableWorld.View.Hud;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>
    /// First person (docs/dev/camera.md): you sit in your own chalker's eyes, its hands hold your cards in a fan instead of
    /// the hand zone and the hand bar, they come up to your face to be read, and the free camera setting keeps you lifted.
    /// </summary>
    public class FirstPersonTests
    {
        private const string Json = @"{ ""schemaVersion"": ""0.1"",
  ""players"": { ""seats"": [ { ""id"": ""south"", ""color"": ""red"", ""handZone"": ""hand_south"" }, { ""id"": ""north"", ""color"": ""blue"", ""handZone"": ""hand_north"" } ] },
  ""zones"": [ { ""id"": ""hand_south"", ""type"": ""HandZone"", ""owner"": ""south"", ""transform"": { ""position"": [0, 0.05, -0.42] }, ""size"": [0.6, 0.1, 0.12] },
               { ""id"": ""hand_north"", ""type"": ""HandZone"", ""owner"": ""north"", ""transform"": { ""position"": [0, 0.05, 0.42], ""rotation"": [0, 180, 0] }, ""size"": [0.6, 0.1, 0.12] } ],
  ""objects"": [
    { ""id"": ""a"", ""type"": ""Card"", ""transform"": { ""position"": [-0.1, 0.05, -0.42] }, ""text"": { ""title"": ""A"" } },
    { ""id"": ""b"", ""type"": ""Card"", ""transform"": { ""position"": [0, 0.05, -0.42] }, ""text"": { ""title"": ""B"" } },
    { ""id"": ""c"", ""type"": ""Card"", ""transform"": { ""position"": [0.1, 0.05, -0.42] }, ""text"": { ""title"": ""C"" } },
    { ""id"": ""n"", ""type"": ""Card"", ""transform"": { ""position"": [0, 0.05, 0.42] }, ""text"": { ""title"": ""N"" } } ] }";

        private TableView _table;
        private TableCamera _camera;
        private TableInteraction _interaction;
        private ChalkersView _chalkers;

        [UnitySetUp]
        public IEnumerator Load()
        {
            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            _table = Object.FindAnyObjectByType<TableView>();
            _interaction = Object.FindAnyObjectByType<TableInteraction>();
            _interaction.IgnoreDevices = true;
            _camera = Object.FindAnyObjectByType<TableCamera>();
            _chalkers = Object.FindAnyObjectByType<ChalkersView>();
            Assert.IsTrue(_table.Load(Json, null));
            yield return new WaitForSeconds(TableCamera.BlendSeconds + 0.6f);
        }

        [UnityTearDown]
        public IEnumerator Restore()
        {
            _camera.FirstPerson = true;
            yield return null;
        }

        [UnityTest]
        public IEnumerator YourChalker_HoldsYourCards_AndRaisesThemIntoView()
        {
            Assert.IsTrue(_camera.IsSeated);
            var yours = _chalkers.Yours;
            Assert.IsNotNull(yours);
            Assert.IsTrue(_table.HeldByChalker("south"), "your chalker holds your cards");
            Assert.IsFalse(_table.HeldByChalker("north"), "nobody sits north: its cards stand at its hand zone");
            var hold = yours.CardHold.Value;
            foreach (var id in new[] { "a", "b", "c" })
                Assert.Less(Vector3.Distance(_table.Views[id].transform.position, hold.Grip), 0.1f, $"{id} is in the fan");
            Assert.IsTrue(Object.FindAnyObjectByType<HudController>().HandBar.SteppedAside, "the hand bar steps aside while the real cards are in your hands");

            var low = _table.Views["b"].transform.position.y;
            _interaction.ToggleHandUp();
            yield return new WaitForSeconds(0.8f);
            Assert.AreEqual(1f, yours.Raised, 0.01f);
            Assert.Greater(_table.Views["b"].transform.position.y, low + 0.15f, "up to your face");
            var cam = _camera.GetComponent<Camera>();
            foreach (var id in new[] { "a", "b", "c" })
            {
                var seen = cam.WorldToViewportPoint(_table.Views[id].transform.position);
                Assert.IsTrue(seen.z > 0f && seen.x > 0.2f && seen.x < 0.8f && seen.y > 0.2f && seen.y < 0.9f, $"{id} is in front of your eyes ({seen})");
                // The face (local +Y) looks back at the eyes.
                Assert.Greater(Vector3.Dot(_table.Views[id].transform.up, (cam.transform.position - _table.Views[id].transform.position).normalized), 0.7f, $"{id} faces you");
            }
            var leftHand = yours.Points["leftHand"].position;
            Assert.Less(Vector3.Distance(leftHand, yours.CardHold.Value.LeftHand), 0.03f, "the hand is on the fan");

            _interaction.ToggleHandUp();
            yield return new WaitForSeconds(0.8f);
            Assert.AreEqual(0f, yours.Raised, 0.01f, "and down again");
        }

        [UnityTest]
        public IEnumerator LiftingOut_ShowsYourChalker_AndTheHandBar()
        {
            _interaction.ToggleSeatView();
            yield return new WaitForSeconds(TableCamera.BlendSeconds + 0.2f);
            Assert.IsFalse(_camera.IsSeated);
            Assert.IsFalse(_chalkers.Yours.HeadHidden, "you see your own chalker from outside");
            Assert.Greater(_camera.transform.position.y, _chalkers.Yours.Eyes.y + 0.2f, "the view rises out of your head");
            Assert.IsTrue(_table.HeldByChalker("south"), "it still holds your cards");
            Assert.IsFalse(Object.FindAnyObjectByType<HudController>().HandBar.SteppedAside, "the hand bar is back");
            _camera.ResetView();
            yield return new WaitForSeconds(TableCamera.BlendSeconds + 0.2f);
            Assert.IsTrue(_camera.IsSeated, "Space sits you again");
        }

        [UnityTest]
        public IEnumerator TurningTheHead_TurnsTheView_InPlace()
        {
            Assert.IsTrue(_camera.IsSeated);
            var from = _camera.transform.position;
            var forward = _camera.transform.forward;
            _camera.TurnHead(90f, 0f);
            yield return new WaitForSeconds(0.8f);
            Assert.Less(Vector3.Angle(forward, _camera.transform.forward), 150f);
            Assert.Greater(Vector3.Angle(forward, _camera.transform.forward), 45f, "the view turned");
            Assert.Less(Vector3.Distance(from, _camera.transform.position), 0.003f, "but the eyes stayed where they were");
            _camera.TurnHead(-60f, 60f);
            yield return new WaitForSeconds(0.8f);
            Assert.Less(Vector3.Distance(from, _camera.transform.position), 0.003f, "looking down too");
        }

        [UnityTest]
        public IEnumerator Lifted_YourChalkerFades_WhereItCoversTheTable()
        {
            _interaction.ToggleSeatView();
            yield return new WaitForSeconds(TableCamera.BlendSeconds + 0.2f);
            var yours = _chalkers.Yours;
            // Behind your chair, looking over your chalker at the table.
            _camera.SetView(new TableCamera.View { Pivot = Vector3.zero, Yaw = 0f, Pitch = 25f, Distance = 1.4f });
            yield return new WaitForSeconds(0.6f);
            Assert.IsTrue(ChalkersView.Covers(yours, _camera.GetComponent<Camera>()));
            Assert.AreEqual(ChalkerGhost.SeeThrough, yours.Ghost.Alpha, 0.01f, "faded");
            var shown = yours.Root.GetComponentsInChildren<Renderer>().Where(r => r.enabled && r.name != "Ghost rim").ToList();
            Assert.IsTrue(shown.All(r => r.sharedMaterials.All(m => m == null || m.renderQueue >= 3000)), "drawn see-through");
            Assert.IsTrue(yours.Root.GetComponentsInChildren<Renderer>().Any(r => r.name == "Ghost rim"), "with a rim in your colour");

            // From across the table it stands behind it, hiding nothing.
            _camera.SetView(new TableCamera.View { Pivot = Vector3.zero, Yaw = 180f, Pitch = 35f, Distance = 1.4f });
            yield return new WaitForSeconds(0.6f);
            Assert.IsFalse(ChalkersView.Covers(yours, _camera.GetComponent<Camera>()));
            Assert.AreEqual(1f, yours.Ghost.Alpha, 0.01f, "solid again");
            Assert.IsTrue(yours.Root.GetComponentsInChildren<Renderer>().All(r => r.sharedMaterials.All(m => m == null || m.renderQueue < 3000)));
            Assert.IsFalse(yours.Root.GetComponentsInChildren<Renderer>().Any(r => r.name == "Ghost rim"));
        }

        [UnityTest]
        public IEnumerator FreeCameraSetting_KeepsYouLifted()
        {
            Object.FindAnyObjectByType<HudController>().Escape.Set(s => s.FreeCamera = true);
            Assert.IsFalse(_camera.FirstPerson);
            _camera.ResetView();
            yield return new WaitForSeconds(TableCamera.BlendSeconds + 0.2f);
            Assert.IsFalse(_camera.IsSeated, "Space goes to the free camera's starting view");
            Assert.IsNotNull(_chalkers.Yours, "your chalker still sits in your chair");
            Assert.IsFalse(_table.HeldByChalker("south"), "your hand stands at your edge of the table, as in TTS");
            Assert.IsFalse(Object.FindAnyObjectByType<HudController>().HandBar.SteppedAside, "and the hand bar shows it");
            Object.FindAnyObjectByType<HudController>().Escape.Set(s => s.FreeCamera = false);
            _interaction.ToggleSeatView();
            yield return new WaitForSeconds(TableCamera.BlendSeconds + 0.2f);
            Assert.IsTrue(_camera.IsSeated, "O sits you all the same");
        }
    }
}
