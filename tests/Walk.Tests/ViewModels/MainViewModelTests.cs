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
        await Task.Delay(150);

        viewModel.Results.Should().HaveCount(12);
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
}
