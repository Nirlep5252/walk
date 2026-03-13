using FluentAssertions;
using System.Windows.Input;
using Walk.Services;

namespace Walk.Tests.Services;

public class HotkeyServiceTests
{
    [Theory]
    [InlineData("Alt+Ctrl", "space", "Ctrl+Alt+Space")]
    [InlineData("Win+Shift", "f12", "Shift+Win+F12")]
    [InlineData("Ctrl", "A", "Ctrl+A")]
    public void TryParseHotkey_NormalizesSupportedCombinations(string modifiers, string key, string displayText)
    {
        var success = HotkeyService.TryParseHotkey(modifiers, key, out var modifierFlags, out var virtualKey, out var actualDisplayText);

        success.Should().BeTrue();
        modifierFlags.Should().NotBe(0);
        virtualKey.Should().NotBe(0);
        actualDisplayText.Should().Be(displayText);
    }

    [Theory]
    [InlineData("", "Space")]
    [InlineData("Ctrl+Ctrl", "A")]
    [InlineData("Ctrl+Magic", "A")]
    [InlineData("Ctrl", "Launch")]
    public void TryParseHotkey_RejectsInvalidCombinations(string modifiers, string key)
    {
        var success = HotkeyService.TryParseHotkey(modifiers, key, out _, out _, out var displayText);

        success.Should().BeFalse();
        displayText.Should().BeEmpty();
    }

    [Fact]
    public void CoerceMethods_FallBackToDefaults()
    {
        HotkeyService.CoerceModifiers("Magic").Should().Be(HotkeyService.DefaultModifiers);
        HotkeyService.CoerceKey("Launch").Should().Be(HotkeyService.DefaultKey);
    }

    [Fact]
    public void TryCreateHotkey_NormalizesRecordedShortcut()
    {
        var success = HotkeyService.TryCreateHotkey(
            ModifierKeys.Control | ModifierKeys.Shift,
            Key.K,
            out var modifiers,
            out var key,
            out var displayText,
            out var errorMessage);

        success.Should().BeTrue();
        modifiers.Should().Be("Ctrl+Shift");
        key.Should().Be("K");
        displayText.Should().Be("Ctrl+Shift+K");
        errorMessage.Should().BeEmpty();
    }

    [Fact]
    public void TryCreateHotkey_RejectsMissingModifier()
    {
        var success = HotkeyService.TryCreateHotkey(
            ModifierKeys.None,
            Key.Space,
            out _,
            out _,
            out _,
            out var errorMessage);

        success.Should().BeFalse();
        errorMessage.Should().Be("Include at least one modifier key in the shortcut.");
    }
}
