using System.Text;
using System.Text.RegularExpressions;

namespace Walk.Helpers;

public static partial class ReleaseNotesFormatter
{
    public static string ToDisplayText(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return "No changelog details were provided for this release.";

        var normalized = markdown.Replace("\r\n", "\n");
        var lines = normalized.Split('\n');
        var builder = new StringBuilder();
        var previousWasBlank = true;
        var inCodeBlock = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            var trimmed = line.Trim();

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                inCodeBlock = !inCodeBlock;
                AppendBlankLine(builder, ref previousWasBlank);
                continue;
            }

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                AppendBlankLine(builder, ref previousWasBlank);
                continue;
            }

            string formatted;
            if (inCodeBlock)
            {
                formatted = line;
            }
            else
            {
                formatted = HeadingPrefixRegex().Replace(trimmed, string.Empty);
                formatted = BulletPrefixRegex().Replace(formatted, "\u2022 ");
                formatted = MarkdownLinkRegex().Replace(formatted, "$1");
                formatted = formatted.Replace("`", string.Empty, StringComparison.Ordinal);

                if (formatted is "---" or "***")
                {
                    AppendBlankLine(builder, ref previousWasBlank);
                    continue;
                }
            }

            builder.AppendLine(formatted);
            previousWasBlank = false;
        }

        return builder.ToString().Trim();
    }

    private static void AppendBlankLine(StringBuilder builder, ref bool previousWasBlank)
    {
        if (previousWasBlank)
            return;

        builder.AppendLine();
        previousWasBlank = true;
    }

    [GeneratedRegex(@"^\s{0,3}#{1,6}\s+")]
    private static partial Regex HeadingPrefixRegex();

    [GeneratedRegex(@"^\s*[-*+]\s+")]
    private static partial Regex BulletPrefixRegex();

    [GeneratedRegex(@"\[(.*?)\]\((.*?)\)")]
    private static partial Regex MarkdownLinkRegex();
}
