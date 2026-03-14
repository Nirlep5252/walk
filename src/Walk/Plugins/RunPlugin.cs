using System.IO;
using Walk.Helpers;
using Walk.Models;
using Walk.Services;

namespace Walk.Plugins;

public sealed class RunPlugin : IQueryPlugin
{
    private readonly RunHistoryService _historyService;
    private readonly IRunTargetLauncher _launcher;

    private sealed record RunCandidate(RunTarget Target, double Score, string Source);

    private static readonly IReadOnlyList<RunTarget> Catalog =
    [
        new() { Title = "Command Prompt", Command = "cmd", Subtitle = "Open cmd.exe", Kind = "Command", SupportsRunAsAdmin = true },
        new() { Title = "PowerShell", Command = "powershell", Subtitle = "Open Windows PowerShell", Kind = "Command", SupportsRunAsAdmin = true },
        new() { Title = "PowerShell 7", Command = "pwsh", Subtitle = "Open PowerShell 7 if installed", Kind = "Command", SupportsRunAsAdmin = true },
        new() { Title = "Registry Editor", Command = "regedit", Subtitle = "Edit the Windows registry", Kind = "Command", SupportsRunAsAdmin = true },
        new() { Title = "Task Manager", Command = "taskmgr", Subtitle = "Open Task Manager", Kind = "Command", SupportsRunAsAdmin = true },
        new() { Title = "System Configuration", Command = "msconfig", Subtitle = "Open system startup and boot settings", Kind = "Command", SupportsRunAsAdmin = true },
        new() { Title = "Windows Terminal", Command = "wt", Subtitle = "Open Windows Terminal", Kind = "Command", SupportsRunAsAdmin = true },
        new() { Title = "File Explorer", Command = "explorer", Subtitle = "Open File Explorer", Kind = "Command" },
        new() { Title = "Control Panel", Command = "control", Subtitle = "Open classic Control Panel", Kind = "Command" },
        new() { Title = "Task Scheduler", Command = "taskschd.msc", Subtitle = "Schedule and inspect tasks", Kind = "MMC", SupportsRunAsAdmin = true },
        new() { Title = "Local Users and Groups", Command = "lusrmgr.msc", Subtitle = "Manage local users and groups", Kind = "MMC", SupportsRunAsAdmin = true },
        new() { Title = "Local Security Policy", Command = "secpol.msc", Subtitle = "Manage local security policies", Kind = "MMC", SupportsRunAsAdmin = true },
        new() { Title = "Performance Monitor", Command = "perfmon", Subtitle = "Open performance monitoring tools", Kind = "Command", SupportsRunAsAdmin = true },
        new() { Title = "Remote Desktop Connection", Command = "mstsc", Subtitle = "Connect to another PC with Remote Desktop", Kind = "Command" },
        new() { Title = "Character Map", Command = "charmap", Subtitle = "Browse special characters and symbols", Kind = "Command" },
        new() { Title = "System Information", Command = "msinfo32", Subtitle = "Inspect detailed system information", Kind = "Command" },
        new() { Title = "Programs and Features", Command = "appwiz.cpl", Subtitle = "Uninstall or change programs", Kind = "Control Panel" },
        new() { Title = "Internet Options", Command = "inetcpl.cpl", Subtitle = "Open Internet Options", Kind = "Control Panel" },
        new() { Title = "Sound", Command = "mmsys.cpl", Subtitle = "Open classic sound settings", Kind = "Control Panel" },
        new() { Title = "Date and Time", Command = "timedate.cpl", Subtitle = "Adjust date, time, and timezone", Kind = "Control Panel" },
        new() { Title = "Mouse Properties", Command = "main.cpl", Subtitle = "Open mouse settings", Kind = "Control Panel" },
        new() { Title = "System Properties", Command = "sysdm.cpl", Subtitle = "Open system properties", Kind = "Control Panel" },
        new() { Title = "Windows Firewall", Command = "firewall.cpl", Subtitle = "Open Windows Defender Firewall", Kind = "Control Panel" },
        new() { Title = "Services", Command = "services.msc", Subtitle = "Manage Windows services", Kind = "MMC", SupportsRunAsAdmin = true },
        new() { Title = "Device Manager", Command = "devmgmt.msc", Subtitle = "Manage hardware devices", Kind = "MMC", SupportsRunAsAdmin = true },
        new() { Title = "Disk Management", Command = "diskmgmt.msc", Subtitle = "Manage disks and partitions", Kind = "MMC", SupportsRunAsAdmin = true },
        new() { Title = "Computer Management", Command = "compmgmt.msc", Subtitle = "Open the Computer Management console", Kind = "MMC", SupportsRunAsAdmin = true },
        new() { Title = "Event Viewer", Command = "eventvwr.msc", Subtitle = "Inspect Windows event logs", Kind = "MMC", SupportsRunAsAdmin = true },
        new() { Title = "Network Connections", Command = "ncpa.cpl", Subtitle = "Manage adapters and network connections", Kind = "Control Panel" },
        new() { Title = "Startup Folder", Command = "shell:startup", Subtitle = "Open the current user's Startup folder", Kind = "Shell Folder" },
        new() { Title = "Apps Folder", Command = "shell:appsfolder", Subtitle = "Browse installed applications", Kind = "Shell Folder" },
        new() { Title = "Downloads Folder", Command = "shell:downloads", Subtitle = "Open the Downloads shell folder", Kind = "Shell Folder" },
        new() { Title = "Desktop Folder", Command = "shell:desktop", Subtitle = "Open the Desktop folder", Kind = "Shell Folder" },
        new() { Title = "Documents Folder", Command = "shell:personal", Subtitle = "Open the Documents folder", Kind = "Shell Folder" },
        new() { Title = "Pictures Folder", Command = "shell:my pictures", Subtitle = "Open the Pictures folder", Kind = "Shell Folder" },
        new() { Title = "Videos Folder", Command = "shell:my video", Subtitle = "Open the Videos folder", Kind = "Shell Folder" },
        new() { Title = "Fonts Folder", Command = "shell:fonts", Subtitle = "Open the Fonts folder", Kind = "Shell Folder" },
        new() { Title = "Windows Settings", Command = "ms-settings:", Subtitle = "Open the main Settings app", Kind = "Settings URI" },
        new() { Title = "Display Settings", Command = "ms-settings:display", Subtitle = "Open display settings", Kind = "Settings URI" },
        new() { Title = "Installed Apps", Command = "ms-settings:appsfeatures", Subtitle = "Open installed apps settings", Kind = "Settings URI" },
        new() { Title = "Windows Update", Command = "ms-settings:windowsupdate", Subtitle = "Open Windows Update settings", Kind = "Settings URI" },
        new() { Title = "Network Status", Command = "ms-settings:network-status", Subtitle = "Open network status settings", Kind = "Settings URI" },
        new() { Title = "Bluetooth Devices", Command = "ms-settings:bluetooth", Subtitle = "Open Bluetooth settings", Kind = "Settings URI" },
        new() { Title = "Default Apps", Command = "ms-settings:defaultapps", Subtitle = "Open default apps settings", Kind = "Settings URI" },
        new() { Title = "Storage", Command = "ms-settings:storagesense", Subtitle = "Open storage settings", Kind = "Settings URI" },
        new() { Title = "Sound Settings", Command = "ms-settings:sound", Subtitle = "Open sound settings", Kind = "Settings URI" },
        new() { Title = "Taskbar Settings", Command = "ms-settings:taskbar", Subtitle = "Open taskbar settings", Kind = "Settings URI" },
        new() { Title = "Personalization", Command = "ms-settings:personalization", Subtitle = "Open personalization settings", Kind = "Settings URI" },
    ];

