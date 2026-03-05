using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Walk.Helpers;
using Walk.Models;

namespace Walk.Plugins;

public sealed partial class FileSearchPlugin : IQueryPlugin
{
    public string Name => "Files";
    public int Priority => 60;

    [GeneratedRegex(@"^[A-Za-z]:\\|^\\\\|^\\[A-Za-z]")]
    private static partial Regex PathPattern();

    public Task<IReadOnlyList<SearchResult>> QueryAsync(string query, CancellationToken ct)
    {
        var trimmed = query.Trim();
        if (string.IsNullOrEmpty(trimmed) || !PathPattern().IsMatch(trimmed))
            return Task.FromResult<IReadOnlyList<SearchResult>>([]);

        var results = new List<SearchResult>();

        try
        {
            string searchDir;
            string filter;

            if (Directory.Exists(trimmed))
            {
                searchDir = trimmed;
                filter = "*";
            }
            else
            {
                searchDir = Path.GetDirectoryName(trimmed) ?? trimmed;
                filter = Path.GetFileName(trimmed) + "*";
                if (!Directory.Exists(searchDir))
                    return Task.FromResult<IReadOnlyList<SearchResult>>([]);
            }

            var entries = Directory.EnumerateFileSystemEntries(searchDir, filter)
                .Take(20);

            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();

                var isDir = Directory.Exists(entry);
                var name = Path.GetFileName(entry);

                results.Add(new SearchResult
                {
                    Title = isDir ? $"\ud83d\udcc1 {name}" : name,
                    Subtitle = entry,
                    PluginName = Name,
                    Score = 0.7,
                    Actions =
                    [
                        new SearchAction
                        {
                            Label = "Open",
                            Execute = () => Process.Start(new ProcessStartInfo(entry) { UseShellExecute = true }),
                            KeyGesture = "Enter"
                        },
                        new SearchAction
                        {
                            Label = "Open Containing Folder",
                            Execute = () => ProcessHelper.OpenFileLocation(entry),
                            KeyGesture = "Ctrl+O"
                        },
                        new SearchAction
                        {
                            Label = "Copy Path",
                            Execute = () => System.Windows.Clipboard.SetText(entry),
                            KeyGesture = "Ctrl+C"
                        }
                    ]
                });
            }
        }
        catch
        {
            // Inaccessible directory
        }

        return Task.FromResult<IReadOnlyList<SearchResult>>(results);
    }
}
