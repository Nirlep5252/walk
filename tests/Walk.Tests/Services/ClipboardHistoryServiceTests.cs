using System.IO;
using FluentAssertions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Walk.Models;
using Walk.Services;

namespace Walk.Tests.Services;

public sealed class ClipboardHistoryServiceTests : IDisposable
{
    private readonly string _testDir;

    public ClipboardHistoryServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "walk_clipboardhistory_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    [Fact]
    public void GetEntries_Returns_Empty_When_No_File_Exists()
    {
        var service = new ClipboardHistoryService(_testDir);

        service.GetEntries().Should().BeEmpty();
    }

    [Fact]
    public void RecordText_Persists_And_Deduplicates_By_Content()
    {
        var service = new ClipboardHistoryService(_testDir);

        service.RecordText("  deploy to staging  ");
        service.RecordText("deploy to staging");

        var reloaded = new ClipboardHistoryService(_testDir);
        var entries = reloaded.GetEntries();

        entries.Should().ContainSingle();
        entries[0].Kind.Should().Be(ClipboardHistoryKind.Text);
        entries[0].Title.Should().Be("deploy to staging");
        entries[0].Text.Should().Be("deploy to staging");
        entries[0].CopyCount.Should().Be(2);
    }

    [Fact]
    public void RecordFiles_Persists_File_Drop_Lists()
    {
        var service = new ClipboardHistoryService(_testDir);
        var firstPath = Path.Combine(_testDir, "one.txt");
        var secondPath = Path.Combine(_testDir, "two.txt");

        service.RecordFiles([firstPath, secondPath]);

        var entries = service.GetEntries();
        entries.Should().ContainSingle();
        entries[0].Kind.Should().Be(ClipboardHistoryKind.Files);
        entries[0].Title.Should().Be("2 files");
        entries[0].FilePaths.Should().ContainInOrder(firstPath, secondPath);
    }

    [Fact]
    public void RecordImage_Persists_Png_And_Metadata()
    {
        var service = new ClipboardHistoryService(_testDir);

        var entry = service.RecordImage(CreateBitmap(3, 2));

        entry.Should().NotBeNull();
        entry!.Kind.Should().Be(ClipboardHistoryKind.Image);
        entry.Title.Should().Be("Image 3 x 2");
        entry.ImageWidth.Should().Be(3);
        entry.ImageHeight.Should().Be(2);
        entry.ImageHash.Should().NotBeNullOrWhiteSpace();
        entry.ImagePath.Should().NotBeNullOrWhiteSpace();
        File.Exists(entry.ImagePath).Should().BeTrue();

        var reloaded = new ClipboardHistoryService(_testDir);
        var entries = reloaded.GetEntries();
        entries.Should().ContainSingle();
        entries[0].Kind.Should().Be(ClipboardHistoryKind.Image);
        entries[0].ImagePath.Should().Be(entry.ImagePath);
    }

    [Fact]
    public void RecordImage_Deduplicates_By_Image_Content()
    {
        var service = new ClipboardHistoryService(_testDir);

        service.RecordImage(CreateBitmap(2, 2));
        service.RecordImage(CreateBitmap(2, 2));

        var entries = service.GetEntries();
        entries.Should().ContainSingle();
        entries[0].CopyCount.Should().Be(2);
    }

    [Fact]
    public void Search_Returns_Matching_Text_Entries()
    {
        var service = new ClipboardHistoryService(_testDir);
        service.RecordText("GitHub pull request notes");
        service.RecordText("Quarterly budget draft");

        var results = service.Search("pull");

        results.Should().ContainSingle();
        results[0].Title.Should().Be("GitHub pull request notes");
    }

    [Fact]
    public void Search_Returns_Image_Entries()
    {
        var service = new ClipboardHistoryService(_testDir);
        service.RecordImage(CreateBitmap(4, 5));

        var results = service.Search("image");

        results.Should().ContainSingle();
        results[0].Kind.Should().Be(ClipboardHistoryKind.Image);
    }

    [Fact]
    public void Search_Blank_Returns_Pinned_Entries_First()
    {
        var service = new ClipboardHistoryService(_testDir);
        var first = service.RecordText("first copied value")!;
        var second = service.RecordText("second copied value")!;

        service.TogglePinned(first.Id);

        var results = service.Search("");

        results.Select(entry => entry.Id).Should().ContainInOrder(first.Id, second.Id);
        results[0].IsPinned.Should().BeTrue();
    }

    [Fact]
    public void DeleteEntry_Removes_Persisted_Entry()
    {
        var service = new ClipboardHistoryService(_testDir);
        var entry = service.RecordText("temporary token")!;

        service.DeleteEntry(entry.Id);

        var reloaded = new ClipboardHistoryService(_testDir);
        reloaded.GetEntries().Should().BeEmpty();
    }

    [Fact]
    public void DeleteEntry_Removes_Unreferenced_Image_File()
    {
        var service = new ClipboardHistoryService(_testDir);
        var entry = service.RecordImage(CreateBitmap(3, 3))!;
        var imagePath = entry.ImagePath!;

        service.DeleteEntry(entry.Id);

        service.GetEntries().Should().BeEmpty();
        File.Exists(imagePath).Should().BeFalse();
    }

    private static BitmapSource CreateBitmap(int width, int height)
    {
        var stride = width * 4;
        var pixels = new byte[stride * height];
        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = 32;
            pixels[index + 1] = 96;
            pixels[index + 2] = 220;
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
