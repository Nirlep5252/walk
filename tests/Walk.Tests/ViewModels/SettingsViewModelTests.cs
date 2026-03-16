using FluentAssertions;
using Walk.Services;
using Walk.ViewModels;

namespace Walk.Tests.ViewModels;

public class SettingsViewModelTests
{
    [Fact]
    public void HasUnsavedChanges_Includes_AutoStartOnLogin()
    {
        var viewModel = new SettingsViewModel(new WalkSettings
        {
            StartWithWindows = true,
        }, "0.2.0");

        viewModel.HasUnsavedChanges.Should().BeFalse();

        viewModel.AutoStartOnLogin = false;

        viewModel.HasUnsavedChanges.Should().BeTrue();
    }

    [Fact]
    public void BuildSettings_Copies_AutoStartOnLogin()
    {
        var viewModel = new SettingsViewModel(new WalkSettings
        {
            StartWithWindows = true,
        }, "0.2.0");

        viewModel.AutoStartOnLogin = false;

        var settings = viewModel.BuildSettings();

        settings.StartWithWindows.Should().BeFalse();
    }

    [Fact]
    public void HasUnsavedChanges_Includes_Extension_Toggles()
    {
        var viewModel = new SettingsViewModel(new WalkSettings
        {
            EnableRunner = true,
        }, "0.2.0");

        viewModel.HasUnsavedChanges.Should().BeFalse();

        viewModel.RunnerEnabled = false;

        viewModel.HasUnsavedChanges.Should().BeTrue();
    }

    [Fact]
    public void BuildSettings_Copies_Extension_Toggles()
    {
        var viewModel = new SettingsViewModel(new WalkSettings
        {
            EnableCalculator = true,
            EnableCurrencyConverter = true,
            EnableSystemCommands = true,
            EnableRunner = true,
            EnableFileSearch = true,
        }, "0.2.0");

        viewModel.CalculatorEnabled = false;
        viewModel.CurrencyConverterEnabled = false;
        viewModel.SystemCommandsEnabled = false;
        viewModel.RunnerEnabled = false;
        viewModel.FileSearchEnabled = false;

        var settings = viewModel.BuildSettings();

        settings.EnableCalculator.Should().BeFalse();
        settings.EnableCurrencyConverter.Should().BeFalse();
        settings.EnableSystemCommands.Should().BeFalse();
        settings.EnableRunner.Should().BeFalse();
        settings.EnableFileSearch.Should().BeFalse();
    }

    [Fact]
    public void DisplayVersion_Shows_Dev_Mode_Label()
    {
        var viewModel = new SettingsViewModel(new WalkSettings(), AppVersionService.DevelopmentModeVersion);

        viewModel.DisplayVersion.Should().Be(AppVersionService.DevelopmentModeLabel);
    }
}
