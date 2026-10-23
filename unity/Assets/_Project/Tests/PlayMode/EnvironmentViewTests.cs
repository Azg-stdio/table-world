using System.Collections;
using NUnit.Framework;
using TableWorld.View;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>A game sets its room: the default game night room, its own lamp and colours, or no room at all.</summary>
    public class EnvironmentViewTests
    {
        private TableView _table;
        private EnvironmentView _environment;

        [UnitySetUp]
        public IEnumerator LoadTable()
        {
            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            _table = Object.FindAnyObjectByType<TableView>();
            _environment = Object.FindAnyObjectByType<EnvironmentView>();
            yield return null;
        }

        private static string Table(string environment) => @"{ ""schemaVersion"": ""0.1"", ""assets"": [ { ""id"": ""felt"", ""type"": ""image"", ""path"": ""builtin:standard_back"" } ]"
            + (environment == null ? "" : @", ""environment"": " + environment) + " }";

        /// <summary>Material colours round-trip through a colour-space conversion: compare within a small tolerance.</summary>
        private static void Same(Color expected, Color actual, string message = null)
        {
            Assert.Less(Mathf.Abs(expected.r - actual.r) + Mathf.Abs(expected.g - actual.g) + Mathf.Abs(expected.b - actual.b), 0.003f,
                $"{message} expected {expected}, was {actual}");
        }

        private static Color FeltColor() => GameObject.Find("Surface").GetComponent<Renderer>().material.color;

        [UnityTest]
        public IEnumerator DefaultTable_GetsTheGameNightRoom()
        {
            Assert.IsTrue(_table.Load(Table(null), null));
            yield return null;
            Assert.IsNotNull(_environment.Room, "the default room is built");
            Assert.IsNotNull(_environment.Lamp);
            Assert.AreEqual(EnvironmentView.DefaultLampIntensity, _environment.Lamp.intensity, 0.001f);
            Assert.IsFalse(GameObject.Find("Sun").GetComponent<Light>().enabled, "the room's lamp replaces the sun");
            Same(EnvironmentView.DefaultFelt, FeltColor());
        }

        [UnityTest]
        public IEnumerator GameSetsItsLampColoursAndFelt()
        {
            Assert.IsTrue(_table.Load(Table(@"{ ""lighting"": { ""lampColor"": [0.5, 0.6, 1], ""lampIntensity"": 1.5 },
                ""table"": { ""feltAsset"": ""felt"", ""woodColor"": [0.1, 0.1, 0.1] }, ""wallColor"": [0.3, 0.1, 0.1] }"), null));
            yield return null;
            Assert.AreEqual(new Color(0.5f, 0.6f, 1f), _environment.Lamp.color);
            Assert.AreEqual(1.5f, _environment.Lamp.intensity, 0.001f);
            var surface = GameObject.Find("Surface").GetComponent<Renderer>().material;
            Assert.IsNotNull(surface.mainTexture, "the felt image is on the table");
            Same(new Color(0.1f, 0.1f, 0.1f), GameObject.Find("RimNorth").GetComponent<Renderer>().material.color);
            Same(new Color(0.3f, 0.1f, 0.1f), GameObject.Find("Wall North").GetComponent<Renderer>().sharedMaterial.color);
        }

        [UnityTest]
        public IEnumerator RoomNone_PlainBackgroundAndSun_AndBackAgain()
        {
            Assert.IsTrue(_table.Load(Table(@"{ ""room"": ""none"", ""wallColor"": [0.2, 0.2, 0.25] }"), null));
            yield return null;
            Assert.IsNull(_environment.Room);
            Assert.IsTrue(GameObject.Find("Sun").GetComponent<Light>().enabled);
            Assert.IsNull(GameObject.Find("Wall North"));

            // Loading another game rebuilds its room; nothing from the previous one is left over.
            Assert.IsTrue(_table.Load(Table(null), null));
            yield return null;
            Assert.IsNotNull(_environment.Room);
            Same(EnvironmentView.DefaultFelt, FeltColor());
            Assert.IsNull(GameObject.Find("Surface").GetComponent<Renderer>().material.mainTexture);
        }
    }
}
