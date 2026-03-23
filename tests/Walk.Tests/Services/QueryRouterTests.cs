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
        results[0].Title.Should().Be("Result2");
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

    [Fact]
    public async Task RouteAsync_Does_Not_Block_Caller_For_Synchronous_Plugins()
    {
        var blockingPlugin = Substitute.For<IQueryPlugin>();
        blockingPlugin.Name.Returns("Blocking");
        blockingPlugin.Priority.Returns(1);
        blockingPlugin.QueryAsync("test", Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<SearchResult>>(_ =>
            {
                Thread.Sleep(200);
                return [];
            });

        var router = new QueryRouter([blockingPlugin]);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var routeTask = router.RouteAsync("test", CancellationToken.None);

        stopwatch.ElapsedMilliseconds.Should().BeLessThan(100);
        await routeTask;
    }

    [Fact]
    public async Task UpdatePlugins_Replaces_Active_Plugin_Set()
    {
        var firstPlugin = Substitute.For<IQueryPlugin>();
        firstPlugin.Name.Returns("First");
        firstPlugin.Priority.Returns(1);
        firstPlugin.QueryAsync("test", Arg.Any<CancellationToken>())
            .Returns([new SearchResult { Title = "FirstResult", Score = 0.5, Actions = [] }]);

        var secondPlugin = Substitute.For<IQueryPlugin>();
        secondPlugin.Name.Returns("Second");
        secondPlugin.Priority.Returns(1);
        secondPlugin.QueryAsync("test", Arg.Any<CancellationToken>())
            .Returns([new SearchResult { Title = "SecondResult", Score = 0.9, Actions = [] }]);

        var router = new QueryRouter([firstPlugin]);
        router.UpdatePlugins([secondPlugin]);

        var results = await router.RouteAsync("test", CancellationToken.None);

        results.Should().ContainSingle();
        results[0].Title.Should().Be("SecondResult");
        await firstPlugin.DidNotReceive().QueryAsync("test", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RouteIncrementalAsync_Publishes_Results_As_Each_Plugin_Completes()
    {
        var fastPlugin = Substitute.For<IQueryPlugin>();
        fastPlugin.Name.Returns("Fast");
        fastPlugin.Priority.Returns(2);
        fastPlugin.QueryAsync("test", Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                await Task.Delay(20);
                return [new SearchResult { Title = "FastResult", Score = 0.6, Actions = [] }];
            });

        var slowPlugin = Substitute.For<IQueryPlugin>();
        slowPlugin.Name.Returns("Slow");
        slowPlugin.Priority.Returns(1);
        slowPlugin.QueryAsync("test", Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                await Task.Delay(200);
                return [new SearchResult { Title = "SlowResult", Score = 0.9, Actions = [] }];
            });

        var router = new QueryRouter([fastPlugin, slowPlugin]);
        var snapshots = new List<IReadOnlyList<SearchResult>>();

        await router.RouteIncrementalAsync(
            "test",
            results =>
            {
                snapshots.Add(results.ToList());
                return Task.CompletedTask;
            },
            CancellationToken.None);

        snapshots.Should().HaveCount(2);
        snapshots[0].Should().ContainSingle(result => result.Title == "FastResult");
        snapshots[1].Select(result => result.Title).Should().ContainInOrder("SlowResult", "FastResult");
    }

    [Fact]
    public async Task RouteIncrementalAsync_Replaces_Previous_Snapshot_For_Incremental_Plugin()
    {
        var plugin = new IncrementalPlugin();
        var router = new QueryRouter([plugin]);
        var snapshots = new List<IReadOnlyList<SearchResult>>();

        await router.RouteIncrementalAsync(
            "test",
            results =>
            {
                snapshots.Add(results.ToList());
                return Task.CompletedTask;
            },
            CancellationToken.None);

        snapshots.Should().HaveCount(2);
        snapshots[0].Select(result => result.Title).Should().ContainSingle().Which.Should().Be("One");
        snapshots[1].Select(result => result.Title).Should().ContainInOrder("Two", "One");
    }

    [Fact]
    public async Task RouteIncrementalAsync_Does_Not_Block_Caller_For_Synchronous_Incremental_Plugins()
    {
        var plugin = new BlockingIncrementalPlugin();
        var router = new QueryRouter([plugin]);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var routeTask = router.RouteIncrementalAsync("test", _ => Task.CompletedTask, CancellationToken.None);

        stopwatch.ElapsedMilliseconds.Should().BeLessThan(100);
        await routeTask;
    }

    private sealed class IncrementalPlugin : IIncrementalQueryPlugin
    {
        public string Name => "Incremental";
        public int Priority => 1;

        public Task<IReadOnlyList<SearchResult>> QueryAsync(string query, CancellationToken ct)
        {
            return Task.FromResult<IReadOnlyList<SearchResult>>(
            [
                new SearchResult { Title = "Two", Score = 0.8, PluginName = Name, Actions = [] },
                new SearchResult { Title = "One", Score = 0.6, PluginName = Name, Actions = [] }
            ]);
        }

        public async Task QueryIncrementalAsync(
            string query,
            Func<IReadOnlyList<SearchResult>, Task> onResultsAvailable,
            CancellationToken ct)
        {
            await onResultsAvailable(
            [
                new SearchResult { Title = "One", Score = 0.6, PluginName = Name, Actions = [] }
            ]);

            await onResultsAvailable(
            [
                new SearchResult { Title = "Two", Score = 0.8, PluginName = Name, Actions = [] }
            ]);
        }
    }

    private sealed class BlockingIncrementalPlugin : IIncrementalQueryPlugin
    {
        public string Name => "BlockingIncremental";
        public int Priority => 1;

        public Task<IReadOnlyList<SearchResult>> QueryAsync(string query, CancellationToken ct)
        {
            return Task.FromResult<IReadOnlyList<SearchResult>>([]);
        }

        public async Task QueryIncrementalAsync(
            string query,
            Func<IReadOnlyList<SearchResult>, Task> onResultsAvailable,
            CancellationToken ct)
        {
            Thread.Sleep(200);
            await onResultsAvailable(
            [
                new SearchResult { Title = "Done", Score = 0.5, PluginName = Name, Actions = [] }
            ]);
        }
    }
}
