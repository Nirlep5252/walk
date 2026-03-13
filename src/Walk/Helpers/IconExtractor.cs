using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Walk.Helpers;

public static class IconExtractor
{
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static bool TryGetCachedIcon(string filePath, out ImageSource? icon)
    {
        return Cache.TryGetValue(filePath, out icon);
    }

    public static Task<ImageSource?> GetIconAsync(string filePath, CancellationToken ct)
    {
        if (TryGetCachedIcon(filePath, out var cachedIcon))
            return Task.FromResult(cachedIcon);

        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            return GetIcon(filePath);
        }, ct);
    }

    public static ImageSource? GetIcon(string filePath)
    {
        return Cache.GetOrAdd(filePath, static path =>
        {
            try
            {
                if (!File.Exists(path))
                    return null;

                using var icon = Icon.ExtractAssociatedIcon(path);
                if (icon is null)
                    return null;

                var source = Imaging.CreateBitmapSourceFromHIcon(
                    icon.Handle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            catch
            {
                return null;
            }
        });
    }
}
