using System.IO;
using FluentAssertions;
using Walk.Models;
using Walk.Services;

namespace Walk.Tests.Services;

public sealed class FavoriteServiceTests : IDisposable
{
    private readonly string _testDir;
    private readonly FakeRunTargetLauncher _launcher = new();

    public FavoriteServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "walk_favoriteservice_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    [Fact]
    public void AddOrUpdate_Search_And_Remove_RoundTrips()
    {
        var service = new FavoriteService(_testDir);
        var favorite = new FavoriteEntry
        {
            Key = "app:notepad",
            Kind = FavoriteKind.App,
            Title = "Notepad",
            Subtitle = "Windows app",
            Target = "notepad.exe",
        };

        service.AddOrUpdate(favorite);

        var reloaded = new FavoriteService(_testDir);
        reloaded.Search("note").Should().ContainSingle(entry => entry.Title == "Notepad");

        reloaded.Remove("app:notepad").Should().BeTrue();
        reloaded.GetEntries().Should().BeEmpty();
    }

    [Fact]
    public void Move_Reorders_Favorites()
    {
        var service = new FavoriteService(_testDir);
        service.AddOrUpdate(new FavoriteEntry
        {
            Key = "run:first",
            Kind = FavoriteKind.Run,
            Title = "First",
            Target = "first",
        });
        var second = service.AddOrUpdate(new FavoriteEntry
        {
            Key = "run:second",
            Kind = FavoriteKind.Run,
            Title = "Second",
            Target = "second",
        });

        service.Move(second.Id, -1).Should().BeTrue();

        service.GetEntries().Select(entry => entry.Title).Should().ContainInOrder("Second", "First");
        service.Move(second.Id, -1).Should().BeFalse();
    }

    [Fact]
    public void Launch_Run_Favorite_Uses_Run_Target_Launcher_And_Records_Use()
    {
        var service = new FavoriteService(_testDir, _launcher);
        var favorite = service.AddOrUpdate(new FavoriteEntry
        {
            Key = "run:powershell",
            Kind = FavoriteKind.Run,
            Title = "PowerShell",
            Subtitle = "Open Windows PowerShell",
            Target = "powershell",
            RunKind = "Command",
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            SupportsRunAsAdmin = true,
        });

        service.Launch(favorite.Id);

        _launcher.Launched.Should().ContainSingle();
        _launcher.Launched[0].command.Should().Be("powershell");
        _launcher.Launched[0].asAdmin.Should().BeFalse();
        service.GetEntries().Should().ContainSingle().Which.UseCount.Should().Be(1);
    }

    [Fact]
    public void AddOrUpdate_Copies_Clipboard_Image_Favorites_To_Favorite_Storage()
    {
        var sourceImage = Path.Combine(_testDir, "source.png");
        File.WriteAllBytes(sourceImage, [1, 2, 3, 4]);
        var service = new FavoriteService(_testDir);

        var favorite = service.AddOrUpdate(new FavoriteEntry
        {
            Key = "clip:image",
            Kind = FavoriteKind.ClipboardImage,
            Title = "Image 2 x 2",
            Subtitle = "Clipboard image",
            Target = sourceImage,
        });

        favorite.Target.Should().NotBe(sourceImage);
        favorite.Target.Should().Contain("FavoriteImages");
        File.Exists(favorite.Target).Should().BeTrue();

        service.Remove(favorite.Id).Should().BeTrue();
        File.Exists(favorite.Target).Should().BeFalse();
    }

    private sealed class FakeRunTargetLauncher : IRunTargetLauncher
    {
        public List<(string command, bool asAdmin)> Launched { get; } = [];
        public List<string> OpenedLocations { get; } = [];

        public void Launch(RunTarget target, bool asAdmin)
        {
            Launched.Add((target.Command, asAdmin));
        }

        public void OpenFileLocation(string path)
        {
            OpenedLocations.Add(path);
        }
    }
}
