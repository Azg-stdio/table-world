using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using TableWorld.View.Hud;
using TableWorld.View.Settings;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>
    /// G31: TTS's keys by default; a key moved in the settings takes over its action, a key another action had swaps with
    /// it, reserved keys (Escape, digits, F1 …) are refused, and the help shows the keys in use.
    /// </summary>
    public class KeyBindingsTests
    {
        [Test]
        public void Rebinding_Swaps_RefusesReservedKeys_AndStoresOnlyChanges()
        {
            var moved = KeyBindings.Rebind(new Dictionary<string, string>(), GameAction.Flip, Key.K);
            CollectionAssert.AreEquivalent(new Dictionary<string, string> { ["Flip"] = "K" }, moved);
            moved = KeyBindings.Rebind(moved, GameAction.Lock, Key.K);
            CollectionAssert.AreEquivalent(new Dictionary<string, string> { ["Flip"] = "L", ["Lock"] = "K" }, moved, "Flip gets Lock's old key");
            CollectionAssert.AreEquivalent(moved, KeyBindings.Rebind(moved, GameAction.Roll, Key.Escape), "Escape stays Escape");
            CollectionAssert.AreEquivalent(moved, KeyBindings.Rebind(moved, GameAction.Roll, Key.Digit3), "digits stay digits");
            Assert.IsEmpty(KeyBindings.Rebind(KeyBindings.Rebind(moved, GameAction.Flip, Key.F), GameAction.Lock, Key.L), "back to the defaults stores nothing");

            var cleaned = KeyBindings.Clean(new Dictionary<string, string> { ["Flip"] = "K", ["Nonsense"] = "J", ["Roll"] = "NotAKey", ["Lock"] = "F1", ["Group"] = "K" });
            CollectionAssert.AreEquivalent(new Dictionary<string, string> { ["Flip"] = "K" }, cleaned, "unknown, reserved and clashing entries go");
        }

        [UnityTest]
        public IEnumerator MovedKeys_ApplyFromTheSettings_SurviveASave_AndShowInTheHelp()
        {
            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            var hud = Object.FindAnyObjectByType<HudController>();
            var path = Path.Combine(Application.temporaryCachePath, "keys-test.json");
            hud.Escape.SettingsPath = path;
            try
            {
                hud.Escape.Set(s => s.Keys = KeyBindings.Rebind(s.Keys, GameAction.Flip, Key.K));
                yield return null;
                Assert.AreEqual(Key.K, KeyBindings.KeyOf(GameAction.Flip));
                Assert.AreEqual(Key.R, KeyBindings.KeyOf(GameAction.Roll), "the others keep TTS's keys");
                Assert.AreEqual("K, or middle click", KeyBindings.Fill("{Flip}, or middle click"));

                hud.Escape.Settings.Save(path);
                var loaded = GameSettings.Load(path);
                Assert.AreEqual("K", loaded.Keys["Flip"]);
            }
            finally
            {
                hud.Escape.Set(s => s.Keys = new Dictionary<string, string>());
                File.Delete(path);
            }
            Assert.AreEqual(Key.F, KeyBindings.KeyOf(GameAction.Flip));
        }
    }
}
