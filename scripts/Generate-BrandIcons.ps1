param()

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$assetDir = Join-Path $repoRoot "src\Walk\Assets"
$workDir = Join-Path $repoRoot "artifacts\icon-generator"

New-Item -ItemType Directory -Force -Path $workDir | Out-Null

$projectPath = Join-Path $workDir "IconGenerator.csproj"
$programPath = Join-Path $workDir "Program.cs"

$project = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
'@

$program = @'
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

var assetDir = args[0];
const string LogoGlyph = "W";

SaveIcon(Path.Combine(assetDir, "walk-app.ico"), 256, false);
SaveIcon(Path.Combine(assetDir, "walk-tray.ico"), 64, false);
SaveIcon(Path.Combine(assetDir, "walk-tray-active.ico"), 64, true);

static void SaveIcon(string path, int size, bool active)
{
    using var bitmap = new Bitmap(size, size);
    bitmap.SetResolution(96, 96);

    using var graphics = Graphics.FromImage(bitmap);
    graphics.SmoothingMode = SmoothingMode.AntiAlias;
    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
    graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
    graphics.Clear(Color.Transparent);

    var inset = Math.Max(4f, size * 0.04f);
    var rect = new RectangleF(inset, inset, size - (inset * 2), size - (inset * 2));
    var radius = size * 0.22f;

    using var backgroundBrush = new LinearGradientBrush(
        rect,
        active ? Color.FromArgb(255, 83, 150, 224) : Color.FromArgb(255, 33, 59, 92),
        active ? Color.FromArgb(255, 43, 96, 176) : Color.FromArgb(255, 17, 32, 53),
        45f);
    using var borderPen = new Pen(
        active ? Color.FromArgb(230, 179, 214, 255) : Color.FromArgb(210, 121, 168, 226),
        Math.Max(2f, size * 0.02f));
    using var shadowBrush = new SolidBrush(Color.FromArgb(active ? 64 : 48, 0, 0, 0));
    using var textBrush = new SolidBrush(Color.White);
    using var outlinePath = CreateRoundedRect(rect, radius);
    using var shadowPath = CreateRoundedRect(
        new RectangleF(rect.X, rect.Y + (size * 0.02f), rect.Width, rect.Height),
        radius);

    graphics.FillPath(shadowBrush, shadowPath);
    graphics.FillPath(backgroundBrush, outlinePath);
    graphics.DrawPath(borderPen, outlinePath);

    using var font = new Font("Segoe UI", size * 0.56f, FontStyle.Bold, GraphicsUnit.Pixel);
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

    graphics.DrawString(LogoGlyph, font, textBrush, textRect, format);

    using var icon = Icon.FromHandle(bitmap.GetHicon());
    using var clone = (Icon)icon.Clone();
    using var stream = File.Create(path);
    clone.Save(stream);
}

static GraphicsPath CreateRoundedRect(RectangleF rect, float radius)
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
'@

[System.IO.File]::WriteAllText($projectPath, $project)
[System.IO.File]::WriteAllText($programPath, $program)

dotnet run --project $projectPath -- $assetDir
if ($LASTEXITCODE -ne 0) {
    throw "Failed to generate brand icons."
}
