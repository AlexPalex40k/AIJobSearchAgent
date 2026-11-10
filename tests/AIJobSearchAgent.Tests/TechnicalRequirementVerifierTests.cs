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
            "C#, ASP.NET Core, and Docker are required.");

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
}
