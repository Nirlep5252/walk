# Walk — Windows App Launcher Kit: Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Build a Raycast-style Windows 11 launcher with app search, calculator, currency conversion, system commands, and file search — all in a native-looking WPF overlay.

**Architecture:** Plugin-based query system. A borderless WPF overlay window (Wpf.Ui FluentWindow with Mica backdrop) hosts a search bar. On each keystroke, a QueryRouter dispatches the input to all registered IQueryPlugin implementations in parallel. Results are merged by confidence/priority and displayed in a ListView. The app lives in the system tray and is activated via global Alt+Space hotkey.

**Tech Stack:** .NET 8, WPF, Wpf.Ui (WPF-UI 4.x), H.NotifyIcon.Wpf 2.x, NCalcSync 5.x, System.Text.Json, CommunityToolkit.Mvvm

---

## Task 1: Scaffold the Solution

**Files:**
- Create: `Walk.sln`
- Create: `src/Walk/Walk.csproj`
- Create: `tests/Walk.Tests/Walk.Tests.csproj`

**Step 1: Create solution and projects**

Run:
```bash
cd C:\Users\admin\Documents\claude\walk
dotnet new sln -n Walk
mkdir src\Walk
dotnet new wpf -n Walk -o src/Walk --framework net8.0
mkdir tests\Walk.Tests
dotnet new xunit -n Walk.Tests -o tests/Walk.Tests --framework net8.0
dotnet sln Walk.sln add src/Walk/Walk.csproj
dotnet sln Walk.sln add tests/Walk.Tests/Walk.Tests.csproj
dotnet add tests/Walk.Tests/Walk.Tests.csproj reference src/Walk/Walk.csproj
```

Expected: Solution with two projects, test project referencing main project.

**Step 2: Add NuGet packages to Walk project**

Run:
```bash
cd C:\Users\admin\Documents\claude\walk
dotnet add src/Walk/Walk.csproj package WPF-UI --version 4.2.0
dotnet add src/Walk/Walk.csproj package H.NotifyIcon.Wpf --version 2.4.1
dotnet add src/Walk/Walk.csproj package NCalcSync --version 5.11.0
dotnet add src/Walk/Walk.csproj package CommunityToolkit.Mvvm --version 8.4.0
```

**Step 3: Add test packages**

Run:
```bash
dotnet add tests/Walk.Tests/Walk.Tests.csproj package FluentAssertions
dotnet add tests/Walk.Tests/Walk.Tests.csproj package NSubstitute
```

**Step 4: Create directory structure**

Run:
```bash
cd C:\Users\admin\Documents\claude\walk\src\Walk
mkdir Models Services Plugins Helpers ViewModels Assets
```

**Step 5: Verify build**

Run:
```bash
cd C:\Users\admin\Documents\claude\walk
dotnet build Walk.sln
```

Expected: Build succeeded with 0 errors.

**Step 6: Commit**

```bash
git add -A
git commit -m "chore: scaffold Walk solution with WPF + test projects and NuGet packages"
```

---

## Task 2: Core Models

**Files:**
- Create: `src/Walk/Models/SearchResult.cs`
- Create: `src/Walk/Models/SearchAction.cs`
- Create: `src/Walk/Models/AppEntry.cs`
- Test: `tests/Walk.Tests/Models/SearchResultTests.cs`

**Step 1: Write the SearchAction model**

```csharp
// src/Walk/Models/SearchAction.cs
namespace Walk.Models;

public sealed class SearchAction
{
    public required string Label { get; init; }
    public required Action Execute { get; init; }
    public string? KeyGesture { get; init; }
}
```

**Step 2: Write the SearchResult model**

```csharp
// src/Walk/Models/SearchResult.cs
using System.Windows.Media;

namespace Walk.Models;

public sealed class SearchResult
{
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public ImageSource? Icon { get; init; }
    public string PluginName { get; init; } = "";
    public double Score { get; init; }
    public required IReadOnlyList<SearchAction> Actions { get; init; }
}
```

**Step 3: Write the AppEntry model**

```csharp
// src/Walk/Models/AppEntry.cs
namespace Walk.Models;

public sealed class AppEntry
{
    public required string Name { get; init; }
    public required string ExecutablePath { get; init; }
    public string? IconPath { get; init; }
    public string? Arguments { get; init; }
    public int LaunchCount { get; set; }
    public DateTime LastUsed { get; set; }
}
```

**Step 4: Write tests for SearchResult**

```csharp
// tests/Walk.Tests/Models/SearchResultTests.cs
using FluentAssertions;
using Walk.Models;

namespace Walk.Tests.Models;

public class SearchResultTests
{
    [Fact]
    public void SearchResult_Should_Store_All_Properties()
    {
        var action = new SearchAction
        {
            Label = "Run",
            Execute = () => { }
        };

        var result = new SearchResult
        {
            Title = "Notepad",
            Subtitle = "C:\\Windows\\notepad.exe",
            PluginName = "Apps",
            Score = 1.0,
            Actions = [action]
        };

        result.Title.Should().Be("Notepad");
        result.Subtitle.Should().Be("C:\\Windows\\notepad.exe");
        result.PluginName.Should().Be("Apps");
        result.Score.Should().Be(1.0);
        result.Actions.Should().HaveCount(1);
        result.Actions[0].Label.Should().Be("Run");
    }

    [Fact]
    public void SearchResult_Icon_Defaults_To_Null()
    {
        var result = new SearchResult
        {
            Title = "Test",
            Actions = []
        };

        result.Icon.Should().BeNull();
    }
}
```

**Step 5: Run tests**

Run:
```bash
cd C:\Users\admin\Documents\claude\walk
dotnet test tests/Walk.Tests/Walk.Tests.csproj --filter "FullyQualifiedName~SearchResultTests"
```

Expected: 2 tests passed.

**Step 6: Commit**

```bash
git add -A
git commit -m "feat: add core models — SearchResult, SearchAction, AppEntry"
```

---

## Task 3: Plugin Interface and QueryRouter

**Files:**
- Create: `src/Walk/Plugins/IQueryPlugin.cs`
- Create: `src/Walk/Services/QueryRouter.cs`
- Test: `tests/Walk.Tests/Services/QueryRouterTests.cs`

**Step 1: Write the IQueryPlugin interface**

```csharp
// src/Walk/Plugins/IQueryPlugin.cs
using Walk.Models;

namespace Walk.Plugins;

public interface IQueryPlugin
{
    string Name { get; }
    int Priority { get; }
    Task<IReadOnlyList<SearchResult>> QueryAsync(string query, CancellationToken ct);
}
```

**Step 2: Write the QueryRouter**

```csharp
// src/Walk/Services/QueryRouter.cs
using Walk.Models;
using Walk.Plugins;

namespace Walk.Services;

public sealed class QueryRouter
{
    private readonly IReadOnlyList<IQueryPlugin> _plugins;

    public QueryRouter(IEnumerable<IQueryPlugin> plugins)
    {
        _plugins = plugins.OrderByDescending(p => p.Priority).ToList();
    }

    public async Task<IReadOnlyList<SearchResult>> RouteAsync(string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var tasks = _plugins.Select(p => SafeQuery(p, query, ct));
        var results = await Task.WhenAll(tasks);

        return results
            .SelectMany(r => r)
            .OrderByDescending(r => r.Score)
            .ToList();
    }

    private static async Task<IReadOnlyList<SearchResult>> SafeQuery(
        IQueryPlugin plugin, string query, CancellationToken ct)
    {
        try
        {
            return await plugin.QueryAsync(query, ct);
        }
        catch (OperationCanceledException)
        {
            return [];
        }
        catch
        {
            return [];
        }
    }
}
```

**Step 3: Write QueryRouter tests**

