using NUnit.Framework;

namespace TableWorld.Tests
{
    public class CoreAssemblyTests
    {
        [Test]
        public void CoreDll_IsLoadedAndReportsVersion()
        {
            Assert.That(Core.Version.Get(), Does.Match(@"^\d+\.\d+\.\d+"));
        }
    }
}
