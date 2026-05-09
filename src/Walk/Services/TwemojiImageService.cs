using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Walk.Services;

public sealed class TwemojiImageService
{
    private const string BaseAssetUrl = "https://cdn.jsdelivr.net/gh/twitter/twemoji@14.0.2/assets/72x72";
    private readonly string _cacheDir;
    private readonly ConcurrentDictionary<string, ImageSource?> _memoryCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HttpClient HttpClient = new();

    public TwemojiImageService(string dataDir)
    {
        _cacheDir = Path.Combine(dataDir, "Twemoji");
        Directory.CreateDirectory(_cacheDir);
    }

    public bool TryGetCachedIcon(string emoji, out ImageSource? icon)
    {
        icon = null;
        var assetCode = GetAssetCode(emoji);
        if (assetCode.Length == 0)
            return false;

        if (_memoryCache.TryGetValue(assetCode, out icon))
            return icon is not null;

        var filePath = GetCachedFilePath(assetCode);
        if (!File.Exists(filePath))
            return false;

        icon = LoadImage(filePath);
        _memoryCache[assetCode] = icon;
        return icon is not null;
    }

    public async Task<ImageSource?> GetIconAsync(string emoji, CancellationToken ct)
    {
        var assetCode = GetAssetCode(emoji);
        if (assetCode.Length == 0)
            return null;

        if (_memoryCache.TryGetValue(assetCode, out var cached))
            return cached;

        var filePath = GetCachedFilePath(assetCode);
        if (!File.Exists(filePath))
        {
            var downloaded = await DownloadAssetAsync(assetCode, filePath, ct).ConfigureAwait(false);
            if (!downloaded)
                return null;
        }

        var icon = LoadImage(filePath);
        _memoryCache[assetCode] = icon;
        return icon;
    }

    public static string GetAssetCode(string emoji)
    {
        return string.Join(
            "-",
            emoji
                .EnumerateRunes()
                .Where(static rune => rune.Value is not 0xFE0E and not 0xFE0F)
                .Select(static rune => rune.Value.ToString("x")));
    }

    private string GetCachedFilePath(string assetCode)
    {
        return Path.Combine(_cacheDir, $"{assetCode}.png");
    }

    private static async Task<bool> DownloadAssetAsync(string assetCode, string filePath, CancellationToken ct)
    {
        try
        {
            var bytes = await HttpClient.GetByteArrayAsync($"{BaseAssetUrl}/{assetCode}.png", ct).ConfigureAwait(false);
            if (bytes.Length == 0)
                return false;

            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            await File.WriteAllBytesAsync(filePath, bytes, ct).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static ImageSource? LoadImage(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }
}
