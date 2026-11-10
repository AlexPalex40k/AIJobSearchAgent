using AIJobSearchAgent.Application.Abstractions;
using AIJobSearchAgent.Application.Exceptions;
using AIJobSearchAgent.Application.Models;
using AIJobSearchAgent.Application.UseCases;
using AIJobSearchAgent.Domain.Entities;
using AIJobSearchAgent.Domain.Profiles;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIJobSearchAgent.Tests;

public sealed class EvaluateJobsBatchUseCaseTests
{
    private const string ProfileHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public async Task ExecuteAsync_RejectsEmptyAndOversizedRequests()
    {
        var useCase = CreateUseCase([]);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            useCase.ExecuteAsync([], CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            useCase.ExecuteAsync(Enumerable.Repeat(Guid.NewGuid(), 21).ToArray(), CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_EvaluatesMultipleVacanciesSequentially()
    {
        var vacancies = new[] { CreateVacancy("job-1"), CreateVacancy("job-2"), CreateVacancy("job-3") };
        var callOrder = new List<Guid>();
        var activeCalls = 0;
        var maximumConcurrentCalls = 0;
        var matchingService = new StubMatchingService(async (vacancy, _, _) =>
        {
            callOrder.Add(vacancy.Id);
            var currentCalls = Interlocked.Increment(ref activeCalls);
            maximumConcurrentCalls = Math.Max(maximumConcurrentCalls, currentCalls);
            await Task.Yield();
            Interlocked.Decrement(ref activeCalls);
            return CreateMatchResult(vacancy.ExternalId == "job-2" ? 3 : 4);
        });
        var useCase = CreateUseCase(vacancies, matchingService: matchingService);

        var result = await useCase.ExecuteAsync(
            vacancies.Select(vacancy => vacancy.Id).ToArray(),
            CancellationToken.None);

        Assert.Equal(3, result.TotalRequestedVacancies);
        Assert.Equal(3, result.SuccessfullyEvaluatedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal(vacancies.Select(vacancy => vacancy.Id), callOrder);
        Assert.Equal(1, maximumConcurrentCalls);
        Assert.Equal([4, 3, 4], result.Results.Select(item => item.Rating));
    }

    [Fact]
    public async Task ExecuteAsync_SkipsMatchingSuccessfulEvaluation()
    {
        var vacancy = CreateVacancy("job-1");
        var matchingService = new StubMatchingService((_, _, _) =>
            throw new InvalidOperationException("Matching must not be called."));
        var existing = CreateEvaluation(
            vacancy.Id,
            ProfileHash,
            matchingService.ModelName,
            matchingService.PromptVersion,
            5);
        var useCase = CreateUseCase([vacancy], [existing], matchingService: matchingService);

        var result = await useCase.ExecuteAsync([vacancy.Id], CancellationToken.None);

        Assert.Equal(0, result.SuccessfullyEvaluatedCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Equal(0, result.FailedCount);
        var item = Assert.Single(result.Results);
        Assert.Equal(BatchJobEvaluationStatus.Skipped, item.Status);
        Assert.Equal(5, item.Rating);
        Assert.Equal(0, matchingService.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_ReevaluatesWhenProfileChanges()
    {
        var vacancy = CreateVacancy("job-1");
        var matchingService = new StubMatchingService((_, _, _) => Task.FromResult(CreateMatchResult(4)));
        var oldEvaluation = CreateEvaluation(
            vacancy.Id,
            new string('b', 64),
            matchingService.ModelName,
            matchingService.PromptVersion,
            5);
        var useCase = CreateUseCase([vacancy], [oldEvaluation], matchingService: matchingService);

        var result = await useCase.ExecuteAsync([vacancy.Id], CancellationToken.None);

        Assert.Equal(1, result.SuccessfullyEvaluatedCount);
        Assert.Equal(1, matchingService.CallCount);
    }

    [Theory]
    [InlineData("previous-model", "job-match-simple-v4")]
    [InlineData("qwen3:1.7b", "job-match-simple-v3")]
    public async Task ExecuteAsync_ReevaluatesWhenModelOrPromptVersionChanges(
        string storedModel,
        string storedPromptVersion)
    {
        var vacancy = CreateVacancy("job-1");
        var matchingService = new StubMatchingService((_, _, _) => Task.FromResult(CreateMatchResult(4)));
        var oldEvaluation = CreateEvaluation(vacancy.Id, ProfileHash, storedModel, storedPromptVersion, 5);
        var useCase = CreateUseCase([vacancy], [oldEvaluation], matchingService: matchingService);

        var result = await useCase.ExecuteAsync([vacancy.Id], CancellationToken.None);

        Assert.Equal(1, result.SuccessfullyEvaluatedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Equal(1, matchingService.CallCount);
    }

    [Theory]
    [InlineData(EvaluationStatus.Failed)]
    [InlineData(EvaluationStatus.TimedOut)]
    public async Task ExecuteAsync_DoesNotSkipFailedOrTimedOutEvaluation(EvaluationStatus status)
    {
        var vacancy = CreateVacancy("job-1");
        var matchingService = new StubMatchingService((_, _, _) => Task.FromResult(CreateMatchResult(4)));
        var previousAttempt = CreateEvaluation(
            vacancy.Id,
            ProfileHash,
            matchingService.ModelName,
            matchingService.PromptVersion,
            null,
            status);
        var useCase = CreateUseCase([vacancy], [previousAttempt], matchingService: matchingService);

        var result = await useCase.ExecuteAsync([vacancy.Id], CancellationToken.None);

        Assert.Equal(1, result.SuccessfullyEvaluatedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Equal(1, matchingService.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_ContinuesAfterIndividualEvaluationFailure()
    {
        var failedVacancy = CreateVacancy("job-failed");
        var successfulVacancy = CreateVacancy("job-succeeded");
        var matchingService = new StubMatchingService((vacancy, _, _) =>
            vacancy.Id == failedVacancy.Id
                ? throw new JobMatchingException(
                    "Sensitive internal failure details.",
                    "qwen3:1.7b",
                    "job-match-simple-v4",
                    EvaluationStatus.Failed)
                : Task.FromResult(CreateMatchResult(4)));
        var repository = new StubEvaluationRepository();
        var useCase = CreateUseCase(
            [failedVacancy, successfulVacancy],
            matchingService: matchingService,
            evaluationRepository: repository);

        var result = await useCase.ExecuteAsync(
            [failedVacancy.Id, successfulVacancy.Id],
            CancellationToken.None);

        Assert.Equal(1, result.SuccessfullyEvaluatedCount);
        Assert.Equal(1, result.FailedCount);
        Assert.Equal("Evaluation failed.", result.Results[0].Explanation);
        Assert.DoesNotContain("Sensitive", result.Results[0].Explanation, StringComparison.Ordinal);
        Assert.Equal(BatchJobEvaluationStatus.Succeeded, result.Results[1].Status);
        Assert.Equal(2, matchingService.CallCount);
        Assert.Contains(repository.Saved, evaluation => evaluation.EvaluationStatus == EvaluationStatus.Failed);
    }

    [Fact]
    public async Task ExecuteAsync_HandlesDuplicateEmptyAndMissingVacancyIds()
    {
        var vacancy = CreateVacancy("job-1");
        var missingId = Guid.NewGuid();
        var matchingService = new StubMatchingService((_, _, _) => Task.FromResult(CreateMatchResult(4)));
        var useCase = CreateUseCase([vacancy], matchingService: matchingService);

        var result = await useCase.ExecuteAsync(
            [vacancy.Id, vacancy.Id, Guid.Empty, missingId],
            CancellationToken.None);

        Assert.Equal(4, result.TotalRequestedVacancies);
        Assert.Equal(1, result.SuccessfullyEvaluatedCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Equal(2, result.FailedCount);
        Assert.Equal(BatchJobEvaluationStatus.Skipped, result.Results[1].Status);
        Assert.Equal(4, result.Results[1].Rating);
        Assert.Equal("Vacancy ID is invalid.", result.Results[2].Explanation);
        Assert.Equal("Vacancy was not found.", result.Results[3].Explanation);
        Assert.Equal(1, matchingService.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesCancellationAndStopsRemainingEvaluations()
    {
        var vacancies = new[] { CreateVacancy("job-1"), CreateVacancy("job-2") };
        using var cancellationSource = new CancellationTokenSource();
        var matchingService = new StubMatchingService((_, _, cancellationToken) =>
        {
            cancellationSource.Cancel();
            return Task.FromCanceled<JobMatchResult>(cancellationToken);
        });
        var useCase = CreateUseCase(vacancies, matchingService: matchingService);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            useCase.ExecuteAsync(vacancies.Select(vacancy => vacancy.Id).ToArray(), cancellationSource.Token));

        Assert.Equal(1, matchingService.CallCount);
    }

    private static EvaluateJobsBatchUseCase CreateUseCase(
        IReadOnlyCollection<JobVacancy> vacancies,
        IReadOnlyCollection<JobMatchEvaluation>? existingEvaluations = null,
        StubMatchingService? matchingService = null,
        StubEvaluationRepository? evaluationRepository = null)
    {
        var vacancyRepository = new StubVacancyRepository(vacancies);
        evaluationRepository ??= new StubEvaluationRepository(existingEvaluations);
        matchingService ??= new StubMatchingService((_, _, _) => Task.FromResult(CreateMatchResult(4)));
        var profileProvider = new StubProfileProvider(ProfileHash);
        var evaluateJob = new EvaluateJobUseCase(
            vacancyRepository,
            evaluationRepository,
            profileProvider,
            matchingService,
            NullLogger<EvaluateJobUseCase>.Instance);
        return new EvaluateJobsBatchUseCase(
            vacancyRepository,
            evaluationRepository,
            profileProvider,
            matchingService,
            evaluateJob,
            NullLogger<EvaluateJobsBatchUseCase>.Instance);
    }

    private static JobVacancy CreateVacancy(string externalId) => new()
    {
        ExternalId = externalId,
        Source = "Test",
        Title = ".NET Engineer",
        Company = "Example",
        Location = "Remote",
        Description = "Build APIs.",
        Url = $"https://example.test/jobs/{externalId}"
    };

    private static JobMatchResult CreateMatchResult(int rating) => new(
        rating,
        ["C#"],
        [],
        [],
        "The vacancy is a good match.",
        "qwen3:1.7b",
        "job-match-simple-v4",
        DateTimeOffset.Parse("2026-11-10T10:00:00Z"));

    private static JobMatchEvaluation CreateEvaluation(
        Guid vacancyId,
        string profileHash,
        string modelName,
        string promptVersion,
        int? rating,
        EvaluationStatus status = EvaluationStatus.Succeeded) => new()
        {
            JobVacancyId = vacancyId,
            ProfileHash = profileHash,
            ModelName = modelName,
            PromptVersion = promptVersion,
            Rating = rating,
            Explanation = "Existing successful evaluation.",
            EvaluationStatus = status
        };

    private sealed class StubVacancyRepository(IEnumerable<JobVacancy> vacancies) : IJobVacancyRepository
    {
        private readonly IReadOnlyDictionary<Guid, JobVacancy> vacancies = vacancies.ToDictionary(vacancy => vacancy.Id);

        public Task<JobVacancy?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(vacancies.GetValueOrDefault(id));

        public Task<SaveJobsResult> AddNewAsync(
            IReadOnlyCollection<JobVacancy> vacancies,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<PagedResult<JobVacancy>> GetAsync(
            JobQuery query,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubEvaluationRepository(IEnumerable<JobMatchEvaluation>? evaluations = null)
        : IJobMatchEvaluationRepository
    {
        private readonly List<JobMatchEvaluation> evaluations = evaluations?.ToList() ?? [];

        public IReadOnlyList<JobMatchEvaluation> Saved => evaluations;

        public Task AddAsync(JobMatchEvaluation evaluation, CancellationToken cancellationToken)
        {
            evaluations.Add(evaluation);
            return Task.CompletedTask;
        }

        public Task<JobMatchEvaluation?> GetSuccessfulAsync(
            Guid jobVacancyId,
            string profileHash,
            string modelName,
            string promptVersion,
            CancellationToken cancellationToken) => Task.FromResult(evaluations
                .Where(evaluation => evaluation.JobVacancyId == jobVacancyId)
                .Where(evaluation => evaluation.ProfileHash == profileHash)
                .Where(evaluation => evaluation.ModelName == modelName)
                .Where(evaluation => evaluation.PromptVersion == promptVersion)
                .Where(evaluation => evaluation.EvaluationStatus == EvaluationStatus.Succeeded)
                .OrderByDescending(evaluation => evaluation.EvaluatedAt)
                .FirstOrDefault());

        public Task<IReadOnlyList<JobMatchEvaluation>> GetByVacancyIdAsync(
            Guid jobVacancyId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubProfileProvider(string profileHash) : ICandidateProfileProvider
    {
        public Task<CandidateProfileSnapshot> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(
            new CandidateProfileSnapshot(
                new CandidateProfile
                {
                    ProfessionalTitle = "Backend Engineer",
                    YearsProfessionalExperience = 5,
                    TechnicalSkills = [new CandidateSkill { Name = "C#" }]
                },
                profileHash));
    }

    private sealed class StubMatchingService(
        Func<JobVacancy, CandidateProfile, CancellationToken, Task<JobMatchResult>> evaluate)
        : IJobMatchingService
    {
        public int CallCount { get; private set; }

        public string ModelName => "qwen3:1.7b";

        public string PromptVersion => "job-match-simple-v4";

        public Task<JobMatchResult> EvaluateAsync(
            JobVacancy vacancy,
            CandidateProfile profile,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return evaluate(vacancy, profile, cancellationToken);
        }
    }
}
