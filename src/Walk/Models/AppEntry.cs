namespace Walk.Models;

public sealed class AppEntry
{
    public required string Name { get; init; }
    public required string ExecutablePath { get; init; }
    public string? IconPath { get; init; }
    public string? Arguments { get; init; }
    public string? WorkingDirectory { get; init; }
    public int LaunchCount { get; set; }
    public DateTime LastUsed { get; set; }
}
