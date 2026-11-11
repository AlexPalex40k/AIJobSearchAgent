using AIJobSearchAgent.Application.Models;
using AIJobSearchAgent.Application.Services;

namespace AIJobSearchAgent.Tests;

public sealed class TechnicalRequirementVerifierTests
{
    [Fact]
    public void Verify_KeepsOnlyNormalizedUniqueCoreSkillsFromVerifiedRequiredSkills()
    {
        var requirements = new ExtractedJobRequirements(
            ["C#", "ASP.NET Core", "Docker"],
            [],
            true,
            null,
            [])
        {
            CoreRequiredSkills = ["C Sharp", "ASP Net Core", "ASP.NET Core", "Python"]
        };

        var result = TechnicalRequirementVerifier.Verify(
            requirements,
            "Develop backend services with C# and ASP.NET Core. Docker is required.");

        Assert.Equal(["C#", "ASP.NET Core"], result.VerifiedRequirements.CoreRequiredSkills);
    }

    [Fact]
    public void Verify_InvalidOrEmptyCoreSkillsPreserveVerifiedRequirementsWithoutCreatingCoreSkills()
    {
        var requirements = new ExtractedJobRequirements(
            ["C#", "Docker"],
            [],
            true,
            null,
            [])
        {
            CoreRequiredSkills = ["Python"]
        };

        var result = TechnicalRequirementVerifier.Verify(
            requirements,
            "C# and Docker are required. Python is optional.");

        Assert.Equal(["C#", "Docker"], result.VerifiedRequirements.RequiredSkills);
        Assert.Empty(result.VerifiedRequirements.CoreRequiredSkills);
    }

    [Fact]
    public void Verify_uses_sections_and_rejects_context_only_mentions()
    {
        var requirements = new ExtractedJobRequirements(
            ["C#", "Kubernetes", "Docker", "SOLID principles"],
            ["Redis"],
            true,
            null,
            []);
        const string description = """
            Requirements:
            - C#
            Preferred:
            - Redis
            Our infrastructure includes Kubernetes and Docker.
            We value SOLID principles.
            """;

        var result = TechnicalRequirementVerifier.Verify(requirements, description);

        Assert.Equal(["C#"], result.VerifiedRequirements.RequiredSkills);
        Assert.Equal(["Redis"], result.VerifiedRequirements.OptionalSkills);
        Assert.Equal(
            ["Kubernetes", "Docker", "SOLID principles"],
            result.UnverifiedRequiredSkills);
    }

    [Fact]
    public void Verify_reclassifies_explicit_evidence_instead_of_trusting_llm_category()
    {
        var requirements = new ExtractedJobRequirements(
            ["C#", "Angular"],
            ["RabbitMQ"],
            true,
            null,
            []);

        var result = TechnicalRequirementVerifier.Verify(
            requirements,
            "C# is required. Angular is optional. RabbitMQ is mandatory.");

        Assert.Equal(["C#", "RabbitMQ"], result.VerifiedRequirements.RequiredSkills);
        Assert.Equal(["Angular"], result.VerifiedRequirements.OptionalSkills);
        Assert.Equal(2, result.ReclassifiedSkills.Count);
    }

    [Fact]
    public void Verify_keeps_technology_essential_to_a_stated_responsibility()
    {
        var requirements = new ExtractedJobRequirements(
            ["Kubernetes"],
            [],
            true,
            null,
            [])
        {
            CoreRequiredSkills = ["Kubernetes"]
        };

        var result = TechnicalRequirementVerifier.Verify(
            requirements,
            "Build and operate Kubernetes clusters for the primary production platform.");

        Assert.Equal(["Kubernetes"], result.VerifiedRequirements.RequiredSkills);
        Assert.Equal(["Kubernetes"], result.VerifiedRequirements.CoreRequiredSkills);
    }

    [Fact]
    public void Verify_does_not_keep_a_generic_supporting_tool_as_core_without_primary_evidence()
    {
        var requirements = new ExtractedJobRequirements(
            ["C#", "Docker"],
            [],
            true,
            null,
            [])
        {
            CoreRequiredSkills = ["C#", "Docker"]
        };

        var result = TechnicalRequirementVerifier.Verify(
            requirements,
            "Develop production services in C#. Docker is required for packaging.");

        Assert.Equal(["C#"], result.VerifiedRequirements.CoreRequiredSkills);
        Assert.Equal(["Docker"], result.RejectedCoreSkills);
    }

    [Theory]
    [InlineData("Docker is not required for this role.")]
    [InlineData("Docker is not a requirement for this role.")]
    [InlineData("No need for Docker.")]
    public void Verify_rejects_explicit_negative_requirement_evidence(string description)
    {
        var requirements = new ExtractedJobRequirements(
            ["Docker"],
            [],
            true,
            null,
            []);

        var result = TechnicalRequirementVerifier.Verify(requirements, description);

        Assert.Empty(result.VerifiedRequirements.RequiredSkills);
        Assert.Equal(["Docker"], result.UnverifiedRequiredSkills);
    }
}
