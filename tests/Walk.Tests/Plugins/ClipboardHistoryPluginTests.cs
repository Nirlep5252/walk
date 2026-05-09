using System.IO;
using FluentAssertions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Walk.Models;
using Walk.Plugins;
using Walk.Services;

namespace Walk.Tests.Plugins;

public sealed class ClipboardHistoryPluginTests : IDisposable
{
    private readonly string _testDir;
    private readonly ClipboardHistoryService _service;
    private readonly ClipboardHistoryPlugin _plugin;

    public ClipboardHistoryPluginTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "walk_clipboardplugin_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _service = new ClipboardHistoryService(_testDir);
        _plugin = new ClipboardHistoryPlugin(_service);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    [Fact]
    public async Task QueryAsync_Returns_Empty_For_NonClipboard_Query()
    {
        _service.RecordText("secret project name");

        var results = await _plugin.QueryAsync("secret", CancellationToken.None);

        results.Should().BeEmpty();
    }

    [Theory]
    [InlineData("clip")]
    [InlineData("clipboard")]
    [InlineData("cb")]
    public async Task QueryAsync_Returns_Recent_Entries_For_Explicit_Prefix(string query)
    {
        _service.RecordText("deploy checklist");

        var results = await _plugin.QueryAsync(query, CancellationToken.None);

        results.Should().ContainSingle();
        results[0].Title.Should().Be("deploy checklist");
        results[0].PluginName.Should().Be("Clipboard");
        results[0].Actions.Should().Contain(action => action.KeyGesture == "Enter");
    }

    [Fact]
    public async Task QueryAsync_Searches_After_Prefix()
    {
        _service.RecordText("database migration notes");
        _service.RecordText("release announcement");

        var results = await _plugin.QueryAsync("clip migration", CancellationToken.None);

        results.Should().ContainSingle();
        results[0].Title.Should().Be("database migration notes");
    }

    [Fact]
    public async Task QueryAsync_Returns_Image_Entries()
    {
        _service.RecordImage(CreateBitmap());

        var results = await _plugin.QueryAsync("clip image", CancellationToken.None);

        results.Should().ContainSingle();
        results[0].Title.Should().Be("Image 2 x 2");
        results[0].Subtitle.Should().Contain("Image");
        results[0].PluginName.Should().Be("Clipboard");
    }

    [Fact]
    public async Task Delete_Action_Removes_Entry()
    {
        _service.RecordText("remove this entry");
        var results = await _plugin.QueryAsync("clip remove", CancellationToken.None);

        var deleteAction = results[0].Actions.Single(action => action.KeyGesture == "Ctrl+X");
        deleteAction.Execute();

        _service.GetEntries().Should().BeEmpty();
    }

    private static BitmapSource CreateBitmap()
    {
        const int width = 2;
        const int height = 2;
        const int stride = width * 4;
        var pixels = new byte[stride * height];
        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = 12;
            pixels[index + 1] = 120;
            pixels[index + 2] = 240;
            pixels[index + 3] = 255;
        }

        var bitmap = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        bitmap.Freeze();
        return bitmap;
    }
}
