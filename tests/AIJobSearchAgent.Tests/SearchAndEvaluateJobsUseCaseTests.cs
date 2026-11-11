using AIJobSearchAgent.Application.Abstractions;
using AIJobSearchAgent.Application.Exceptions;
using AIJobSearchAgent.Application.Models;
using AIJobSearchAgent.Application.UseCases;
using AIJobSearchAgent.Domain.Entities;
using AIJobSearchAgent.Domain.Profiles;

namespace AIJobSearchAgent.Tests;

public sealed class SearchAndEvaluateJobsUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_invokes_provider_and_evaluates_unique_vacancies_with_exact_profile()
    {
        var lowRated = CreateVacancy("low", ratingDescription: "Low", publishedAt: "2026-11-10");
        var highRated = CreateVacancy("high", ratingDescription: "High", publishedAt: "2026-11-09");
        var duplicate = CreateVacancy("high", ratingDescription: "Duplicate");
        var provider = new StubLinkedInProvider([lowRated, highRated, duplicate]);
        var profile = CreateProfile();
        var receivedProfiles = new List<CandidateProfile>();
        var matcher = new StubMatchingService((vacancy, candidateProfile, _) =>
        {
            receivedProfiles.Add(candidateProfile);
            var rating = vacancy.ExternalId == "high" ? 5 : 1;
            return Task.FromResult(CreateMatch(rating));
        });
        var useCase = new SearchAndEvaluateJobsUseCase(provider, matcher);
        var criteria = new LinkedInJobSearchCriteria(".NET", "Serbia", 7, 10);

        var result = await useCase.ExecuteAsync(criteria, profile, CancellationToken.None);

        Assert.Same(criteria, provider.ReceivedCriteria);
        Assert.Equal(2, result.TotalVacancies);
        Assert.Equal(2, result.SuccessfullyEvaluated);
        Assert.Equal(["high", "low"], result.Results.Select(item => item.ExternalId));
        Assert.Equal([5, 1], result.Results.Select(item => item.Rating));
        Assert.All(receivedProfiles, received => Assert.Same(profile, received));
    }

    [Fact]
    public async Task ExecuteAsync_evaluates_sequentially()
    {
        var vacancies = Enumerable.Range(1, 3)
            .Select(index => CreateVacancy(index.ToString()))
            .ToArray();
        var activeCalls = 0;
        var maximumActiveCalls = 0;
        var callOrder = new List<string>();
        var matcher = new StubMatchingService(async (vacancy, _, _) =>
        {
            callOrder.Add(vacancy.ExternalId);
            var active = Interlocked.Increment(ref activeCalls);
            maximumActiveCalls = Math.Max(maximumActiveCalls, active);
            await Task.Yield();
            Interlocked.Decrement(ref activeCalls);
            return CreateMatch(3);
        });
        var useCase = new SearchAndEvaluateJobsUseCase(
            new StubLinkedInProvider(vacancies),
            matcher);

        await useCase.ExecuteAsync(CreateCriteria(), CreateProfile(), CancellationToken.None);

        Assert.Equal(1, maximumActiveCalls);
        Assert.Equal(["1", "2", "3"], callOrder);
    }

    [Fact]
    public async Task ExecuteAsync_sorts_equal_ratings_by_publication_date_then_identity()
    {
        var vacancies = new[]
        {
            CreateVacancy("old", publishedAt: "2026-11-01"),
            CreateVacancy("z", publishedAt: "2026-11-10"),
            CreateVacancy("a", publishedAt: "2026-11-10")
        };
        var useCase = new SearchAndEvaluateJobsUseCase(
            new StubLinkedInProvider(vacancies),
            new StubMatchingService((_, _, _) => Task.FromResult(CreateMatch(4))));

        var result = await useCase.ExecuteAsync(
            CreateCriteria(),
            CreateProfile(),
            CancellationToken.None);

        Assert.Equal(["a", "z", "old"], result.Results.Select(item => item.ExternalId));
    }

    [Fact]
    public async Task ExecuteAsync_keeps_missing_descriptions_and_individual_failures_without_ratings()
    {
        var missingDescription = CreateVacancy("missing", ratingDescription: string.Empty);
        var failed = CreateVacancy("failed");
        var timedOut = CreateVacancy("timeout");
        var succeeded = CreateVacancy("succeeded");
        var matcher = new StubMatchingService((vacancy, _, _) => vacancy.ExternalId switch
        {
            "failed" => throw CreateMatchingException(EvaluationStatus.Failed),
            "timeout" => throw CreateMatchingException(EvaluationStatus.TimedOut),
            _ => Task.FromResult(CreateMatch(2))
        });
        var useCase = new SearchAndEvaluateJobsUseCase(
            new StubLinkedInProvider(
                [missingDescription, failed, timedOut, succeeded],
                isPartial: true,
                limitations: ["Descriptions may require login."]),
            matcher);

        var result = await useCase.ExecuteAsync(
            CreateCriteria(),
            CreateProfile(),
            CancellationToken.None);

        Assert.Equal(1, result.SuccessfullyEvaluated);
        Assert.Equal(2, result.FailedEvaluations);
        Assert.Equal(1, result.NotEvaluated);
        Assert.True(result.IsPartial);
        Assert.Contains("Descriptions may require login.", result.SourceLimitations);
        Assert.Equal(SearchJobEvaluationStatus.NotEvaluated, Find("missing").EvaluationStatus);
        Assert.Equal(SearchJobEvaluationStatus.Failed, Find("failed").EvaluationStatus);
        Assert.Equal(SearchJobEvaluationStatus.TimedOut, Find("timeout").EvaluationStatus);
        Assert.All(
            result.Results.Where(item => item.ExternalId != "succeeded"),
            item =>
            {
                Assert.Null(item.Rating);
                Assert.Null(item.Explanation);
            });
        Assert.Equal(3, matcher.CallCount);

        SearchAndEvaluateJobResult Find(string id) =>
            Assert.Single(result.Results, item => item.ExternalId == id);
    }

    [Fact]
    public async Task ExecuteAsync_omits_invalid_vacancies_and_preserves_blocking_metadata()
    {
        var invalid = CreateVacancy("invalid");
        invalid.Url = "not-a-url";
        var provider = new StubLinkedInProvider(
            [invalid],
            isPartial: true,
            isBlocked: true,
            restrictionReason: "Verification required.",
            limitations: ["Verification required."]);
        var useCase = new SearchAndEvaluateJobsUseCase(
            provider,
            new StubMatchingService((_, _, _) => Task.FromResult(CreateMatch(3))));

        var result = await useCase.ExecuteAsync(
            CreateCriteria(),
            CreateProfile(),
            CancellationToken.None);

        Assert.Empty(result.Results);
        Assert.True(result.IsBlocked);
        Assert.True(result.IsPartial);
        Assert.Equal(2, result.SourceLimitations.Count);
        Assert.Single(result.SourceLimitations, item => item == "Verification required.");
        Assert.Contains(result.SourceLimitations, item => item.Contains("invalid", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_propagates_cancellation_during_matching()
    {
        using var cancellationSource = new CancellationTokenSource();
        var provider = new StubLinkedInProvider(
            [CreateVacancy("one"), CreateVacancy("two")]);
        var matcher = new StubMatchingService((_, _, token) =>
        {
            cancellationSource.Cancel();
            return Task.FromCanceled<JobMatchResult>(token);
        });
        var useCase = new SearchAndEvaluateJobsUseCase(provider, matcher);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => useCase.ExecuteAsync(
            CreateCriteria(),
            CreateProfile(),
            cancellationSource.Token));

        Assert.Equal(cancellationSource.Token, provider.ReceivedCancellationToken);
        Assert.Equal(1, matcher.CallCount);
    }

    [Fact]
    public void Constructor_has_no_repository_or_profile_file_dependency()
    {
        var constructor = Assert.Single(typeof(SearchAndEvaluateJobsUseCase).GetConstructors());

        Assert.Equal(
            [typeof(ILinkedInJobProvider), typeof(IJobMatchingService)],
            constructor.GetParameters().Select(parameter => parameter.ParameterType));
    }

    private static LinkedInJobSearchCriteria CreateCriteria() =>
        new(".NET", "Serbia", 7, 10);

    private static CandidateProfile CreateProfile() => new()
    {
        ProfessionalTitle = "Senior .NET Developer",
        YearsProfessionalExperience = 7,
        TechnicalSkills = [new CandidateSkill { Name = "C#", Level = "Advanced" }],
        SpokenLanguages = [new SpokenLanguage { Language = "English", Level = "B2" }]
    };

    private static JobVacancy CreateVacancy(
        string externalId,
        string ratingDescription = "Build APIs with .NET.",
        string? publishedAt = null) => new()
        {
            ExternalId = externalId,
            Source = "LinkedIn",
            Title = $"Engineer {externalId}",
            Company = "Example",
            Location = "Serbia",
            Description = ratingDescription,
            Url = $"https://www.linkedin.com/jobs/view/{externalId}",
            PublishedAt = publishedAt is null ? null : DateTimeOffset.Parse(publishedAt)
        };

    private static JobMatchResult CreateMatch(int rating) => new(
        rating,
        ["C#"],
        [],
        ["Kubernetes"],
        $"Rating {rating}.",
        "test-model",
        "test-prompt",
        DateTimeOffset.UtcNow);

    private static JobMatchingException CreateMatchingException(EvaluationStatus status) => new(
        "Internal matching detail.",
        "test-model",
        "test-prompt",
        status);

    private sealed class StubLinkedInProvider(
        IReadOnlyList<JobVacancy> vacancies,
        bool isPartial = false,
        bool isBlocked = false,
        string? restrictionReason = null,
        IReadOnlyList<string>? limitations = null) : ILinkedInJobProvider
    {
        public string SourceName => "LinkedIn";
        public LinkedInJobSearchCriteria? ReceivedCriteria { get; private set; }
        public CancellationToken ReceivedCancellationToken { get; private set; }

        public Task<IReadOnlyList<JobVacancy>> SearchAsync(
            JobSearchCriteria criteria,
            CancellationToken cancellationToken) => Task.FromResult(vacancies);

        public Task<LinkedInJobSearchResult> SearchWithDiagnosticsAsync(
            LinkedInJobSearchCriteria criteria,
            CancellationToken cancellationToken)
        {
            ReceivedCriteria = criteria;
            ReceivedCancellationToken = cancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new LinkedInJobSearchResult(
                vacancies,
                isPartial,
                isBlocked,
                false,
                restrictionReason,
                limitations ?? []));
        }
    }

    private sealed class StubMatchingService(
        Func<JobVacancy, CandidateProfile, CancellationToken, Task<JobMatchResult>> handler)
        : IJobMatchingService
    {
        public string ModelName => "test-model";
        public string PromptVersion => "test-prompt";
        public int CallCount { get; private set; }

        public Task<JobMatchResult> EvaluateAsync(
            JobVacancy vacancy,
            CandidateProfile profile,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return handler(vacancy, profile, cancellationToken);
        }
    }
}
