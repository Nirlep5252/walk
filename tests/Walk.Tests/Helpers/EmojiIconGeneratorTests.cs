using System.Drawing;
using FluentAssertions;
using Walk.Helpers;

namespace Walk.Tests.Helpers;

public class EmojiIconGeneratorTests
{
    [Fact]
    public void Create_ReturnsIconWithRequestedSize()
    {
        var icon = EmojiIconGenerator.Create("\U0001f6b6", 32);

        icon.Should().NotBeNull();
        icon.Width.Should().Be(32);
        icon.Height.Should().Be(32);
    }

    [Fact]
    public void Create_WithDifferentEmoji_ReturnsIcon()
    {
        var icon = EmojiIconGenerator.Create("\U0001f3c3", 32);

        icon.Should().NotBeNull();
        icon.Width.Should().Be(32);
        icon.Height.Should().Be(32);
    }

    [Fact]
    public void Create_ProducesNonEmptyBitmap()
    {
        var icon = EmojiIconGenerator.Create("\U0001f6b6", 32);

        using var bmp = icon.ToBitmap();
        var hasContent = false;
        for (var x = 0; x < bmp.Width && !hasContent; x++)
            for (var y = 0; y < bmp.Height && !hasContent; y++)
                if (bmp.GetPixel(x, y).A > 0)
                    hasContent = true;

        hasContent.Should().BeTrue("emoji should render visible pixels");
    }

    [Fact]
    public void Create_16x16_ReturnsSmallIcon()
    {
        var icon = EmojiIconGenerator.Create("\U0001f50d", 16);

        icon.Should().NotBeNull();
        icon.Width.Should().Be(16);
        icon.Height.Should().Be(16);
    }
}
