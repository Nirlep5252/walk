using FluentAssertions;
using Walk.Services;

namespace Walk.Tests.Services;

public class AppVersionServiceTests
{
    [Theory]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("1.2.3+abcdef", "1.2.3")]
    [InlineData(" 2.0.0-beta.1 +sha ", "2.0.0-beta.1")]
    public void Normalize_UsesInformationalVersion_WhenPresent(string informationalVersion, string expected)
    {
        var result = AppVersionService.Normalize(informationalVersion);

        result.Should().Be(expected);
    }

    [Fact]
    public void Normalize_FallsBackToAssemblyVersion_WhenInformationalVersionMissing()
    {
        var result = AppVersionService.Normalize(null, new Version(3, 4, 5, 6));

        result.Should().Be("3.4.5");
    }
}
