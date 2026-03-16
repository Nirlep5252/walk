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
    private const int ShellIconSize = 64;

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
        try
        {
            var factory = CreateShellItemImageFactory(shellPath);
            if (factory is null)
                return null;

            try
            {
                factory.GetImage(
                    new NativeSize { Width = ShellIconSize, Height = ShellIconSize },
                    ShellItemImageFlags.BiggerSizeOk | ShellItemImageFlags.IconOnly,
                    out var bitmapHandle);

                if (bitmapHandle == IntPtr.Zero)
                    return null;

                try
                {
                    var source = Imaging.CreateBitmapSourceFromHBitmap(
                        bitmapHandle,
                        IntPtr.Zero,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromWidthAndHeight(ShellIconSize, ShellIconSize));
                    source.Freeze();
                    return source;
                }
                finally
                {
                    DeleteObject(bitmapHandle);
                }
            }
            finally
            {
                if (Marshal.IsComObject(factory))
                    Marshal.ReleaseComObject(factory);
            }
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

    private static IShellItemImageFactory? CreateShellItemImageFactory(string shellPath)
    {
        var interfaceId = typeof(IShellItemImageFactory).GUID;
        SHCreateItemFromParsingName(shellPath, IntPtr.Zero, in interfaceId, out IShellItemImageFactory? factory);
        return factory;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize
    {
        public int Width;
        public int Height;
    }

    [Flags]
    private enum ShellItemImageFlags
    {
        ResizeToFit = 0x0,
        BiggerSizeOk = 0x1,
        MemoryOnly = 0x2,
        IconOnly = 0x4,
        ThumbnailOnly = 0x8,
        InCacheOnly = 0x10,
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        void GetImage(NativeSize size, ShellItemImageFlags flags, out IntPtr phbm);
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

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        string path,
        IntPtr pbc,
        in Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory? shellItemImageFactory);
}
