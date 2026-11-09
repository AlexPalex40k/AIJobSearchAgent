using AIJobSearchAgent.Application.Models;
using AIJobSearchAgent.Application.Services;

namespace AIJobSearchAgent.Tests;

public sealed class LanguageRequirementVerifierTests
{
    [Fact]
    public void Verify_RejectsLanguageThatIsNotMentioned()
    {
        var result = LanguageRequirementVerifier.Verify(
            [new RequiredLanguage("English", "native")],
            "C# and ASP.NET Core are required.");

        Assert.Empty(result);
    }

    [Fact]
    public void Verify_DowngradesUnsupportedNativeLevelButPreservesMandatoryLanguage()
    {
        var result = LanguageRequirementVerifier.Verify(
            [new RequiredLanguage("English", "native")],
            "The role requires professional English.");

        var language = Assert.Single(result);
        Assert.Equal("English", language.Language);
        Assert.Null(language.MinimumLevel);
    }

    [Theory]
    [InlineData("English B2 is required.", "English", "B2")]
    [InlineData("English C1 is explicitly mandatory.", "English", "C1")]
    [InlineData("Native English is explicitly required.", "English", "native")]
    [InlineData("German C1 is required.", "German", "C1")]
    public void Verify_PreservesExplicitMandatoryLevels(
        string description,
        string languageName,
        string minimumLevel)
    {
        var result = LanguageRequirementVerifier.Verify(
            [new RequiredLanguage(languageName, minimumLevel)],
            description);

        Assert.Equal(new RequiredLanguage(languageName, minimumLevel), Assert.Single(result));
    }

    [Fact]
    public void Verify_RejectsContextualLanguageMention()
    {
        var result = LanguageRequirementVerifier.Verify(
            [new RequiredLanguage("English", "B2")],
            "Our English-speaking team maintains documentation written in English.");

        Assert.Empty(result);
    }
}
