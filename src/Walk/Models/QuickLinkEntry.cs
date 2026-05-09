namespace Walk.Models;

public sealed class QuickLinkEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Alias { get; set; } = "";
    public string Target { get; set; } = "";
    public string? Description { get; set; }
    public bool IsBuiltIn { get; set; }
    public int UseCount { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastUsedUtc { get; set; } = DateTime.MinValue;

    public bool RequiresQuery =>
        Target.Contains("{query}", StringComparison.OrdinalIgnoreCase) ||
        Target.Contains("{queryRaw}", StringComparison.OrdinalIgnoreCase);
}
