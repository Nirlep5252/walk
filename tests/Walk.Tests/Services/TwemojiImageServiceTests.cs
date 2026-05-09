using FluentAssertions;
using Walk.Services;

namespace Walk.Tests.Services;

public sealed class TwemojiImageServiceTests
{
    [Theory]
    [InlineData("😂", "1f602")]
    [InlineData("❤️", "2764")]
    [InlineData("✌️", "270c")]
    [InlineData("☕", "2615")]
    public void GetAssetCode_Returns_Twemoji_Asset_Code(string emoji, string expected)
    {
        TwemojiImageService.GetAssetCode(emoji).Should().Be(expected);
    }
}
