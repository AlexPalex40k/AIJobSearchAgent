using AIJobSearchAgent.Application.Services;

namespace AIJobSearchAgent.Tests;

public sealed class TechnicalSkillMatcherTests
{
    [Theory]
    [InlineData("C#", "C Sharp")]
    [InlineData("Microsoft SQL Server", "MS SQL")]
    [InlineData("SQL Server", "MSSQL")]
    [InlineData("ASP.NET Core", "ASP Net Core")]
    [InlineData("Entity Framework Core", "EF Core")]
    [InlineData("Microsoft .NET Framework 4.8", ".NET Framework 4.8")]
    [InlineData("Microsoft .NET Core 3.1", "dotnet core 3.1")]
    public void IsMatch_accepts_only_explicit_aliases(
        string requiredSkill,
        string candidateSkill)
    {
        Assert.True(TechnicalSkillMatcher.IsMatch(candidateSkill, requiredSkill));
    }

    [Fact]
    public void IsMatch_accepts_an_explicit_version_list_match()
    {
        Assert.True(TechnicalSkillMatcher.IsMatch(
            ".NET Framework 4.8",
            "Microsoft .NET Framework 4.7/4.8"));
        Assert.True(TechnicalSkillMatcher.IsMatch(
            ".NET Framework 4.7",
            "Microsoft .NET Framework 4.7 or 4.8"));
        Assert.False(TechnicalSkillMatcher.IsMatch(
            ".NET Framework 4.6",
            "Microsoft .NET Framework 4.7/4.8"));
    }

    [Fact]
    public void IsMatch_allows_specific_candidate_for_general_same_family_but_not_reverse()
    {
        Assert.True(TechnicalSkillMatcher.IsMatch(
            ".NET Framework 4.8",
            ".NET Framework"));
        Assert.False(TechnicalSkillMatcher.IsMatch(
            ".NET Framework",
            ".NET Framework 4.8"));
    }

    [Theory]
    [InlineData(".NET", ".NET Core")]
    [InlineData(".NET", ".NET Framework")]
    [InlineData(".NET Core", ".NET Framework")]
    [InlineData("SQL", "SQL Server")]
    [InlineData("SQL", "PostgreSQL")]
    [InlineData("Java", "JavaScript")]
    [InlineData("C", "C++")]
    [InlineData("C", "C#")]
    [InlineData("Azure", "Azure Functions")]
    [InlineData("Docker", "Kubernetes")]
    public void IsMatch_rejects_similar_but_non_equivalent_technologies(
        string candidateSkill,
        string requiredSkill)
    {
        Assert.False(TechnicalSkillMatcher.IsMatch(candidateSkill, requiredSkill));
        Assert.False(TechnicalSkillMatcher.IsMatch(requiredSkill, candidateSkill));
    }

    [Fact]
    public void IsMentioned_supports_aliases_and_versions_without_substring_collisions()
    {
        const string description =
            "Modernize Microsoft .NET Framework 4.7/4.8 services backed by MS SQL.";

        Assert.True(TechnicalSkillMatcher.IsMentioned(".NET Framework 4.8", description));
        Assert.True(TechnicalSkillMatcher.IsMentioned("SQL Server", description));
        Assert.False(TechnicalSkillMatcher.IsMentioned("Java", "Develop JavaScript applications."));
        Assert.False(TechnicalSkillMatcher.IsMentioned("C", "Develop C++ and C# applications."));
    }
}
