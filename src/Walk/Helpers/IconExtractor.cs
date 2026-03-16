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
    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiLargeIcon = 0x000000000;
    private const uint ShgfiSmallIcon = 0x000000001;
    private const uint ShgfiPidl = 0x000000008;

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
            if (IsShellPath(filePath))
                return ExtractShellIconImage(filePath);

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

    public static bool IsShellPath(string path)
    {
        return path.StartsWith(@"shell:", StringComparison.OrdinalIgnoreCase);
    }

    private static ImageSource? ExtractShellIconImage(string shellPath)
    {
        var pidl = IntPtr.Zero;

        try
        {
            var parseResult = SHParseDisplayName(shellPath, IntPtr.Zero, out pidl, 0, out _);
            if (parseResult != 0 || pidl == IntPtr.Zero)
                return null;

            return TryCreateShellIconFromPidl(pidl, ShgfiLargeIcon) ??
                   TryCreateShellIconFromPidl(pidl, ShgfiSmallIcon);
        }
        finally
        {
            if (pidl != IntPtr.Zero)
                CoTaskMemFree(pidl);
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

    private static ImageSource? TryCreateShellIconFromPidl(IntPtr pidl, uint sizeFlag)
    {
        var result = SHGetFileInfo(
            pidl,
            0,
            out var shellFileInfo,
            (uint)Marshal.SizeOf<SHFILEINFO>(),
            ShgfiPidl | ShgfiIcon | sizeFlag);

        if (result == IntPtr.Zero || shellFileInfo.hIcon == IntPtr.Zero)
            return null;

        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(
                shellFileInfo.hIcon,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            DestroyIcon(shellFileInfo.hIcon);
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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
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

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(
        string name,
        IntPtr pbc,
        out IntPtr ppidl,
        uint sfgaoIn,
        out uint psfgaoOut);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        IntPtr pszPath,
        uint dwFileAttributes,
        out SHFILEINFO psfi,
        uint cbFileInfo,
        uint uFlags);

    [DllImport("ole32.dll")]
    private static extern void CoTaskMemFree(IntPtr pv);
}
