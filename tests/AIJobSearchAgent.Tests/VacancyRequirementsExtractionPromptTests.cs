using System.Text.Json;
using AIJobSearchAgent.Application.Services;
using AIJobSearchAgent.Domain.Entities;

namespace AIJobSearchAgent.Tests;

public sealed class VacancyRequirementsExtractionPromptTests
{
    [Fact]
    public void CreateUserMessage_IncludesOnlyBoundedUntrustedDescription()
    {
        var vacancy = new JobVacancy
        {
            ExternalId = "job-1",
            Source = "Test",
            Title = "Private title metadata",
            Company = "Private company metadata",
            Location = "Private location metadata",
            IsRemote = true,
            Description = "0123456789",
            Url = "https://example.test/jobs/1"
        };

        var message = VacancyRequirementsExtractionPrompt.CreateUserMessage(vacancy, 5);

        using var document = JsonDocument.Parse(message);
        Assert.Equal("01234", document.RootElement.GetProperty("vacancyDescriptionUntrusted").GetString());
        Assert.Single(document.RootElement.EnumerateObject());
        Assert.DoesNotContain(vacancy.Title, message, StringComparison.Ordinal);
        Assert.Contains("Never assign overall experience years to a technology", VacancyRequirementsExtractionPrompt.SystemInstructions);
    }
}
