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
            Assert.Equal("C#", Assert.Single(first.Profile.TechnicalSkills).Name);
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
                  "technicalSkills": [{ "name": "C#", "yearsExperience": 2 }]
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
            { "name": "C#", "yearsExperience": 5, "level": "advanced" }
          ],
          "preferredJobTitles": ["Backend Engineer"],
          "preferredLocations": ["Remote"],
          "remoteWorkPreference": "remotePreferred",
          "spokenLanguages": [
            { "language": "English", "proficiency": "professional" }
          ]
        }
        """;
}
