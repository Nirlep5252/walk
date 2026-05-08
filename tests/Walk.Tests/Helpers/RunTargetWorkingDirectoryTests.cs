using System.IO;
using FluentAssertions;
using Walk.Helpers;
using Walk.Models;

namespace Walk.Tests.Helpers;

public class RunTargetWorkingDirectoryTests : IDisposable
{
    private readonly string _testDir;

    public RunTargetWorkingDirectoryTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "walk_runtarget_workdir_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    [Theory]
    [InlineData("powershell")]
    [InlineData("powershell.exe")]
    [InlineData("pwsh")]
    [InlineData("pwsh.exe")]
    [InlineData(@"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe")]
    [InlineData(@"""C:\Program Files\PowerShell\7\pwsh.exe""")]
    public void Resolve_Uses_User_Profile_For_PowerShell_Targets(string command)
    {
        var target = new RunTarget
        {
            Title = command,
            Command = command,
        };

        RunTargetWorkingDirectory.Resolve(target)
            .Should()
            .Be(RunTargetWorkingDirectory.GetDefaultPowerShellDirectory());
    }

    [Fact]
    public void Resolve_Preserves_Configured_WorkingDirectory()
    {
        var target = new RunTarget
        {
            Title = "PowerShell",
            Command = "powershell",
            WorkingDirectory = _testDir,
        };

        RunTargetWorkingDirectory.Resolve(target).Should().Be(_testDir);
    }

    [Fact]
    public void Resolve_Does_Not_Set_WorkingDirectory_For_Other_Commands()
    {
        var target = new RunTarget
        {
            Title = "Services",
            Command = "services.msc",
        };

        RunTargetWorkingDirectory.Resolve(target).Should().BeNull();
    }
}