```csharp
// tests/Walk.Tests/Services/QueryRouterTests.cs
using FluentAssertions;
using NSubstitute;
using Walk.Models;
using Walk.Plugins;
using Walk.Services;

namespace Walk.Tests.Services;

public class QueryRouterTests
{
    [Fact]
    public async Task RouteAsync_Returns_Empty_For_Empty_Query()
    {
        var router = new QueryRouter([]);
        var results = await router.RouteAsync("", CancellationToken.None);
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task RouteAsync_Returns_Empty_For_Whitespace_Query()
    {
        var router = new QueryRouter([]);
        var results = await router.RouteAsync("   ", CancellationToken.None);
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task RouteAsync_Merges_Results_From_Multiple_Plugins()
    {
        var plugin1 = Substitute.For<IQueryPlugin>();
        plugin1.Name.Returns("Plugin1");
        plugin1.Priority.Returns(1);
        plugin1.QueryAsync("test", Arg.Any<CancellationToken>())
            .Returns([new SearchResult { Title = "Result1", Score = 0.5, Actions = [] }]);

        var plugin2 = Substitute.For<IQueryPlugin>();
        plugin2.Name.Returns("Plugin2");
        plugin2.Priority.Returns(2);
        plugin2.QueryAsync("test", Arg.Any<CancellationToken>())
            .Returns([new SearchResult { Title = "Result2", Score = 0.9, Actions = [] }]);

        var router = new QueryRouter([plugin1, plugin2]);
        var results = await router.RouteAsync("test", CancellationToken.None);

        results.Should().HaveCount(2);
        results[0].Title.Should().Be("Result2"); // higher score first
        results[1].Title.Should().Be("Result1");
    }

    [Fact]
    public async Task RouteAsync_Handles_Plugin_Exception_Gracefully()
    {
        var faultyPlugin = Substitute.For<IQueryPlugin>();
        faultyPlugin.Name.Returns("Faulty");
        faultyPlugin.Priority.Returns(1);
        faultyPlugin.QueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<SearchResult>>(_ => throw new InvalidOperationException("boom"));

        var goodPlugin = Substitute.For<IQueryPlugin>();
        goodPlugin.Name.Returns("Good");
        goodPlugin.Priority.Returns(2);
        goodPlugin.QueryAsync("test", Arg.Any<CancellationToken>())
            .Returns([new SearchResult { Title = "GoodResult", Score = 1.0, Actions = [] }]);

        var router = new QueryRouter([faultyPlugin, goodPlugin]);
        var results = await router.RouteAsync("test", CancellationToken.None);

        results.Should().HaveCount(1);
        results[0].Title.Should().Be("GoodResult");
    }

    [Fact]
    public async Task RouteAsync_Respects_Cancellation()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var slowPlugin = Substitute.For<IQueryPlugin>();
        slowPlugin.Name.Returns("Slow");
        slowPlugin.Priority.Returns(1);
        slowPlugin.QueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<SearchResult>>(callInfo =>
            {
                callInfo.Arg<CancellationToken>().ThrowIfCancellationRequested();
                return [];
            });

        var router = new QueryRouter([slowPlugin]);
        var results = await router.RouteAsync("test", cts.Token);

        results.Should().BeEmpty();
    }
}
```

**Step 4: Run tests**

Run:
```bash
cd C:\Users\admin\Documents\claude\walk
dotnet test tests/Walk.Tests/Walk.Tests.csproj --filter "FullyQualifiedName~QueryRouterTests"
```

Expected: 5 tests passed.

**Step 5: Commit**

```bash
git add -A
git commit -m "feat: add IQueryPlugin interface and QueryRouter with parallel dispatch"
```

---

## Task 4: FuzzyMatcher Helper

**Files:**
- Create: `src/Walk/Helpers/FuzzyMatcher.cs`
- Test: `tests/Walk.Tests/Helpers/FuzzyMatcherTests.cs`

**Step 1: Write the FuzzyMatcher tests**

```csharp
// tests/Walk.Tests/Helpers/FuzzyMatcherTests.cs
using FluentAssertions;
using Walk.Helpers;

namespace Walk.Tests.Helpers;

public class FuzzyMatcherTests
{
    [Theory]
    [InlineData("notepad", "Notepad", true)]
    [InlineData("note", "Notepad", true)]
    [InlineData("np", "Notepad", true)]       // subsequence match: N...p
    [InlineData("ntp", "Notepad", true)]       // subsequence match: N.t.p
    [InlineData("chrome", "Google Chrome", true)]
    [InlineData("gc", "Google Chrome", true)]  // subsequence: G...C
    [InlineData("xyz", "Notepad", false)]
    [InlineData("", "Notepad", true)]          // empty query matches everything
    [InlineData("notepadx", "Notepad", false)] // longer than target
    public void Match_Returns_Expected_Result(string query, string target, bool shouldMatch)
    {
        var result = FuzzyMatcher.Match(query, target);
        result.IsMatch.Should().Be(shouldMatch);
    }

    [Fact]
    public void Match_Scores_Exact_Match_Higher_Than_Subsequence()
    {
        var exact = FuzzyMatcher.Match("notepad", "Notepad");
        var subsequence = FuzzyMatcher.Match("np", "Notepad");

        exact.Score.Should().BeGreaterThan(subsequence.Score);
    }

    [Fact]
    public void Match_Scores_Prefix_Higher_Than_Substring()
    {
        var prefix = FuzzyMatcher.Match("note", "Notepad");
        var substring = FuzzyMatcher.Match("tepa", "Notepad");

        prefix.Score.Should().BeGreaterThan(substring.Score);
    }

    [Fact]
    public void Match_Is_Case_Insensitive()
    {
        var lower = FuzzyMatcher.Match("notepad", "Notepad");
        var upper = FuzzyMatcher.Match("NOTEPAD", "Notepad");

        lower.IsMatch.Should().BeTrue();
        upper.IsMatch.Should().BeTrue();
        lower.Score.Should().Be(upper.Score);
    }
}
```

**Step 2: Run tests to verify they fail**

Run:
```bash
cd C:\Users\admin\Documents\claude\walk
dotnet test tests/Walk.Tests/Walk.Tests.csproj --filter "FullyQualifiedName~FuzzyMatcherTests"
```

Expected: FAIL — FuzzyMatcher doesn't exist yet.

**Step 3: Implement FuzzyMatcher**

```csharp
// src/Walk/Helpers/FuzzyMatcher.cs
namespace Walk.Helpers;

public readonly record struct FuzzyMatchResult(bool IsMatch, double Score);

public static class FuzzyMatcher
{
    public static FuzzyMatchResult Match(string query, string target)
    {
        if (string.IsNullOrEmpty(query))
            return new FuzzyMatchResult(true, 0.0);

        var queryLower = query.ToLowerInvariant();
        var targetLower = target.ToLowerInvariant();

        // Exact match
        if (queryLower == targetLower)
            return new FuzzyMatchResult(true, 1.0);

        // Prefix match
        if (targetLower.StartsWith(queryLower))
            return new FuzzyMatchResult(true, 0.9 + (0.1 * query.Length / target.Length));

        // Contains (substring) match
        if (targetLower.Contains(queryLower))
            return new FuzzyMatchResult(true, 0.6 + (0.1 * query.Length / target.Length));

        // Subsequence match: every char in query appears in target in order
        int qi = 0;
        int consecutiveBonus = 0;
        int lastMatchIndex = -2;

        for (int ti = 0; ti < targetLower.Length && qi < queryLower.Length; ti++)
        {
            if (targetLower[ti] == queryLower[qi])
            {
                if (ti == lastMatchIndex + 1)
                    consecutiveBonus++;
                lastMatchIndex = ti;
                qi++;
            }
        }

        if (qi == queryLower.Length)
        {
            double baseScore = 0.3 * query.Length / target.Length;
            double bonus = 0.1 * consecutiveBonus / query.Length;
            return new FuzzyMatchResult(true, Math.Min(0.59, baseScore + bonus));
        }

        return new FuzzyMatchResult(false, 0.0);
    }
}
```

**Step 4: Run tests to verify they pass**

Run:
```bash
cd C:\Users\admin\Documents\claude\walk
dotnet test tests/Walk.Tests/Walk.Tests.csproj --filter "FullyQualifiedName~FuzzyMatcherTests"
```

Expected: All tests pass.

**Step 5: Commit**

```bash
git add -A
git commit -m "feat: add FuzzyMatcher with scoring for exact, prefix, substring, and subsequence matches"
```

---

## Task 5: Calculator Plugin

**Files:**
- Create: `src/Walk/Plugins/CalculatorPlugin.cs`
- Test: `tests/Walk.Tests/Plugins/CalculatorPluginTests.cs`

**Step 1: Write calculator tests**

```csharp
// tests/Walk.Tests/Plugins/CalculatorPluginTests.cs
using FluentAssertions;
using Walk.Plugins;

namespace Walk.Tests.Plugins;

public class CalculatorPluginTests
{
    private readonly CalculatorPlugin _plugin = new();

    [Theory]
    [InlineData("2+2", "4")]
    [InlineData("10 * 5", "50")]
    [InlineData("100 / 4", "25")]
    [InlineData("2 ^ 10", "1024")]
    [InlineData("(3 + 4) * 2", "14")]
    [InlineData("Sqrt(144)", "12")]
    [InlineData("Sin(0)", "0")]
    [InlineData("10 % 3", "1")]
    public async Task QueryAsync_Evaluates_Valid_Expressions(string input, string expected)
    {
        var results = await _plugin.QueryAsync(input, CancellationToken.None);
        results.Should().HaveCount(1);
        results[0].Title.Should().Be($"= {expected}");
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("notepad")]
    [InlineData("")]
    [InlineData("C:\\Users")]
    [InlineData("100 USD to EUR")]
    public async Task QueryAsync_Returns_Empty_For_Non_Math(string input)
    {
        var results = await _plugin.QueryAsync(input, CancellationToken.None);
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task QueryAsync_Handles_Division_By_Zero()
    {
        var results = await _plugin.QueryAsync("1/0", CancellationToken.None);
        // Either returns Infinity or empty — should not throw
        results.Should().NotBeNull();
    }

    [Fact]
    public void Priority_Should_Be_High()
    {
        _plugin.Priority.Should().BeGreaterOrEqualTo(80);
    }
}
```

**Step 2: Run tests to verify they fail**

