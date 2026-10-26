using System;
using TableWorld.View.Bridge;
using UnityEngine;
using UnityEngine.UIElements;

namespace TableWorld.View.Hud
{
    /// <summary>
    /// Escape → Connect an AI agent: the setup lines for Claude Code, Claude Desktop and Codex with this install's server
    /// path, each with a Copy button, whether the server file is there, and whether an agent is connected now.
    /// </summary>
    public sealed class AgentPanel
    {
        private const string Hidden = "hidden";

        private readonly VisualElement _panel;
        private readonly Label _status;
        private bool _connectedShown;

        public AgentPanel(VisualElement root, Action back)
        {
            _panel = root.Q("agentPanel");
            _status = root.Q<Label>("agentStatus");
            root.Q<Button>("agentBack").clicked += back;
            Block(root, "agentClaudeCode", "agentCopyClaudeCode", AgentSetup.ClaudeCodeCommand);
            Block(root, "agentClaudeDesktop", "agentCopyClaudeDesktop", AgentSetup.ClaudeDesktopConfig);
            Block(root, "agentCodex", "agentCopyCodex", AgentSetup.CodexConfig);
            root.Q<Label>("agentGames").text =
                $"Games your agent builds are saved in {AgentSetup.GamesFolder} and listed under Games. " +
                "Keep this window open while it works to watch the table being built.";
        }

        public bool Visible => !_panel.ClassListContains(Hidden);

        public void Show()
        {
            _connectedShown = !Connected();
            Update();
            _panel.RemoveFromClassList(Hidden);
        }

        public void Hide() => _panel.AddToClassList(Hidden);

        /// <summary>Keeps the status line current while the panel is open (an agent may connect while the player reads).</summary>
        public void Update()
        {
            var connected = Connected();
            if (connected == _connectedShown)
                return;
            _connectedShown = connected;
            _status.text = connected ? "An agent is connected to this window now."
                : AgentSetup.ServerFound ? "No agent connected yet. After setting it up, start your agent and ask it to build a game."
                : $"The server was not found at {AgentSetup.ServerPath}. " +
                  (Application.isEditor ? "Build it with: dotnet build mcp-server -c Release" : "Reinstall the game to restore it.");
            _status.EnableInClassList("agent-status--ok", connected);
        }

        private static bool Connected() => UnityEngine.Object.FindAnyObjectByType<BridgeClient>() is { Connected: true };

        private void Block(VisualElement root, string textName, string copyName, string text)
        {
            var label = root.Q<Label>(textName);
            label.text = text;
            label.selection.isSelectable = true;
            var copy = root.Q<Button>(copyName);
            copy.clicked += () =>
            {
                GUIUtility.systemCopyBuffer = text;
                copy.text = "Copied";
                copy.schedule.Execute(() => copy.text = "Copy").StartingIn(1500);
            };
        }
    }
}
