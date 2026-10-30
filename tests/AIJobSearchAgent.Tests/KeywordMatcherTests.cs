using AIJobSearchAgent.Application.Services;
using AIJobSearchAgent.Domain.Entities;

namespace AIJobSearchAgent.Tests;

public sealed class KeywordMatcherTests
{
    [Fact]
    public void Matches_IsCaseInsensitive()
    {
        var vacancy = CreateVacancy("Senior ASP.NET Core Engineer", "Build APIs");

        var result = KeywordMatcher.Matches(vacancy, ["asp.net core"]);

        Assert.True(result);
    }

    [Fact]
    public void Matches_UsesOrSemanticsAcrossTitleAndDescription()
    {
        var vacancy = CreateVacancy("Backend Engineer", "Work with modern C# services");

        var result = KeywordMatcher.Matches(vacancy, ["Python", "c#"]);

        Assert.True(result);
    }

    [Fact]
    public void Matches_WithEmptyKeywords_ReturnsTrue()
    {
        var vacancy = CreateVacancy("Backend Engineer", "Build services");

        Assert.True(KeywordMatcher.Matches(vacancy, []));
        Assert.Empty(KeywordMatcher.NormalizeKeywords([" ", "\t"]));
    }

    private static JobVacancy CreateVacancy(string title, string description) => new()
    {
        ExternalId = "job-1",
        Source = "Test",
        Title = title,
        Company = "Example",
        Location = "Remote",
        IsRemote = true,
        Description = description,
        Url = "https://example.test/jobs/1"
    };
}
