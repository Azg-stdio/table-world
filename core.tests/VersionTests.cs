namespace Core.Tests
{
    public class VersionTests
    {
        [Fact]
        public void Get_ReturnsSemanticVersionFromBuildProps()
        {
            Assert.Equal("0.1.0", Core.Version.Get());
        }
    }
}
