namespace Walk.Services;

public static class ReleaseInfo
{
    public const string AppId = "Walk";
    public const string Channel = "win";
    public const string RepositoryUrl = "https://github.com/Nirlep5252/walk";
    public const string RepositoryOwner = "Nirlep5252";
    public const string RepositoryName = "walk";

    public static string BuildReleaseNotesUrl(string version)
    {
        return $"https://raw.githubusercontent.com/{RepositoryOwner}/{RepositoryName}/v{version}/docs/releases/{version}.md";
    }
}
