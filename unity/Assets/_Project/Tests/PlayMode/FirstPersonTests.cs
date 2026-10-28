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
    /// First person (docs/dev/camera.md): you sit in your own chalker's eyes with nothing of it drawn but its shadow, your
    /// cards in the hand bar; the free camera setting keeps you lifted.
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
        public IEnumerator Seated_YourCardsAreInTheHandBar_AndNothingOfYoursCoversTheTable()
        {
            Assert.IsTrue(_camera.IsSeated);
            var yours = _chalkers.Yours;
            Assert.IsNotNull(yours);
            Assert.IsFalse(_table.HeldByChalker("south"), "on your own screen your cards are not in your chalker's hands");
            Assert.IsFalse(Object.FindAnyObjectByType<HudController>().HandBar.SteppedAside, "the hand bar shows them, readable");
            Assert.IsTrue(yours.HeadHidden);
            foreach (var renderer in yours.Root.GetComponentsInChildren<Renderer>())
                Assert.IsTrue(!renderer.enabled || renderer.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly,
                    $"{renderer.name} is drawn in first person");
            yield return null;
        }

        [UnityTest]
        public IEnumerator LiftingOut_ShowsYourChalker_AndTheHandBar()
        {
            _interaction.ToggleSeatView();
            yield return new WaitForSeconds(TableCamera.BlendSeconds + 0.2f);
            Assert.IsFalse(_camera.IsSeated);
            Assert.IsFalse(_chalkers.Yours.HeadHidden, "you see your own chalker from outside");
            Assert.Greater(_camera.transform.position.y, _chalkers.Yours.Eyes.y + 0.2f, "the view rises out of your head");
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
