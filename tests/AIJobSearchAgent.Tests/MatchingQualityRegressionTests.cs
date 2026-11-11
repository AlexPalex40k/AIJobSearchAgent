using AIJobSearchAgent.Application.Models;
using AIJobSearchAgent.Application.Services;
using AIJobSearchAgent.Domain.Profiles;

namespace AIJobSearchAgent.Tests;

public sealed class MatchingQualityRegressionTests
{
    private readonly JobMatchCalculator calculator = new();

    [Fact]
    public void Synthetic_logifuture_case_requires_explicit_product_and_version_evidence()
    {
        var extracted = CreateRequirements(
            [
                "Microsoft .NET Framework 4.7/4.8",
                "Microsoft SQL Server",
                "Redis"
            ],
            core:
            [
                "Microsoft .NET Framework 4.7/4.8",
                "Microsoft SQL Server",
                "Redis"
            ]);
        const string description =
            "Modernize backend services using Microsoft .NET Framework 4.7/4.8. Microsoft SQL Server and Redis are required.";
        var verified = TechnicalRequirementVerifier.Verify(extracted, description).VerifiedRequirements;

        var genericProfileResult = calculator.Calculate(
            verified,
            CreateProfile("C#", ".NET", "SQL"));
        var explicitProfileResult = calculator.Calculate(
            verified,
            CreateProfile(".NET Framework 4.8", "Microsoft SQL Server"));

        Assert.Equal(1, genericProfileResult.Rating);
        Assert.Empty(genericProfileResult.MatchedSkills);
        Assert.Equal(3, explicitProfileResult.Rating);
        Assert.Equal(
            ["Microsoft .NET Framework 4.7/4.8", "Microsoft SQL Server"],
            explicitProfileResult.MatchedSkills);
        Assert.Equal(["Redis"], explicitProfileResult.MissingRequiredSkills);
        Assert.Contains("not listed in the candidate profile", explicitProfileResult.Explanation);
    }

    [Fact]
    public void Synthetic_numerator_case_does_not_treat_context_and_practices_as_mandatory()
    {
        var rawRequired = new[]
        {
            "C#", ".NET Core", "ASP.NET Core", "Azure App Services", "Azure Functions",
            "Azure SQL", "RESTful APIs", "Microservices", "SOLID principles", "SQL Server",
            "Git/GitHub", "HTML", "CSS", "JavaScript", "Chrome DevTools", "Fiddler",
            "Testing frameworks"
        };
        var extracted = CreateRequirements(rawRequired, core: ["C#", ".NET Core", "ASP.NET Core"]);
        const string description = """
            Requirements:
            - Build RESTful APIs and microservices with C#, .NET Core, and ASP.NET Core.
            Preferred:
            - Azure Functions experience is a plus.
            Possible technologies used across company projects include Azure App Services, Azure SQL,
              SQL Server, Git/GitHub, HTML, CSS, JavaScript, Chrome DevTools, Fiddler, and testing frameworks.
            Participate in code reviews and follow SOLID principles.
            """;

        var verification = TechnicalRequirementVerifier.Verify(extracted, description);
        var result = calculator.Calculate(
            verification.VerifiedRequirements,
            CreateProfile("C#", ".NET", "ASP.NET Core", "SQL"));

        Assert.Equal(
            ["C#", ".NET Core", "ASP.NET Core", "RESTful APIs", "Microservices"],
            verification.VerifiedRequirements.RequiredSkills);
        Assert.Equal(["Azure Functions"], verification.VerifiedRequirements.OptionalSkills);
        Assert.Contains("SOLID principles", verification.UnverifiedRequiredSkills);
        Assert.Contains("Chrome DevTools", verification.UnverifiedRequiredSkills);
        Assert.Contains(
            verification.ReclassifiedSkills,
            change => change.Skill == "Azure Functions" &&
                change.VerifiedClassification == "Optional");
        Assert.Equal(3, result.Rating);
        Assert.Equal(["C#", "ASP.NET Core"], result.MatchedSkills);
        Assert.Equal(
            [".NET Core", "RESTful APIs", "Microservices"],
            result.MissingRequiredSkills);
        Assert.Equal(["Azure Functions"], result.MissingOptionalSkills);
    }

    [Fact]
    public void Synthetic_renesas_case_preserves_a_genuine_cloud_platform_core_mismatch()
    {
        var extracted = CreateRequirements(
            [
                "Microservices",
                "AWS",
                "Docker",
                "Kubernetes",
                "Microsoft .NET Core",
                "Microsoft .NET Framework 4.8"
            ],
            core: ["Microservices", "AWS", "Docker", "Kubernetes"]);
        const string description = """
            The primary platform responsibility is to build and operate microservices on AWS using Docker and Kubernetes.
            Maintain supporting services written in Microsoft .NET Core
            and Microsoft .NET Framework 4.8.
            """;
        var verification = TechnicalRequirementVerifier.Verify(extracted, description);

        var result = calculator.Calculate(
            verification.VerifiedRequirements,
            CreateProfile("C#", ".NET Core", ".NET Framework 4.8"));

        Assert.Equal(
            ["Microservices", "AWS", "Docker", "Kubernetes"],
            verification.VerifiedRequirements.CoreRequiredSkills);
        Assert.Equal(1, result.Rating);
        Assert.Contains("role-defining core technologies", result.Explanation);
    }

    private static ExtractedJobRequirements CreateRequirements(
        IReadOnlyList<string> required,
        IReadOnlyList<string>? optional = null,
        IReadOnlyList<string>? core = null) => new(
            required,
            optional ?? [],
            true,
            null,
            [])
        {
            CoreRequiredSkills = core ?? []
        };

    private static CandidateProfile CreateProfile(params string[] skills) => new()
    {
        ProfessionalTitle = "Senior Software Engineer",
        YearsProfessionalExperience = 7,
        TechnicalSkills = skills
            .Select(skill => new CandidateSkill { Name = skill, Level = "Advanced" })
            .ToArray()
    };
}
