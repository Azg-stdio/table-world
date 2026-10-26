using NUnit.Framework;
using TableWorld.View.Bridge;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>
    /// For the whole PlayMode run, the game ignores agents' MCP servers running on this machine: one connecting mid-test
    /// loads its own table over the test's (a game an agent is building in another folder).
    /// </summary>
    [SetUpFixture]
    public sealed class NoAgentBridge
    {
        [OneTimeSetUp]
        public void Off() => BridgeClient.Disabled = true;

        [OneTimeTearDown]
        public void On() => BridgeClient.Disabled = false;
    }
}
