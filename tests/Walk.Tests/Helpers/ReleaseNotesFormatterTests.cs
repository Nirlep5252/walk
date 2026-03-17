using FluentAssertions;
using Walk.Helpers;

namespace Walk.Tests.Helpers;

public class ReleaseNotesFormatterTests
{
    [Fact]
    public void ToDisplayText_StripsCommonMarkdown_Syntax()
    {
        var markdown = """
            # Walk 0.4.0

            - Added [mandatory changelogs](https://example.com)
            - Fixed `launch` update checks
            """;

        var result = ReleaseNotesFormatter.ToDisplayText(markdown);

        result.Should().Contain("Walk 0.4.0");
        result.Should().Contain("\u2022 Added mandatory changelogs");
        result.Should().Contain("\u2022 Fixed launch update checks");
        result.Should().NotContain("#");
        result.Should().NotContain("`");
        result.Should().NotContain("https://example.com");
    }

    [Fact]
    public void ToDisplayText_ReturnsFallback_WhenMarkdownIsMissing()
    {
        var result = ReleaseNotesFormatter.ToDisplayText(" ");

        result.Should().Be("No changelog details were provided for this release.");
    }
}
