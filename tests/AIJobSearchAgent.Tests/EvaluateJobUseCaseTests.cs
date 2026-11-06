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
    public async Task ExecuteAsync_PersistsFailureWithoutInventingRatingAndRethrows()
    {
        var vacancy = CreateVacancy();
        var evaluationRepository = new CapturingEvaluationRepository();
        var useCase = new EvaluateJobUseCase(
            new StubVacancyRepository(vacancy),
            evaluationRepository,
            new StubProfileProvider(),
            new FailingMatchingService(),
            NullLogger<EvaluateJobUseCase>.Instance);

        await Assert.ThrowsAsync<JobMatchingException>(
            () => useCase.ExecuteAsync(vacancy.Id, CancellationToken.None));

        var saved = Assert.Single(evaluationRepository.Saved);
        Assert.Null(saved.Rating);
        Assert.Equal(EvaluationStatus.Failed, saved.EvaluationStatus);
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

    private sealed class FailingMatchingService : IJobMatchingService
    {
        public Task<JobMatchResult> EvaluateAsync(
            JobVacancy vacancy,
            CandidateProfile profile,
            CancellationToken cancellationToken) => throw new JobMatchingException(
                "Malformed model output.",
                "qwen3:1.7b",
                "job-match-v1",
                EvaluationStatus.Failed);
    }

    private sealed class StubProfileProvider : ICandidateProfileProvider
    {
        public Task<CandidateProfileSnapshot> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(
            new CandidateProfileSnapshot(
                new CandidateProfile
                {
                    ProfessionalTitle = "Backend Engineer",
                    YearsProfessionalExperience = 5,
                    TechnicalSkills = [new CandidateSkill { Name = "C#" }]
                },
                new string('a', 64)));
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
