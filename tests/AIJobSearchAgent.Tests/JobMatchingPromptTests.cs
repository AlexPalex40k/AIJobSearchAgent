using System.Text.Json;
using AIJobSearchAgent.Application.Services;
using AIJobSearchAgent.Domain.Entities;
using AIJobSearchAgent.Domain.Profiles;

namespace AIJobSearchAgent.Tests;

public sealed class JobMatchingPromptTests
{
    [Fact]
    public void CreateUserMessage_ExcludesCandidateNameAndIncludesEvaluationFields()
    {
        const string privateName = "Private Candidate Name";
        var profile = new CandidateProfile
        {
            Name = privateName,
            ProfessionalTitle = "Backend Engineer",
            YearsProfessionalExperience = 5,
            TechnicalSkills = [new CandidateSkill { Name = "C#", YearsExperience = 5 }],
            PreferredJobTitles = ["Backend Engineer"],
            PreferredLocations = ["Remote"],
            RemoteWorkPreference = RemoteWorkPreference.RemotePreferred,
            SpokenLanguages = [new SpokenLanguage { Language = "English", Proficiency = "professional" }],
            AdditionalExperienceOrPreferences = "Backend APIs"
        };
        var vacancy = new JobVacancy
        {
            ExternalId = "job-1",
            Source = "Test",
            Title = ".NET Engineer",
            Company = "Example",
            Location = "Remote",
            Description = "Build APIs.",
            Url = "https://example.test/jobs/1"
        };

        var message = JobMatchingPrompt.CreateUserMessage(vacancy, profile, 30_000);

        Assert.DoesNotContain(privateName, message, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(message);
        var candidate = document.RootElement.GetProperty("candidateProfile");
        Assert.False(candidate.TryGetProperty("name", out _));
        Assert.Equal("Backend Engineer", candidate.GetProperty("professionalTitle").GetString());
        Assert.Equal("remotePreferred", candidate.GetProperty("remoteWorkPreference").GetString());
        Assert.Equal("Backend APIs", candidate.GetProperty("relevantProfessionalPreferences").GetString());
    }
}