Run:
```bash
cd C:\Users\admin\Documents\claude\walk
dotnet test tests/Walk.Tests/Walk.Tests.csproj --filter "FullyQualifiedName~CalculatorPluginTests"
```

Expected: FAIL — CalculatorPlugin doesn't exist.

**Step 3: Implement CalculatorPlugin**

```csharp
// src/Walk/Plugins/CalculatorPlugin.cs
using System.Text.RegularExpressions;
using NCalc;
using Walk.Models;

namespace Walk.Plugins;

public sealed partial class CalculatorPlugin : IQueryPlugin
{
    public string Name => "Calculator";
    public int Priority => 80;

    // Matches strings that look like math expressions
    [GeneratedRegex(@"^[\d\s+\-*/^().,%]+$|^.*\b(sqrt|sin|cos|tan|abs|log|ln|pow|round|ceiling|floor|exp|pi|e)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex MathPattern();

    public Task<IReadOnlyList<SearchResult>> QueryAsync(string query, CancellationToken ct)
    {
        var trimmed = query.Trim();
        if (string.IsNullOrEmpty(trimmed) || !MathPattern().IsMatch(trimmed))
            return Task.FromResult<IReadOnlyList<SearchResult>>([]);

        try
        {
            // NCalc uses ** for power but users type ^, so replace
            var expression = new Expression(trimmed.Replace('^', ' ' ).Replace("^", "**"));
            // Actually, NCalc uses Pow() — let's just replace ^ with Pow notation
            // Simpler: NCalc handles ^ natively as XOR, so remap it
            var normalized = Regex.Replace(trimmed, @"(\d+)\s*\^\s*(\d+)", "Pow($1,$2)");
            expression = new Expression(normalized);

            var result = expression.Evaluate();
            if (result is null)
                return Task.FromResult<IReadOnlyList<SearchResult>>([]);

            // Format: remove trailing zeros for decimals
            var formatted = result is double d
                ? d.ToString("G")
                : result.ToString()!;

            var copyAction = new SearchAction
            {
                Label = "Copy to Clipboard",
                Execute = () => System.Windows.Clipboard.SetText(formatted),
                KeyGesture = "Enter"
            };

            IReadOnlyList<SearchResult> results =
            [
                new SearchResult
                {
                    Title = $"= {formatted}",
                    Subtitle = trimmed,
                    PluginName = Name,
                    Score = 0.95,
                    Actions = [copyAction]
                }
            ];
            return Task.FromResult(results);
        }
        catch
        {
            return Task.FromResult<IReadOnlyList<SearchResult>>([]);
        }
    }
}
```

**Step 4: Run tests to verify they pass**

Run:
```bash
cd C:\Users\admin\Documents\claude\walk
dotnet test tests/Walk.Tests/Walk.Tests.csproj --filter "FullyQualifiedName~CalculatorPluginTests"
```

Expected: All tests pass. (Note: some NCalc formatting details may need adjustment — fix any minor formatting mismatches.)

**Step 5: Commit**

```bash
git add -A
git commit -m "feat: add CalculatorPlugin with NCalc expression evaluation"
```

---

## Task 6: CacheService

**Files:**
- Create: `src/Walk/Services/CacheService.cs`
- Test: `tests/Walk.Tests/Services/CacheServiceTests.cs`

**Step 1: Write CacheService tests**

```csharp
// tests/Walk.Tests/Services/CacheServiceTests.cs
using FluentAssertions;
using Walk.Services;

namespace Walk.Tests.Services;

public class CacheServiceTests : IDisposable
{
    private readonly string _testDir;
    private readonly CacheService _cache;

    public CacheServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "walk_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _cache = new CacheService(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    [Fact]
    public async Task GetOrSetAsync_Returns_Fresh_Data_On_First_Call()
    {
        var result = await _cache.GetOrSetAsync("test.json", TimeSpan.FromHours(6),
            () => Task.FromResult(new TestData { Value = 42 }));

        result.Should().NotBeNull();
        result!.Value.Should().Be(42);
    }

    [Fact]
    public async Task GetOrSetAsync_Returns_Cached_Data_On_Second_Call()
    {
        int callCount = 0;
        Task<TestData> Factory() => Task.FromResult(new TestData { Value = ++callCount });

        await _cache.GetOrSetAsync("test.json", TimeSpan.FromHours(6), Factory);
        var result = await _cache.GetOrSetAsync("test.json", TimeSpan.FromHours(6), Factory);

        result!.Value.Should().Be(1); // factory called only once
    }

    [Fact]
    public async Task GetOrSetAsync_Refreshes_When_TTL_Expired()
    {
        int callCount = 0;
        Task<TestData> Factory() => Task.FromResult(new TestData { Value = ++callCount });

        await _cache.GetOrSetAsync("test.json", TimeSpan.Zero, Factory); // TTL=0 means always expired
        var result = await _cache.GetOrSetAsync("test.json", TimeSpan.Zero, Factory);

        result!.Value.Should().Be(2); // factory called twice
    }

    private class TestData
    {
        public int Value { get; set; }
    }
}
```

**Step 2: Run tests to verify they fail**

Run:
```bash
cd C:\Users\admin\Documents\claude\walk
dotnet test tests/Walk.Tests/Walk.Tests.csproj --filter "FullyQualifiedName~CacheServiceTests"
```

Expected: FAIL — CacheService doesn't exist.

**Step 3: Implement CacheService**

```csharp
// src/Walk/Services/CacheService.cs
using System.Text.Json;

namespace Walk.Services;

public sealed class CacheService
{
    private readonly string _cacheDir;

    public CacheService(string cacheDir)
    {
        _cacheDir = cacheDir;
        Directory.CreateDirectory(_cacheDir);
    }

    public async Task<T?> GetOrSetAsync<T>(string fileName, TimeSpan ttl, Func<Task<T>> factory)
        where T : class
    {
        var filePath = Path.Combine(_cacheDir, fileName);

        // Try read from cache
        if (File.Exists(filePath))
        {
            var cacheEntry = await ReadCacheEntry<T>(filePath);
            if (cacheEntry is not null && DateTime.UtcNow - cacheEntry.FetchedAt < ttl)
                return cacheEntry.Data;
        }

        // Fetch fresh data
        try
        {
            var data = await factory();
            await WriteCacheEntry(filePath, data);
            return data;
        }
        catch
        {
            // On failure, return stale cache if available
            var stale = await ReadCacheEntry<T>(filePath);
            return stale?.Data;
        }
    }

    private static async Task<CacheEntry<T>?> ReadCacheEntry<T>(string path) where T : class
    {
        try
        {
            var json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<CacheEntry<T>>(json);
        }
        catch
        {
            return null;
        }
    }

    private static async Task WriteCacheEntry<T>(string path, T data) where T : class
    {
        var entry = new CacheEntry<T> { Data = data, FetchedAt = DateTime.UtcNow };
        var json = JsonSerializer.Serialize(entry, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json);
    }

    private sealed class CacheEntry<T> where T : class
    {
        public T? Data { get; set; }
        public DateTime FetchedAt { get; set; }
    }
}
```

**Step 4: Run tests to verify they pass**

Run:
```bash
cd C:\Users\admin\Documents\claude\walk
dotnet test tests/Walk.Tests/Walk.Tests.csproj --filter "FullyQualifiedName~CacheServiceTests"
```

Expected: All tests pass.

**Step 5: Commit**

```bash
git add -A
git commit -m "feat: add CacheService with file-based JSON caching and TTL support"
```

---

## Task 7: Currency Plugin

**Files:**
- Create: `src/Walk/Plugins/CurrencyPlugin.cs`
- Test: `tests/Walk.Tests/Plugins/CurrencyPluginTests.cs`

**Step 1: Write currency plugin tests**

```csharp
// tests/Walk.Tests/Plugins/CurrencyPluginTests.cs
using FluentAssertions;
using NSubstitute;
using Walk.Plugins;
using Walk.Services;

namespace Walk.Tests.Plugins;

public class CurrencyPluginTests : IDisposable
{
    private readonly string _testDir;
    private readonly CacheService _cache;

    public CurrencyPluginTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "walk_currency_test_" + Guid.NewGuid().ToString("N"));
        _cache = new CacheService(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    [Theory]
    [InlineData("100 USD to EUR")]
    [InlineData("50 eur in gbp")]
    [InlineData("1000 JPY to USD")]
    [InlineData("25.50 CAD to GBP")]
    public void ParseQuery_Should_Match_Valid_Currency_Patterns(string input)
    {
        CurrencyPlugin.TryParseQuery(input, out var amount, out var from, out var to)
            .Should().BeTrue();
        amount.Should().BeGreaterThan(0);
        from.Should().NotBeNullOrEmpty();
        to.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("2+2")]
    [InlineData("notepad")]
    [InlineData("100 USD")]         // missing target currency
    [InlineData("USD to EUR")]      // missing amount
    public void ParseQuery_Should_Not_Match_Invalid_Patterns(string input)
    {
        CurrencyPlugin.TryParseQuery(input, out _, out _, out _)
            .Should().BeFalse();
    }

    [Fact]
    public void Priority_Should_Be_High()
    {
        var plugin = new CurrencyPlugin(_cache, TimeSpan.FromHours(6));
        plugin.Priority.Should().BeGreaterOrEqualTo(85);
    }
}
```

