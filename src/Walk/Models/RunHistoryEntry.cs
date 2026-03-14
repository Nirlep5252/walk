namespace Walk.Models;

public sealed class RunHistoryEntry
{
    public string Title { get; set; } = "";
    public string Command { get; set; } = "";
    public string? Subtitle { get; set; }
    public string Kind { get; set; } = "Command";
    public bool SupportsRunAsAdmin { get; set; }
    public string? FileLocationPath { get; set; }
    public string? LastQuery { get; set; }
    public int LaunchCount { get; set; }
    public DateTime LastUsedUtc { get; set; }

    public RunTarget ToRunTarget()
    {
        return new RunTarget
        {
            Title = Title,
            Command = Command,
            Subtitle = Subtitle,
            Kind = Kind,
            SupportsRunAsAdmin = SupportsRunAsAdmin,
            FileLocationPath = FileLocationPath,
        };
    }
}
