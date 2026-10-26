using System.Collections;
using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using TableWorld.View;
using TableWorld.View.Bridge;
using TableWorld.View.Hud;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>
    /// Escape → Connect an AI agent: the setup lines carry this install's server path, Copy puts them on the clipboard,
    /// Escape goes back to the menu, and the game tells the server where the player's Games folder is.
    /// </summary>
    public class AgentPanelTests
    {
        [UnityTest]
        public IEnumerator ThePanel_ShowsTheSetupLines_AndTheServerFindsTheGamesFolder()
        {
            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            var hud = Object.FindAnyObjectByType<HudController>();
            var root = hud.GetComponent<UIDocument>().rootVisualElement;

            var info = JObject.Parse(File.ReadAllText(AgentSetup.GameInfoPath));
            Assert.AreEqual(Path.GetFullPath(GameLibrary.UserFolder), (string)info["gamesFolder"], "the server learns where the Games folder is");

            hud.Escape.Open();
            yield return null;
            using (var click = NavigationSubmitEvent.GetPooled())
            {
                click.target = root.Q<Button>("escAgent");
                root.Q<Button>("escAgent").SendEvent(click);
            }
            yield return null;
            var panel = root.Q("agentPanel");
            Assert.IsFalse(panel.ClassListContains("hidden"), "the menu opens the agent page");
            var command = root.Q<Label>("agentClaudeCode").text;
            StringAssert.StartsWith("claude mcp add --scope user table-world -- ", command);
            StringAssert.Contains(AgentSetup.ServerPath, command);
            StringAssert.Contains("\"mcpServers\"", root.Q<Label>("agentClaudeDesktop").text);
            StringAssert.Contains("[mcp_servers.table-world]", root.Q<Label>("agentCodex").text);
            Assert.IsNotEmpty(root.Q<Label>("agentStatus").text);

            using (var click = NavigationSubmitEvent.GetPooled())
            {
                click.target = root.Q<Button>("agentCopyClaudeCode");
                root.Q<Button>("agentCopyClaudeCode").SendEvent(click);
            }
            yield return null;
            Assert.AreEqual(command, GUIUtility.systemCopyBuffer, "Copy puts the line on the clipboard");

            hud.PressEscape();
            yield return null;
            Assert.IsTrue(panel.ClassListContains("hidden"), "Escape leaves the page");
            Assert.IsTrue(hud.Escape.MenuOpen, "back to the menu");
            hud.Escape.Close();
        }
    }
}