**Step 2: Run tests to verify they fail**

Expected: FAIL — CurrencyPlugin doesn't exist.

**Step 3: Implement CurrencyPlugin**

```csharp
// src/Walk/Plugins/CurrencyPlugin.cs
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using Walk.Models;
using Walk.Services;

namespace Walk.Plugins;

public sealed partial class CurrencyPlugin : IQueryPlugin
{
    public string Name => "Currency";
    public int Priority => 85;

    private readonly CacheService _cache;
    private readonly TimeSpan _cacheTtl;
    private static readonly HttpClient HttpClient = new();

    [GeneratedRegex(@"^([\d.,]+)\s*([A-Za-z]{3})\s+(?:to|in)\s+([A-Za-z]{3})$",
        RegexOptions.IgnoreCase)]
    private static partial Regex CurrencyPattern();

    public CurrencyPlugin(CacheService cache, TimeSpan cacheTtl)
    {
        _cache = cache;
        _cacheTtl = cacheTtl;
    }

    public static bool TryParseQuery(string query, out decimal amount, out string from, out string to)
    {
        amount = 0;
        from = "";
        to = "";

        var match = CurrencyPattern().Match(query.Trim());
        if (!match.Success)
            return false;

        if (!decimal.TryParse(match.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out amount) || amount <= 0)
            return false;

        from = match.Groups[2].Value.ToUpperInvariant();
        to = match.Groups[3].Value.ToUpperInvariant();
        return true;
    }

    public async Task<IReadOnlyList<SearchResult>> QueryAsync(string query, CancellationToken ct)
    {
        if (!TryParseQuery(query, out var amount, out var from, out var to))
            return [];

        try
        {
            var rates = await _cache.GetOrSetAsync(
                $"currency_{from}.json",
                _cacheTtl,
                () => FetchRatesAsync(from, ct));

            if (rates is null || !rates.Rates.TryGetValue(to, out var rate))
                return [];

            var converted = amount * rate;
            var formatted = converted.ToString("N2", CultureInfo.InvariantCulture);
            var resultText = $"{amount} {from} = {formatted} {to}";

            return
            [
                new SearchResult
                {
                    Title = resultText,
                    Subtitle = $"Rate: 1 {from} = {rate:N6} {to}",
                    PluginName = Name,
                    Score = 0.95,
                    Actions =
                    [
                        new SearchAction
                        {
                            Label = "Copy Result",
                            Execute = () => System.Windows.Clipboard.SetText(formatted),
                            KeyGesture = "Enter"
                        },
                        new SearchAction
                        {
                            Label = "Swap Currencies",
                            Execute = () => { } // handled by ViewModel
                        }
                    ]
                }
            ];
        }
        catch
        {
            return [];
        }
    }

    private static async Task<ExchangeRateData> FetchRatesAsync(string baseCurrency, CancellationToken ct)
    {
        var url = $"https://open.er-api.com/v6/latest/{baseCurrency}";
        var json = await HttpClient.GetStringAsync(url, ct);
        var doc = JsonDocument.Parse(json);
        var ratesElement = doc.RootElement.GetProperty("rates");

        var rates = new Dictionary<string, decimal>();
        foreach (var prop in ratesElement.EnumerateObject())
        {
            rates[prop.Name] = prop.Value.GetDecimal();
        }

        return new ExchangeRateData { BaseCurrency = baseCurrency, Rates = rates };
    }

    public class ExchangeRateData
    {
        public string BaseCurrency { get; set; } = "";
        public Dictionary<string, decimal> Rates { get; set; } = new();
    }
}
```

**Step 4: Run tests to verify they pass**

Run:
```bash
cd C:\Users\admin\Documents\claude\walk
dotnet test tests/Walk.Tests/Walk.Tests.csproj --filter "FullyQualifiedName~CurrencyPluginTests"
```

Expected: All tests pass.

**Step 5: Commit**

```bash
git add -A
git commit -m "feat: add CurrencyPlugin with exchangerate-api.com integration and caching"
```

---

## Task 8: System Commands Plugin

**Files:**
- Create: `src/Walk/Plugins/SystemCommandPlugin.cs`
- Test: `tests/Walk.Tests/Plugins/SystemCommandPluginTests.cs`

**Step 1: Write system command tests**

```csharp
// tests/Walk.Tests/Plugins/SystemCommandPluginTests.cs
using FluentAssertions;
using Walk.Plugins;

namespace Walk.Tests.Plugins;

public class SystemCommandPluginTests
{
    private readonly SystemCommandPlugin _plugin = new();

    [Theory]
    [InlineData("shutdown")]
    [InlineData("restart")]
    [InlineData("sleep")]
    [InlineData("lock")]
    [InlineData("log off")]
    [InlineData("recycle bin")]
    [InlineData("settings")]
    public async Task QueryAsync_Finds_Known_Commands(string query)
    {
        var results = await _plugin.QueryAsync(query, CancellationToken.None);
        results.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("shut")]     // partial match for shutdown
    [InlineData("rest")]     // partial match for restart
    [InlineData("loc")]      // partial match for lock
    public async Task QueryAsync_Finds_Commands_By_Partial_Match(string query)
    {
        var results = await _plugin.QueryAsync(query, CancellationToken.None);
        results.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("2+2")]
    [InlineData("notepad")]
    [InlineData("xyz123")]
    public async Task QueryAsync_Returns_Empty_For_Non_Commands(string query)
    {
        var results = await _plugin.QueryAsync(query, CancellationToken.None);
        results.Should().BeEmpty();
    }
}
```

**Step 2: Run tests to verify they fail**

Expected: FAIL — SystemCommandPlugin doesn't exist.

**Step 3: Implement SystemCommandPlugin**

```csharp
// src/Walk/Plugins/SystemCommandPlugin.cs
using System.Diagnostics;
using System.Runtime.InteropServices;
using Walk.Helpers;
using Walk.Models;

namespace Walk.Plugins;

public sealed class SystemCommandPlugin : IQueryPlugin
{
    public string Name => "System";
    public int Priority => 70;

    private static readonly List<(string Name, string Description, Action Execute, bool NeedsConfirmation)> Commands =
    [
        ("Shutdown", "Shut down the computer", () => Process.Start("shutdown", "/s /t 0"), true),
        ("Restart", "Restart the computer", () => Process.Start("shutdown", "/r /t 0"), true),
        ("Sleep", "Put the computer to sleep", () => SetSuspendState(false, true, true), false),
        ("Lock", "Lock the workstation", () => LockWorkStation(), false),
        ("Log Off", "Sign out of the current session", () => Process.Start("shutdown", "/l"), true),
        ("Empty Recycle Bin", "Empty the Recycle Bin", () => SHEmptyRecycleBin(IntPtr.Zero, null, 0x07), false),
        ("Open Settings", "Open Windows Settings", () => Process.Start(new ProcessStartInfo("ms-settings:") { UseShellExecute = true }), false),
    ];

    [DllImport("user32.dll")]
    private static extern bool LockWorkStation();

    [DllImport("PowrProf.dll", CharSet = CharSet.Auto)]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    [DllImport("Shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, int dwFlags);

    public Task<IReadOnlyList<SearchResult>> QueryAsync(string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Task.FromResult<IReadOnlyList<SearchResult>>([]);

        var results = new List<SearchResult>();

        foreach (var (name, description, execute, needsConfirmation) in Commands)
        {
            var match = FuzzyMatcher.Match(query, name);
            if (!match.IsMatch || match.Score < 0.2)
                continue;

            results.Add(new SearchResult
            {
                Title = name,
                Subtitle = description,
                PluginName = Name,
                Score = match.Score * 0.85, // slightly lower than calculator/currency
                Actions =
                [
                    new SearchAction
                    {
                        Label = needsConfirmation ? "Execute (requires confirmation)" : "Execute",
                        Execute = execute,
                        KeyGesture = "Enter"
                    }
                ]
            });
        }

        return Task.FromResult<IReadOnlyList<SearchResult>>(results);
    }
}
```

**Step 4: Run tests to verify they pass**

Run:
```bash
cd C:\Users\admin\Documents\claude\walk
dotnet test tests/Walk.Tests/Walk.Tests.csproj --filter "FullyQualifiedName~SystemCommandPluginTests"
```

Expected: All tests pass.

**Step 5: Commit**

```bash
git add -A
git commit -m "feat: add SystemCommandPlugin with shutdown, restart, sleep, lock, and more"
```

---

## Task 9: ProcessHelper and IconExtractor

**Files:**
- Create: `src/Walk/Helpers/ProcessHelper.cs`
- Create: `src/Walk/Helpers/IconExtractor.cs`
- Test: `tests/Walk.Tests/Helpers/ProcessHelperTests.cs`

**Step 1: Write ProcessHelper tests**

