using AIJobSearchAgent.Application.Models;
using AIJobSearchAgent.Application.Services;
using AIJobSearchAgent.Domain.Profiles;

namespace AIJobSearchAgent.Tests;

public sealed class JobMatchCalculatorTests
{
    private readonly JobMatchCalculator calculator = new();

    [Fact]
    public void Calculate_ReturnsFiveWhenAllRequiredAndOptionalSkillsMatch()
    {
        var result = Calculate(["C#", "SQL"], ["Docker"], ["C#", "SQL", "Docker"]);

        Assert.Equal(5, result.Rating);
        Assert.Equal(["C#", "SQL", "Docker"], result.MatchedSkills);
        Assert.Empty(result.MissingRequiredSkills);
        Assert.Empty(result.MissingOptionalSkills);
    }

    [Fact]
    public void Calculate_ReturnsFourWhenOnlyAnOptionalSkillIsMissing()
    {
        var result = Calculate(["C#", "SQL"], ["Docker"], ["C#", "SQL"]);

        Assert.Equal(4, result.Rating);
        Assert.Equal(["Docker"], result.MissingOptionalSkills);
    }

    [Fact]
    public void Calculate_ReturnsThreeWhenSomeRequiredSkillsAreMissing()
    {
        var result = Calculate(["C#", "SQL"], [], ["C#"]);

        Assert.Equal(3, result.Rating);
        Assert.Equal(["C#"], result.MatchedSkills);
        Assert.Equal(["SQL"], result.MissingRequiredSkills);
    }

    [Fact]
    public void Calculate_ReturnsTwoWhenRequirementsAreNotClear()
    {
        var result = Calculate([], [], ["C#"], hasClearRequirements: false);

        Assert.Equal(2, result.Rating);
        Assert.StartsWith("Insufficient information:", result.Explanation);
    }

    [Fact]
    public void Calculate_ReturnsOneWhenNoRequiredSkillsMatch()
    {
        var result = Calculate(["Java", "Spring"], [], ["C#", "ASP.NET Core"]);

        Assert.Equal(1, result.Rating);
        Assert.Equal(["Java", "Spring"], result.MissingRequiredSkills);
    }

    [Theory]
    [MemberData(nameof(CoreStackMismatchCases))]
    public void Calculate_ReturnsOneWhenOnlySupportingRequiredSkillsMatch(
        IReadOnlyList<string> requiredSkills,
        IReadOnlyList<string> coreRequiredSkills,
        IReadOnlyList<string> candidateSkills)
    {
        var requirements = CreateRequirements(
            requiredSkills,
            [],
            coreRequiredSkills: coreRequiredSkills);

        var result = calculator.Calculate(requirements, CreateProfile(candidateSkills));

        Assert.Equal(1, result.Rating);
        Assert.Contains("role-defining core technologies", result.Explanation);
    }

    public static TheoryData<IReadOnlyList<string>, IReadOnlyList<string>, IReadOnlyList<string>> CoreStackMismatchCases => new()
    {
        { ["Python", "FastAPI", "Docker"], ["Python", "FastAPI"], ["Docker"] },
        { ["Java", "Spring Boot", "SQL", "Docker"], ["Java", "Spring Boot"], ["SQL", "Docker"] },
        { ["C#", "WPF", "XAML", "MVVM"], ["WPF", "XAML", "MVVM"], ["C#"] }
    };

    [Fact]
    public void Calculate_PreservesPartialRatingWhenSomeCoreSkillsMatch()
    {
        var requirements = CreateRequirements(
            ["C#", "ASP.NET Core", "React"],
            [],
            coreRequiredSkills: ["C#", "ASP.NET Core", "React"]);

        var result = calculator.Calculate(requirements, CreateProfile(["C#", "ASP.NET Core"]));

        Assert.Equal(3, result.Rating);
        Assert.Equal(["React"], result.MissingRequiredSkills);
    }

    [Theory]
    [InlineData(false, 5)]
    [InlineData(true, 4)]
    public void Calculate_AllCoreSkillsMatchPreservesExistingExcellentAndGoodRatings(
        bool omitOptionalSkill,
        int expectedRating)
    {
        var requirements = CreateRequirements(
            ["C#", "ASP.NET Core"],
            ["Docker"],
            coreRequiredSkills: ["C#", "ASP.NET Core"]);
        var candidateSkills = omitOptionalSkill
            ? new[] { "C#", "ASP.NET Core" }
            : new[] { "C#", "ASP.NET Core", "Docker" };

        var result = calculator.Calculate(requirements, CreateProfile(candidateSkills));

        Assert.Equal(expectedRating, result.Rating);
    }

    [Fact]
    public void Calculate_InvalidCoreSkillsFallBackToExistingRatingLogic()
    {
        var requirements = CreateRequirements(
            ["C#", "Docker"],
            [],
            coreRequiredSkills: ["Python"]);

        var result = calculator.Calculate(requirements, CreateProfile(["Docker"]));

        Assert.Equal(3, result.Rating);
    }

