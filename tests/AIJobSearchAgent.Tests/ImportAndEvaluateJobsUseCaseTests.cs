using AIJobSearchAgent.Application.Abstractions;
using AIJobSearchAgent.Application.Exceptions;
using AIJobSearchAgent.Application.Models;
using AIJobSearchAgent.Application.UseCases;
using AIJobSearchAgent.Domain.Entities;
using AIJobSearchAgent.Domain.Profiles;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIJobSearchAgent.Tests;

public sealed class ImportAndEvaluateJobsUseCaseTests
{
    private const string ProfileHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string ModelName = "qwen3:1.7b";
    private const string PromptVersion = "job-match-simple-v4";

    [Fact]
    public async Task ExecuteAsync_ImportsOnceAndEvaluatesEveryPersistedVacancyInSequentialBatches()
    {
        var vacancies = Enumerable.Range(1, 25)
            .Select(index => CreateVacancy($"job-{index}"))
            .ToArray();
        var unrelatedVacancy = CreateVacancy("unrelated");
        var repository = new StubVacancyRepository(vacancies.Append(unrelatedVacancy))
        {
            SaveResult = new SaveJobsResult(20, 5)
            {
                PersistedVacancyIds = [vacancies[0].Id, .. vacancies.Select(vacancy => vacancy.Id)]
            }
        };
        var evaluationRepository = new StubEvaluationRepository();
        evaluationRepository.Stored.Add(CreateSuccessfulEvaluation(vacancies[0].Id));
        var provider = new StubProvider(vacancies);
        var profileProvider = new StubProfileProvider();
        var matchingService = new StubMatchingService(vacancies[1].Id);
        var importJobs = new ImportJobsUseCase(
            provider,
            repository,
            NullLogger<ImportJobsUseCase>.Instance);
        var evaluateJob = new EvaluateJobUseCase(
            repository,
            evaluationRepository,
            profileProvider,
            matchingService,
            NullLogger<EvaluateJobUseCase>.Instance);
        var evaluateBatch = new EvaluateJobsBatchUseCase(
            repository,
            evaluationRepository,
            profileProvider,
            matchingService,
            evaluateJob,
            NullLogger<EvaluateJobsBatchUseCase>.Instance);
        var useCase = new ImportAndEvaluateJobsUseCase(importJobs, evaluateBatch);

        var summary = await useCase.ExecuteAsync(
            new JobSearchCriteria([".NET"], 3),
            CancellationToken.None);

        Assert.Equal(1, provider.CallCount);
        Assert.Equal(1, repository.AddCallCount);
        Assert.Equal(25, summary.Import.TotalFetched);
        Assert.Equal(25, summary.Import.TotalMatched);
        Assert.Equal(20, summary.Import.Inserted);
        Assert.Equal(5, summary.Import.SkippedDuplicates);
        Assert.Equal(23, summary.SuccessfullyEvaluatedCount);
        Assert.Equal(1, summary.SkippedCount);
        Assert.Equal(1, summary.FailedCount);
        Assert.Equal(2, profileProvider.LoadCount);
        Assert.Equal(24, matchingService.EvaluatedVacancyIds.Count);
        Assert.DoesNotContain(vacancies[0].Id, matchingService.EvaluatedVacancyIds);
        Assert.Contains(vacancies[1].Id, matchingService.EvaluatedVacancyIds);
        Assert.DoesNotContain(unrelatedVacancy.Id, matchingService.EvaluatedVacancyIds);
        Assert.Equal(26, repository.Stored.Count);
        Assert.Contains(evaluationRepository.Stored, evaluation =>
            evaluation.JobVacancyId == vacancies[1].Id &&
            evaluation.EvaluationStatus == EvaluationStatus.Failed);
    }

    private static JobVacancy CreateVacancy(string externalId) => new()
    {
        ExternalId = externalId,
        Source = "Test",
        Title = ".NET Engineer",
        Company = "Example",
        Location = "Remote",
        IsRemote = true,
        Description = "Build .NET services.",
        Url = $"https://example.test/jobs/{externalId}"
    };

