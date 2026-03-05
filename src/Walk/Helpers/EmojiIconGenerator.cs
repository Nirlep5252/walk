using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace Walk.Helpers;

public static class EmojiIconGenerator
{
    public static Icon Create(string emoji, int size)
    {
        using var bitmap = new Bitmap(size, size);
        bitmap.SetResolution(96, 96);

        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.Clear(Color.Transparent);

        var fontSize = size * 0.7f;
        using var font = new Font("Segoe UI Emoji", fontSize, FontStyle.Regular, GraphicsUnit.Pixel);

        var textSize = graphics.MeasureString(emoji, font);
        var x = (size - textSize.Width) / 2f;
        var y = (size - textSize.Height) / 2f;

        graphics.DrawString(emoji, font, Brushes.White, x, y);

        var hIcon = bitmap.GetHicon();
        return Icon.FromHandle(hIcon);
    }
}
