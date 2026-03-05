using FluentAssertions;
using Walk.Helpers;

namespace Walk.Tests.Helpers;

public class FuzzyMatcherTests
{
    [Theory]
    [InlineData("notepad", "Notepad", true)]
    [InlineData("note", "Notepad", true)]
    [InlineData("np", "Notepad", true)]
    [InlineData("ntp", "Notepad", true)]
    [InlineData("chrome", "Google Chrome", true)]
    [InlineData("gc", "Google Chrome", true)]
    [InlineData("xyz", "Notepad", false)]
    [InlineData("", "Notepad", true)]
    [InlineData("notepadx", "Notepad", false)]
    public void Match_Returns_Expected_Result(string query, string target, bool shouldMatch)
    {
        var result = FuzzyMatcher.Match(query, target);
        result.IsMatch.Should().Be(shouldMatch);
    }

    [Fact]
    public void Match_Scores_Exact_Match_Higher_Than_Subsequence()
    {
        var exact = FuzzyMatcher.Match("notepad", "Notepad");
        var subsequence = FuzzyMatcher.Match("np", "Notepad");

        exact.Score.Should().BeGreaterThan(subsequence.Score);
    }

    [Fact]
    public void Match_Scores_Prefix_Higher_Than_Substring()
    {
        var prefix = FuzzyMatcher.Match("note", "Notepad");
        var substring = FuzzyMatcher.Match("tepa", "Notepad");

        prefix.Score.Should().BeGreaterThan(substring.Score);
    }

    [Fact]
    public void Match_Is_Case_Insensitive()
    {
        var lower = FuzzyMatcher.Match("notepad", "Notepad");
        var upper = FuzzyMatcher.Match("NOTEPAD", "Notepad");

        lower.IsMatch.Should().BeTrue();
        upper.IsMatch.Should().BeTrue();
        lower.Score.Should().Be(upper.Score);
    }
}
