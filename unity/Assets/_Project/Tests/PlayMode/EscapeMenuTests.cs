using System.Collections;
using System.IO;
using NUnit.Framework;
using TableWorld.View;
using TableWorld.View.Hud;
using TableWorld.View.Settings;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>G13 Escape menu and G31 settings: Escape order, settings applied and saved, quit asked twice.</summary>
    public class EscapeMenuTests
    {
        private HudController _hud;
        private VisualElement _root;
        private string _path;
        private GameSettings _before;

        [UnitySetUp]
        public IEnumerator LoadTable()
        {
            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            _hud = Object.FindAnyObjectByType<HudController>();
            _root = _hud.GetComponent<UIDocument>().rootVisualElement;
            Object.FindAnyObjectByType<TableInteraction>().IgnoreDevices = true;
            _path = Path.Combine(Application.temporaryCachePath, "settings-test.json");
            File.Delete(_path);
            _hud.Escape.SettingsPath = _path;
            _before = _hud.Escape.Settings.Copy();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator Restore()
        {
            // Leave this machine as the player's settings had it.
            _hud.Escape.Set(s => { s.MasterVolume = _before.MasterVolume; s.TableVolume = _before.TableVolume; s.OrbitSensitivity = _before.OrbitSensitivity; s.UiScale = _before.UiScale; s.InvertOrbitY = _before.InvertOrbitY; });
            File.Delete(_path);
            yield return null;
        }

        private void Click(string name)
        {
            var button = _root.Q<Button>(name);
            using var submit = NavigationSubmitEvent.GetPooled();
            submit.target = button;
            button.SendEvent(submit);
        }

        [UnityTest]
        public IEnumerator Escape_ClosesWhatIsOpenFirst_ThenOpensAndClosesTheMenu()
        {
            var menu = _hud.Escape;
            _hud.Help.Show();
            _hud.PressEscape();
            Assert.IsFalse(_hud.Help.Visible, "Escape closes the controls first");
            Assert.IsFalse(menu.MenuOpen, "and nothing else");

            _hud.Overlay.OpenMenu(null, new Vector2(200f, 200f));
            Assert.IsTrue(_hud.Overlay.MenuOpen);
            _hud.PressEscape();
            Assert.IsFalse(_hud.Overlay.MenuOpen, "then the right-click menu");
            Assert.IsFalse(menu.MenuOpen);

            _hud.PressEscape();
            Assert.IsTrue(menu.MenuOpen, "with nothing open, Escape opens the menu");
            yield return null;
            Assert.IsTrue(_root.Q<Button>("escLeave").ClassListContains("hidden"), "no Leave button playing alone");
            Click("escSettings");
            Assert.IsTrue(menu.SettingsOpen);
            _hud.PressEscape();
            Assert.IsFalse(menu.SettingsOpen, "Escape steps back from settings");
            Assert.IsTrue(menu.MenuOpen, "to the menu");
            _hud.PressEscape();
            Assert.IsFalse(menu.MenuOpen, "and then to the table");

            _hud.PressEscape();
            Click("escControls");
            Assert.IsFalse(menu.MenuOpen);
            Assert.IsTrue(_hud.Help.Visible, "Controls opens the list");
            _hud.PressEscape();
        }

        [UnityTest]
        public IEnumerator Settings_ApplyAtOnce_SaveOnClose_AndClampOnLoad()
        {
            var menu = _hud.Escape;
            var camera = Object.FindAnyObjectByType<TableCamera>();
            menu.Open();
            menu.OpenSettings();
            Assert.Greater(_root.Query(className: "settings-row").ToList().Count, 10, "every setting has a row");
            menu.Set(s => { s.MasterVolume = 0.3f; s.TableVolume = 0.5f; s.OrbitSensitivity = 2f; s.InvertOrbitY = true; });
            Assert.AreEqual(0.3f, AudioListener.volume, 0.001f);
            Assert.AreEqual(0.5f, TableSounds.Volume, 0.001f);
            Assert.AreEqual(2f, camera.OrbitScale, 0.001f);
            Assert.IsTrue(camera.InvertOrbitY);
            Assert.IsFalse(File.Exists(_path), "saved when the page closes, not on every drag");
            menu.CloseSettings();
            Assert.IsTrue(File.Exists(_path));
            var saved = GameSettings.Load(_path);
            Assert.AreEqual(0.3f, saved.MasterVolume, 0.001f);
            Assert.IsTrue(saved.InvertOrbitY);
            StringAssert.Contains("\"Antialiasing\": \"Smaa\"", File.ReadAllText(_path), "enums are saved by name");

            File.WriteAllText(_path, "{ \"MasterVolume\": 7, \"OrbitSensitivity\": -3, \"FrameLimit\": 59, \"Width\": 10, \"Height\": 10, \"UiScale\": \"NaN\" }");
            var hostile = GameSettings.Load(_path);
            Assert.AreEqual(1f, hostile.MasterVolume);
            Assert.AreEqual(GameSettings.MinScale, hostile.OrbitSensitivity);
            Assert.AreEqual(0, hostile.FrameLimit);
            Assert.AreEqual(0, hostile.Width);
            Assert.AreEqual(1f, hostile.UiScale);
            File.WriteAllText(_path, "not json");
            Assert.AreEqual(1f, GameSettings.Load(_path).MasterVolume, "an unreadable file gives defaults");
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("could not be read"));
            menu.Close();
            yield return null;
        }

        [UnityTest]
        public IEnumerator InterfaceSize_ScalesTheHud_WithoutTouchingTheSharedAsset()
        {
            var document = _hud.GetComponent<UIDocument>();
            var shared = Resources.FindObjectsOfTypeAll<PanelSettings>();
            var before = document.panelSettings.referenceResolution;
            _hud.Escape.Set(s => s.UiScale = 1.25f);
            yield return null;
            Assert.Less(document.panelSettings.referenceResolution.y, before.y, "a larger interface uses a smaller reference size");
            foreach (var asset in shared)
                if (asset != document.panelSettings && asset.name == "PanelSettings")
                    Assert.AreEqual(900, asset.referenceResolution.y, "the PanelSettings asset is never changed");
            Assert.IsNotNull(_root.Q("escapePanel"), "the HUD survived the panel copy");
        }

        [UnityTest]
        public IEnumerator Quit_IsAskedTwice()
        {
            var quit = 0;
            _hud.Escape.QuitOverride = () => quit++;
            _hud.PressEscape();
            Click("escQuit");
            Assert.IsTrue(_hud.Escape.QuitAsked);
            Assert.AreEqual(0, quit, "the first click only asks");
            _hud.PressEscape();
            Assert.IsFalse(_hud.Escape.QuitAsked, "Escape cancels the question");
            Assert.IsTrue(_hud.Escape.MenuOpen);
            Click("escQuit");
            Click("escQuitYes");
            Assert.AreEqual(1, quit);
            _hud.Escape.Close();
            yield return null;
        }
    }
}
