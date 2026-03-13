using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace Walk.Helpers;

public static class TrayIconGenerator
{
    public static Icon Create(int size, bool active = false)
    {
        using var bitmap = new Bitmap(size, size);
        bitmap.SetResolution(96, 96);

        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.Clear(Color.Transparent);

        // Draw a rounded rectangle background
        var bgColor = active ? Color.FromArgb(200, 77, 143, 201) : Color.FromArgb(160, 30, 45, 68);
        var borderColor = active ? Color.FromArgb(220, 136, 178, 217) : Color.FromArgb(180, 126, 166, 224);

        var rect = new RectangleF(1, 1, size - 2, size - 2);
        var radius = size * 0.25f;

        using var bgBrush = new SolidBrush(bgColor);
        using var borderPen = new Pen(borderColor, 1.5f);
        using var path = CreateRoundedRect(rect, radius);

        graphics.FillPath(bgBrush, path);
        graphics.DrawPath(borderPen, path);

        var fontSize = size * 0.55f;
        using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };

        var textRect = new RectangleF(
            rect.X,
            rect.Y - (size * 0.04f),
            rect.Width,
            rect.Height + (size * 0.08f));

        graphics.DrawString(Branding.LogoGlyph, font, Brushes.White, textRect, format);

        var hIcon = bitmap.GetHicon();
        return Icon.FromHandle(hIcon);
    }

    private static GraphicsPath CreateRoundedRect(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;

        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();

        return path;
    }
}