    private static JobMatchEvaluation CreateSuccessfulEvaluation(Guid vacancyId) => new()
    {
        JobVacancyId = vacancyId,
        ProfileHash = ProfileHash,
        ModelName = ModelName,
        PromptVersion = PromptVersion,
        Rating = 5,
        Explanation = "Already evaluated.",
        EvaluationStatus = EvaluationStatus.Succeeded
    };

    private sealed class StubProvider(IReadOnlyList<JobVacancy> vacancies) : IJobProvider
    {
        public string SourceName => "Test";
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<JobVacancy>> SearchAsync(
            JobSearchCriteria criteria,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(vacancies);
        }
    }

    private sealed class StubVacancyRepository(IEnumerable<JobVacancy> vacancies) : IJobVacancyRepository
    {
        public Dictionary<Guid, JobVacancy> Stored { get; } = vacancies.ToDictionary(vacancy => vacancy.Id);
        public SaveJobsResult SaveResult { get; set; } = new(0, 0);
        public int AddCallCount { get; private set; }

        public Task<SaveJobsResult> AddNewAsync(
            IReadOnlyCollection<JobVacancy> vacancies,
            CancellationToken cancellationToken)
        {
            AddCallCount++;
            return Task.FromResult(SaveResult);
        }

        public Task<PagedResult<JobVacancy>> GetAsync(
            JobQuery query,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<JobVacancy?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Stored.GetValueOrDefault(id));
    }

    private sealed class StubEvaluationRepository : IJobMatchEvaluationRepository
    {
        public List<JobMatchEvaluation> Stored { get; } = [];

        public Task AddAsync(JobMatchEvaluation evaluation, CancellationToken cancellationToken)
        {
            Stored.Add(evaluation);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<JobMatchEvaluation>> GetByVacancyIdAsync(
            Guid jobVacancyId,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<JobMatchEvaluation>>(
                Stored.Where(evaluation => evaluation.JobVacancyId == jobVacancyId).ToArray());

        public Task<JobMatchEvaluation?> GetSuccessfulAsync(
            Guid jobVacancyId,
            string profileHash,
            string modelName,
            string promptVersion,
            CancellationToken cancellationToken) => Task.FromResult(Stored.LastOrDefault(evaluation =>
                evaluation.JobVacancyId == jobVacancyId &&
                evaluation.ProfileHash == profileHash &&
                evaluation.ModelName == modelName &&
                evaluation.PromptVersion == promptVersion &&
                evaluation.EvaluationStatus == EvaluationStatus.Succeeded));
    }

    private sealed class StubProfileProvider : ICandidateProfileProvider
    {
        public int LoadCount { get; private set; }

        public Task<CandidateProfileSnapshot> LoadAsync(CancellationToken cancellationToken)
        {
            LoadCount++;
            return Task.FromResult(new CandidateProfileSnapshot(
                new CandidateProfile
                {
                    ProfessionalTitle = "Backend Engineer",
                    YearsProfessionalExperience = 5,
                    TechnicalSkills = [new CandidateSkill { Name = ".NET" }]
                },
                ProfileHash));
        }
    }

    private sealed class StubMatchingService(Guid failingVacancyId) : IJobMatchingService
    {
        public string ModelName => ImportAndEvaluateJobsUseCaseTests.ModelName;
        public string PromptVersion => ImportAndEvaluateJobsUseCaseTests.PromptVersion;
        public List<Guid> EvaluatedVacancyIds { get; } = [];

        public Task<JobMatchResult> EvaluateAsync(
            JobVacancy vacancy,
            CandidateProfile profile,
            CancellationToken cancellationToken)
        {
            EvaluatedVacancyIds.Add(vacancy.Id);
            if (vacancy.Id == failingVacancyId)
            {
                throw new JobMatchingException(
                    "Ollama failed.",
                    ModelName,
                    PromptVersion,
                    EvaluationStatus.Failed);
            }

            return Task.FromResult(new JobMatchResult(
                4,
                [".NET"],
                [],
                [],
                "Good match.",
                ModelName,
                PromptVersion,
                DateTimeOffset.UtcNow));
        }
    }
}
