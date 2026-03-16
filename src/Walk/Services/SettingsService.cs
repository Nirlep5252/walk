using System.IO;
using System.Text.Json;

namespace Walk.Services;

public sealed class WalkSettings
{
    public string HotkeyModifiers { get; set; } = HotkeyService.DefaultModifiers;
    public string HotkeyKey { get; set; } = HotkeyService.DefaultKey;
    public string Theme { get; set; } = "Auto";
    public int CurrencyCacheTtlHours { get; set; } = 6;
    public bool StartWithWindows { get; set; } = true;
    public int MaxResults { get; set; } = 8;
    public bool EnableCalculator { get; set; } = true;
    public bool EnableCurrencyConverter { get; set; } = true;
    public bool EnableSystemCommands { get; set; } = true;
    public bool EnableRunner { get; set; } = true;
    public bool EnableFileSearch { get; set; } = true;

    public WalkSettings Clone()
    {
        return new WalkSettings
        {
            HotkeyModifiers = HotkeyModifiers,
            HotkeyKey = HotkeyKey,
            Theme = Theme,
            CurrencyCacheTtlHours = CurrencyCacheTtlHours,
            StartWithWindows = StartWithWindows,
            MaxResults = MaxResults,
            EnableCalculator = EnableCalculator,
            EnableCurrencyConverter = EnableCurrencyConverter,
            EnableSystemCommands = EnableSystemCommands,
            EnableRunner = EnableRunner,
            EnableFileSearch = EnableFileSearch,
        };
    }
}

public sealed class SettingsService
{
    private readonly string _settingsPath;

    public SettingsService(string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        _settingsPath = Path.Combine(dataDir, "settings.json");
    }

    public async Task<WalkSettings> LoadAsync()
    {
        if (!File.Exists(_settingsPath))
            return new WalkSettings();

        try
        {
            var json = await File.ReadAllTextAsync(_settingsPath);
            return JsonSerializer.Deserialize<WalkSettings>(json) ?? new WalkSettings();
        }
        catch
        {
            return new WalkSettings();
        }
    }

    public async Task SaveAsync(WalkSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_settingsPath, json);
    }
}
