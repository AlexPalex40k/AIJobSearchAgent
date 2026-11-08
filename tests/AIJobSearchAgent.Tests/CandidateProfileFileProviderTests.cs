using AIJobSearchAgent.Application.Exceptions;
using AIJobSearchAgent.Infrastructure.Profiles;
using Microsoft.Extensions.Options;

namespace AIJobSearchAgent.Tests;

public sealed class CandidateProfileFileProviderTests
{
    [Fact]
    public async Task LoadAsync_LoadsValidProfileAndCreatesStableHash()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "profile.json"), ValidProfileJson);
            var provider = CreateProvider(directory);

            var first = await provider.LoadAsync(CancellationToken.None);
            var second = await provider.LoadAsync(CancellationToken.None);

            Assert.Equal("Backend Engineer", first.Profile.ProfessionalTitle);
            Assert.Equal(5, first.Profile.YearsProfessionalExperience);
            var skill = Assert.Single(first.Profile.TechnicalSkills);
            Assert.Equal("C#", skill.Name);
            Assert.Equal("advanced", skill.Level);
            Assert.Equal("B2", Assert.Single(first.Profile.SpokenLanguages).Level);
            Assert.Equal(64, first.ProfileHash.Length);
            Assert.Equal(first.ProfileHash, second.ProfileHash);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_RejectsInvalidProfile()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            const string invalidJson = """
                {
                  "professionalTitle": "Backend Engineer",
                  "yearsProfessionalExperience": -1,
                  "technicalSkills": [{ "name": "C#", "level": "advanced" }]
                }
                """;
            await File.WriteAllTextAsync(Path.Combine(directory, "profile.json"), invalidJson);
            var provider = CreateProvider(directory);

            var exception = await Assert.ThrowsAsync<CandidateProfileException>(
                () => provider.LoadAsync(CancellationToken.None));

            Assert.Contains("yearsProfessionalExperience", exception.Message);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_RejectsMalformedOrMissingProfile()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var provider = CreateProvider(directory);
            await Assert.ThrowsAsync<CandidateProfileException>(
                () => provider.LoadAsync(CancellationToken.None));

            await File.WriteAllTextAsync(Path.Combine(directory, "profile.json"), "{not-json");
            await Assert.ThrowsAsync<CandidateProfileException>(
                () => provider.LoadAsync(CancellationToken.None));

            await File.WriteAllTextAsync(
                Path.Combine(directory, "profile.json"),
                """
                {
                  "professionalTitle": "Backend Engineer",
                  "yearsProfessionalExperience": 2,
                  "technicalSkills": null
                }
                """);
            await Assert.ThrowsAsync<CandidateProfileException>(
                () => provider.LoadAsync(CancellationToken.None));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("""
        {
          "professionalTitle": "Backend Engineer",
          "yearsProfessionalExperience": 5,
          "technicalSkills": [{ "name": "C#" }],
          "unknownProperty": true
        }
        """)]
    [InlineData("""
        {
          "professionalTitle": "Backend Engineer",
          "yearsProfessionalExperience": 5,
          "technicalSkills": [{ "name": "C#" }],
          "preferredJobTitle": ["Backend Engineer"]
        }
        """)]
    [InlineData("""
        {
          "professionalTitle": "Backend Engineer",
          "yearsProfessionalExperience": 5,
          "technicalSkills": [{ "name": "C#", "certification": "Example" }]
        }
        """)]
    public async Task LoadAsync_RejectsUnmappedRootOptionalAndNestedProperties(string json)
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "profile.json"), json);
            var provider = CreateProvider(directory);

            await Assert.ThrowsAsync<CandidateProfileException>(
                () => provider.LoadAsync(CancellationToken.None));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("A1")]
    [InlineData("A2")]
    [InlineData("B1")]
    [InlineData("B2")]
    [InlineData("C1")]
    [InlineData("C2")]
    [InlineData("native")]
    [InlineData("b2")]
    [InlineData("NATIVE")]
    public async Task LoadAsync_AcceptsSupportedLanguageLevelsCaseInsensitively(string level)
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var json = $$"""
                {
                  "professionalTitle": "Backend Engineer",
                  "yearsProfessionalExperience": 5,
                  "technicalSkills": [{ "name": "C#", "level": "advanced" }],
                  "spokenLanguages": [{ "language": "English", "level": "{{level}}" }]
                }
                """;
            await File.WriteAllTextAsync(Path.Combine(directory, "profile.json"), json);
            var provider = CreateProvider(directory);

            var snapshot = await provider.LoadAsync(CancellationToken.None);

            Assert.Equal(level, Assert.Single(snapshot.Profile.SpokenLanguages).Level);
            Assert.Equal(5, snapshot.Profile.YearsProfessionalExperience);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("B3")]
    [InlineData("professional working proficiency")]
    [InlineData("fluent")]
    public async Task LoadAsync_RejectsUnsupportedLanguageLevelsWithSupportedValues(string level)
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var json = $$"""
                {
                  "professionalTitle": "Backend Engineer",
                  "yearsProfessionalExperience": 5,
                  "technicalSkills": [{ "name": "C#" }],
                  "spokenLanguages": [{ "language": "English", "level": "{{level}}" }]
                }
                """;
            await File.WriteAllTextAsync(Path.Combine(directory, "profile.json"), json);
            var provider = CreateProvider(directory);

            var exception = await Assert.ThrowsAsync<CandidateProfileException>(
                () => provider.LoadAsync(CancellationToken.None));

            Assert.Contains(level, exception.Message);
            Assert.Contains("A1, A2, B1, B2, C1, C2, or native", exception.Message);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(
        "yearsExperience",
        "remove deprecated 'technicalSkills[].yearsExperience'",
        """
        {
          "professionalTitle": "Backend Engineer",
          "yearsProfessionalExperience": 5,
          "technicalSkills": [{ "name": "C#", "yearsExperience": 5 }]
        }
        """)]
    [InlineData(
        "proficiency",
        "rename deprecated 'spokenLanguages[].proficiency' to 'spokenLanguages[].level'",
        """
        {
          "professionalTitle": "Backend Engineer",
          "yearsProfessionalExperience": 5,
          "technicalSkills": [{ "name": "C#" }],
          "spokenLanguages": [{ "language": "English", "proficiency": "professional" }]
        }
        """)]
    public async Task LoadAsync_RejectsDeprecatedProfileFieldsWithMigrationGuidance(
        string deprecatedProperty,
        string guidance,
        string json)
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "profile.json"), json);
            var provider = CreateProvider(directory);

            var exception = await Assert.ThrowsAsync<CandidateProfileException>(
                () => provider.LoadAsync(CancellationToken.None));

            Assert.Contains(deprecatedProperty, exception.Message);
            Assert.Contains(guidance, exception.Message);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static CandidateProfileFileProvider CreateProvider(string directory) => new(
        Options.Create(new CandidateProfileOptions { Path = "profile.json" }),
        new CandidateProfileFileLocation(directory));

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"AIJobSearchAgent-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private const string ValidProfileJson = """
        {
          "professionalTitle": "Backend Engineer",
          "yearsProfessionalExperience": 5,
          "technicalSkills": [
            { "name": "C#", "level": "advanced" }
          ],
          "preferredJobTitles": ["Backend Engineer"],
          "preferredLocations": ["Remote"],
          "remoteWorkPreference": "remotePreferred",
          "spokenLanguages": [
            { "language": "English", "level": "B2" }
          ]
        }
        """;
}
