using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Walk.Helpers;

public static class FileSearchQueryHelper
{
    public static bool ShouldUseFuzzySearch(string query)
    {
        var normalized = NormalizeForFuzzy(query);
        if (normalized.Length < 3)
            return false;

        return !ContainsExplicitSearchSyntax(query);
    }

    public static string BuildRegexPattern(string query)
    {
        var normalized = NormalizeForFuzzy(query);
        if (normalized.Length == 0)
            return "";

        var builder = new StringBuilder(normalized.Length * 4);
        for (int index = 0; index < normalized.Length; index++)
        {
            if (index > 0)
                builder.Append(".*");

            builder.Append(Regex.Escape(normalized[index].ToString()));
        }

        return builder.ToString();
    }

    public static double ScorePath(string query, string path)
    {
        if (string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(path))
            return 0.0;

        var fileName = Path.GetFileName(path);
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(path);
        var directory = Path.GetDirectoryName(path) ?? "";

        var fileNameScore = ScoreCandidate(query, fileName);
        var stemScore = ScoreCandidate(query, nameWithoutExtension);
        var fullPathScore = ScoreCandidate(query, path);
        var directoryScore = ScoreCandidate(query, directory);

        var bestScore = Math.Max(
            fileNameScore * 1.18,
            Math.Max(
                stemScore * 1.1,
                Math.Max(fullPathScore, directoryScore * 0.72)));

        if (path.Contains(query, StringComparison.OrdinalIgnoreCase))
            bestScore += 0.06;

        if (fileName.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            bestScore += 0.08;

        return Math.Min(1.0, bestScore);
    }

    private static double ScoreCandidate(string query, string candidate)
    {
        var match = FuzzyMatcher.Match(query, candidate);
        return match.IsMatch ? match.Score : 0.0;
    }

    private static bool ContainsExplicitSearchSyntax(string query)
    {
        return query.Contains('*') ||
               query.Contains('?') ||
               query.Contains(':') ||
               query.Contains('\\') ||
               query.Contains('/') ||
               query.Contains('"');
    }

    private static string NormalizeForFuzzy(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
                builder.Append(char.ToLowerInvariant(ch));
        }

        return builder.ToString();
    }
}
