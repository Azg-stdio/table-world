using System.Collections;
using System.Linq;
using Core.Engine;
using Core.Rules;
using NUnit.Framework;
using TableWorld.View;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>
    /// Pokémon Master Trainer's scripts put a 3D figure on a face-up Pokémon chip and put it away when the chip turns
    /// face down. It needs TTS's getObjectFromGUID (nil inside bags), takeObject callbacks, Wait.condition timeouts,
    /// scale(), getTransformUp(), real getBounds() and resting. Uses the imported package in the Games library; without it
    /// the test is skipped.
    /// </summary>
    public class PokemonFigureTests
    {
        private const string Chip = "1d7d60";       // Weepinbell's chip, in "Gen1 Green Pokémon" inside "Kanto / Generation 1"
        private const string Figure = "961d7f";     // its figure, in "Battle Models"

        [UnityTest]
        public IEnumerator FaceUpChip_GetsItsFigure_AndFaceDownPutsItAway()
        {
            var entry = GameLibrary.List().FirstOrDefault(e => e.Name.Contains("Master Trainer"));
            if (entry == null)
                Assert.Ignore("Pokémon Master Trainer is not imported on this machine.");
            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            Object.FindAnyObjectByType<TableInteraction>().IgnoreDevices = true;
            var table = Object.FindAnyObjectByType<TableView>();
            yield return new WaitForSeconds(0.2f);
            Assert.IsTrue(table.Load(System.IO.File.ReadAllText(entry.File), entry.PackageRoot));
            yield return new WaitForSeconds(0.5f);

            // The save starts with figures off: its Options Table enables them.
            var interaction = Object.FindAnyObjectByType<TableInteraction>();
            ScriptButtonView Button(string click) => table.ViewOf("57b27f").GetComponentsInChildren<ScriptButtonView>(true).FirstOrDefault(b => b.Def.Click == click);
            interaction.ClickButton(Button("click_model_options"), false);
            yield return null;
            interaction.ClickButton(Button("toggle_models"), false);
            yield return null;

            var bag = table.Engine.Find(table.Engine.Find(Chip).Container);
            Assert.IsTrue(table.Execute(new TakeCommand { ContainerId = bag.Container, EntryId = bag.Id, Position = new Vec3(0.3f, 0.02f, 0.3f) }).Ok);
            Assert.IsTrue(table.Execute(new TakeCommand { ContainerId = bag.Id, EntryId = Chip, Position = new Vec3(0f, 0.02f, 0f), Rotation = new Vec3(0f, 0f, 0f) }).Ok);
            for (var t = 0f; t < 6f && table.ViewOf(Figure)?.transform.Find("Bundle") == null; t += Time.deltaTime)
                yield return null;
            yield return new WaitForSeconds(1f);

            var figure = table.ViewOf(Figure);
            Assert.IsNotNull(figure?.transform.Find("Bundle"), "the figure came out with its bundle");
            var chip = table.ViewOf(Chip);
            var flat = new Vector2(figure.transform.position.x - chip.transform.position.x, figure.transform.position.z - chip.transform.position.z);
            Assert.Less(flat.magnitude, 0.01f, "standing on its chip");
            Assert.Less(figure.transform.position.y - chip.transform.position.y, 0.02f, "on it, not floating above");
            Assert.IsTrue(figure.Model.Locked, "the script locks it");

            Assert.IsTrue(table.Execute(new FlipCommand { ObjectId = Chip }).Ok);
            for (var t = 0f; t < 6f && table.Engine.Find(Figure).Container == null; t += Time.deltaTime)
                yield return null;
            Assert.IsNotNull(table.Engine.Find(Figure).Container, "face down, the figure goes back in its bag");
        }
    }
}
