using FluentAssertions;
using Walk.Models;
using Walk.Plugins;
using Walk.Services;
using Walk.ViewModels;

namespace Walk.Tests.ViewModels;

public class MainViewModelTests
{
    [Fact]
    public void ExecuteSelectedCommand_Does_Not_Throw_When_Action_Fails()
    {
        var plugin = new ThrowingPlugin();
        var viewModel = new MainViewModel(new QueryRouter([plugin]));
        viewModel.Show();
        viewModel.Results.Add(new SearchResult
        {
            Title = "Broken",
            PluginName = "Test",
            Score = 1.0,
            Actions =
            [
                new SearchAction
                {
                    Label = "Run",
                    Execute = () => throw new InvalidOperationException("boom"),
                }
            ]
        });
        viewModel.SelectedIndex = -1;
        viewModel.SelectedIndex = -1;
        viewModel.SelectedIndex = 0;

        var act = () => viewModel.ExecuteSelectedCommand.Execute(null);

        act.Should().NotThrow();
        viewModel.IsVisible.Should().BeTrue();
    }

    [Fact]
    public void VisibleActions_Tracks_Selected_Result_Actions()
    {
        var viewModel = new MainViewModel(new QueryRouter([]));
        viewModel.Results.Add(new SearchResult
        {
            Title = "Run",
            PluginName = "Test",
            Score = 1.0,
            Actions =
            [
                new SearchAction
                {
                    Label = "Run",
                    HintLabel = "Run",
                    Execute = () => { },
                    KeyGesture = "Enter",
                },
                new SearchAction
                {
                    Label = "Copy Path",
                    HintLabel = "Copy",
                    Execute = () => { },
                    KeyGesture = "Ctrl+C",
                    ClosesLauncher = false,
                }
            ]
        });

        viewModel.SelectedIndex = -1;
        viewModel.SelectedIndex = 0;

        viewModel.VisibleActions.Select(action => action.DisplayLabel)
            .Should()
            .ContainInOrder("Run", "Copy");
    }

    [Fact]
    public void TryExecuteSelectedAction_Leaves_Launcher_Open_For_NonClosing_Action()
    {
        var viewModel = new MainViewModel(new QueryRouter([]));
        var executed = false;
        viewModel.Show();
        viewModel.Results.Add(new SearchResult
        {
            Title = "Copy",
            PluginName = "Test",
            Score = 1.0,
            Actions =
            [
                new SearchAction
                {
                    Label = "Copy Path",
                    HintLabel = "Copy",
                    Execute = () => executed = true,
                    KeyGesture = "Ctrl+C",
                    ClosesLauncher = false,
                }
            ]
        });
        viewModel.SelectedIndex = 0;

        var handled = viewModel.TryExecuteSelectedAction("Ctrl+C");

        handled.Should().BeTrue();
        executed.Should().BeTrue();
        viewModel.IsVisible.Should().BeTrue();
    }

    [Fact]
    public async Task SearchAsync_Does_Not_Limit_Results_When_MaxResults_Is_Unbounded()
    {
        var plugin = new BulkResultPlugin(12);
        var viewModel = new MainViewModel(new QueryRouter([plugin]));

        viewModel.SearchText = "bulk";
        await WaitForAsync(
            () => viewModel.Results.Count == 12,
            TimeSpan.FromSeconds(2));

        viewModel.Results.Should().HaveCount(12);
    }

    [Fact]
    public async Task SearchAsync_Respects_Configured_MaxResults()
    {
        var plugin = new BulkResultPlugin(120);
        var viewModel = new MainViewModel(new QueryRouter([plugin]), maxResults: 80);

        viewModel.SearchText = "bulk";
        await WaitForAsync(
            () => viewModel.Results.Count == 80,
            TimeSpan.FromSeconds(2));

        viewModel.Results.Should().HaveCount(80);
    }

    [Fact]
    public async Task SearchAsync_Toggles_IsSearching_While_Query_Is_In_Flight()
    {
        var plugin = new DelayedPlugin(TimeSpan.FromMilliseconds(250));
        var viewModel = new MainViewModel(new QueryRouter([plugin]));

        viewModel.SearchText = "wait";

        await WaitForAsync(() => viewModel.IsSearching, TimeSpan.FromSeconds(2));
        viewModel.IsSearching.Should().BeTrue();

        await WaitForAsync(() => !viewModel.IsSearching, TimeSpan.FromSeconds(2));
        viewModel.IsSearching.Should().BeFalse();
    }

    [Fact]
    public async Task SearchAsync_Shows_Fast_Results_Before_Slower_Ones_Finish()
    {
        var fastPlugin = new NamedDelayPlugin("App", 0.8, TimeSpan.FromMilliseconds(20));
        var slowPlugin = new NamedDelayPlugin("File", 0.6, TimeSpan.FromMilliseconds(250));
        var viewModel = new MainViewModel(new QueryRouter([fastPlugin, slowPlugin]));

        viewModel.SearchText = "note";

        await WaitForAsync(() => viewModel.Results.Count == 1, TimeSpan.FromSeconds(2));
        viewModel.Results.Select(result => result.Title).Should().ContainSingle().Which.Should().Be("App");

        await WaitForAsync(() => viewModel.Results.Count == 2, TimeSpan.FromSeconds(2));
        viewModel.Results.Select(result => result.Title).Should().ContainInOrder("App", "File");
    }

    private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;

            await Task.Delay(25);
        }
    }

    private sealed class ThrowingPlugin : IQueryPlugin
    {
        public string Name => "Test";
        public int Priority => 1;

        public Task<IReadOnlyList<SearchResult>> QueryAsync(string query, CancellationToken ct)
        {
            return Task.FromResult<IReadOnlyList<SearchResult>>([]);
        }
    }

    private sealed class BulkResultPlugin(int count) : IQueryPlugin
    {
        public string Name => "Bulk";
        public int Priority => 1;

        public Task<IReadOnlyList<SearchResult>> QueryAsync(string query, CancellationToken ct)
        {
            IReadOnlyList<SearchResult> results = Enumerable.Range(1, count)
                .Select(index => new SearchResult
                {
                    Title = $"Result {index}",
                    PluginName = "Bulk",
                    Score = 1.0 - (index * 0.01),
                    Actions = []
                })
                .ToList();

            return Task.FromResult(results);
        }
    }

    private sealed class DelayedPlugin(TimeSpan delay) : IQueryPlugin
    {
        public string Name => "Delayed";
        public int Priority => 1;

        public async Task<IReadOnlyList<SearchResult>> QueryAsync(string query, CancellationToken ct)
        {
            await Task.Delay(delay, ct);
            return
            [
                new SearchResult
                {
                    Title = "Done",
                    PluginName = "Delayed",
                    Score = 0.9,
                    Actions = []
                }
            ];
        }
    }

    private sealed class NamedDelayPlugin(string title, double score, TimeSpan delay) : IQueryPlugin
    {
        public string Name => title;
        public int Priority => 1;

        public async Task<IReadOnlyList<SearchResult>> QueryAsync(string query, CancellationToken ct)
        {
            await Task.Delay(delay, ct);
            return
            [
                new SearchResult
                {
                    Title = title,
                    PluginName = title,
                    Score = score,
                    Actions = []
                }
            ];
        }
    }
}
