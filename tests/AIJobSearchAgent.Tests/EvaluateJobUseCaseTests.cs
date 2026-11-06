using AIJobSearchAgent.Application.Abstractions;
using AIJobSearchAgent.Application.Exceptions;
using AIJobSearchAgent.Application.Models;
using AIJobSearchAgent.Application.UseCases;
using AIJobSearchAgent.Domain.Entities;
using AIJobSearchAgent.Domain.Profiles;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIJobSearchAgent.Tests;

public sealed class EvaluateJobUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_PersistsSuccessfulEvaluationAndMapsDto()
    {
        var vacancy = CreateVacancy();
        var evaluatedAt = DateTimeOffset.Parse("2026-11-06T12:00:00Z");
        var result = new JobMatchResult(
            4,
            ["C#", "ASP.NET Core"],
            ["Azure"],
            ["Kubernetes"],
            "A good match.",
            "qwen3:1.7b",
            "job-match-v1",
            evaluatedAt);
        var evaluationRepository = new CapturingEvaluationRepository();
        var useCase = CreateUseCase(
            vacancy,
            evaluationRepository,
            new DelegateMatchingService((_, _, _) => Task.FromResult(result)));

        var dto = await useCase.ExecuteAsync(vacancy.Id, CancellationToken.None);

        Assert.NotNull(dto);
        Assert.Equal(vacancy.Id, dto.JobVacancyId);
        Assert.Equal(new string('a', 64), dto.ProfileHash);
        Assert.Equal(4, dto.Rating);
        Assert.Equal(["C#", "ASP.NET Core"], dto.MatchedSkills);
        Assert.Equal(["Azure"], dto.MissingRequiredSkills);
        Assert.Equal(["Kubernetes"], dto.MissingOptionalSkills);
        Assert.Equal("A good match.", dto.Explanation);
        Assert.Equal("qwen3:1.7b", dto.ModelName);
        Assert.Equal("job-match-v1", dto.PromptVersion);
        Assert.Equal(EvaluationStatus.Succeeded, dto.EvaluationStatus);
        Assert.Equal(evaluatedAt, dto.EvaluatedAt);

        var saved = Assert.Single(evaluationRepository.Saved);
        Assert.Equal(EvaluationStatus.Succeeded, saved.EvaluationStatus);
        Assert.Equal(4, saved.Rating);
    }

    [Fact]
    public async Task ExecuteAsync_PersistsFailureWithoutInventingRatingAndRethrows()
    {
        var vacancy = CreateVacancy();
        var evaluationRepository = new CapturingEvaluationRepository();
        var useCase = CreateUseCase(
            vacancy,
            evaluationRepository,
            CreateThrowingService(EvaluationStatus.Failed));

        var exception = await Assert.ThrowsAsync<JobMatchingException>(
            () => useCase.ExecuteAsync(vacancy.Id, CancellationToken.None));

        Assert.Equal(EvaluationStatus.Failed, exception.EvaluationStatus);
        AssertFailureSaved(evaluationRepository, EvaluationStatus.Failed);
    }

    [Fact]
    public async Task ExecuteAsync_PersistsTimeoutWithoutInventingRatingAndRethrows()
    {
        var vacancy = CreateVacancy();
        var evaluationRepository = new CapturingEvaluationRepository();
        var useCase = CreateUseCase(
            vacancy,
            evaluationRepository,
            CreateThrowingService(EvaluationStatus.TimedOut));

        var exception = await Assert.ThrowsAsync<JobMatchingException>(
            () => useCase.ExecuteAsync(vacancy.Id, CancellationToken.None));

        Assert.Equal(EvaluationStatus.TimedOut, exception.EvaluationStatus);
        AssertFailureSaved(evaluationRepository, EvaluationStatus.TimedOut);
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesCancellationWithoutPersistingFailure()
    {
        var vacancy = CreateVacancy();
        var evaluationRepository = new CapturingEvaluationRepository();
        CancellationToken receivedToken = default;
        var matchingService = new DelegateMatchingService((_, _, cancellationToken) =>
        {
            receivedToken = cancellationToken;
            return Task.FromCanceled<JobMatchResult>(cancellationToken);
        });
        var useCase = CreateUseCase(vacancy, evaluationRepository, matchingService);
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => useCase.ExecuteAsync(vacancy.Id, cancellationSource.Token));

        Assert.Equal(cancellationSource.Token, receivedToken);
        Assert.Empty(evaluationRepository.Saved);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsNullWithoutLoadingProfileWhenVacancyDoesNotExist()
    {
        var vacancy = CreateVacancy();
        var profileProvider = new StubProfileProvider();
        var matchingService = new DelegateMatchingService((_, _, _) =>
            throw new InvalidOperationException("The matching service must not be called."));
        var evaluationRepository = new CapturingEvaluationRepository();
        var useCase = new EvaluateJobUseCase(
            new StubVacancyRepository(vacancy),
            evaluationRepository,
            profileProvider,
            matchingService,
            NullLogger<EvaluateJobUseCase>.Instance);

        var result = await useCase.ExecuteAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(0, profileProvider.CallCount);
        Assert.Equal(0, matchingService.CallCount);
        Assert.Empty(evaluationRepository.Saved);
    }

    private static EvaluateJobUseCase CreateUseCase(
        JobVacancy vacancy,
        CapturingEvaluationRepository evaluationRepository,
        IJobMatchingService matchingService) => new(
            new StubVacancyRepository(vacancy),
            evaluationRepository,
            new StubProfileProvider(),
            matchingService,
            NullLogger<EvaluateJobUseCase>.Instance);

    private static IJobMatchingService CreateThrowingService(EvaluationStatus status) =>
        new DelegateMatchingService((_, _, _) => throw new JobMatchingException(
            "Model evaluation failed.",
            "qwen3:1.7b",
            "job-match-v1",
            status));

    private static void AssertFailureSaved(
        CapturingEvaluationRepository repository,
        EvaluationStatus expectedStatus)
    {
        var saved = Assert.Single(repository.Saved);
        Assert.Null(saved.Rating);
        Assert.Equal(expectedStatus, saved.EvaluationStatus);
        Assert.Equal("qwen3:1.7b", saved.ModelName);
        Assert.Equal("job-match-v1", saved.PromptVersion);
    }

    private static JobVacancy CreateVacancy() => new()
    {
        ExternalId = "job-1",
        Source = "Test",
        Title = ".NET Engineer",
        Company = "Example",
        Location = "Remote",
        Description = "Build APIs.",
        Url = "https://example.test/jobs/1"
    };

    private sealed class DelegateMatchingService(
        Func<JobVacancy, CandidateProfile, CancellationToken, Task<JobMatchResult>> handler)
        : IJobMatchingService
    {
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

    private sealed class StubProfileProvider : ICandidateProfileProvider
    {
        public int CallCount { get; private set; }

        public Task<CandidateProfileSnapshot> LoadAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new CandidateProfileSnapshot(
                new CandidateProfile
                {
                    ProfessionalTitle = "Backend Engineer",
                    YearsProfessionalExperience = 5,
                    TechnicalSkills = [new CandidateSkill { Name = "C#" }]
                },
                new string('a', 64)));
        }
    }

    private sealed class CapturingEvaluationRepository : IJobMatchEvaluationRepository
    {
        public List<JobMatchEvaluation> Saved { get; } = [];

        public Task AddAsync(JobMatchEvaluation evaluation, CancellationToken cancellationToken)
        {
            Saved.Add(evaluation);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<JobMatchEvaluation>> GetByVacancyIdAsync(
            Guid jobVacancyId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubVacancyRepository(JobVacancy vacancy) : IJobVacancyRepository
    {
        public Task<JobVacancy?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<JobVacancy?>(id == vacancy.Id ? vacancy : null);

        public Task<SaveJobsResult> AddNewAsync(
            IReadOnlyCollection<JobVacancy> vacancies,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<PagedResult<JobVacancy>> GetAsync(
            JobQuery query,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
