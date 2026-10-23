using Core.Rules;

namespace Core.Tests.Rules
{
    public class FaceUpTests
    {
        [Theory]
        [InlineData(0, 0, 0, true)]
        [InlineData(0, 0, 180, false)]
        [InlineData(0, 90, 180, false)]
        [InlineData(180, 0, 0, false)]
        [InlineData(180, 0, 180, true)]
        [InlineData(0, 270, 360, true)]
        [InlineData(10, 45, -170, false)]
        public void IsFaceUp_FollowsTheUpAxis(float x, float y, float z, bool expected)
        {
            var card = new Card();
            card.Transform.Rotation = new Vec3(x, y, z);

            Assert.Equal(expected, card.IsFaceUp);
        }
    }
}
