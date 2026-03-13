using System.IO;
using FluentAssertions;
using Walk.Services;

namespace Walk.Tests.Services;

public class SettingsServiceTests : IDisposable
{
    private readonly string _testDir;

    public SettingsServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "walk_settings_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    [Fact]
    public async Task Load_Returns_Defaults_When_No_File_Exists()
    {
        var service = new SettingsService(_testDir);
        var settings = await service.LoadAsync();

        settings.HotkeyModifiers.Should().Be("Ctrl+Alt");
        settings.HotkeyKey.Should().Be("Space");
        settings.CurrencyCacheTtlHours.Should().Be(6);
        settings.StartWithWindows.Should().BeTrue();
        settings.MaxResults.Should().Be(8);
    }

    [Fact]
    public async Task Save_And_Load_Round_Trips()
    {
        var service = new SettingsService(_testDir);
        var settings = await service.LoadAsync();
        settings.HotkeyModifiers = "Ctrl+Shift";
        settings.HotkeyKey = "K";
        settings.MaxResults = 15;
        settings.CurrencyCacheTtlHours = 12;
        settings.StartWithWindows = false;

        await service.SaveAsync(settings);

        var reloaded = await service.LoadAsync();
        reloaded.HotkeyModifiers.Should().Be("Ctrl+Shift");
        reloaded.HotkeyKey.Should().Be("K");
        reloaded.MaxResults.Should().Be(15);
        reloaded.CurrencyCacheTtlHours.Should().Be(12);
        reloaded.StartWithWindows.Should().BeFalse();
    }
}
