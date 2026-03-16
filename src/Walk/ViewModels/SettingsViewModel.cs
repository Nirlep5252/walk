using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows.Input;
using Walk.Services;

namespace Walk.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly WalkSettings _settings;
    private const string ResetHotkeyModifiers = "Alt";
    private const string ResetHotkeyKey = "Space";

    [ObservableProperty]
    private string _hotkeyModifiers;

    [ObservableProperty]
    private string _hotkeyKey;

    [ObservableProperty]
    private bool _isRecording;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private bool _autoStartOnLogin;

    [ObservableProperty]
    private bool _calculatorEnabled;

    [ObservableProperty]
    private bool _currencyConverterEnabled;

    [ObservableProperty]
    private bool _systemCommandsEnabled;

    [ObservableProperty]
    private bool _runnerEnabled;

    [ObservableProperty]
    private bool _fileSearchEnabled;

    public event Action? SaveRequested;
    public event Action? CancelRequested;

    public string DisplayVersion { get; }

    public string HotkeyPreview => HotkeyService.FormatDisplayText(HotkeyModifiers, HotkeyKey);

    public string HotkeyButtonText => IsRecording ? "Press Shortcut..." : HotkeyPreview;

    public bool HasUnsavedChanges =>
        HotkeyService.CoerceModifiers(_settings.HotkeyModifiers) != HotkeyService.CoerceModifiers(HotkeyModifiers) ||
        HotkeyService.CoerceKey(_settings.HotkeyKey) != HotkeyService.CoerceKey(HotkeyKey) ||
        _settings.StartWithWindows != AutoStartOnLogin ||
        _settings.EnableCalculator != CalculatorEnabled ||
        _settings.EnableCurrencyConverter != CurrencyConverterEnabled ||
        _settings.EnableSystemCommands != SystemCommandsEnabled ||
        _settings.EnableRunner != RunnerEnabled ||
        _settings.EnableFileSearch != FileSearchEnabled;

    public bool ShouldShowResetHotkey =>
        HotkeyService.CoerceModifiers(HotkeyModifiers) != ResetHotkeyModifiers ||
        HotkeyService.CoerceKey(HotkeyKey) != ResetHotkeyKey;

    public SettingsViewModel(WalkSettings settings, string displayVersion)
    {
        _settings = settings.Clone();
        DisplayVersion = AppVersionService.FormatSettingsLabel(displayVersion);
        _hotkeyModifiers = HotkeyService.CoerceModifiers(_settings.HotkeyModifiers);
        _hotkeyKey = HotkeyService.CoerceKey(_settings.HotkeyKey);
        _autoStartOnLogin = _settings.StartWithWindows;
        _calculatorEnabled = _settings.EnableCalculator;
        _currencyConverterEnabled = _settings.EnableCurrencyConverter;
        _systemCommandsEnabled = _settings.EnableSystemCommands;
        _runnerEnabled = _settings.EnableRunner;
        _fileSearchEnabled = _settings.EnableFileSearch;
    }

    public WalkSettings BuildSettings()
    {
        var updatedSettings = _settings.Clone();
        updatedSettings.HotkeyModifiers = HotkeyService.CoerceModifiers(HotkeyModifiers);
        updatedSettings.HotkeyKey = HotkeyService.CoerceKey(HotkeyKey);
        updatedSettings.StartWithWindows = AutoStartOnLogin;
        updatedSettings.EnableCalculator = CalculatorEnabled;
        updatedSettings.EnableCurrencyConverter = CurrencyConverterEnabled;
        updatedSettings.EnableSystemCommands = SystemCommandsEnabled;
        updatedSettings.EnableRunner = RunnerEnabled;
        updatedSettings.EnableFileSearch = FileSearchEnabled;
        return updatedSettings;
    }

    public void ApplyRecordedHotkey(ModifierKeys modifiers, Key key)
    {
        if (!IsRecording)
            return;

        if (HotkeyService.TryCreateHotkey(
            modifiers,
            key,
            out var normalizedModifiers,
            out var normalizedKey,
            out _,
            out var errorMessage))
        {
            HotkeyModifiers = normalizedModifiers;
            HotkeyKey = normalizedKey;
            StatusMessage = "";
        }
        else
        {
            StatusMessage = errorMessage;
        }

        IsRecording = false;
    }

    public void ApplyRecordedHotkey(string modifiers, string key)
    {
        if (!IsRecording)
            return;

        HotkeyModifiers = HotkeyService.CoerceModifiers(modifiers);
        HotkeyKey = HotkeyService.CoerceKey(key);
        StatusMessage = "";
        IsRecording = false;
    }

    public void CancelRecording()
    {
        if (!IsRecording)
            return;

        IsRecording = false;
        StatusMessage = "";
    }

    [RelayCommand]
    private void StartRecording()
    {
        if (IsRecording)
            return;

        IsRecording = true;
        StatusMessage = "Esc to cancel";
    }

    [RelayCommand(CanExecute = nameof(CanResetHotkey))]
    private void ResetHotkey()
    {
        if (IsRecording)
            return;

        HotkeyModifiers = ResetHotkeyModifiers;
        HotkeyKey = ResetHotkeyKey;
        StatusMessage = "";
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        SaveRequested?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        CancelRecording();
        CancelRequested?.Invoke();
    }

    partial void OnHotkeyModifiersChanged(string value)
    {
        NotifyStateChanged();
    }

    partial void OnHotkeyKeyChanged(string value)
    {
        NotifyStateChanged();
    }

    partial void OnIsRecordingChanged(bool value)
    {
        OnPropertyChanged(nameof(HotkeyButtonText));
        ResetHotkeyCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
    }

    partial void OnAutoStartOnLoginChanged(bool value)
    {
        NotifyStateChanged();
    }

    partial void OnCalculatorEnabledChanged(bool value)
    {
        NotifyStateChanged();
    }

    partial void OnCurrencyConverterEnabledChanged(bool value)
    {
        NotifyStateChanged();
    }

    partial void OnSystemCommandsEnabledChanged(bool value)
    {
        NotifyStateChanged();
    }

    partial void OnRunnerEnabledChanged(bool value)
    {
        NotifyStateChanged();
    }

    partial void OnFileSearchEnabledChanged(bool value)
    {
        NotifyStateChanged();
    }

    private bool CanSave()
    {
        return !IsRecording && HasUnsavedChanges;
    }

    private bool CanResetHotkey()
    {
        return !IsRecording &&
               (HotkeyService.CoerceModifiers(HotkeyModifiers) != ResetHotkeyModifiers ||
                HotkeyService.CoerceKey(HotkeyKey) != ResetHotkeyKey);
    }

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(HotkeyPreview));
        OnPropertyChanged(nameof(HotkeyButtonText));
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(ShouldShowResetHotkey));
        ResetHotkeyCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
    }
}