    public RunPlugin(RunHistoryService historyService, IRunTargetLauncher? launcher = null)
    {
        _historyService = historyService;
        _launcher = launcher ?? new RunTargetLauncher();
    }

    public string Name => "Run";

    public int Priority => 75;

    public Task<IReadOnlyList<SearchResult>> QueryAsync(string query, CancellationToken ct)
    {
        var (isExplicitRunQuery, normalizedQuery) = NormalizeQuery(query);
        if (string.IsNullOrWhiteSpace(normalizedQuery) && !isExplicitRunQuery)
            return Task.FromResult<IReadOnlyList<SearchResult>>([]);

        var candidates = new List<RunCandidate>();

        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            AddDiscoveryCandidates(candidates);
        }
        else
        {
            AddHistoryCandidates(normalizedQuery, candidates, isExplicitRunQuery);
            AddCatalogCandidates(normalizedQuery, candidates, isExplicitRunQuery);

            var directTarget = TryCreateDirectTarget(normalizedQuery, isExplicitRunQuery);
            if (directTarget is not null)
            {
                candidates.Add(new RunCandidate(
                    directTarget,
                    isExplicitRunQuery ? 0.995 : 0.97,
                    "Direct"));
            }
        }

        IReadOnlyList<SearchResult> results = candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Target.Title, StringComparer.OrdinalIgnoreCase)
            .DistinctBy(candidate => $"{candidate.Target.Kind}|{candidate.Target.Command}", StringComparer.OrdinalIgnoreCase)
            .Select(candidate => CreateResult(normalizedQuery, candidate))
            .ToList();

        return Task.FromResult(results);
    }

    private void AddDiscoveryCandidates(ICollection<RunCandidate> candidates)
    {
        var historyEntries = _historyService.GetEntries()
            .OrderByDescending(entry => entry.LastUsedUtc)
            .ThenByDescending(entry => entry.LaunchCount)
            .Take(4)
            .ToList();

        for (int index = 0; index < historyEntries.Count; index++)
        {
            candidates.Add(new RunCandidate(
                historyEntries[index].ToRunTarget(),
                0.95 - (index * 0.01),
                "History"));
        }

        for (int index = 0; index < Catalog.Count; index++)
        {
            candidates.Add(new RunCandidate(Catalog[index], Math.Max(0.6, 0.78 - (index * 0.002)), "Catalog"));
        }
    }

    private void AddHistoryCandidates(string query, ICollection<RunCandidate> candidates, bool isExplicitRunQuery)
    {
        foreach (var entry in _historyService.GetEntries())
        {
            var matchScore = GetHistoryScore(query, entry);
            if (matchScore < 0.45)
                continue;

            candidates.Add(new RunCandidate(entry.ToRunTarget(), ApplyExplicitBoost(matchScore, isExplicitRunQuery), "History"));
        }
    }

    private void AddCatalogCandidates(string query, ICollection<RunCandidate> candidates, bool isExplicitRunQuery)
    {
        foreach (var target in Catalog)
        {
            var matchScore = GetCatalogScore(query, target);
            if (matchScore < 0.45)
                continue;

            candidates.Add(new RunCandidate(target, ApplyExplicitBoost(matchScore, isExplicitRunQuery), "Catalog"));
        }
    }

    private SearchResult CreateResult(string query, RunCandidate candidate)
    {
        var target = candidate.Target;
        var actions = new List<SearchAction>
        {
            new()
            {
                Label = "Run",
                HintLabel = "Run",
                Execute = () => ExecuteTarget(query, target, asAdmin: false),
                KeyGesture = "Enter",
            },
            new()
            {
                Label = "Copy Command",
                Execute = () => System.Windows.Clipboard.SetText(target.Command),
                KeyGesture = "Ctrl+C",
                HintLabel = "Copy",
                ClosesLauncher = false,
            }
        };

        if (target.SupportsRunAsAdmin)
        {
            actions.Insert(1, new SearchAction
            {
                Label = "Run as Administrator",
                HintLabel = "Admin",
                Execute = () => ExecuteTarget(query, target, asAdmin: true),
                KeyGesture = "Ctrl+Enter",
            });
        }

        if (!string.IsNullOrWhiteSpace(target.FileLocationPath) && File.Exists(target.FileLocationPath))
        {
            actions.Add(new SearchAction
            {
                Label = "Open File Location",
                HintLabel = "Reveal",
                Execute = () => _launcher.OpenFileLocation(target.FileLocationPath!),
                KeyGesture = "Ctrl+O",
            });
        }

        var subtitle = candidate.Source == "History"
            ? $"Recent {target.Kind.ToLowerInvariant()} - {target.Command}"
            : target.Subtitle ?? target.Command;

        var result = new SearchResult
        {
            Title = target.Title,
            Subtitle = subtitle,
            PluginName = Name,
            Score = candidate.Score,
            Actions = actions,
            IconGlyph = GetIconGlyph(target),
        };

        if (!string.IsNullOrWhiteSpace(target.FileLocationPath) && File.Exists(target.FileLocationPath))
            result.Icon = IconExtractor.GetIcon(target.FileLocationPath!);

        return result;
    }

    private void ExecuteTarget(string query, RunTarget target, bool asAdmin)
    {
        _launcher.Launch(target, asAdmin);
        _historyService.RecordLaunch(string.IsNullOrWhiteSpace(query) ? target.Command : query, target);
    }

    private static double GetCatalogScore(string query, RunTarget target)
    {
        var title = FuzzyMatcher.Match(query, target.Title);
        var command = FuzzyMatcher.Match(query, target.Command);
        var subtitle = string.IsNullOrWhiteSpace(target.Subtitle)
            ? new FuzzyMatchResult(false, 0.0)
            : FuzzyMatcher.Match(query, target.Subtitle!);

        var maxMatch = new[]
        {
            title.IsMatch ? title.Score : 0.0,
            command.IsMatch ? command.Score * 0.98 : 0.0,
            subtitle.IsMatch ? subtitle.Score * 0.85 : 0.0,
        }.Max();

        return maxMatch == 0.0 ? 0.0 : Math.Min(0.92, 0.48 + (maxMatch * 0.4));
    }

    private static double GetHistoryScore(string query, RunHistoryEntry entry)
    {
        var title = FuzzyMatcher.Match(query, entry.Title);
        var command = FuzzyMatcher.Match(query, entry.Command);
        var lastQuery = string.IsNullOrWhiteSpace(entry.LastQuery)
            ? new FuzzyMatchResult(false, 0.0)
            : FuzzyMatcher.Match(query, entry.LastQuery!);

        var baseMatch = new[]
        {
            title.IsMatch ? title.Score : 0.0,
            command.IsMatch ? command.Score * 0.98 : 0.0,
            lastQuery.IsMatch ? lastQuery.Score * 0.9 : 0.0,
        }.Max();

        if (baseMatch == 0.0)
            return 0.0;

        var usageBoost = Math.Min(0.12, entry.LaunchCount * 0.02);
        var ageHours = Math.Max(0.0, (DateTime.UtcNow - entry.LastUsedUtc).TotalHours);
        var recencyBoost = ageHours switch
        {
            <= 24 => 0.08,
            <= 24 * 7 => 0.04,
            _ => 0.0,
        };

        return Math.Min(0.99, 0.56 + (baseMatch * 0.24) + usageBoost + recencyBoost);
    }

    private static RunTarget? TryCreateDirectTarget(string query, bool allowArbitraryCommand)
    {
        if (TryNormalizeMistypedShellTarget(query, out var normalizedShellTarget))
        {
            return new RunTarget
            {
                Title = $"Run {normalizedShellTarget}",
                Command = normalizedShellTarget,
                Subtitle = $"Normalized from {query}",
                Kind = GetRunKind(normalizedShellTarget),
                SupportsRunAsAdmin = SupportsRunAsAdmin(normalizedShellTarget),
            };
        }

        if (IsUriOrShellTarget(query))
        {
            return new RunTarget
            {
                Title = $"Run {query}",
                Command = query,
                Subtitle = "Launch the exact target",
                Kind = GetRunKind(query),
                SupportsRunAsAdmin = false,
            };
        }

        var expanded = Environment.ExpandEnvironmentVariables(query);
        if (!expanded.Equals(query, StringComparison.Ordinal) && (Directory.Exists(expanded) || File.Exists(expanded)))
        {
            return CreatePathTarget(query, expanded);
        }

        if (Path.IsPathFullyQualified(query) && (Directory.Exists(query) || File.Exists(query)))
        {
            return CreatePathTarget(query, query);
        }

        if (allowArbitraryCommand)
        {
            return new RunTarget
            {
                Title = $"Run {query}",
                Command = query,
                Subtitle = "Launch the exact command",
                Kind = "Command",
                SupportsRunAsAdmin = SupportsRunAsAdminForExplicitQuery(query),
            };
        }

        return null;
    }

    private static (bool IsExplicitRunQuery, string Query) NormalizeQuery(string query)
    {
        var trimmed = query.Trim();

        if (trimmed.Equals(">", StringComparison.Ordinal))
            return (true, "");

        if (trimmed.StartsWith(">", StringComparison.Ordinal))
            return (true, trimmed[1..].Trim());

        if (trimmed.Equals("run", StringComparison.OrdinalIgnoreCase))
            return (true, "");

        if (trimmed.StartsWith("run ", StringComparison.OrdinalIgnoreCase))
            return (true, trimmed[4..].Trim());

        return (false, trimmed);
    }

    private static double ApplyExplicitBoost(double score, bool isExplicitRunQuery)
    {
        return isExplicitRunQuery
            ? Math.Min(0.995, score + 0.12)
            : score;
    }

    private static RunTarget CreatePathTarget(string originalQuery, string resolvedPath)
    {
        var isDirectory = Directory.Exists(resolvedPath);
        var isElevatable = !isDirectory && SupportsRunAsAdmin(resolvedPath);
        var directoryName = Path.GetFileName(resolvedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(directoryName))
            directoryName = resolvedPath;

        return new RunTarget
        {
            Title = isDirectory ? $"Open {directoryName}" : Path.GetFileName(resolvedPath),
            Command = resolvedPath,
            Subtitle = $"Expanded from {originalQuery}",
            Kind = isDirectory ? "Folder" : "File",
            SupportsRunAsAdmin = isElevatable,
            FileLocationPath = !isDirectory ? resolvedPath : null,
        };
    }

    private static bool IsUriOrShellTarget(string query)
    {
        if (query.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            return true;

        if (query.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase))
            return true;

        var colonIndex = query.IndexOf(':');
        return colonIndex > 1 &&
               !query.Contains(Path.DirectorySeparatorChar) &&
               !query.Contains(Path.AltDirectorySeparatorChar) &&
               !query.Contains(' ');
    }

    private static bool TryNormalizeMistypedShellTarget(string query, out string normalized)
    {
        normalized = "";

        if (!query.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            return false;

        var suffix = query["shell:".Length..].Trim();
        if (string.IsNullOrWhiteSpace(suffix) || suffix.Contains(' '))
            return false;

        if (!LooksLikeLaunchableToken(suffix))
            return false;

        normalized = suffix;
        return true;
    }

    private static bool LooksLikeLaunchableToken(string query)
    {
        if (query.Contains(Path.DirectorySeparatorChar) || query.Contains(Path.AltDirectorySeparatorChar))
            return false;

        var extension = Path.GetExtension(query);
        return extension.Equals(".cpl", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".msc", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".bat", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".com", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetRunKind(string query)
    {
        if (query.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            return "Shell Folder";

        if (query.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase))
            return "Settings URI";

        return "URI";
    }

    private static string GetIconGlyph(RunTarget target)
    {
        return target.Kind switch
        {
            "Command" => "\u2328",
            "Control Panel" => "\u2699",
            "File" => "\uD83D\uDCC4",
            "Folder" => "\uD83D\uDCC1",
            "MMC" => "\uD83D\uDEE0",
            "Settings URI" => "\u2699",
            "Shell Folder" => "\uD83D\uDCC1",
            _ => "\u2197",
        };
    }

    private static bool SupportsRunAsAdminForExplicitQuery(string query)
    {
        if (IsUriOrShellTarget(query))
            return false;

        if (Path.IsPathFullyQualified(query) && Directory.Exists(query))
            return false;

        return true;
    }

    private static bool SupportsRunAsAdmin(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".bat", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".com", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".msc", StringComparison.OrdinalIgnoreCase);
    }
}
