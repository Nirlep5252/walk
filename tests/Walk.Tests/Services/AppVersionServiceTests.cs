using System.IO;
using FluentAssertions;
using Walk.Services;

namespace Walk.Tests.Services;

public class AppVersionServiceTests : IDisposable
{
    private readonly string _testDir;

    public AppVersionServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "walk_appversion_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

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

    [Fact]
    public void IsDevelopmentBuild_Returns_True_For_Source_Build_Output()
    {
        File.WriteAllText(Path.Combine(_testDir, "Walk.sln"), "");
        var buildOutput = Path.Combine(_testDir, "src", "Walk", "bin", "Debug", "net8.0-windows");
        Directory.CreateDirectory(buildOutput);

        var result = AppVersionService.IsDevelopmentBuild(buildOutput);

        result.Should().BeTrue();
    }

    [Fact]
    public void FormatVersionBadge_Uses_Dev_Mode_Label()
    {
        var result = AppVersionService.FormatVersionBadge(AppVersionService.DevelopmentModeVersion);

        result.Should().Be(AppVersionService.DevelopmentModeLabel);
    }
}
