using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Walk.Helpers;

public static class IconExtractor
{
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static bool TryGetCachedIcon(string filePath, int iconIndex, out ImageSource? icon)
    {
        var expandedPath = ExpandPath(filePath);
        return Cache.TryGetValue(GetCacheKey(expandedPath, iconIndex), out icon);
    }

    public static Task<ImageSource?> GetIconAsync(string filePath, int iconIndex, CancellationToken ct)
    {
        var expandedPath = ExpandPath(filePath);
        if (TryGetCachedIcon(expandedPath, iconIndex, out var cachedIcon))
            return Task.FromResult(cachedIcon);

        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            return GetIcon(expandedPath, iconIndex);
        }, ct);
    }

    public static ImageSource? GetIcon(string filePath, int iconIndex = 0)
    {
        var expandedPath = ExpandPath(filePath);
        return Cache.GetOrAdd(
            GetCacheKey(expandedPath, iconIndex),
            _ => ExtractIconImage(expandedPath, iconIndex));
    }

    private static ImageSource? ExtractIconImage(string filePath, int iconIndex)
    {
        try
        {
            if (!File.Exists(filePath))
                return null;

            if (Path.GetExtension(filePath).Equals(".ico", StringComparison.OrdinalIgnoreCase))
                return LoadIcoImage(filePath);

            using var icon = ExtractIconFromFile(filePath, iconIndex);
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
    }

    private static ImageSource? LoadIcoImage(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames.FirstOrDefault();
        frame?.Freeze();
        return frame;
    }

    private static Icon? ExtractIconFromFile(string filePath, int iconIndex)
    {
        if (CanExtractIndexedIcon(filePath))
        {
            var indexedIcon = ExtractIndexedIcon(filePath, iconIndex);
            if (indexedIcon is not null)
                return indexedIcon;
        }

        return Icon.ExtractAssociatedIcon(filePath);
    }

    private static bool CanExtractIndexedIcon(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".icl", StringComparison.OrdinalIgnoreCase);
    }

    private static Icon? ExtractIndexedIcon(string filePath, int iconIndex)
    {
        var largeIconHandles = new IntPtr[1];
        var smallIconHandles = new IntPtr[1];
        var extractedCount = ExtractIconEx(filePath, iconIndex, largeIconHandles, smallIconHandles, 1);
        var largeHandle = largeIconHandles[0];
        var smallHandle = smallIconHandles[0];
        var selectedHandle = largeHandle != IntPtr.Zero ? largeHandle : smallHandle;

        if (extractedCount == 0 || selectedHandle == IntPtr.Zero)
        {
            DestroyHandleIfNeeded(largeHandle);
            DestroyHandleIfNeeded(smallHandle);
            return null;
        }

        try
        {
            using var shellIcon = Icon.FromHandle(selectedHandle);
            return (Icon)shellIcon.Clone();
        }
        finally
        {
            DestroyHandleIfNeeded(largeHandle);
            DestroyHandleIfNeeded(smallHandle);
        }
    }

    private static void DestroyHandleIfNeeded(IntPtr handle)
    {
        if (handle != IntPtr.Zero)
            DestroyIcon(handle);
    }

    private static string ExpandPath(string filePath)
    {
        return Environment.ExpandEnvironmentVariables(filePath.Trim());
    }

    private static string GetCacheKey(string filePath, int iconIndex)
    {
        return $"{filePath}|{iconIndex}";
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(
        string lpszFile,
        int nIconIndex,
        IntPtr[]? phiconLarge,
        IntPtr[]? phiconSmall,
        uint nIcons);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