    [Fact]
    public void Calculate_IgnoresExtraCandidateSkillsAndRemovesDuplicates()
    {
        var baseline = Calculate(["C#", "c sharp"], [], ["C#"]);
        var withExtras = Calculate(["C#", "c sharp"], [], ["C#", "Go", "Rust", "Python"]);

        Assert.Equal(5, baseline.Rating);
        Assert.Equal(baseline.Rating, withExtras.Rating);
        Assert.Equal(baseline.MatchedSkills, withExtras.MatchedSkills);
        Assert.Equal(baseline.MissingRequiredSkills, withExtras.MissingRequiredSkills);
        Assert.Equal(baseline.MissingOptionalSkills, withExtras.MissingOptionalSkills);
        Assert.Equal(baseline.Explanation, withExtras.Explanation);
        Assert.Equal(["C#"], baseline.MatchedSkills);
    }

    [Fact]
    public void Calculate_MatchesOnlyExplicitTechnologyAliases()
    {
        var aliasResult = Calculate(
            ["C Sharp", "Entity Framework Core", "ASP.NET Core"],
            [],
            ["C#", "EF Core", "ASP Net Core"]);
        var unsupportedResult = Calculate(["PostgreSQL", "JavaScript"], [], ["SQL", "Java"]);

        Assert.Equal(5, aliasResult.Rating);
        Assert.Equal(1, unsupportedResult.Rating);
        Assert.Equal(["PostgreSQL", "JavaScript"], unsupportedResult.MissingRequiredSkills);
    }

    [Fact]
    public void Calculate_ConfirmedLanguageMismatchCapsRatingAtThree()
    {
        var requirements = CreateRequirements(
            ["C#"],
            [],
            requiredLanguages: [new RequiredLanguage("English", "C1")]);
        var profile = CreateProfile(
            ["C#"],
            spokenLanguages: [new SpokenLanguage { Language = "English", Level = "B2" }]);

        var result = calculator.Calculate(requirements, profile);

        Assert.Equal(3, result.Rating);
        Assert.Contains("language requirement not met", result.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Calculate_MissingLanguageDataIsUnknownAndCapsRatingAtThree()
    {
        var requirements = CreateRequirements(
            ["C#"],
            [],
            requiredLanguages: [new RequiredLanguage("English", "C1")]);

        var result = calculator.Calculate(requirements, CreateProfile(["C#"]));

        Assert.Equal(3, result.Rating);
        Assert.Contains("not verified", result.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("not met", result.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Calculate_ConfirmedLanguageMatchAllowsRatingFive()
    {
        var requirements = CreateRequirements(
            ["C#"],
            [],
            requiredLanguages: [new RequiredLanguage("English", "B2")]);
        var profile = CreateProfile(
            ["C#"],
            spokenLanguages: [new SpokenLanguage { Language = "English", Level = "C1" }]);

        var result = calculator.Calculate(requirements, profile);

        Assert.Equal(5, result.Rating);
        Assert.DoesNotContain("language requirement", result.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Calculate_OverallExperienceDoesNotProveARequiredTechnology()
    {
        var requirements = CreateRequirements(["Kubernetes"], [], minimumYears: 3);
        var profile = CreateProfile(["C#"], yearsProfessionalExperience: 10);

        var result = calculator.Calculate(requirements, profile);

        Assert.Equal(1, result.Rating);
        Assert.Equal(["Kubernetes"], result.MissingRequiredSkills);
    }

    [Fact]
    public void Calculate_OverallExperienceShortfallCapsExcellentMatchAtFour()
    {
        var requirements = CreateRequirements(["C#"], [], minimumYears: 5);
        var profile = CreateProfile(["C#"], yearsProfessionalExperience: 3);

        var result = calculator.Calculate(requirements, profile);

        Assert.Equal(4, result.Rating);
        Assert.Contains("Overall professional experience", result.Explanation);
    }

    private JobMatchCalculation Calculate(
        IReadOnlyList<string> required,
        IReadOnlyList<string> optional,
        IReadOnlyList<string> candidateSkills,
        bool hasClearRequirements = true) => calculator.Calculate(
            CreateRequirements(required, optional, hasClearRequirements),
            CreateProfile(candidateSkills));

    private static ExtractedJobRequirements CreateRequirements(
        IReadOnlyList<string> required,
        IReadOnlyList<string> optional,
        bool hasClearRequirements = true,
        decimal? minimumYears = null,
        IReadOnlyList<RequiredLanguage>? requiredLanguages = null,
        IReadOnlyList<string>? coreRequiredSkills = null) => new(
            required,
            optional,
            hasClearRequirements,
            minimumYears,
            requiredLanguages ?? [])
        {
            CoreRequiredSkills = coreRequiredSkills ?? []
        };

    private static CandidateProfile CreateProfile(
        IReadOnlyList<string> skills,
        decimal yearsProfessionalExperience = 5,
        IReadOnlyList<SpokenLanguage>? spokenLanguages = null) => new()
        {
            ProfessionalTitle = "Backend Engineer",
            YearsProfessionalExperience = yearsProfessionalExperience,
            TechnicalSkills = skills.Select(skill => new CandidateSkill { Name = skill }).ToArray(),
            SpokenLanguages = spokenLanguages ?? []
        };
}
