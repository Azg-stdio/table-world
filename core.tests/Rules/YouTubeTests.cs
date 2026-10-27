using Core.Rules;

namespace Core.Tests.Rules
{
    /// <summary>Karaoke: the video id in every common form of YouTube link, and nothing from other sites.</summary>
    public class YouTubeTests
    {
        [Theory]
        [InlineData("M7lc1UVf-VE")]
        [InlineData("https://www.youtube.com/watch?v=M7lc1UVf-VE")]
        [InlineData("https://www.youtube.com/watch?list=PL123&v=M7lc1UVf-VE&t=42s")]
        [InlineData("youtube.com/watch?v=M7lc1UVf-VE")]
        [InlineData("https://youtu.be/M7lc1UVf-VE?si=abc")]
        [InlineData("https://www.youtube.com/embed/M7lc1UVf-VE?autoplay=1")]
        [InlineData("https://www.youtube.com/shorts/M7lc1UVf-VE")]
        [InlineData("https://m.youtube.com/watch?v=M7lc1UVf-VE")]
        [InlineData("https://music.youtube.com/watch?v=M7lc1UVf-VE&feature=share")]
        [InlineData("  https://www.youtube-nocookie.com/embed/M7lc1UVf-VE  ")]
        public void FindsTheVideo(string link) => Assert.Equal("M7lc1UVf-VE", YouTube.VideoId(link));

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("hello world")]
        [InlineData("https://example.com/watch?v=M7lc1UVf-VE")]
        [InlineData("https://www.youtube.com/channel/UC123")]
        [InlineData("https://www.youtube.com/watch?v=short")]
        [InlineData("https://evil-youtube.com/watch?v=M7lc1UVf-VE")]
        public void RefusesAnythingElse(string? link) => Assert.Null(YouTube.VideoId(link));
    }
}
