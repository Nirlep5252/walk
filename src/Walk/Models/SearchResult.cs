using System.Windows.Media;

namespace Walk.Models;

public sealed class SearchResult
{
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public ImageSource? Icon { get; init; }
    public string PluginName { get; init; } = "";
    public double Score { get; init; }
    public required IReadOnlyList<SearchAction> Actions { get; init; }
}
