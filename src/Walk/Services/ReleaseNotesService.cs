using System.Net.Http;
using System.Net.Http.Headers;

namespace Walk.Services;

public sealed class ReleaseNotesService
{
    private readonly HttpClient _httpClient;

    public ReleaseNotesService()
        : this(CreateDefaultHttpClient())
    {
    }

    public ReleaseNotesService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<string?> TryGetReleaseNotesAsync(string version, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(version) ||
            string.Equals(version, AppVersionService.DevelopmentModeVersion, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ReleaseInfo.BuildReleaseNotesUrl(version));
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            var markdown = await response.Content.ReadAsStringAsync(cancellationToken);
            return string.IsNullOrWhiteSpace(markdown)
                ? null
                : markdown.Trim();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static HttpClient CreateDefaultHttpClient()
    {
        var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10),
        };
        httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Walk", "1.0"));
        return httpClient;
    }
}