```csharp
// tests/Walk.Tests/Helpers/ProcessHelperTests.cs
using FluentAssertions;
using Walk.Helpers;

namespace Walk.Tests.Helpers;

public class ProcessHelperTests
{
    [Fact]
    public void Launch_Should_Not_Throw_For_Valid_Executable()
    {
        // We can't truly launch in tests, but we can verify the method exists and handles paths
        var act = () => ProcessHelper.Launch("notepad.exe", asAdmin: false);
        // This will actually launch notepad — we need to kill it
        act.Should().NotThrow();
    }

    [Fact]
    public void OpenFileLocation_Should_Not_Throw_For_Valid_Path()
    {
        var act = () => ProcessHelper.OpenFileLocation(@"C:\Windows\notepad.exe");
        act.Should().NotThrow();
    }
}
```

**Step 2: Implement ProcessHelper**

```csharp
// src/Walk/Helpers/ProcessHelper.cs
using System.Diagnostics;

namespace Walk.Helpers;

public static class ProcessHelper
{
    public static void Launch(string path, bool asAdmin, string? arguments = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = path,
            Arguments = arguments ?? "",
            UseShellExecute = true,
        };

        if (asAdmin)
            startInfo.Verb = "runas";

        Process.Start(startInfo);
    }

    public static void OpenFileLocation(string filePath)
    {
        Process.Start("explorer.exe", $"/select,\"{filePath}\"");
    }
}
```

**Step 3: Implement IconExtractor**

```csharp
// src/Walk/Helpers/IconExtractor.cs
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Walk.Helpers;

public static class IconExtractor
{
    public static ImageSource? GetIcon(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return null;

            using var icon = Icon.ExtractAssociatedIcon(filePath);
            if (icon is null)
                return null;

            return Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
        }
        catch
        {
            return null;
        }
    }
}
```

**Step 4: Verify build compiles**

Run:
```bash
cd C:\Users\admin\Documents\claude\walk
dotnet build Walk.sln
```

Expected: Build succeeded.

**Step 5: Commit**

```bash
git add -A
git commit -m "feat: add ProcessHelper for launching apps and IconExtractor for exe/lnk icons"
```

---

## Task 10: AppIndexService and AppSearchPlugin

**Files:**
- Create: `src/Walk/Services/AppIndexService.cs`
- Create: `src/Walk/Plugins/AppSearchPlugin.cs`
- Test: `tests/Walk.Tests/Plugins/AppSearchPluginTests.cs`

**Step 1: Write AppIndexService**

```csharp
// src/Walk/Services/AppIndexService.cs
using System.IO;
using System.Text.Json;
using IWshRuntimeLibrary;
using Walk.Models;

namespace Walk.Services;

public sealed class AppIndexService : IDisposable
{
    private readonly string _indexPath;
    private List<AppEntry> _entries = [];
    private readonly List<FileSystemWatcher> _watchers = [];

    private static readonly string[] StartMenuPaths =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            @"Microsoft\Windows\Start Menu\Programs"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            @"Microsoft\Windows\Start Menu\Programs"),
    ];

    public AppIndexService(string dataDir)
    {
        _indexPath = Path.Combine(dataDir, "appindex.json");
    }

    public IReadOnlyList<AppEntry> Entries => _entries;

    public async Task BuildIndexAsync()
    {
        var entries = new List<AppEntry>();

        // Index Start Menu shortcuts
        foreach (var dir in StartMenuPaths)
        {
            if (!Directory.Exists(dir))
                continue;

            foreach (var lnk in Directory.EnumerateFiles(dir, "*.lnk", SearchOption.AllDirectories))
            {
                var entry = ResolveShortcut(lnk);
                if (entry is not null)
                    entries.Add(entry);
            }
        }

        // Index PATH executables
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Directory.Exists(dir))
                continue;

            try
            {
                foreach (var exe in Directory.EnumerateFiles(dir, "*.exe"))
                {
                    var name = Path.GetFileNameWithoutExtension(exe);
                    if (!entries.Any(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    {
                        entries.Add(new AppEntry
                        {
                            Name = name,
                            ExecutablePath = exe,
                        });
                    }
                }
            }
            catch
            {
                // Skip inaccessible directories
            }
        }

        // Merge with existing usage data
        await MergeWithExistingIndex(entries);
        _entries = entries;
        await SaveIndexAsync();
    }

    public async Task RecordLaunchAsync(string executablePath)
    {
        var entry = _entries.FirstOrDefault(e =>
            e.ExecutablePath.Equals(executablePath, StringComparison.OrdinalIgnoreCase));
        if (entry is not null)
        {
            entry.LaunchCount++;
            entry.LastUsed = DateTime.UtcNow;
            await SaveIndexAsync();
        }
    }

    public void StartWatching()
    {
        foreach (var dir in StartMenuPaths)
        {
            if (!Directory.Exists(dir))
                continue;

            var watcher = new FileSystemWatcher(dir)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                EnableRaisingEvents = true
            };
            watcher.Created += async (_, _) => await BuildIndexAsync();
            watcher.Deleted += async (_, _) => await BuildIndexAsync();
            _watchers.Add(watcher);
        }
    }

    public void Dispose()
    {
        foreach (var w in _watchers)
            w.Dispose();
    }

    private static AppEntry? ResolveShortcut(string lnkPath)
    {
        try
        {
            var shell = new WshShell();
            var shortcut = (IWshShortcut)shell.CreateShortcut(lnkPath);
            var targetPath = shortcut.TargetPath;

            if (string.IsNullOrEmpty(targetPath) || !System.IO.File.Exists(targetPath))
                return null;

            return new AppEntry
            {
                Name = Path.GetFileNameWithoutExtension(lnkPath),
                ExecutablePath = targetPath,
                Arguments = shortcut.Arguments,
            };
        }
        catch
        {
            return null;
        }
    }

    private async Task MergeWithExistingIndex(List<AppEntry> newEntries)
    {
        if (!System.IO.File.Exists(_indexPath))
            return;

        try
        {
            var json = await System.IO.File.ReadAllTextAsync(_indexPath);
            var existing = JsonSerializer.Deserialize<List<AppEntry>>(json) ?? [];

            foreach (var newEntry in newEntries)
            {
                var old = existing.FirstOrDefault(e =>
                    e.ExecutablePath.Equals(newEntry.ExecutablePath, StringComparison.OrdinalIgnoreCase));
                if (old is not null)
                {
                    newEntry.LaunchCount = old.LaunchCount;
                    newEntry.LastUsed = old.LastUsed;
                }
            }
        }
        catch
        {
            // Corrupted index — start fresh
        }
    }

    private async Task SaveIndexAsync()
    {
        var json = JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true });
        await System.IO.File.WriteAllTextAsync(_indexPath, json);
    }
}
```

**Note:** The `IWshRuntimeLibrary` requires a COM reference. In the .csproj, add:
```xml
<ItemGroup>
    <COMReference Include="IWshRuntimeLibrary">
        <WrapperTool>tlbimp</WrapperTool>
        <VersionMinor>0</VersionMinor>
        <VersionMajor>1</VersionMajor>
        <Guid>F935DC20-1CF0-11D0-ADB9-00C04FD58A0B</Guid>
    </COMReference>
</ItemGroup>
```

Alternatively, use `Shell32` or P/Invoke to resolve .lnk files without COM. The implementor should choose the approach that compiles cleanest on .NET 8. A simpler alternative is to use `File.ReadAllBytes` and parse the .lnk binary format, or use the `ShellLink` NuGet package.

**Step 2: Write AppSearchPlugin**

```csharp
// src/Walk/Plugins/AppSearchPlugin.cs
using Walk.Helpers;
using Walk.Models;
using Walk.Services;

namespace Walk.Plugins;

public sealed class AppSearchPlugin : IQueryPlugin
{
    public string Name => "Apps";
    public int Priority => 50;

    private readonly AppIndexService _indexService;

    public AppSearchPlugin(AppIndexService indexService)
    {
        _indexService = indexService;
    }

    public Task<IReadOnlyList<SearchResult>> QueryAsync(string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Task.FromResult<IReadOnlyList<SearchResult>>([]);

        var results = new List<SearchResult>();

        foreach (var entry in _indexService.Entries)
        {
            ct.ThrowIfCancellationRequested();

            var match = FuzzyMatcher.Match(query, entry.Name);
            if (!match.IsMatch || match.Score < 0.1)
                continue;

            // Boost by usage frequency
            var usageBoost = Math.Min(0.1, entry.LaunchCount * 0.005);
            var finalScore = match.Score + usageBoost;

            results.Add(new SearchResult
            {
                Title = entry.Name,
                Subtitle = entry.ExecutablePath,
                Icon = IconExtractor.GetIcon(entry.ExecutablePath),
                PluginName = Name,
                Score = finalScore,
                Actions =
                [
                    new SearchAction
                    {
                        Label = "Run",
                        Execute = () =>
                        {
                            ProcessHelper.Launch(entry.ExecutablePath, asAdmin: false, entry.Arguments);
                            _ = _indexService.RecordLaunchAsync(entry.ExecutablePath);
                        },
                        KeyGesture = "Enter"
                    },
                    new SearchAction
                    {
                        Label = "Run as Administrator",
                        Execute = () =>
                        {
                            ProcessHelper.Launch(entry.ExecutablePath, asAdmin: true, entry.Arguments);
                            _ = _indexService.RecordLaunchAsync(entry.ExecutablePath);
                        },
                        KeyGesture = "Ctrl+Enter"
                    },
                    new SearchAction
                    {
                        Label = "Open File Location",
                        Execute = () => ProcessHelper.OpenFileLocation(entry.ExecutablePath),
                        KeyGesture = "Ctrl+O"
                    },
                ]
            });
        }

        IReadOnlyList<SearchResult> sorted = results.OrderByDescending(r => r.Score).Take(10).ToList();
        return Task.FromResult(sorted);
    }
}
```

