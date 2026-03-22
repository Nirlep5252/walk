using FluentAssertions;
using Walk.Services;

namespace Walk.Tests.Services;

public class EverythingBundledRuntimeTests
{
    [Fact]
    public void BuildConfigContent_Includes_Folder_Index_Settings_When_Folders_Are_Present()
    {
        var config = EverythingBundledRuntime.BuildConfigContent(
        [
            @"C:\Users\someone\Downloads",
            @"C:\Users\someone\Documents",
        ]);

        config.Should().Contain("folders=C:\\Users\\someone\\Downloads,C:\\Users\\someone\\Documents");
        config.Should().Contain("folder_monitor_changes=1,1");
        config.Should().Contain("folder_update_intervals=15,15");
        config.Should().Contain("folder_update_rescan_asap=1");
        config.Should().Contain("folder_update_rescan_asap=1" + Environment.NewLine + "folders=");
    }
}
