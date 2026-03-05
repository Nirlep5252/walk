using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Walk.Helpers;

public static class IconExtractor
{
    public static ImageSource? GetIcon(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return null;

            using var icon = Icon.ExtractAssociatedIcon(filePath);
            if (icon is null)
                return null;

            return Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
        }
        catch
        {
            return null;
        }
    }
}