**Step 3: Write AppSearchPlugin tests**

```csharp
// tests/Walk.Tests/Plugins/AppSearchPluginTests.cs
using FluentAssertions;
using Walk.Models;
using Walk.Plugins;
using Walk.Services;

namespace Walk.Tests.Plugins;

public class AppSearchPluginTests : IDisposable
{
    private readonly string _testDir;
    private readonly AppIndexService _indexService;
    private readonly AppSearchPlugin _plugin;

    public AppSearchPluginTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "walk_appsearch_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _indexService = new AppIndexService(_testDir);
        _plugin = new AppSearchPlugin(_indexService);
    }

    public void Dispose()
    {
        _indexService.Dispose();
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    [Fact]
    public async Task QueryAsync_Returns_Empty_For_Empty_Query()
    {
        var results = await _plugin.QueryAsync("", CancellationToken.None);
        results.Should().BeEmpty();
    }

    [Fact]
    public void Priority_Should_Be_Default()
    {
        _plugin.Priority.Should().Be(50);
    }

    [Fact]
    public void Name_Should_Be_Apps()
    {
        _plugin.Name.Should().Be("Apps");
    }
}
```

**Step 4: Run tests**

Run:
```bash
cd C:\Users\admin\Documents\claude\walk
dotnet test tests/Walk.Tests/Walk.Tests.csproj --filter "FullyQualifiedName~AppSearchPluginTests"
```

Expected: All tests pass.

**Step 5: Commit**

```bash
git add -A
git commit -m "feat: add AppIndexService and AppSearchPlugin with fuzzy search and usage tracking"
```

---

## Task 11: File Search Plugin

**Files:**
- Create: `src/Walk/Plugins/FileSearchPlugin.cs`
- Test: `tests/Walk.Tests/Plugins/FileSearchPluginTests.cs`

**Step 1: Write file search tests**

```csharp
// tests/Walk.Tests/Plugins/FileSearchPluginTests.cs
using FluentAssertions;
using Walk.Plugins;

namespace Walk.Tests.Plugins;

public class FileSearchPluginTests
{
    private readonly FileSearchPlugin _plugin = new();

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"C:\Users")]
    [InlineData(@"C:\Windows\System32")]
    [InlineData(@"\Users")]
    public async Task QueryAsync_Returns_Results_For_Valid_Paths(string query)
    {
        var results = await _plugin.QueryAsync(query, CancellationToken.None);
        results.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("notepad")]
    [InlineData("2+2")]
    [InlineData("100 USD to EUR")]
    public async Task QueryAsync_Returns_Empty_For_Non_Paths(string query)
    {
        var results = await _plugin.QueryAsync(query, CancellationToken.None);
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task QueryAsync_Returns_Empty_For_NonExistent_Path()
    {
        var results = await _plugin.QueryAsync(@"Z:\nonexistent\path", CancellationToken.None);
        results.Should().BeEmpty();
    }
}
```

**Step 2: Implement FileSearchPlugin**

```csharp
// src/Walk/Plugins/FileSearchPlugin.cs
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
                    Title = isDir ? $"📁 {name}" : name,
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
```

**Step 3: Run tests**

Run:
```bash
cd C:\Users\admin\Documents\claude\walk
dotnet test tests/Walk.Tests/Walk.Tests.csproj --filter "FullyQualifiedName~FileSearchPluginTests"
```

Expected: All tests pass.

**Step 4: Commit**

```bash
git add -A
git commit -m "feat: add FileSearchPlugin with directory browsing and file actions"
```

---

## Task 12: SettingsService

**Files:**
- Create: `src/Walk/Services/SettingsService.cs`
- Test: `tests/Walk.Tests/Services/SettingsServiceTests.cs`

**Step 1: Write SettingsService tests**

```csharp
// tests/Walk.Tests/Services/SettingsServiceTests.cs
using FluentAssertions;
using Walk.Services;

namespace Walk.Tests.Services;

public class SettingsServiceTests : IDisposable
{
    private readonly string _testDir;

    public SettingsServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "walk_settings_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    [Fact]
    public async Task Load_Returns_Defaults_When_No_File_Exists()
    {
        var service = new SettingsService(_testDir);
        var settings = await service.LoadAsync();

        settings.HotkeyModifiers.Should().Be("Alt");
        settings.HotkeyKey.Should().Be("Space");
        settings.CurrencyCacheTtlHours.Should().Be(6);
        settings.StartWithWindows.Should().BeTrue();
        settings.MaxResults.Should().Be(8);
    }

    [Fact]
    public async Task Save_And_Load_Round_Trips()
    {
        var service = new SettingsService(_testDir);
        var settings = await service.LoadAsync();
        settings.MaxResults = 15;
        settings.CurrencyCacheTtlHours = 12;

        await service.SaveAsync(settings);

        var reloaded = await service.LoadAsync();
        reloaded.MaxResults.Should().Be(15);
        reloaded.CurrencyCacheTtlHours.Should().Be(12);
    }
}
```

**Step 2: Implement SettingsService**

```csharp
// src/Walk/Services/SettingsService.cs
using System.IO;
using System.Text.Json;

namespace Walk.Services;

public sealed class WalkSettings
{
    public string HotkeyModifiers { get; set; } = "Alt";
    public string HotkeyKey { get; set; } = "Space";
    public string Theme { get; set; } = "Auto";
    public int CurrencyCacheTtlHours { get; set; } = 6;
    public bool StartWithWindows { get; set; } = true;
    public int MaxResults { get; set; } = 8;
}

public sealed class SettingsService
{
    private readonly string _settingsPath;

    public SettingsService(string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        _settingsPath = Path.Combine(dataDir, "settings.json");
    }

    public async Task<WalkSettings> LoadAsync()
    {
        if (!File.Exists(_settingsPath))
            return new WalkSettings();

        try
        {
            var json = await File.ReadAllTextAsync(_settingsPath);
            return JsonSerializer.Deserialize<WalkSettings>(json) ?? new WalkSettings();
        }
        catch
        {
            return new WalkSettings();
        }
    }

    public async Task SaveAsync(WalkSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_settingsPath, json);
    }
}
```

**Step 3: Run tests**

Run:
```bash
cd C:\Users\admin\Documents\claude\walk
dotnet test tests/Walk.Tests/Walk.Tests.csproj --filter "FullyQualifiedName~SettingsServiceTests"
```

Expected: All tests pass.

**Step 4: Commit**

```bash
git add -A
git commit -m "feat: add SettingsService with JSON persistence and sensible defaults"
```

---

## Task 13: HotkeyService

**Files:**
- Create: `src/Walk/Services/HotkeyService.cs`

**Step 1: Implement HotkeyService**

```csharp
// src/Walk/Services/HotkeyService.cs
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Walk.Services;

public sealed class HotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID = 9000;

    private HwndSource? _source;
    private IntPtr _windowHandle;

    public event Action? HotkeyPressed;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // Modifier constants
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CTRL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;
    private const uint MOD_NOREPEAT = 0x4000;

    // VK_SPACE = 0x20
    private const uint VK_SPACE = 0x20;

    public bool Register(IntPtr windowHandle)
    {
        _windowHandle = windowHandle;
        _source = HwndSource.FromHwnd(windowHandle);
        _source?.AddHook(HwndHook);

        return RegisterHotKey(_windowHandle, HOTKEY_ID, MOD_ALT | MOD_NOREPEAT, VK_SPACE);
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            HotkeyPressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterHotKey(_windowHandle, HOTKEY_ID);
        _source?.RemoveHook(HwndHook);
    }
}
```

**Step 2: Verify build**

Run:
```bash
cd C:\Users\admin\Documents\claude\walk
dotnet build src/Walk/Walk.csproj
```

Expected: Build succeeded.

**Step 3: Commit**

```bash
git add -A
git commit -m "feat: add HotkeyService with Alt+Space global hotkey via Win32 RegisterHotKey"
```

---

## Task 14: MainViewModel

**Files:**
- Create: `src/Walk/ViewModels/MainViewModel.cs`

**Step 1: Implement MainViewModel**

