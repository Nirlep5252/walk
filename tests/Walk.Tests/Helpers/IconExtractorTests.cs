using FluentAssertions;
using Walk.Helpers;

namespace Walk.Tests.Helpers;

public class IconExtractorTests
{
    [Fact]
    public void IsShellPath_Detects_AppsFolder_Path()
    {
        IconExtractor.IsShellPath(@"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App")
            .Should()
            .BeTrue();
    }

    [Fact]
    public void GetIcon_Returns_Image_For_AppsFolder_Path()
    {
        var icon = IconExtractor.GetIcon(@"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App");

        icon.Should().NotBeNull();
    }
}
