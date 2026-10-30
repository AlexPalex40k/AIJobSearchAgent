using AIJobSearchAgent.Application.Abstractions;
using AIJobSearchAgent.Application.Models;
using AIJobSearchAgent.Application.UseCases;
using AIJobSearchAgent.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIJobSearchAgent.Tests;

public sealed class ImportJobsUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_CalculatesSummaryForMatchedInvalidAndDuplicateJobs()
    {
        var provider = new StubProvider(
        [
            CreateVacancy("valid", "C# Developer", "Build services"),
            CreateVacancy("unmatched", "Java Developer", "Build JVM services"),
            CreateVacancy("invalid", "", "C# but missing a title")
        ]);
        var repository = new StubRepository(new SaveJobsResult(0, 1));
        var useCase = new ImportJobsUseCase(
            provider,
            repository,
            NullLogger<ImportJobsUseCase>.Instance);

        var summary = await useCase.ExecuteAsync(
            new JobSearchCriteria(["C#"], 1),
            CancellationToken.None);

        Assert.Equal(3, summary.TotalFetched);
        Assert.Equal(1, summary.TotalMatched);
        Assert.Equal(0, summary.Inserted);
        Assert.Equal(1, summary.SkippedDuplicates);
        Assert.Equal(1, summary.RejectedInvalid);
        Assert.Single(repository.ReceivedVacancies);
    }

    private static JobVacancy CreateVacancy(string id, string title, string description) => new()
    {
        ExternalId = id,
        Source = "Stub",
        Title = title,
        Company = "Example",
        Location = "Remote",
        Description = description,
        Url = $"https://example.test/jobs/{id}"
    };

    private sealed class StubProvider(IReadOnlyList<JobVacancy> vacancies) : IJobProvider
    {
        public string SourceName => "Stub";

        public Task<IReadOnlyList<JobVacancy>> SearchAsync(
            JobSearchCriteria criteria,
            CancellationToken cancellationToken) => Task.FromResult(vacancies);
    }

    private sealed class StubRepository(SaveJobsResult result) : IJobVacancyRepository
    {
        public IReadOnlyCollection<JobVacancy> ReceivedVacancies { get; private set; } = [];

        public Task<SaveJobsResult> AddNewAsync(
            IReadOnlyCollection<JobVacancy> vacancies,
            CancellationToken cancellationToken)
        {
            ReceivedVacancies = vacancies;
            return Task.FromResult(result);
        }

        public Task<PagedResult<JobVacancy>> GetAsync(JobQuery query, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<JobVacancy?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
