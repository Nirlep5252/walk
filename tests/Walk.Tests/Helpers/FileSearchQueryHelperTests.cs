using FluentAssertions;
using Walk.Helpers;

namespace Walk.Tests.Helpers;

public class FileSearchQueryHelperTests
{
    [Fact]
    public void ShouldUseFuzzySearch_Returns_True_For_Plain_Text_Query()
    {
        FileSearchQueryHelper.ShouldUseFuzzySearch("smthng").Should().BeTrue();
    }

    [Theory]
    [InlineData("*.mp4")]
    [InlineData(@"C:\Windows")]
    [InlineData("path:video")]
    public void ShouldUseFuzzySearch_Returns_False_For_Explicit_Search_Syntax(string query)
    {
        FileSearchQueryHelper.ShouldUseFuzzySearch(query).Should().BeFalse();
    }

    [Fact]
    public void BuildRegexPattern_Creates_Subsequence_Pattern()
    {
        FileSearchQueryHelper.BuildRegexPattern("smthng").Should().Be("s.*m.*t.*h.*n.*g");
    }

    [Fact]
    public void ScorePath_Prefers_File_Name_Matches_Over_Only_Path_Matches()
    {
        var fileNameScore = FileSearchQueryHelper.ScorePath("smthng", @"C:\media\something.mp4");
        var pathOnlyScore = FileSearchQueryHelper.ScorePath("smthng", @"C:\something\clip.mp4");

        fileNameScore.Should().BeGreaterThan(pathOnlyScore);
    }

    [Fact]
    public void ScorePath_Matches_Fuzzy_Full_Path_Query()
    {
        var score = FileSearchQueryHelper.ScorePath("smthng", @"C:\videos\something.mp4");

        score.Should().BeGreaterThan(0.22);
    }
}
