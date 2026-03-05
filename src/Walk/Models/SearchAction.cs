namespace Walk.Models;

public sealed class SearchAction
{
    public required string Label { get; init; }
    public required Action Execute { get; init; }
    public string? KeyGesture { get; init; }
}
