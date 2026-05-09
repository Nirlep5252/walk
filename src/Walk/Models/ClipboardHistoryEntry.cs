namespace Walk.Models;

public enum ClipboardHistoryKind
{
    Text,
    Files,
    Image,
}

public sealed class ClipboardHistoryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public ClipboardHistoryKind Kind { get; set; }
    public string Title { get; set; } = "";
    public string? Text { get; set; }
    public List<string> FilePaths { get; set; } = [];
    public string? ImagePath { get; set; }
    public string? ImageHash { get; set; }
    public int ImageWidth { get; set; }
    public int ImageHeight { get; set; }
    public bool IsPinned { get; set; }
    public int CopyCount { get; set; }
    public int UseCount { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastCopiedUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastUsedUtc { get; set; } = DateTime.UtcNow;

    public string PreviewText => Kind switch
    {
        ClipboardHistoryKind.Text => Text ?? "",
        ClipboardHistoryKind.Files => string.Join(Environment.NewLine, FilePaths),
        ClipboardHistoryKind.Image => ImagePath ?? "",
        _ => "",
    };
}