```csharp
// src/Walk/ViewModels/MainViewModel.cs
using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Walk.Models;
using Walk.Services;

namespace Walk.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly QueryRouter _router;
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private int _selectedIndex;

    [ObservableProperty]
    private bool _isVisible;

    public ObservableCollection<SearchResult> Results { get; } = [];

    public MainViewModel(QueryRouter router)
    {
        _router = router;
    }

    partial void OnSearchTextChanged(string value)
    {
        _ = SearchAsync(value);
    }

    private async Task SearchAsync(string query)
    {
        // Cancel previous query
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        // Debounce
        try
        {
            await Task.Delay(100, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var results = await _router.RouteAsync(query, token);

        if (token.IsCancellationRequested)
            return;

        Results.Clear();
        foreach (var result in results.Take(8))
            Results.Add(result);

        SelectedIndex = Results.Count > 0 ? 0 : -1;
    }

    [RelayCommand]
    private void ExecuteSelected()
    {
        if (SelectedIndex >= 0 && SelectedIndex < Results.Count)
        {
            var result = Results[SelectedIndex];
            if (result.Actions.Count > 0)
                result.Actions[0].Execute();

            Hide();
        }
    }

    [RelayCommand]
    private void ExecuteAsAdmin()
    {
        if (SelectedIndex >= 0 && SelectedIndex < Results.Count)
        {
            var result = Results[SelectedIndex];
            var adminAction = result.Actions.FirstOrDefault(a => a.Label.Contains("Admin"));
            adminAction?.Execute();
            Hide();
        }
    }

    public void Show()
    {
        SearchText = "";
        Results.Clear();
        IsVisible = true;
    }

    public void Hide()
    {
        IsVisible = false;
    }

    public void Toggle()
    {
        if (IsVisible) Hide();
        else Show();
    }
}
```

**Step 2: Verify build**

Run:
```bash
cd C:\Users\admin\Documents\claude\walk
dotnet build src/Walk/Walk.csproj
```

Expected: Build succeeded.

**Step 3: Commit**

```bash
git add -A
git commit -m "feat: add MainViewModel with search dispatching, debouncing, and cancellation"
```

---

## Task 15: MainWindow UI (XAML)

**Files:**
- Modify: `src/Walk/MainWindow.xaml`
- Modify: `src/Walk/MainWindow.xaml.cs`

**Step 1: Write MainWindow.xaml**

Replace the default MainWindow.xaml with a borderless Wpf.Ui FluentWindow:

```xml
<!-- src/Walk/MainWindow.xaml -->
<ui:FluentWindow
    x:Class="Walk.MainWindow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
    Title="Walk"
    Width="650"
    SizeToContent="Height"
    MaxHeight="600"
    WindowStartupLocation="CenterScreen"
    WindowStyle="None"
    AllowsTransparency="True"
    Background="Transparent"
    ShowInTaskbar="False"
    Topmost="True"
    ResizeMode="NoResize"
    ExtendsContentIntoTitleBar="True"
    WindowBackdropType="Mica">

    <Border CornerRadius="8"
            Background="{DynamicResource ApplicationBackgroundBrush}"
            Padding="0"
            Margin="16"
            BorderThickness="1"
            BorderBrush="{DynamicResource ControlElevationBorderBrush}">
        <Border.Effect>
            <DropShadowEffect BlurRadius="16" ShadowDepth="2" Opacity="0.3" />
        </Border.Effect>

        <StackPanel>
            <!-- Search Bar -->
            <ui:TextBox
                x:Name="SearchBox"
                PlaceholderText="Search apps, calculate, convert..."
                FontSize="20"
                Margin="16,16,16,8"
                Text="{Binding SearchText, UpdateSourceTrigger=PropertyChanged}"
                Icon="{ui:SymbolIcon Search24}" />

            <!-- Results List -->
            <ListBox
                x:Name="ResultsList"
                ItemsSource="{Binding Results}"
                SelectedIndex="{Binding SelectedIndex}"
                Margin="8,0,8,8"
                MaxHeight="400"
                BorderThickness="0"
                Background="Transparent"
                ScrollViewer.HorizontalScrollBarVisibility="Disabled"
                Visibility="{Binding Results.Count, Converter={StaticResource CountToVisibilityConverter}}">

                <ListBox.ItemTemplate>
                    <DataTemplate>
                        <Grid Margin="8,6">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="32" />
                                <ColumnDefinition Width="*" />
                                <ColumnDefinition Width="Auto" />
                            </Grid.ColumnDefinitions>

                            <!-- Icon -->
                            <Image Grid.Column="0"
                                   Source="{Binding Icon}"
                                   Width="24" Height="24"
                                   VerticalAlignment="Center" />

                            <!-- Title + Subtitle -->
                            <StackPanel Grid.Column="1" Margin="8,0">
                                <TextBlock Text="{Binding Title}"
                                           FontSize="14"
                                           FontWeight="SemiBold"
                                           TextTrimming="CharacterEllipsis" />
                                <TextBlock Text="{Binding Subtitle}"
                                           FontSize="11"
                                           Opacity="0.6"
                                           TextTrimming="CharacterEllipsis" />
                            </StackPanel>

                            <!-- Keyboard hint -->
                            <TextBlock Grid.Column="2"
                                       Text="{Binding Actions[0].KeyGesture}"
                                       FontSize="11"
                                       Opacity="0.4"
                                       VerticalAlignment="Center" />
                        </Grid>
                    </DataTemplate>
                </ListBox.ItemTemplate>
            </ListBox>
        </StackPanel>
    </Border>
</ui:FluentWindow>
```

**Note for implementor:** The exact Wpf.Ui XAML namespace and FluentWindow API may need minor adjustments based on the installed version. Check the Wpf.Ui docs at https://wpfui.lepo.co/ for the correct namespace and control names. The `CountToVisibilityConverter` needs to be added as a resource — a simple IValueConverter that returns `Visible` when count > 0, `Collapsed` otherwise.

**Step 2: Write MainWindow.xaml.cs**

```csharp
// src/Walk/MainWindow.xaml.cs
using System.Windows;
using System.Windows.Input;
using Walk.ViewModels;

namespace Walk;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = _viewModel;
        InitializeComponent();

        Deactivated += (_, _) => _viewModel.Hide();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                _viewModel.Hide();
                e.Handled = true;
                break;

            case Key.Enter when Keyboard.Modifiers == ModifierKeys.Control:
                _viewModel.ExecuteAsAdminCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Enter:
                _viewModel.ExecuteSelectedCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Down:
                if (_viewModel.SelectedIndex < _viewModel.Results.Count - 1)
                    _viewModel.SelectedIndex++;
                e.Handled = true;
                break;

            case Key.Up:
                if (_viewModel.SelectedIndex > 0)
                    _viewModel.SelectedIndex--;
                e.Handled = true;
                break;
        }

        base.OnPreviewKeyDown(e);
    }

    public void ShowLauncher()
    {
        _viewModel.Show();
        Show();
        Activate();
        SearchBox.Focus();
    }

    public void HideLauncher()
    {
        Hide();
    }
}
```

**Step 3: Verify build**

Run:
```bash
cd C:\Users\admin\Documents\claude\walk
dotnet build src/Walk/Walk.csproj
```

Expected: Build succeeded (may need minor XAML adjustments).

**Step 4: Commit**

```bash
git add -A
git commit -m "feat: add MainWindow with Fluent Design search bar, results list, and keyboard navigation"
```

---

## Task 16: App.xaml — Startup, Tray, and Wiring

**Files:**
- Modify: `src/Walk/App.xaml`
- Modify: `src/Walk/App.xaml.cs`

**Step 1: Update App.xaml for Wpf.Ui theming**

```xml
<!-- src/Walk/App.xaml -->
<Application
    x:Class="Walk.App"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
    ShutdownMode="OnExplicitShutdown">

    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ui:ThemesDictionary Theme="Dark" />
                <ui:ControlsDictionary />
            </ResourceDictionary.MergedDictionaries>

            <!-- CountToVisibilityConverter -->
            <local:CountToVisibilityConverter x:Key="CountToVisibilityConverter"
                xmlns:local="clr-namespace:Walk.Helpers" />
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

**Step 2: Create CountToVisibilityConverter**

```csharp
// src/Walk/Helpers/CountToVisibilityConverter.cs
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Walk.Helpers;

public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is int count && count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
```

**Step 3: Write App.xaml.cs with full startup wiring**

```csharp
// src/Walk/App.xaml.cs
using System.Windows;
using H.NotifyIcon;
using Walk.Plugins;
using Walk.Services;
using Walk.ViewModels;

namespace Walk;

public partial class App : Application
{
    private MainWindow? _mainWindow;
    private HotkeyService? _hotkeyService;
    private TaskbarIcon? _trayIcon;
    private AppIndexService? _indexService;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Walk");
        Directory.CreateDirectory(dataDir);

        // Settings
        var settingsService = new SettingsService(dataDir);
        var settings = await settingsService.LoadAsync();

        // Services
        var cacheService = new CacheService(dataDir);
        _indexService = new AppIndexService(dataDir);
        await _indexService.BuildIndexAsync();
        _indexService.StartWatching();

