namespace Walk.Models;

public sealed class RunTarget
{
    public required string Title { get; init; }
    public required string Command { get; init; }
    public string? Subtitle { get; init; }
    public string Kind { get; init; } = "Command";
    public bool SupportsRunAsAdmin { get; init; }
    public string? FileLocationPath { get; init; }
}
