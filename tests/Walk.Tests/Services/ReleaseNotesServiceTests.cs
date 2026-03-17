using System.Net;
using System.Net.Http;
using FluentAssertions;
using Walk.Services;

namespace Walk.Tests.Services;

public class ReleaseNotesServiceTests
{
    [Fact]
    public async Task TryGetReleaseNotesAsync_ReturnsMarkdownFromTaggedReleaseFile()
    {
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("# Walk 0.3.7"),
            });
        var httpClient = new HttpClient(handler);
        var service = new ReleaseNotesService(httpClient);

        var markdown = await service.TryGetReleaseNotesAsync("0.3.7");

        markdown.Should().Be("# Walk 0.3.7");
        handler.RequestUri.Should().Be(ReleaseInfo.BuildReleaseNotesUrl("0.3.7"));
    }

    [Fact]
    public async Task TryGetReleaseNotesAsync_ReturnsNull_WhenRequestFails()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var service = new ReleaseNotesService(new HttpClient(handler));

        var markdown = await service.TryGetReleaseNotesAsync("0.3.7");

        markdown.Should().BeNull();
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public string? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri?.ToString();
            return Task.FromResult(responseFactory(request));
        }
    }
}
