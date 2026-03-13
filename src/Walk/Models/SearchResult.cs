using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Media;

namespace Walk.Models;

public sealed class SearchResult : ObservableObject
{
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public string PluginName { get; init; } = "";
    public double Score { get; init; }
    public required IReadOnlyList<SearchAction> Actions { get; init; }

    private ImageSource? _icon;

    public ImageSource? Icon
    {
        get => _icon;
        set => SetProperty(ref _icon, value);
    }
}
