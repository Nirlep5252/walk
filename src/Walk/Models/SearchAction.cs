namespace Walk.Models;

public sealed class SearchAction
{
    public required string Label { get; init; }
    public string? HintLabel { get; init; }
    public required Action Execute { get; init; }
    public string? KeyGesture { get; init; }
    public bool ClosesLauncher { get; init; } = true;

    public string DisplayLabel => string.IsNullOrWhiteSpace(HintLabel) ? Label : HintLabel!;

    public string? DisplayKeyGesture => FormatKeyGesture(KeyGesture);

    private static string? FormatKeyGesture(string? keyGesture)
    {
        if (string.IsNullOrWhiteSpace(keyGesture))
            return null;

        return keyGesture
            .Replace("Ctrl+", "Ctrl ", StringComparison.OrdinalIgnoreCase)
            .Replace("Enter", "\u21B5", StringComparison.OrdinalIgnoreCase);
    }
}
