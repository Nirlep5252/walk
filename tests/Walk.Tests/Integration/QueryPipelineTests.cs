using System.IO;
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

            var results = await router.RouteAsync("2+2", CancellationToken.None);
            results.Should().NotBeEmpty();
            results[0].PluginName.Should().Be("Calculator");
        }
        finally
        {
            Directory.Delete(testDir, true);
        }
    }

    [Fact]
    public async Task Full_Pipeline_File_Search_For_CPath()
    {
        var plugins = new IQueryPlugin[] { new FileSearchPlugin() };
        var router = new QueryRouter(plugins);

        var results = await router.RouteAsync(@"C:\Windows", CancellationToken.None);

        results.Should().NotBeEmpty();
        results[0].PluginName.Should().Be("Files");
    }
}
