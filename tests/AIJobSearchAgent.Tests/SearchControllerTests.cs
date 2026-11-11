using AIJobSearchAgent.Api.Contracts;
using AIJobSearchAgent.Api.Controllers;
using AIJobSearchAgent.Application.Abstractions;
using AIJobSearchAgent.Application.Models;
using AIJobSearchAgent.Application.UseCases;
using AIJobSearchAgent.Domain.Entities;
using AIJobSearchAgent.Domain.Profiles;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;

namespace AIJobSearchAgent.Tests;

public sealed class SearchControllerTests
{
    [Fact]
    public async Task Evaluate_maps_valid_request_and_returns_ranked_result()
    {
        var provider = new CapturingLinkedInProvider();
        CandidateProfile? receivedProfile = null;
        var matchingService = new CapturingMatchingService((_, profile, _) =>
        {
            receivedProfile = profile;
            return Task.FromResult(new JobMatchResult(
                5,
                ["C#"],
                [],
                [],
                "Strong match.",
                "test-model",
                "test-prompt",
                DateTimeOffset.UtcNow));
        });
        var controller = CreateController(provider, matchingService);
        var request = CreateValidRequest();

        var action = await controller.Evaluate(request, CancellationToken.None);

        var response = Assert.IsType<SearchAndEvaluateJobsResult>(
            Assert.IsType<OkObjectResult>(action.Result).Value);
        Assert.Equal(1, response.SuccessfullyEvaluated);
        Assert.NotNull(provider.ReceivedCriteria);
        Assert.Equal(".NET", provider.ReceivedCriteria.JobTitle);
        Assert.Equal("Serbia", provider.ReceivedCriteria.Location);
        Assert.Equal(7, provider.ReceivedCriteria.PublishedWithinDays);
        Assert.Equal(10, provider.ReceivedCriteria.MaxJobs);
        Assert.NotNull(receivedProfile);
        Assert.Equal("Senior .NET Developer", receivedProfile.ProfessionalTitle);
        Assert.Equal(7, receivedProfile.YearsProfessionalExperience);
        Assert.Equal("C#", Assert.Single(receivedProfile.TechnicalSkills).Name);
        Assert.Equal("B2", Assert.Single(receivedProfile.SpokenLanguages).Level);
    }

    public static IEnumerable<object[]> InvalidSearchRequests()
    {
        yield return [CreateValidRequest(jobTitle: null)];
        yield return [CreateValidRequest(jobTitle: string.Empty)];
        yield return [CreateValidRequest(jobTitle: "   ")];
        yield return [CreateValidRequest(jobTitle: new string('x', 101))];
        yield return [CreateValidRequest(location: null)];
        yield return [CreateValidRequest(location: "   ")];
        yield return [CreateValidRequest(publishedWithinDays: 30)];
        yield return [CreateValidRequest(maxJobs: 0)];
        yield return [CreateValidRequest(maxJobs: 11)];
    }

