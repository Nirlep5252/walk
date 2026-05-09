namespace Walk.Models;

public enum FavoriteKind
{
    App,
    Run,
    File,
    QuickLink,
    ClipboardText,
    ClipboardFiles,
    ClipboardImage,
    Currency,
    SystemCommand,
}

public sealed class FavoriteEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string Key { get; set; }
    public FavoriteKind Kind { get; set; }
    public required string Title { get; set; }
    public string? Subtitle { get; set; }
    public required string Target { get; set; }
    public string? Arguments { get; set; }
    public string? WorkingDirectory { get; set; }
    public string? RunKind { get; set; }
    public string? IconPath { get; set; }
    public int IconIndex { get; set; }
    public bool SupportsRunAsAdmin { get; set; }
    public int SortOrder { get; set; }
    public int UseCount { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastUsedUtc { get; set; } = DateTime.MinValue;
}
