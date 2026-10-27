using NUnit.Framework;
using TableWorld.View.Bridge;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>
    /// For the whole PlayMode run, the game ignores agents' MCP servers running on this machine: one connecting mid-test
    /// loads its own table over the test's (a game an agent is building in another folder). The interface reads English
    /// whatever this machine's language, since tests find buttons by their words.
    /// </summary>
    [SetUpFixture]
    public sealed class NoAgentBridge
    {
        [OneTimeSetUp]
        public void Off()
        {
            BridgeClient.Disabled = true;
            TableWorld.View.Ui.Override = "en";
            TableWorld.View.Ui.SetLanguage("");
        }

        [OneTimeTearDown]
        public void On()
        {
            BridgeClient.Disabled = false;
            TableWorld.View.Ui.Override = null;
        }
    }
}