        // Plugins
        var plugins = new IQueryPlugin[]
        {
            new CalculatorPlugin(),
            new CurrencyPlugin(cacheService, TimeSpan.FromHours(settings.CurrencyCacheTtlHours)),
            new SystemCommandPlugin(),
            new FileSearchPlugin(),
            new AppSearchPlugin(_indexService),
        };

        var router = new QueryRouter(plugins);
        var viewModel = new MainViewModel(router);

        // Main window
        _mainWindow = new MainWindow(viewModel);

        // Bind visibility
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.IsVisible))
            {
                if (viewModel.IsVisible)
                    _mainWindow.ShowLauncher();
                else
                    _mainWindow.HideLauncher();
            }
        };

        // Hotkey
        _hotkeyService = new HotkeyService();
        // Need a window handle — show briefly then hide
        _mainWindow.Show();
        var handle = new System.Windows.Interop.WindowInteropHelper(_mainWindow).Handle;
        _mainWindow.Hide();

        if (!_hotkeyService.Register(handle))
        {
            MessageBox.Show(
                "Could not register Alt+Space hotkey. Another application may be using it.\nYou can change the hotkey in Settings.",
                "Walk", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        _hotkeyService.HotkeyPressed += () =>
        {
            Current.Dispatcher.Invoke(() => viewModel.Toggle());
        };

        // System tray
        SetupTray(viewModel);
    }

    private void SetupTray(MainViewModel viewModel)
    {
        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "Walk Launcher",
            ContextMenu = new System.Windows.Controls.ContextMenu
            {
                Items =
                {
                    CreateMenuItem("Show Launcher", () => viewModel.Show()),
                    new System.Windows.Controls.Separator(),
                    CreateMenuItem("Quit", () => Shutdown()),
                }
            }
        };

        _trayIcon.TrayMouseDoubleClick += (_, _) => viewModel.Show();
    }

    private static System.Windows.Controls.MenuItem CreateMenuItem(string header, Action action)
    {
        var item = new System.Windows.Controls.MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeyService?.Dispose();
        _indexService?.Dispose();
        _trayIcon?.Dispose();
        base.OnExit(e);
    }
}
```

**Step 4: Verify build**

Run:
```bash
cd C:\Users\admin\Documents\claude\walk
dotnet build src/Walk/Walk.csproj
```

Expected: Build succeeded.

**Step 5: Commit**

```bash
git add -A
git commit -m "feat: wire up App startup with tray icon, hotkey registration, and plugin initialization"
```

---

## Task 17: Position Window in Upper Third of Screen

**Files:**
- Modify: `src/Walk/MainWindow.xaml.cs`

**Step 1: Add window positioning logic**

In `MainWindow.xaml.cs`, update `ShowLauncher()`:

```csharp
public void ShowLauncher()
{
    _viewModel.Show();

    // Position in upper third of primary screen
    var screen = SystemParameters.WorkArea;
    Left = (screen.Width - Width) / 2 + screen.Left;
    Top = screen.Height * 0.2 + screen.Top;

    Show();
    Activate();
    SearchBox.Focus();
}
```

**Step 2: Verify build and run**

Run:
```bash
cd C:\Users\admin\Documents\claude\walk
dotnet build src/Walk/Walk.csproj
```

Expected: Build succeeded.

**Step 3: Commit**

```bash
git add -A
git commit -m "feat: position launcher window in upper third of screen"
```

---

## Task 18: Startup with Windows

**Files:**
- Modify: `src/Walk/App.xaml.cs`

**Step 1: Add auto-start registration via registry**

Add to `App.xaml.cs` in `OnStartup`:

```csharp
private static void ConfigureAutoStart(bool enable)
{
    const string keyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    const string valueName = "Walk";

    using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
    if (key is null) return;

    if (enable)
    {
        var exePath = Environment.ProcessPath;
        if (exePath is not null)
            key.SetValue(valueName, $"\"{exePath}\"");
    }
    else
    {
        key.DeleteValue(valueName, throwOnMissingValue: false);
    }
}
```

Call `ConfigureAutoStart(settings.StartWithWindows)` in `OnStartup`.

**Step 2: Verify build**

Run:
```bash
cd C:\Users\admin\Documents\claude\walk
dotnet build src/Walk/Walk.csproj
```

Expected: Build succeeded.

**Step 3: Commit**

```bash
git add -A
git commit -m "feat: add auto-start with Windows via registry Run key"
```

---

## Task 19: Integration Test — Full Pipeline Smoke Test

**Files:**
- Create: `tests/Walk.Tests/Integration/QueryPipelineTests.cs`

**Step 1: Write integration test**

```csharp
// tests/Walk.Tests/Integration/QueryPipelineTests.cs
using FluentAssertions;
using Walk.Plugins;
using Walk.Services;

namespace Walk.Tests.Integration;

public class QueryPipelineTests
{
    [Fact]
    public async Task Full_Pipeline_Returns_Calculator_Result_For_Math()
    {
        var plugins = new IQueryPlugin[] { new CalculatorPlugin() };
        var router = new QueryRouter(plugins);

        var results = await router.RouteAsync("2+2", CancellationToken.None);

        results.Should().NotBeEmpty();
        results[0].Title.Should().Contain("4");
    }

    [Fact]
    public async Task Full_Pipeline_Returns_System_Command_For_Lock()
    {
        var plugins = new IQueryPlugin[] { new SystemCommandPlugin() };
        var router = new QueryRouter(plugins);

        var results = await router.RouteAsync("lock", CancellationToken.None);

        results.Should().NotBeEmpty();
        results[0].Title.Should().Contain("Lock");
    }

    [Fact]
    public async Task Full_Pipeline_Multiple_Plugins_Merge_Results()
    {
        var testDir = Path.Combine(Path.GetTempPath(), "walk_integration_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDir);

        try
        {
            var cache = new CacheService(testDir);
            var plugins = new IQueryPlugin[]
            {
                new CalculatorPlugin(),
                new SystemCommandPlugin(),
                new FileSearchPlugin(),
                new CurrencyPlugin(cache, TimeSpan.FromHours(6)),
            };

            var router = new QueryRouter(plugins);

            // This should only match calculator
            var results = await router.RouteAsync("2+2", CancellationToken.None);
            results.Should().NotBeEmpty();
            results[0].PluginName.Should().Be("Calculator");
        }
        finally
        {
            Directory.Delete(testDir, true);
        }
    }
}
```

**Step 2: Run all tests**

Run:
```bash
cd C:\Users\admin\Documents\claude\walk
dotnet test Walk.sln -v normal
```

Expected: All tests pass.

**Step 3: Commit**

```bash
git add -A
git commit -m "test: add integration tests for full query pipeline"
```

---

## Task 20: Manual Testing and Polish

**Step 1: Run the application**

Run:
```bash
cd C:\Users\admin\Documents\claude\walk
dotnet run --project src/Walk/Walk.csproj
```

**Step 2: Manual test checklist**

- [ ] Alt+Space shows the launcher
- [ ] Typing "notepad" shows Notepad in results
- [ ] Pressing Enter launches the selected app
- [ ] Ctrl+Enter runs as admin (UAC prompt appears)
- [ ] Typing "2+2" shows "= 4"
- [ ] Typing "100 USD to EUR" shows a conversion result
- [ ] Typing "lock" shows the Lock system command
- [ ] Typing `C:\` shows files/folders
- [ ] Escape hides the window
- [ ] Clicking outside hides the window
- [ ] System tray icon appears
- [ ] Right-click tray shows context menu
- [ ] "Quit" from tray exits the app
- [ ] Dark/light theme follows system

**Step 3: Fix any issues found during manual testing**

Address any UI glitches, missing icons, layout issues, or crashes.

**Step 4: Final commit**

```bash
git add -A
git commit -m "chore: polish and fix issues from manual testing"
```

---

## Summary

| Task | Component | Tests |
|------|-----------|-------|
| 1 | Solution scaffold | Build verification |
| 2 | Core models | SearchResultTests |
| 3 | IQueryPlugin + QueryRouter | QueryRouterTests |
| 4 | FuzzyMatcher | FuzzyMatcherTests |
| 5 | CalculatorPlugin | CalculatorPluginTests |
| 6 | CacheService | CacheServiceTests |
| 7 | CurrencyPlugin | CurrencyPluginTests |
| 8 | SystemCommandPlugin | SystemCommandPluginTests |
| 9 | ProcessHelper + IconExtractor | ProcessHelperTests |
| 10 | AppIndexService + AppSearchPlugin | AppSearchPluginTests |
| 11 | FileSearchPlugin | FileSearchPluginTests |
| 12 | SettingsService | SettingsServiceTests |
| 13 | HotkeyService | Build verification |
| 14 | MainViewModel | Build verification |
| 15 | MainWindow XAML | Build verification |
| 16 | App.xaml wiring | Build verification |
| 17 | Window positioning | Build verification |
| 18 | Auto-start with Windows | Build verification |
| 19 | Integration tests | QueryPipelineTests |
| 20 | Manual testing + polish | Manual checklist |

**Total: 20 tasks, ~50 files, estimated 7 test classes with 25+ tests**
