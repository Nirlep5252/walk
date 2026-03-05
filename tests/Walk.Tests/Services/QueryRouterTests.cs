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
}
