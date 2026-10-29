using System.Collections;
using System.IO;
using System.Linq;
using Core.Engine;
using Core.Music;
using Core.Rules;
using NUnit.Framework;
using TableWorld.View;
using TableWorld.View.Hud;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>
    /// M12 in the game window: the tavern room with its candles and fire, light that fades, the game's music from the
    /// player's library, sound cues, names in their colours, the parchment theme, and zone outlines that keep out of the
    /// way on a table with rules.
    /// </summary>
    public class AtmosphereTests
    {
        private const string Game = @"{
  ""environment"": { ""room"": ""tavern"", ""theme"": ""parchment"" },
  ""music"": { ""playlists"": { ""tavern"": [ { ""title"": ""Hum"", ""url"": ""library:test-pack/hum"" } ] }, ""start"": ""tavern"", ""volume"": 0.5 },
  ""players"": { ""seats"": [ { ""id"": ""s1"", ""color"": ""red"" }, { ""id"": ""s2"", ""color"": ""blue"" } ] },
  ""zones"": [ { ""id"": ""spot"", ""type"": ""LayoutZone"", ""transform"": { ""position"": [0, 0.05, 0] }, ""size"": [0.2, 0.1, 0.2] } ],
  ""turn"": { ""phases"": [ ""day"", ""night"" ] },
  ""phases"": [
    { ""id"": ""day"", ""actions"": [ ""vote_day"", ""sleep"" ] },
    { ""id"": ""night"", ""actions"": [ ""wake"" ], ""onEnter"": [ { ""light"": { ""part"": ""room"", ""scale"": 0, ""seconds"": 1 } }, { ""sound"": { ""play"": ""gong"" } } ] } ],
  ""actions"": [
    { ""id"": ""sleep"", ""actor"": ""any"", ""effects"": [ { ""message"": { ""text"": ""Red says Reject."" } }, { ""endPhase"": {} } ] },
    { ""id"": ""vote_day"", ""actor"": ""any"", ""button"": false,
      ""vote"": { ""options"": [ ""approve"", ""reject"" ], ""labels"": { ""approve"": ""Approve"", ""reject"": ""Reject"" }, ""colors"": { ""approve"": ""green"", ""reject"": ""#d8322a"" } } },
    { ""id"": ""wake"", ""actor"": ""any"", ""effects"": [ { ""endPhase"": {} } ] } ]
}";

        private TableView _table;
        private EnvironmentView _environment;
        private VisualElement _root;
        private string _libraryFolder;
        private MusicLibrary _previous;

        /// <summary>A one-second WAV tone: enough for the player to load and start it.</summary>
        private static byte[] Tone()
        {
            const int rate = 8000;
            var data = new byte[44 + rate];
            System.Text.Encoding.ASCII.GetBytes("RIFF").CopyTo(data, 0);
            System.BitConverter.GetBytes(36 + rate).CopyTo(data, 4);
            System.Text.Encoding.ASCII.GetBytes("WAVEfmt ").CopyTo(data, 8);
            System.BitConverter.GetBytes(16).CopyTo(data, 16);
            System.BitConverter.GetBytes((short)1).CopyTo(data, 20);
            System.BitConverter.GetBytes((short)1).CopyTo(data, 22);
            System.BitConverter.GetBytes(rate).CopyTo(data, 24);
            System.BitConverter.GetBytes(rate).CopyTo(data, 28);
            System.BitConverter.GetBytes((short)1).CopyTo(data, 32);
            System.BitConverter.GetBytes((short)8).CopyTo(data, 34);
            System.Text.Encoding.ASCII.GetBytes("data").CopyTo(data, 36);
            System.BitConverter.GetBytes(rate).CopyTo(data, 40);
            for (var i = 0; i < rate; i++)
                data[44 + i] = (byte)(128 + 60 * Mathf.Sin(i * 0.2f));
            return data;
        }

        [UnitySetUp]
        public IEnumerator LoadTable()
        {
            // A library of the test's own with one pack, as a player would have after importing it.
            _libraryFolder = Path.Combine(Application.temporaryCachePath, "tw-music-test");
            if (Directory.Exists(_libraryFolder))
                Directory.Delete(_libraryFolder, true);
            var source = Path.Combine(Application.temporaryCachePath, "tw-music-source", "Test Pack");
            Directory.CreateDirectory(source);
            File.WriteAllBytes(Path.Combine(source, "Hum.wav"), Tone());
            _previous = MusicPlayerView.Library;
            MusicPlayerView.Library = new MusicLibrary(_libraryFolder);
            MusicPlayerView.Library.Import(source, rightsConfirmed: true);

            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            Object.FindAnyObjectByType<TableInteraction>().IgnoreDevices = true;
            _table = Object.FindAnyObjectByType<TableView>();
            _environment = Object.FindAnyObjectByType<EnvironmentView>();
            _root = Object.FindAnyObjectByType<HudController>().GetComponent<UIDocument>().rootVisualElement;
            Assert.IsTrue(_table.Load(Game, null));
            yield return new WaitForSeconds(0.3f);
        }

        [UnityTearDown]
        public IEnumerator Restore()
        {
            MusicPlayerView.Library = _previous;
            TableView.ShowZones = false;
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheTavern_HasCandlesAndAFire_ThatFlicker_AndPiecesKeepTheirLight()
        {
            Assert.AreEqual("Room (tavern)", _environment.Room.name);
            // Eight on the chandelier, five on the walls, four in the hearth.
            Assert.AreEqual(EnvironmentView.ChandelierCandles + 5 + 4, _environment.Flames.Count);
            var lights = _environment.Room.GetComponentsInChildren<Light>();
            var flames = lights.Where(l => l.name == "Sconce Light" || l.name == "Hearth Light").ToList();
            Assert.AreEqual(6, flames.Count);
            Assert.IsTrue(flames.All(l => EnvironmentView.LightingLayersOf(l) == EnvironmentView.RoomLayers && l.shadows == LightShadows.None),
                "candles and the fire light only the room, without shadows");
            var fire = flames.First(l => l.name == "Hearth Light");
            var seen = new System.Collections.Generic.HashSet<float>();
            for (var i = 0; i < 6; i++)
            {
                seen.Add(Mathf.Round(fire.intensity * 1000f));
                yield return new WaitForSeconds(0.1f);
            }
            Assert.Greater(seen.Count, 2, "the fire flickers");
            Assert.AreEqual(EnvironmentView.PiecesLayers, EnvironmentView.LightingLayersOf(_environment.TableLight));
            Assert.Greater(_environment.Look.Bloom, 0f, "flames glow in the tavern");
            Assert.AreEqual(0f, _environment.Look.Fog, "no fog: it dimmed the flames");
            Assert.IsTrue(GameObject.Find("Surface").GetComponent<Renderer>().material.mainTexture != null, "a plank tabletop");
        }

        [UnityTest]
        public IEnumerator TheGamesMusic_PlaysFromTheLibrary_AndAPhaseFadesTheRoomAndRingsAGong()
        {
            Assert.IsTrue(_table.Execute(new StartGameCommand()).Ok);
            var player = _table.GetComponent<MusicPlayerView>();
            var sounds = Object.FindAnyObjectByType<TableSounds>();
            var deadline = Time.realtimeSinceStartup + 5f;
            while (!player.IsPlaying && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.IsTrue(player.IsPlaying, "the start playlist plays");
            Assert.AreEqual("library:test-pack/hum", player.LoadedUrl);

            var lamp = _environment.Lamp.intensity;
            Assert.IsTrue(_table.Execute(new PerformActionCommand { Actor = "s1", ActionId = "sleep" }).Ok);
            Assert.AreEqual(1, sounds.Played["cue:gong"]);
            yield return new WaitForSeconds(0.35f);
            Assert.IsTrue(_environment.Fading, "the room darkens over a second");
            Assert.Less(_environment.Lamp.intensity, lamp * 0.95f);
            Assert.Greater(_environment.Lamp.intensity, 0.02f);
            yield return new WaitForSeconds(1f);
            Assert.IsFalse(_environment.Fading);
            Assert.Less(_environment.Lamp.intensity, 0.01f);
        }

        [UnityTest]
        public IEnumerator Names_AreColoured_InTheParchmentTheme_AndZoneOutlinesWaitForAPiece()
        {
            Assert.IsTrue(_table.Execute(new StartGameCommand()).Ok);
            Assert.IsTrue(_table.Execute(new PerformActionCommand { Actor = "s1", ActionId = "sleep" }).Ok);
            yield return new WaitForSeconds(0.3f);
            Assert.IsTrue(_root.ClassListContains(HudController.ParchmentTheme));
            var log = _root.Q<Label>("gameLog").text;
            StringAssert.Contains(">Red</color>", log);
            StringAssert.Contains(">Reject</color>", log);
            Assert.AreEqual("Red says Reject.", Core.Text.Highlights.Strip(log.Split('\n').Last()));

            Assert.IsFalse(_table.QuietZonesShown, "a rules table hides layout spots until something is carried");
            TableView.ShowZones = true;
            yield return null;
            Assert.IsTrue(_table.QuietZonesShown);
        }
    }
}
