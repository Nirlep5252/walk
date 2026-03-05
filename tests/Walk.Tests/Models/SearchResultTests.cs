using FluentAssertions;
using Walk.Models;

namespace Walk.Tests.Models;

public class SearchResultTests
{
    [Fact]
    public void SearchResult_Should_Store_All_Properties()
    {
        var action = new SearchAction
        {
            Label = "Run",
            Execute = () => { }
        };

        var result = new SearchResult
        {
            Title = "Notepad",
            Subtitle = @"C:\Windows\notepad.exe",
            PluginName = "Apps",
            Score = 1.0,
            Actions = [action]
        };

        result.Title.Should().Be("Notepad");
        result.Subtitle.Should().Be(@"C:\Windows\notepad.exe");
        result.PluginName.Should().Be("Apps");
        result.Score.Should().Be(1.0);
        result.Actions.Should().HaveCount(1);
        result.Actions[0].Label.Should().Be("Run");
    }

    [Fact]
    public void SearchResult_Icon_Defaults_To_Null()
    {
        var result = new SearchResult
        {
            Title = "Test",
            Actions = []
        };

        result.Icon.Should().BeNull();
    }
}