    [Theory]
    [MemberData(nameof(InvalidSearchRequests))]
    public async Task Evaluate_rejects_invalid_search_parameters_without_invoking_provider(
        SearchAndEvaluateRequest request)
    {
        var provider = new CapturingLinkedInProvider();
        var controller = CreateController(provider);

        var action = await controller.Evaluate(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(action.Result);
        Assert.False(provider.Called);
    }

    public static IEnumerable<object[]> InvalidCandidateProfiles()
    {
        yield return [null!];
        yield return [CreateValidProfile(professionalTitle: null)];
        yield return [CreateValidProfile(professionalTitle: "   ")];
        yield return [CreateValidProfile(professionalTitle: new string('x', 201))];
        yield return [CreateValidProfile(yearsProfessionalExperience: -1)];
        yield return [CreateValidProfile(technicalSkills: [])];
        yield return [CreateValidProfile(technicalSkills: [null])];
        yield return [CreateValidProfile(technicalSkills: [new SearchCandidateSkillRequest()])];
        yield return [new SearchCandidateProfileRequest
        {
            ProfessionalTitle = "Senior .NET Developer",
            YearsProfessionalExperience = 7,
            TechnicalSkills =
            [
                new SearchCandidateSkillRequest { Name = "C#", Level = "Advanced" }
            ],
            SpokenLanguages = null
        }];
        yield return [CreateValidProfile(spokenLanguages: [null])];
        yield return [CreateValidProfile(spokenLanguages:
        [
            new SearchSpokenLanguageRequest { Language = "English", Level = "fluent" }
        ])];
    }

    [Theory]
    [MemberData(nameof(InvalidCandidateProfiles))]
    public async Task Evaluate_rejects_invalid_candidate_profile_without_invoking_provider(
        SearchCandidateProfileRequest candidateProfile)
    {
        var provider = new CapturingLinkedInProvider();
        var controller = CreateController(provider);
        var request = new SearchAndEvaluateRequest
        {
            JobTitle = ".NET",
            Location = "Serbia",
            CandidateProfile = candidateProfile
        };

        var action = await controller.Evaluate(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(action.Result);
        Assert.False(provider.Called);
    }

    [Fact]
    public async Task Evaluate_is_not_available_outside_development()
    {
        var provider = new CapturingLinkedInProvider();
        var controller = CreateController(
            provider,
            environmentName: "Production");

        var action = await controller.Evaluate(CreateValidRequest(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(action.Result);
        Assert.False(provider.Called);
    }

    private static SearchController CreateController(
        CapturingLinkedInProvider provider,
        IJobMatchingService? matchingService = null,
        string environmentName = "Development")
    {
        matchingService ??= new CapturingMatchingService((_, _, _) =>
            Task.FromResult(new JobMatchResult(
                3,
                [],
                [],
                [],
                "Match.",
                "test-model",
                "test-prompt",
                DateTimeOffset.UtcNow)));
        return new SearchController(
            new SearchAndEvaluateJobsUseCase(provider, matchingService),
            new StubEnvironment(environmentName));
    }

    private static SearchAndEvaluateRequest CreateValidRequest(
        string? jobTitle = ".NET",
        string? location = "Serbia",
        int? publishedWithinDays = 7,
        int maxJobs = 10) => new()
        {
            JobTitle = jobTitle,
            Location = location,
            PublishedWithinDays = publishedWithinDays,
            MaxJobs = maxJobs,
            CandidateProfile = CreateValidProfile()
        };

    private static SearchCandidateProfileRequest CreateValidProfile(
        string? professionalTitle = "Senior .NET Developer",
        decimal yearsProfessionalExperience = 7,
        IReadOnlyList<SearchCandidateSkillRequest?>? technicalSkills = null,
        IReadOnlyList<SearchSpokenLanguageRequest?>? spokenLanguages = null) => new()
        {
            ProfessionalTitle = professionalTitle,
            YearsProfessionalExperience = yearsProfessionalExperience,
            TechnicalSkills = technicalSkills ??
            [
                new SearchCandidateSkillRequest { Name = "C#", Level = "Advanced" }
            ],
            SpokenLanguages = spokenLanguages ??
            [
                new SearchSpokenLanguageRequest { Language = "English", Level = "b2" }
            ]
        };

    private sealed class CapturingLinkedInProvider : ILinkedInJobProvider
    {
        public string SourceName => "LinkedIn";
        public bool Called { get; private set; }
        public LinkedInJobSearchCriteria? ReceivedCriteria { get; private set; }

        public Task<IReadOnlyList<JobVacancy>> SearchAsync(
            JobSearchCriteria criteria,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<LinkedInJobSearchResult> SearchWithDiagnosticsAsync(
            LinkedInJobSearchCriteria criteria,
            CancellationToken cancellationToken)
        {
            Called = true;
            ReceivedCriteria = criteria;
            IReadOnlyList<JobVacancy> vacancies =
            [
                new JobVacancy
                {
                    ExternalId = "job-1",
                    Source = "LinkedIn",
                    Title = ".NET Engineer",
                    Company = "Example",
                    Location = "Serbia",
                    Description = "Build APIs with C#.",
                    Url = "https://www.linkedin.com/jobs/view/job-1"
                }
            ];
            return Task.FromResult(new LinkedInJobSearchResult(
                vacancies,
                false,
                false,
                false,
                null,
                []));
        }
    }

    private sealed class CapturingMatchingService(
        Func<JobVacancy, CandidateProfile, CancellationToken, Task<JobMatchResult>> handler)
        : IJobMatchingService
    {
        public string ModelName => "test-model";
        public string PromptVersion => "test-prompt";

        public Task<JobMatchResult> EvaluateAsync(
            JobVacancy vacancy,
            CandidateProfile profile,
            CancellationToken cancellationToken) => handler(vacancy, profile, cancellationToken);
    }

    private sealed class StubEnvironment(string environmentName) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "AIJobSearchAgent.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = Directory.GetCurrentDirectory();
        public string EnvironmentName { get; set; } = environmentName;
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
