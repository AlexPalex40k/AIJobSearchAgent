using AIJobSearchAgent.Application.Abstractions;
using AIJobSearchAgent.Application.Models;
using AIJobSearchAgent.Application.UseCases;
using AIJobSearchAgent.Domain.Entities;

namespace AIJobSearchAgent.Tests;

public sealed class GetJobEvaluationsUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsEvaluationHistoryWithCompleteDtoMapping()
    {
        var vacancy = CreateVacancy();
        var evaluation = CreateEvaluation(vacancy.Id);
        var evaluationRepository = new StubEvaluationRepository([evaluation]);
        var useCase = new GetJobEvaluationsUseCase(
            new StubVacancyRepository(vacancy),
            evaluationRepository);

        var result = await useCase.ExecuteAsync(vacancy.Id, CancellationToken.None);

        var dto = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<JobMatchEvaluationDto>>(result));
        Assert.Equal(evaluation.Id, dto.Id);
        Assert.Equal(evaluation.JobVacancyId, dto.JobVacancyId);
        Assert.Equal(evaluation.ProfileHash, dto.ProfileHash);
        Assert.Equal(evaluation.ModelName, dto.ModelName);
        Assert.Equal(evaluation.PromptVersion, dto.PromptVersion);
        Assert.Equal(evaluation.Rating, dto.Rating);
        Assert.Equal(evaluation.MatchedSkills, dto.MatchedSkills);
        Assert.Equal(evaluation.MissingRequiredSkills, dto.MissingRequiredSkills);
        Assert.Equal(evaluation.MissingOptionalSkills, dto.MissingOptionalSkills);
        Assert.Equal(evaluation.Explanation, dto.Explanation);
        Assert.Equal(evaluation.EvaluationStatus, dto.EvaluationStatus);
        Assert.Equal(evaluation.EvaluatedAt, dto.EvaluatedAt);
        Assert.Equal(vacancy.Id, evaluationRepository.RequestedVacancyId);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsNullWithoutQueryingHistoryWhenVacancyDoesNotExist()
    {
        var vacancy = CreateVacancy();
        var evaluationRepository = new StubEvaluationRepository([]);
        var useCase = new GetJobEvaluationsUseCase(
            new StubVacancyRepository(vacancy),
            evaluationRepository);

        var result = await useCase.ExecuteAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result);
        Assert.Null(evaluationRepository.RequestedVacancyId);
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

    private static JobMatchEvaluation CreateEvaluation(Guid vacancyId) => new()
    {
        JobVacancyId = vacancyId,
        ProfileHash = new string('b', 64),
        ModelName = "qwen3:1.7b",
        PromptVersion = "job-match-v1",
        Rating = 4,
        MatchedSkills = ["C#"],
        MissingRequiredSkills = ["Azure"],
        MissingOptionalSkills = ["Kubernetes"],
        Explanation = "Good match.",
        EvaluationStatus = EvaluationStatus.Succeeded,
        EvaluatedAt = DateTimeOffset.Parse("2026-11-06T12:00:00Z")
    };

    private sealed class StubEvaluationRepository(IReadOnlyList<JobMatchEvaluation> evaluations)
        : IJobMatchEvaluationRepository
    {
        public Guid? RequestedVacancyId { get; private set; }

        public Task AddAsync(JobMatchEvaluation evaluation, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<JobMatchEvaluation>> GetByVacancyIdAsync(
            Guid jobVacancyId,
            CancellationToken cancellationToken)
        {
            RequestedVacancyId = jobVacancyId;
            return Task.FromResult(evaluations);
        }
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
