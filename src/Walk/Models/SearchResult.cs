using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Media;

namespace Walk.Models;

public sealed class SearchResult : ObservableObject
{
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public string PluginName { get; init; } = "";
    public double Score { get; set; }
    public required IReadOnlyList<SearchAction> Actions { get; init; }
    public string? IconGlyph { get; init; }

    private ImageSource? _icon;

    public ImageSource? Icon
    {
        get => _icon;
        set => SetProperty(ref _icon, value);
    }

    public string DisplayIconGlyph => string.IsNullOrWhiteSpace(IconGlyph)
        ? GetDefaultIconGlyph()
        : IconGlyph!;

    private string GetDefaultIconGlyph()
    {
        return PluginName switch
        {
            "Calculator" => "=",
            "Currency" => "$",
            "Files" => "\uD83D\uDCC4",
            "Run" => "\u25B6",
            "System" => "\u23FB",
            _ => PluginName.Length > 0 ? PluginName[..1].ToUpperInvariant() : "?",
        };
    }
}
