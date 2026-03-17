using System.IO;
using System.Net;
using System.Net.Http;
using FluentAssertions;
using Walk.Services;

namespace Walk.Tests.Services;

public class ChangelogRecoveryServiceTests : IDisposable
{
    private readonly string _testDir;

    public ChangelogRecoveryServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "walk_changelog_recovery_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    [Fact]
    public async Task EnsureCurrentVersionPendingAsync_StagesPendingEntry_WhenCurrentVersionIsMissing()
    {
        var changelogService = new ChangelogService(_testDir);
        var recoveryService = new ChangelogRecoveryService(
            changelogService,
            BuildReleaseNotesService("# Walk 0.3.7"));

        await recoveryService.EnsureCurrentVersionPendingAsync("0.3.7");

        var pending = await changelogService.GetPendingAsync("0.3.7");

        pending.Should().NotBeNull();
        pending!.Version.Should().Be("0.3.7");
        pending.Markdown.Should().Contain("Walk 0.3.7");
    }

    [Fact]
    public async Task GetLatestAvailableForVersionAsync_FetchesAndPersistsCurrentVersion_WhenStateIsEmpty()
    {
        var changelogService = new ChangelogService(_testDir);
        var recoveryService = new ChangelogRecoveryService(
            changelogService,
            BuildReleaseNotesService("# Walk 0.3.7"));

        var entry = await recoveryService.GetLatestAvailableForVersionAsync("0.3.7");
        var latest = await changelogService.GetLatestAsync();

        entry.Should().NotBeNull();
        entry!.Version.Should().Be("0.3.7");
        latest.Should().NotBeNull();
        latest!.Version.Should().Be("0.3.7");
    }

    private static ReleaseNotesService BuildReleaseNotesService(string markdown)
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(markdown),
            }));

        return new ReleaseNotesService(httpClient);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(responseFactory(request));
        }
    }
}
