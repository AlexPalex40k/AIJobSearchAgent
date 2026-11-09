using System.Text.Json;
using AIJobSearchAgent.Application.Services;
using AIJobSearchAgent.Domain.Entities;

namespace AIJobSearchAgent.Tests;

public sealed class JobMatchingPromptTests
{
    [Fact]
    public void SystemInstructions_RequireExplicitMandatoryLanguageEvidence()
    {
        Assert.Equal("job-match-simple-v2", JobMatchingPrompt.Version);
        Assert.Contains("Never assume English", JobMatchingPrompt.SystemInstructions);
        Assert.Contains("Use minimumLevel null", JobMatchingPrompt.SystemInstructions);
        Assert.Contains("Use native only when native proficiency is explicitly required", JobMatchingPrompt.SystemInstructions);
        Assert.Contains("mentioned only as company, team, documentation, customer, or location context", JobMatchingPrompt.SystemInstructions);
    }

    [Fact]
    public void CreateUserMessage_ContainsOnlyUntrustedVacancyData()
    {
        var vacancy = new JobVacancy
        {
            ExternalId = "job-1",
            Source = "Test",
            Title = ".NET Engineer",
            Company = "Private Company Detail",
            Location = "Remote",
            Description = "Build APIs with C#.",
            Url = "https://example.test/jobs/1"
        };

        var message = JobMatchingPrompt.CreateUserMessage(vacancy, 30_000);

        using var document = JsonDocument.Parse(message);
        Assert.False(document.RootElement.TryGetProperty("candidateProfile", out _));
        var untrusted = document.RootElement.GetProperty("vacancyDataUntrusted");
        Assert.Equal(".NET Engineer", untrusted.GetProperty("title").GetString());
        Assert.Equal("Build APIs with C#.", untrusted.GetProperty("description").GetString());
        Assert.False(untrusted.TryGetProperty("company", out _));
        Assert.False(untrusted.TryGetProperty("location", out _));
    }

    [Fact]
    public void CreateUserMessage_TruncatesDescriptionAtConfiguredLimit()
    {
        var vacancy = new JobVacancy
        {
            ExternalId = "job-1",
            Source = "Test",
            Title = "Engineer",
            Company = "Example",
            Location = "Remote",
            Description = "123456789",
            Url = "https://example.test/jobs/1"
        };

        var message = JobMatchingPrompt.CreateUserMessage(vacancy, 5);

        using var document = JsonDocument.Parse(message);
        Assert.Equal(
            "12345",
            document.RootElement.GetProperty("vacancyDataUntrusted").GetProperty("description").GetString());
    }
}
